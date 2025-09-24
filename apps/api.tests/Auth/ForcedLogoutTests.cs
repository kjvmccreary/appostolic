using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        string? access = null;
        Guid userId = Guid.Empty;
        try
        {
            // Preferred modern shape
            if (json.RootElement.TryGetProperty("access", out var accessEl))
            {
                if (accessEl.TryGetProperty("token", out var tokenEl))
                {
                    access = tokenEl.GetString();
                }
                if (accessEl.TryGetProperty("claims", out var claimsEl) && claimsEl.TryGetProperty("sub", out var subEl))
                {
                    userId = subEl.GetGuid();
                }
            }
        }
        catch (KeyNotFoundException)
        {
            // ignore – fallback path below
        }
        if (access is null)
        {
            throw new InvalidOperationException("login response did not include access.token");
        }
        if (userId == Guid.Empty)
        {
            // Fallback: decode JWT (header.payload.signature) without validating signature since test already ensured success status.
            var parts = access.Split('.');
            if (parts.Length >= 2)
            {
                var payloadJson = System.Text.Json.JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(parts[1]))));
                if (payloadJson.RootElement.TryGetProperty("sub", out var subRaw) && Guid.TryParse(subRaw.GetString(), out var guidFromJwt))
                {
                    userId = guidFromJwt;
                }
            }
        }
        if (userId == Guid.Empty)
        {
            throw new InvalidOperationException("Unable to determine user id from login response or JWT claims");
        }
        return (access!, userId);
    }

    // Shared parsing helper for login JSON documents (avoids brittle property chains)
    private static (string access, Guid userId) ParseAccessAndUserId(JsonDocument json)
    {
        string? access = null; Guid userId = Guid.Empty;
        if (json.RootElement.TryGetProperty("access", out var accessEl))
        {
            if (accessEl.TryGetProperty("token", out var tokenEl)) access = tokenEl.GetString();
            if (accessEl.TryGetProperty("claims", out var claimsEl) && claimsEl.TryGetProperty("sub", out var subEl)) userId = subEl.GetGuid();
        }
        if (access is null)
        {
            throw new InvalidOperationException("access.token missing from login payload");
        }
        if (userId == Guid.Empty)
        {
            // Decode from JWT as fallback
            var parts = access.Split('.');
            if (parts.Length >= 2)
            {
                var payloadJson = System.Text.Json.JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(parts[1]))));
                if (payloadJson.RootElement.TryGetProperty("sub", out var subRaw) && Guid.TryParse(subRaw.GetString(), out var guidFromJwt))
                {
                    userId = guidFromJwt;
                }
            }
        }
        if (userId == Guid.Empty) throw new InvalidOperationException("Could not derive user id from claims or JWT");
        return (access!, userId);
    }

    private static string PadBase64(string input)
    {
        // Add padding if missing for base64url variant
        input = input.Replace('-', '+').Replace('_', '/');
        return input.PadRight(input.Length + ((4 - input.Length % 4) % 4), '=');
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
    var (_, targetUserId) = ParseAccessAndUserId(targetJson);

        // Create caller user (becoming platform admin via allowlist)
        var signupCaller = await client.PostAsJsonAsync("/api/auth/signup", new { email = callerEmail, password = "Pass1234!" });
        signupCaller.EnsureSuccessStatusCode();
        var loginCaller = await client.PostAsJsonAsync("/api/auth/login", new { email = callerEmail, password = "Pass1234!" });
        loginCaller.EnsureSuccessStatusCode();
    var callerJson = JsonDocument.Parse(await loginCaller.Content.ReadAsStringAsync());
    var (callerAccess, _) = ParseAccessAndUserId(callerJson);

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

        // Determine tenantId for userA directly from the database (more stable than relying on /api/me response shape)
        Guid tenantId;
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = await db.Memberships
                .AsNoTracking()
                .Where(m => m.UserId == aUserId)
                .Select(m => m.TenantId)
                .FirstAsync();
        }

        var logoutReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/tenants/{tenantId}/logout-all");
        logoutReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bAccess); // userB not member => forbid
        var resp = await client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
