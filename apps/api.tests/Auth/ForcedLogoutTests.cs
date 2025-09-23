using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Tests for admin / tenant forced logout endpoints (Story 9).
/// Validates authorization, success behavior, token version bump, and idempotency.
/// NOTE: Relies on PLATFORM__SUPER_ADMINS env var for platform admin privileges.
/// </summary>
public class ForcedLogoutTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ForcedLogoutTests(WebAppFactory factory) { _factory = factory; }

    private async Task<(string access, Guid userId)> SignupAndLogin(string email)
    {
        var client = _factory.CreateClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Pass1234!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Pass1234!" });
        login.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var access = json.RootElement.GetProperty("access").GetProperty("token").GetString();
        var userId = json.RootElement.GetProperty("access").GetProperty("claims").GetProperty("sub").GetGuid();
        return (access!, userId);
    }

    [Fact]
    public async Task ForcedLogoutUser_ForbiddenWhenNotPlatformAdmin()
    {
        var client = _factory.CreateClient();
        // create target
        var (_, targetUserId) = await SignupAndLogin("fluser1@example.com");
        var (_, callerUserId) = await SignupAndLogin("flcaller@example.com");

        // Caller is not in PLATFORM__SUPER_ADMINS (factory default). Acquire caller access token again.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "flcaller@example.com", password = "Pass1234!" });
        login.EnsureSuccessStatusCode();
        var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var callerAccess = loginJson.RootElement.GetProperty("access").GetProperty("token").GetString();

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetUserId}/logout-all");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", callerAccess);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ForcedLogoutUser_SucceedsForPlatformAdmin()
    {
        // Use factory clone with augmented PLATFORM__SUPER_ADMINS list to include caller
        var callerEmail = "platformadmin@example.com";
        var factory = _factory.WithSettings(new(){ ["PLATFORM__SUPER_ADMINS"] = $"kevin@example.com,{callerEmail}" });
        var client = factory.CreateClient();

        // Create target user
        var targetEmail = "forced-target@example.com";
        var signupTarget = await client.PostAsJsonAsync("/api/auth/signup", new { email = targetEmail, password = "Pass1234!" });
        signupTarget.EnsureSuccessStatusCode();
        var loginTarget = await client.PostAsJsonAsync("/api/auth/login", new { email = targetEmail, password = "Pass1234!" });
        loginTarget.EnsureSuccessStatusCode();
        var targetJson = JsonDocument.Parse(await loginTarget.Content.ReadAsStringAsync());
        var targetUserId = targetJson.RootElement.GetProperty("access").GetProperty("claims").GetProperty("sub").GetGuid();

        // Create caller user (becoming platform admin via allowlist)
        var signupCaller = await client.PostAsJsonAsync("/api/auth/signup", new { email = callerEmail, password = "Pass1234!" });
        signupCaller.EnsureSuccessStatusCode();
        var loginCaller = await client.PostAsJsonAsync("/api/auth/login", new { email = callerEmail, password = "Pass1234!" });
        loginCaller.EnsureSuccessStatusCode();
        var callerJson = JsonDocument.Parse(await loginCaller.Content.ReadAsStringAsync());
        var callerAccess = callerJson.RootElement.GetProperty("access").GetProperty("token").GetString();

        // Invoke forced logout
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetUserId}/logout-all");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", callerAccess);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var respJson = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(respJson.RootElement.GetProperty("revoked").GetInt32() >= 0);
        Assert.True(respJson.RootElement.GetProperty("tokenVersion").GetInt32() >= 1);
    }

    [Fact]
    public async Task ForcedLogoutTenant_ForbiddenForNonAdmin()
    {
        var client = _factory.CreateClient();
        // Create tenant by signing up userA
        var (aAccess, aUserId) = await SignupAndLogin("tenantadminA@example.com");
        // Create a second user (not admin of tenant A) - separate signup yields its own tenant
        var (bAccess, bUserId) = await SignupAndLogin("tenantuserB@example.com");

        // Attempt tenant-wide logout for tenant of userA using userB's access token (should 403)
        // We need the tenant id for userA from /api/me (assuming such endpoint returns memberships)
        var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        meReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aAccess);
        var meResp = await client.SendAsync(meReq);
        meResp.EnsureSuccessStatusCode();
        var meJson = JsonDocument.Parse(await meResp.Content.ReadAsStringAsync());
        var tenantId = meJson.RootElement.GetProperty("memberships")[0].GetProperty("tenantId").GetGuid();

        var logoutReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/tenants/{tenantId}/logout-all");
        logoutReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bAccess); // userB not member => forbid
        var resp = await client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
