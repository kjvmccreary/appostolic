using Microsoft.EntityFrameworkCore;
using Appostolic.Api.Domain.Agents;

public partial class AppDbContext : DbContext
{
    // DbSets for Agent runtime
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();
    public DbSet<AgentTrace> AgentTraces => Set<AgentTrace>();
}
