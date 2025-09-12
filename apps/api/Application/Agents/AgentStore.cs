using Appostolic.Api.Domain.Agents;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.Application.Agents;

/// <summary>
/// AgentStore resolves agent definitions, preferring the database and falling back to the static AgentRegistry.
/// </summary>
public sealed class AgentStore
{
    private readonly AppDbContext _db;

    public AgentStore(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// DB-first agent lookup by primary key; if not found, falls back to AgentRegistry.FindById.
    /// </summary>
    public async Task<Agent?> GetAsync(Guid id, CancellationToken ct)
    {
        var fromDb = await _db.Set<Agent>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (fromDb is not null) return fromDb;
        return AgentRegistry.FindById(id);
    }

    /// <summary>
    /// List agents from the database (paged, newest first).
    /// </summary>
    public async Task<IReadOnlyList<Agent>> ListAsync(int take = 50, int skip = 0, CancellationToken ct = default)
    {
        if (take <= 0) take = 50;
        if (skip < 0) skip = 0;
        return await _db.Set<Agent>()
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }
}
