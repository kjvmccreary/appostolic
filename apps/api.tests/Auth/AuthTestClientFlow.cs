using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.Application.Auth;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Appostolic.Api.AuthTests;

/// <summary>
/// Flow-based auth test helper exercising real /api/auth/login + /api/auth/select-tenant endpoints
/// instead of the test mint helper. This is used during migration off dev headers (Story 2 Phase A)
/// so tests validate the production auth pipeline (password verify, memberships projection, refresh rotation).
/// </summary>
public static class AuthTestClientFlow
{
    private const string DefaultPassword = "Password123!"; // Matches seeded test usages

    /// <summary>
    /// Ensure the specified user exists with the default password (idempotent) and perform a password login.
    /// Returns the raw neutral access token plus the parsed login JSON (memberships, refresh, optional tenantToken).
    /// </summary>
    public static async Task<(string access, JsonObject json)> LoginNeutralAsync(WebApplicationFactory<Program> factory, HttpClient client, string email)
    {
        await EnsureUserAsync(factory, email, DefaultPassword);
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DefaultPassword });
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>() ?? throw new InvalidOperationException("login returned null json");
        var access = json["access"]!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return (access, json);
    }

    /// <summary>
    /// Login neutrally then select a tenant (by slug or id) returning the tenant-scoped access token and JSON body.
    /// Requires the user to have membership; will throw if selection fails.
    /// </summary>
    public static async Task<(string neutral, string tenant, JsonObject selectJson)> LoginAndSelectTenantAsync(WebApplicationFactory<Program> factory, HttpClient client, string email, string tenantSlugOrId)
    {
        // Re-implement the neutral login locally so we can inspect headers (cookies) before returning.
        await EnsureUserAsync(factory, email, DefaultPassword);
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DefaultPassword });
        loginResp.EnsureSuccessStatusCode();
        var loginJson = await loginResp.Content.ReadFromJsonAsync<JsonObject>() ?? throw new InvalidOperationException("login returned null json");
        var neutral = loginJson["access"]!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", neutral);

        // Prefer plaintext refresh token in body when exposed; otherwise parse cookie header.
        string? refresh = null;
        if (loginJson?["refresh"] is JsonObject refreshObj && refreshObj.TryGetPropertyValue("token", out var tokenNode) && tokenNode is JsonValue tv && tv.TryGetValue<string>(out var tokenStr) && !string.IsNullOrWhiteSpace(tokenStr))
        {
            refresh = tokenStr;
        }
        if (refresh is null && loginResp.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            foreach (var c in cookieHeaders)
            {
                // Cookie rename: refresh= (legacy) -> rt (current). Support both for transitional test stability.
                // We intentionally do NOT break if we see legacy first; prefer the current 'rt' name.
                var parts = c.Split(';', 2);
                if (parts.Length > 0)
                {
                    var first = parts[0].TrimStart();
                    string? value = null;
                    if (first.StartsWith("rt="))
                    {
                        value = first.Substring("rt=".Length);
                    }
                    else if (first.StartsWith("refresh="))
                    {
                        value = first.Substring("refresh=".Length);
                    }
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        refresh = value;
                        // Prefer modern cookie; if legacy encountered first keep scanning for rt
                        if (first.StartsWith("rt=")) break;
                    }
                }
            }
        }

        HttpResponseMessage sel;
        if (!string.IsNullOrWhiteSpace(refresh))
        {
            sel = await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = tenantSlugOrId, refreshToken = refresh });
        }
        else
        {
            // Fallback: attempt select-tenant relying on cookie (works when test host configured with cookie container).
            sel = await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = tenantSlugOrId });
        }
        if (!sel.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"select-tenant failed: {(int)sel.StatusCode} {sel.ReasonPhrase}");
        }
        var selJson = await sel.Content.ReadFromJsonAsync<JsonObject>() ?? throw new InvalidOperationException("select-tenant returned null json");
        var tenantAccess = selJson["access"]!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantAccess);
        return (neutral, tenantAccess, selJson);
    }

    /// <summary>
    /// Idempotently ensures a user row exists with a usable password hash for login flow tests.
    /// Does NOT create any memberships; caller may add as needed for tenant selection scenarios.
    /// </summary>
    private static async Task EnsureUserAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = db.Users.FirstOrDefault(u => u.Email == email);
        if (existing is { PasswordHash: not null, PasswordSalt: not null }) return;
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var (hash, salt, iterations) = hasher.HashPassword(password);
        if (existing == null)
        {
            var user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow, PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow };
            db.Users.Add(user);
        }
        else
        {
            // Use record 'with' pattern to update init-only props then apply modifications via EF tracking entry
            var updated = existing with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow };
            db.Entry(existing).CurrentValues.SetValues(updated);
        }
        await db.SaveChangesAsync();
    }
}
