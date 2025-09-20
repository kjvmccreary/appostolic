using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Appostolic.Api.Tests; // WebAppFactory namespace
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Appostolic.Api.AuthTests;

public class SelectTenantTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SelectTenantTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SelectTenant_Succeeds_RotatesRefreshToken()
    {
        using var client = _factory.CreateClient();
        var email = $"sel-{Guid.NewGuid():N}@example.com";
        // Signup to create user + personal tenant membership
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        // Add a second tenant membership so we can explicitly choose it
        Guid secondTenantId; string secondSlug;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            secondSlug = $"{Guid.NewGuid():N}".Substring(0, 12);
            var t2 = new Tenant { Id = Guid.NewGuid(), Name = secondSlug, CreatedAt = DateTime.UtcNow };
            db.Add(t2);
            await db.SaveChangesAsync();
            secondTenantId = t2.Id;
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = secondTenantId, UserId = userId, Roles = Roles.Creator, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // Login to get neutral refresh token (do not request tenant auto)
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var loginDoc = await login.Content.ReadFromJsonAsync<JsonObject>();
        var refreshToken = loginDoc!["refresh"]!["token"]!.GetValue<string>();

        // Call select-tenant endpoint
        var select = await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = secondTenantId.ToString(), refreshToken });
        select.EnsureSuccessStatusCode();
        var doc = await select.Content.ReadFromJsonAsync<JsonObject>();
        doc.Should().NotBeNull();
        doc!["access"]!["tenantId"]!.GetValue<string>().Should().Be(secondTenantId.ToString());
        doc!["refresh"]!["token"]!.GetValue<string>().Should().NotBe(refreshToken); // rotated

        var newRefresh = doc!["refresh"]!["token"]!.GetValue<string>();

        // Reuse old refresh token should now 401 (revoked)
        var reuse = await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = secondTenantId.ToString(), refreshToken });
        reuse.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);

        // New refresh still works (rotate again) to same tenant
        var again = await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = secondTenantId.ToString(), refreshToken = newRefresh });
        again.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SelectTenant_InvalidRefresh_Returns401()
    {
        using var client = _factory.CreateClient();
        var email = $"invref-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        // Create an arbitrary tenant id (user already has personal one). We'll request with random refresh token.
        var bogusRefresh = Guid.NewGuid().ToString("N");

        // Fetch a valid tenant id to use (the personal one)
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = await db.Memberships
                .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.TenantId, u.Email })
                .Where(x => x.Email == email)
                .Select(x => x.TenantId)
                .FirstAsync();
        }

        var resp = await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = tenantId.ToString(), refreshToken = bogusRefresh });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SelectTenant_ForbiddenTenant_Returns403()
    {
        using var client = _factory.CreateClient();
        var email = $"forbid-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        // Create an unrelated tenant with no membership for the user
        Guid otherTenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            otherTenantId = Guid.NewGuid();
            var t = new Tenant { Id = otherTenantId, Name = $"oth-{Guid.NewGuid():N}".Substring(0, 10), CreatedAt = DateTime.UtcNow };
            db.Add(t);
            await db.SaveChangesAsync();
        }

        // Login to get refresh token
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var loginDoc = await login.Content.ReadFromJsonAsync<JsonObject>();
        var refreshToken = loginDoc!["refresh"]!["token"]!.GetValue<string>();

        var resp = await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = otherTenantId.ToString(), refreshToken });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SelectTenant_ExpiredRefresh_Returns401()
    {
        using var client = _factory.CreateClient();
        var email = $"exp-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        // Login to obtain refresh
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var loginDoc = await login.Content.ReadFromJsonAsync<JsonObject>();
        var refreshToken = loginDoc!["refresh"]!["token"]!.GetValue<string>();

        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = await db.Memberships.Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.TenantId, u.Email })
                .Where(x => x.Email == email)
                .Select(x => x.TenantId)
                .FirstAsync();
            // Expire the refresh token in DB
            var all = await db.RefreshTokens.Where(r => r.Purpose == "neutral").OrderByDescending(r => r.CreatedAt).ToListAsync();
            foreach (var r in all)
            {
                if (r.ExpiresAt > DateTime.UtcNow)
                {
                    r.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
                    db.Update(r);
                }
            }
            await db.SaveChangesAsync();
        }

        var resp = await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = tenantId.ToString(), refreshToken });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
}
