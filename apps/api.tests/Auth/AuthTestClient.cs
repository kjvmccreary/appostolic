using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Appostolic.Api.AuthTests;

/// <summary>
/// Thin facade over <see cref="TestAuthClient"/> providing convenience methods that mint
/// tokens then automatically set the Authorization header on a supplied HttpClient.
/// This reduces repeated boilerplate during Dev Header decommission migrations.
/// </summary>
public static class AuthTestClient
{
    /// <summary>
    /// Mint a neutral access token for the given email and attach as Bearer auth header.
    /// Returns the raw token for additional assertions if needed.
    /// </summary>
    public static async Task<string> UseNeutralAsync(HttpClient client, string email, bool? forceAllRoles = null)
    {
        var helper = new TestAuthClient(client);
        // When forceAllRoles is explicitly false we suppress automatic role elevation so tests
        // can exercise least-privilege or negative authorization scenarios.
        var (neutral, _) = await helper.MintAsync(email, tenant: null, autoTenant: false, forceAllRoles: forceAllRoles);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", neutral);
        return neutral;
    }

    /// <summary>
    /// Mint tokens using auto-tenant selection (first or single membership) and attach tenant token if produced otherwise neutral.
    /// Returns the tuple (neutral, tenant?) where tenant may be null if auto selection not possible.
    /// </summary>
    public static async Task<(string neutral, string? tenant)> UseAutoTenantAsync(HttpClient client, string email)
    {
        var helper = new TestAuthClient(client);
        var (neutral, tenant) = await helper.MintAsync(email, tenant: null, autoTenant: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenant ?? neutral);
        return (neutral, tenant);
    }

    /// <summary>
    /// Exposes the detailed mint helper so tests can override lifetimes and inspect expiry metadata for advanced scenarios.
    /// </summary>
    public static Task<MintDetailedResult> MintDetailedAsync(
        HttpClient client,
        string email,
        string? tenant = null,
        bool autoTenant = false,
        bool superAdmin = false,
        bool? forceAllRoles = null,
        int? neutralAccessTtlMinutes = null,
        int? tenantAccessTtlMinutes = null,
        int? refreshTtlMinutes = null)
    {
        var helper = new TestAuthClient(client);
        return helper.MintDetailedAsync(email, tenant, autoTenant, superAdmin, forceAllRoles, neutralAccessTtlMinutes, tenantAccessTtlMinutes, refreshTtlMinutes);
    }

}
