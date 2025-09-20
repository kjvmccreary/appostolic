using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Appostolic.Api.Tests;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Appostolic.Api.AuthTests;

public class LoginTenantSelectionTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LoginTenantSelectionTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_MultiMembership_NoTenantParam_HasNoTenantToken()
    {
        using var client = _factory.CreateClient();
        var email = $"multi-{Guid.NewGuid():N}@example.com";
        // Signup creates initial personal tenant + membership
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        // Add second tenant + membership
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            var secondSlug = $"{Guid.NewGuid():N}".Substring(0, 12);
            var t2 = new Tenant { Id = Guid.NewGuid(), Name = secondSlug, CreatedAt = DateTime.UtcNow };
            db.Add(t2);
            await db.SaveChangesAsync();
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = t2.Id, UserId = userId, Roles = Roles.Creator, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var doc = await login.Content.ReadFromJsonAsync<JsonObject>();
        doc.Should().NotBeNull();
        var tenantToken = doc!["tenantToken"];
        (tenantToken == null || tenantToken!.GetValue<JsonNode?>() == null).Should().BeTrue();
    }

    [Fact]
    public async Task Login_MultiMembership_TenantAuto_Returns409()
    {
        using var client = _factory.CreateClient();
        var email = $"multi-auto-{Guid.NewGuid():N}@example.com";
        // Signup first to create user and password
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        // Add second tenant + membership manually
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            var t2 = new Tenant { Id = Guid.NewGuid(), Name = $"{Guid.NewGuid():N}".Substring(0, 10), CreatedAt = DateTime.UtcNow };
            db.Add(t2);
            await db.SaveChangesAsync();
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = t2.Id, UserId = userId, Roles = Roles.Creator, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var login = await client.PostAsJsonAsync($"/api/auth/login?tenant=auto", new { email, password = "Password123!" });
        login.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_MultiMembership_TenantSlug_EmitsTenantToken()
    {
        using var client = _factory.CreateClient();
        var email = $"multi-slug-{Guid.NewGuid():N}@example.com";
        // Signup -> first tenant membership auto created? Signup currently creates personal tenant? Confirm: signup endpoint creates personal tenant and membership.
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        string secondTenantSlug;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            secondTenantSlug = $"{Guid.NewGuid():N}".Substring(0, 10);
            var t2 = new Tenant { Id = Guid.NewGuid(), Name = secondTenantSlug, CreatedAt = DateTime.UtcNow };
            db.Add(t2);
            await db.SaveChangesAsync();
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = t2.Id, UserId = userId, Roles = Roles.Creator, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var login = await client.PostAsJsonAsync($"/api/auth/login?tenant={secondTenantSlug}", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var doc = await login.Content.ReadFromJsonAsync<JsonObject>();
        doc.Should().NotBeNull();
        doc!["tenantToken"]!.Should().NotBeNull();
        doc!["tenantToken"]!["access"]!["tenantSlug"]!.GetValue<string>().Should().Be(secondTenantSlug);
    }
}
