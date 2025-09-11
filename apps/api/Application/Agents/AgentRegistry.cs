using System.Linq;
using Appostolic.Api.Domain.Agents;

namespace Appostolic.Api.Application.Agents;

/// <summary>
/// Read-only registry for v1 agent runtime.
///
/// Sprint S1-09 intentionally uses fixtures (in-memory, deterministic agents) as the
/// single source of truth to enable fast iteration and predictable behavior while the
/// authoring UI and storage model are being designed (planned S1-10).
///
/// These IDs are stable GUIDs so downstream systems can reference agents by ID now and
/// migrate seamlessly to database-backed authoring later.
/// </summary>
public static class AgentRegistry
{
    /// <summary>Deterministic ID for the ResearchAgent.</summary>
    public static readonly Guid ResearchAgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Deterministic ID for the FilesystemAgent.</summary>
    public static readonly Guid FilesystemAgentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly IReadOnlyList<Agent> _all = new List<Agent>
    {
        new Agent(
            id: ResearchAgentId,
            name: "ResearchAgent",
            systemPrompt: "You are a concise researcher. Prefer reputable sources and include brief citations. Before answering, propose a short 2-step plan and await confirmation if unclear.",
            toolAllowlist: new[] { "web.search" },
            model: "gpt-4o-mini",
            temperature: 0.2,
            maxSteps: 8
        ),
        new Agent(
            id: FilesystemAgentId,
            name: "FilesystemAgent",
            systemPrompt: "Summarize the input succinctly and write a note to a file beneath the configured safe root. Never escape the root. Confirm write location before persisting.",
            toolAllowlist: new[] { "fs.write" },
            model: "gpt-4o-mini",
            temperature: 0.1,
            maxSteps: 4
        )
    };

    /// <summary>
    /// All available v1 agents. This list is immutable for S1-09.
    /// </summary>
    public static IReadOnlyList<Agent> All => _all;

    /// <summary>
    /// Find an agent by its stable ID.
    /// </summary>
    public static Agent? FindById(Guid id) => _all.FirstOrDefault(a => a.Id == id);
}
