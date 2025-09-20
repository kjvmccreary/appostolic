using Microsoft.EntityFrameworkCore;
using Appostolic.Api.Domain.Agents;
using Appostolic.Api.Domain.Notifications;

public partial class AppDbContext : DbContext
{
    // DbSets for Agent runtime
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();
    public DbSet<AgentTrace> AgentTraces => Set<AgentTrace>();

    // Notifications outbox
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDedupe> NotificationDedupes => Set<NotificationDedupe>();

    /// <summary>
    /// Persisted refresh tokens for neutral (and future tenant) JWT flows.
    /// Mapped in Program.cs partial OnModelCreating.
    /// </summary>
    public DbSet<Appostolic.Api.Infrastructure.Auth.Jwt.RefreshToken> RefreshTokens => Set<Appostolic.Api.Infrastructure.Auth.Jwt.RefreshToken>();

}
