using System.Collections.Concurrent;

namespace Appostolic.Api.Application.Agents.Tools;

/// <summary>
/// In-memory registry for tools keyed by canonical name (case-insensitive).
/// </summary>
public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _byName;

    /// <summary>
    /// Create a registry from discovered tools via DI.
    /// </summary>
    public ToolRegistry(IEnumerable<ITool> tools)
    {
        _byName = new ConcurrentDictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tools ?? Array.Empty<ITool>())
        {
            if (string.IsNullOrWhiteSpace(t.Name)) continue;
            _byName[t.Name] = t; // last wins if duplicates
        }
    }

    /// <summary>
    /// Try to resolve a tool by name (case-insensitive).
    /// </summary>
    public bool TryGet(string name, out ITool? tool)
    {
        tool = null;
        if (string.IsNullOrWhiteSpace(name)) return false;
        return _byName.TryGetValue(name, out tool);
    }

    /// <summary>
    /// Snapshot list of registered tool names (sorted, case-insensitive).
    /// </summary>
    public IReadOnlyList<string> ListNames()
        => _byName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
}
