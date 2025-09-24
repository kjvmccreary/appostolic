using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.Application.Auth;

/// <summary>
/// Caching abstraction for per-user TokenVersion values to reduce repetitive database lookups
/// during JWT bearer validation (Story 10). Provides opt-in in-memory caching with short TTL and
/// explicit invalidation hooks used by revocation endpoints (logout-all / forced logout / password change).
/// </summary>
public interface ITokenVersionCache
{
    /// <summary>Try get a cached token version for a user. Returns false on miss or expired entry.</summary>
    bool TryGet(Guid userId, out int version);

    /// <summary>Store or update the cached token version for a user.</summary>
    void Set(Guid userId, int version);

    /// <summary>Invalidate a single user's cached entry (e.g., after TokenVersion bump).</summary>
    void Invalidate(Guid userId);

    /// <summary>Invalidate many user entries (bulk tenant forced logout path).</summary>
    void InvalidateMany(IEnumerable<Guid> userIds);
}

/// <summary>
/// Configuration options for TokenVersion cache.
/// </summary>
public sealed class TokenVersionCacheOptions
{
    public bool Enabled { get; set; } = true;
    public int TtlSeconds { get; set; } = 30; // short TTL balances freshness vs DB load.
    public int MaxEntries { get; set; } = 25_000; // soft cap; not enforced strictly yet.
}

internal sealed class InMemoryTokenVersionCache : ITokenVersionCache
{
    private readonly ConcurrentDictionary<Guid, CacheEntry> _entries = new();
    private readonly TokenVersionCacheOptions _options;
    private readonly TimeProvider _timeProvider;

    private record CacheEntry(int Version, DateTimeOffset ExpiresAt);

    public InMemoryTokenVersionCache(IOptions<TokenVersionCacheOptions> options, TimeProvider? timeProvider = null)
    {
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryGet(Guid userId, out int version)
    {
        version = 0;
        if (!_options.Enabled) return false;
        if (_entries.TryGetValue(userId, out var entry))
        {
            if (entry.ExpiresAt > _timeProvider.GetUtcNow())
            {
                version = entry.Version;
                return true;
            }
            _entries.TryRemove(userId, out _); // expired
        }
        return false;
    }

    public void Set(Guid userId, int version)
    {
        if (!_options.Enabled) return;
        var now = _timeProvider.GetUtcNow();
        var entry = new CacheEntry(version, now.AddSeconds(_options.TtlSeconds));
        _entries[userId] = entry;
        if (_entries.Count > _options.MaxEntries)
        {
            // Simple trimming heuristic: remove expired entries; future enhancement could random-sample.
            foreach (var kvp in _entries.ToArray())
            {
                if (kvp.Value.ExpiresAt <= now)
                    _entries.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Invalidate(Guid userId)
    {
        _entries.TryRemove(userId, out _);
    }

    public void InvalidateMany(IEnumerable<Guid> userIds)
    {
        foreach (var id in userIds)
            _entries.TryRemove(id, out _);
    }
}
