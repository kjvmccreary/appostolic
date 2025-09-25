using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Appostolic.Api.AuthTests;

/// <summary>
/// Test helper client for minting tokens via the gated /api/test/mint-tenant-token endpoint (Story 2a).
/// Provides ergonomic methods to obtain neutral and tenant-scoped access tokens without exercising full auth flow.
/// </summary>
public class TestAuthClient
{
    private readonly HttpClient _client;
    public TestAuthClient(HttpClient client) => _client = client;

    public async Task<(string neutralAccess, string? tenantAccess)> MintAsync(
        string email,
        string? tenant = null,
        bool autoTenant = false,
        bool superAdmin = false,
        bool? forceAllRoles = null,
        int? neutralAccessTtlMinutes = null,
        int? tenantAccessTtlMinutes = null,
        int? refreshTtlMinutes = null)
    {
        var detailed = await MintDetailedAsync(email, tenant, autoTenant, superAdmin, forceAllRoles, neutralAccessTtlMinutes, tenantAccessTtlMinutes, refreshTtlMinutes);
        return (detailed.Neutral.Token, detailed.Tenant?.Token);
    }

    // Issues a tenant token via the mint helper and returns parsed token metadata for assertions.
    public async Task<MintDetailedResult> MintDetailedAsync(
        string email,
        string? tenant = null,
        bool autoTenant = false,
        bool superAdmin = false,
        bool? forceAllRoles = null,
        int? neutralAccessTtlMinutes = null,
        int? tenantAccessTtlMinutes = null,
        int? refreshTtlMinutes = null)
    {
        var force = forceAllRoles ?? (tenant != null || autoTenant); // default behavior preserved unless explicitly overridden
        var payload = new
        {
            Email = email,
            Tenant = tenant,
            AutoTenant = autoTenant,
            ForceAllRoles = force,
            SuperAdmin = superAdmin,
            NeutralAccessTtlMinutes = neutralAccessTtlMinutes,
            TenantAccessTtlMinutes = tenantAccessTtlMinutes,
            RefreshTtlMinutes = refreshTtlMinutes
        };
        var res = await _client.PostAsJsonAsync("/api/test/mint-tenant-token", payload);
        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Mint endpoint failed: {(int)res.StatusCode} {res.ReasonPhrase}");
        }
        var json = await res.Content.ReadFromJsonAsync<JsonObject>();
        if (json is null) throw new InvalidOperationException("Mint endpoint returned null JSON");

        var accessObj = json["access"]?.AsObject() ?? throw new InvalidOperationException("Mint endpoint missing access payload");
        var neutral = ParseToken(accessObj);

        MintTokenResult? tenantToken = null;
        if (json["tenantToken"] is JsonObject tenantObj && tenantObj["access"] is JsonObject tenantAccessObj)
        {
            tenantToken = ParseToken(tenantAccessObj);
        }

        var refreshObj = json["refresh"]?.AsObject() ?? throw new InvalidOperationException("Mint endpoint missing refresh payload");
        if (refreshObj["token"] is null)
        {
            throw new InvalidOperationException("Mint endpoint did not return plaintext refresh token. Ensure AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=true for tests.");
        }
        var refresh = ParseToken(refreshObj);

        var memberships = json["memberships"]?.AsArray() ?? new JsonArray();

        return new MintDetailedResult(neutral, tenantToken, refresh, memberships, json);
    }

    // Extracts standardized token metadata (raw token, ID, and expiry) from the helper response payload.
    private static MintTokenResult ParseToken(JsonObject obj)
    {
        var token = obj["token"]?.GetValue<string>() ?? throw new InvalidOperationException("Token payload missing 'token' value");
        var expiresRaw = obj["expiresAt"]?.GetValue<string>() ?? throw new InvalidOperationException("Token payload missing 'expiresAt'");
        var expires = DateTimeOffset.Parse(expiresRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var type = obj["type"]?.GetValue<string>() ?? "neutral";
        return new MintTokenResult(token, expires, type, obj);
    }
}

public sealed record MintTokenResult(string Token, DateTimeOffset ExpiresAt, string Type, JsonObject Raw);

public sealed record MintDetailedResult(MintTokenResult Neutral, MintTokenResult? Tenant, MintTokenResult Refresh, JsonArray Memberships, JsonObject RawResponse);
