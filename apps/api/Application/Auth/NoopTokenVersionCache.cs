using System;

namespace Appostolic.Api.Application.Auth;

/// <summary>
/// Story 14: Emergency revert no-op TokenVersion cache. Always returns miss; ensures DB lookup path is used every time.
/// </summary>
public sealed class NoopTokenVersionCache : ITokenVersionCache
{
    public bool TryGet(Guid userId, out int version)
    {
        version = default;
        return false;
    }
    public void Set(Guid userId, int version) { /* no-op */ }
    public void Invalidate(Guid userId) { /* no-op */ }
}
