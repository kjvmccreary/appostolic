using Microsoft.Extensions.Logging;

namespace Appostolic.Api.App.Notifications;

public interface IEmailDedupeStore
{
    bool TryMark(string key, TimeSpan ttl);
}

public sealed class InMemoryEmailDedupeStore : IEmailDedupeStore
{
    private readonly ILogger<InMemoryEmailDedupeStore> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);

    public InMemoryEmailDedupeStore(ILogger<InMemoryEmailDedupeStore> logger)
    {
        _logger = logger;
    }

    public bool TryMark(string key, TimeSpan ttl)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now + ttl;

        lock (_gate)
        {
            if (_seen.TryGetValue(key, out var until) && until > now)
            {
                return false; // already seen within ttl
            }
            _seen[key] = expires;

            // opportunistic cleanup
            if (_seen.Count > 1024)
            {
                foreach (var k in _seen.Where(kvp => kvp.Value <= now).Select(kvp => kvp.Key).ToList())
                {
                    _seen.Remove(k);
                }
            }

            return true;
        }
    }
}
