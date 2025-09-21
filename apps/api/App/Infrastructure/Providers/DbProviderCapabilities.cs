using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Appostolic.Api.Infrastructure.Providers;

/// <summary>
/// Centralized helper/extension methods for detecting database provider capabilities.
/// This prevents scattered ad-hoc checks (e.g., ProviderName.Contains("InMemory")) and keeps
/// behavioral fallbacks (like skipping explicit transactions) consistent across the codebase.
/// </summary>
public static class DbProviderCapabilities
{
    /// <summary>
    /// Returns true when the underlying provider supports explicit relational transactions that we rely on
    /// for RLS tenant context setup or atomic multi-statement operations. EF InMemory and other non-relational
    /// providers return false, in which case calling code should perform a best-effort sequential set of operations
    /// without wrapping them in an explicit transaction.
    /// </summary>
    public static bool SupportsExplicitTransactions(this DatabaseFacade db)
    {
        // We rely on relational semantics (BEGIN/COMMIT) and provider transaction enforcement. EF InMemory provider
        // advertises itself as non-relational via IsRelational()==false, but we double-guard with ProviderName check in case
        // tests swap providers. Additional providers that are non-relational (Cosmos, etc.) will also be filtered by IsRelational.
        if (!db.IsRelational()) return false;
        var provider = db.ProviderName ?? string.Empty;
        if (provider.Contains("InMemory", System.StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
