using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Covers multi-membership (two-stage) login scenarios ensuring the first stage (neutral login)
/// does NOT auto-select a tenant, and that subsequent /api/auth/select-tenant calls rotate refresh
/// tokens, revoke the prior refresh, and enforce membership existence.
/// These complement existing <see cref="SelectTenantTests"/> which already cover invalid, expired,
/// forbidden, and reuse scenarios, but did not assert initial login response shape or DB revocation.
/// </summary>
public class LoginMultiTenantTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LoginMultiTenantTests(WebAppFactory factory) => _factory = factory;

    private HttpClient CreateManualCookieClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>
    /// Helper: SHA256 Base64 hashing used for refresh token storage (duplicated here for assertion).
    /// </summary>
    private static string Hash(string token)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private static (string CookieHeader, string TokenValue) ExtractRefreshCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var raw = cookies!.First(c => c.StartsWith("rt=", StringComparison.OrdinalIgnoreCase));
        var header = raw.Split(';')[0];
        var token = header[(header.IndexOf('=') + 1)..];
        return (header, token);
    }

    [Fact]
    public async Task Login_MultiMembership_NoAutoSelection_NoTenantToken()
    {
    using var client = CreateManualCookieClient();
        var email = $"mm1-{Guid.NewGuid():N}@example.com";

        // 1. Signup (creates user + personal tenant membership)
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        // 2. Add second membership BEFORE login so the login response includes two memberships
        string secondSlug;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            secondSlug = $"sec-{Guid.NewGuid():N}".Substring(0, 16);
            var t2 = new Tenant { Id = Guid.NewGuid(), Name = secondSlug, CreatedAt = DateTime.UtcNow };
            db.Add(t2); await db.SaveChangesAsync();
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = t2.Id, UserId = userId, Roles = Roles.Creator, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // 3. Login (neutral)
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var doc = await login.Content.ReadFromJsonAsync<JsonObject>();
        doc.Should().NotBeNull();
    var refreshObj = doc!["refresh"]!.AsObject();
    refreshObj.ContainsKey("token").Should().BeFalse();
        var memberships = doc!["memberships"]!.AsArray();
        memberships.Count.Should().Be(2, "multi-membership users must manually select a tenant");
        foreach (var membershipNode in memberships)
        {
            membershipNode.Should().NotBeNull();
            var membership = membershipNode!.AsObject();
            var rolesValue = membership["roles"]!.GetValue<int>();
            var labelsJson = membership["rolesLabels"]!.AsArray();
            var labels = new List<string>(labelsJson.Count);
            foreach (var labelNode in labelsJson)
            {
                labels.Add(labelNode!.GetValue<string>());
            }
            labels.Should().Equal(ExpectedRoleLabels(rolesValue));
        }
        // Ensure no tenantToken auto-issued
        doc!["tenantToken"].Should().BeNull("tenantToken must not be present for multi-membership neutral login");
        // Neutral access assertion
        doc!["access"]!["type"]!.GetValue<string>().Should().Be("neutral");
    }

    [Fact]
    public async Task Login_MultiMembership_SelectTenant_RotatesAndRevokesOldRefresh()
    {
    using var client = CreateManualCookieClient();
        var email = $"mm2-{Guid.NewGuid():N}@example.com";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        Guid secondTenantId; string secondSlug;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            secondSlug = $"sec-{Guid.NewGuid():N}".Substring(0, 16);
            var t2 = new Tenant { Id = Guid.NewGuid(), Name = secondSlug, CreatedAt = DateTime.UtcNow };
            db.Add(t2); await db.SaveChangesAsync();
            secondTenantId = t2.Id;
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = secondTenantId, UserId = userId, Roles = Roles.Creator | Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var loginDoc = await login.Content.ReadFromJsonAsync<JsonObject>();
        var (refreshCookie, oldRefresh) = ExtractRefreshCookie(login);
        var oldHash = Hash(oldRefresh);

        // Perform tenant selection
        var selectReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/select-tenant")
        {
            Content = JsonContent.Create(new { tenant = secondTenantId.ToString(), refreshToken = oldRefresh })
        };
        selectReq.Headers.Add("Cookie", refreshCookie);
        var select = await client.SendAsync(selectReq);
        select.EnsureSuccessStatusCode();
        var selectDoc = await select.Content.ReadFromJsonAsync<JsonObject>();
        selectDoc!["access"]!["type"]!.GetValue<string>().Should().Be("tenant");
        selectDoc!["access"]!["tenantId"]!.GetValue<string>().Should().Be(secondTenantId.ToString());
        var selectedRoles = selectDoc!["roles"]!.GetValue<int>();
        selectedRoles.Should().Be((int)(Roles.Creator | Roles.Learner));
        var selectedLabelsJson = selectDoc!["rolesLabels"]!.AsArray();
        var selectedLabels = new List<string>(selectedLabelsJson.Count);
        foreach (var labelNode in selectedLabelsJson)
        {
            selectedLabels.Add(labelNode!.GetValue<string>());
        }
        selectedLabels.Should().Equal(ExpectedRoleLabels(selectedRoles));
        selectDoc!["refresh"]!.AsObject().ContainsKey("token").Should().BeFalse();
        var (newRefreshCookie, newRefresh) = ExtractRefreshCookie(select);
        newRefresh.Should().NotBe(oldRefresh, "refresh token must rotate on tenant selection");
        var newHash = Hash(newRefresh);

        // DB assertions: old revoked, new active
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var oldEntry = await db.RefreshTokens.SingleAsync(r => r.TokenHash == oldHash);
            oldEntry.RevokedAt.Should().NotBeNull("old refresh must be revoked after rotation");
            var newEntry = await db.RefreshTokens.SingleAsync(r => r.TokenHash == newHash);
            newEntry.RevokedAt.Should().BeNull("new refresh must be active");
        }
    }

    [Fact]
    public async Task Login_MultiMembership_RemovedMembership_BetweenLoginAndSelect_Forbidden()
    {
    using var client = CreateManualCookieClient();
        var email = $"mm3-{Guid.NewGuid():N}@example.com";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        Guid secondTenantId; string secondSlug;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            secondSlug = $"sec-{Guid.NewGuid():N}".Substring(0, 16);
            var t2 = new Tenant { Id = Guid.NewGuid(), Name = secondSlug, CreatedAt = DateTime.UtcNow };
            db.Add(t2); await db.SaveChangesAsync();
            secondTenantId = t2.Id;
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = secondTenantId, UserId = userId, Roles = Roles.Creator, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

    var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
    login.EnsureSuccessStatusCode();
    var loginDoc = await login.Content.ReadFromJsonAsync<JsonObject>();
    var (refreshCookie, refresh) = ExtractRefreshCookie(login);

        // Remove the second membership before attempting selection
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var membership = await db.Memberships.FirstOrDefaultAsync(m => m.TenantId == secondTenantId);
            if (membership != null)
            {
                db.Memberships.Remove(membership);
                await db.SaveChangesAsync();
            }
        }

        var selectReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/select-tenant")
        {
            Content = JsonContent.Create(new { tenant = secondTenantId.ToString(), refreshToken = refresh })
        };
        selectReq.Headers.Add("Cookie", refreshCookie);
        var select = await client.SendAsync(selectReq);
        select.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Converts a roles bitmask into the expected string labels using the same ordering as the API.
    /// </summary>
    private static string[] ExpectedRoleLabels(int rolesBitmask)
    {
        if (rolesBitmask == 0) return Array.Empty<string>();
        var rolesEnum = (Roles)rolesBitmask;
        var labels = new List<string>(4);
        foreach (var role in Enum.GetValues<Roles>())
        {
            if (role == Roles.None) continue;
            if (rolesEnum.HasFlag(role))
            {
                labels.Add(role.ToString());
            }
        }
        return labels.ToArray();
    }
}
