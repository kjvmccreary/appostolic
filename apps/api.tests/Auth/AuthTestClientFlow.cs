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
        var (neutral, loginJson) = await LoginNeutralAsync(factory, client, email);
        // Story 2: plaintext refresh token retired; helper attempts to read it (for backward compat) but
        // falls back to relying on the httpOnly cookie when omitted.
        string? refresh = null;
        if (loginJson?["refresh"] is JsonObject refreshObj && refreshObj.TryGetPropertyValue("token", out var tokenNode) && tokenNode is JsonValue tv && tv.TryGetValue<string>(out var tokenStr) && !string.IsNullOrWhiteSpace(tokenStr))
        {
            refresh = tokenStr;
        }
        var sel = refresh is not null
            ? await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = tenantSlugOrId, refreshToken = refresh })
            : await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = tenantSlugOrId });
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
