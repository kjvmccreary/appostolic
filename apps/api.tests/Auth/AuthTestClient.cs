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
    public static async Task<string> UseNeutralAsync(HttpClient client, string email)
    {
        var helper = new TestAuthClient(client);
        var (neutral, _) = await helper.MintAsync(email, tenant: null, autoTenant: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", neutral);
        return neutral;
    }

    /// <summary>
    /// Mint both neutral and tenant access tokens (explicit tenant slug) and attach the tenant token.
    /// Returns the tuple (neutral, tenant) for test assertions.
    /// </summary>
    public static async Task<(string neutral, string tenant)> UseTenantAsync(HttpClient client, string email, string tenantSlug, bool? forceAllRoles = null)
    {
        var helper = new TestAuthClient(client);
        var (neutral, tenant) = await helper.MintAsync(email, tenantSlug, autoTenant: false, forceAllRoles: forceAllRoles);
        if (tenant is null)
            throw new InvalidOperationException($"Expected tenant token for slug '{tenantSlug}' but helper returned null.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenant);
        return (neutral, tenant);
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
    /// Mint a neutral (or tenant if provided) token elevating to superadmin for cross-tenant tests.
    /// Returns (neutral, tenant?) identical to other helpers.
    /// </summary>
    public static async Task<(string neutral, string? tenant)> UseSuperAdminAsync(HttpClient client, string email, string? tenantSlug = null)
    {
        var helper = new TestAuthClient(client);
        var (neutral, tenant) = await helper.MintAsync(email, tenantSlug, autoTenant: tenantSlug == null, superAdmin: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenant ?? neutral);
        return (neutral, tenant);
    }
}
