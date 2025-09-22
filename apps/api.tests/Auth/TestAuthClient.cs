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

    public async Task<(string neutralAccess, string? tenantAccess)> MintAsync(string email, string? tenant = null, bool autoTenant = false, bool superAdmin = false, bool? forceAllRoles = null)
    {
        var force = forceAllRoles ?? (tenant != null || autoTenant); // default behavior preserved unless explicitly overridden
        var payload = new { Email = email, Tenant = tenant, AutoTenant = autoTenant, ForceAllRoles = force, SuperAdmin = superAdmin };
        var res = await _client.PostAsJsonAsync("/api/test/mint-tenant-token", payload);
        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Mint endpoint failed: {(int)res.StatusCode} {res.ReasonPhrase}");
        }
        var json = await res.Content.ReadFromJsonAsync<JsonObject>();
        if (json is null) throw new InvalidOperationException("Mint endpoint returned null JSON");
        var neutral = json["access"]! ["token"]!.GetValue<string>();
        string? tenantAccess = null;
        if (json["tenantToken"] is JsonObject tt && tt["access"] is JsonObject ta && ta["token"] is JsonNode tn)
        {
            tenantAccess = tn.GetValue<string>();
        }
        return (neutral, tenantAccess);
    }
}
