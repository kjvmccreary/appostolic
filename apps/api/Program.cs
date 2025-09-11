using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Appostolic.Api.Infrastructure.Auth;
using Appostolic.Api.Infrastructure.MultiTenancy;
using Appostolic.Api.App.Endpoints;
using Microsoft.OpenApi.Models;
using Appostolic.Api.Application.Agents.Tools;
using Appostolic.Api.App.Options;
using Appostolic.Api.Application.Agents.Runtime;
using Appostolic.Api.Application.Agents.Model;
using Appostolic.Api.Application.Agents.Queue;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var configuration = builder.Configuration;

// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = GetConnectionString(configuration);
    options.UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema: "app"));
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Appostolic API", Version = "v1" });

    // Dev headers as API key security scheme
    c.AddSecurityDefinition("DevHeaders", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "x-dev-user",
        Type = SecuritySchemeType.ApiKey,
        Description = "Provide dev user email in x-dev-user and tenant slug in x-tenant"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "DevHeaders"
                }
            },
            new List<string>()
        }
    });
});

// AuthN/Z
builder.Services
    .AddAuthentication(DevHeaderAuthHandler.DevScheme)
    .AddScheme<AuthenticationSchemeOptions, DevHeaderAuthHandler>(DevHeaderAuthHandler.DevScheme, _ => { });
builder.Services.AddAuthorization();

// Agent Tools
builder.Services.AddSingleton<ITool, WebSearchTool>();
builder.Services.AddSingleton<ITool, DbQueryTool>();
builder.Services.AddSingleton<ITool, FsWriteTool>();
builder.Services.AddSingleton<ToolRegistry>();

// Options
builder.Services.Configure<ToolsOptions>(builder.Configuration.GetSection("Tools"));

// Orchestration services
builder.Services.AddScoped<ITraceWriter, Appostolic.Api.Application.Agents.Runtime.TraceWriter>();
builder.Services.AddScoped<IAgentOrchestrator, Appostolic.Api.Application.Agents.Runtime.AgentOrchestrator>();
builder.Services.AddSingleton<IModelAdapter, MockModelAdapter>();
builder.Services.AddSingleton<IAgentTaskQueue, InMemoryAgentTaskQueue>();

// Remove scoped registration for TenantScopeMiddleware (not needed with UseMiddleware)
// builder.Services.AddScoped<TenantScopeMiddleware>();

var app = builder.Build();

// Swagger middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    // Ensure the UI is hosted at /swagger and points to the v1 JSON
    c.RoutePrefix = "swagger";
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Appostolic API v1");
});

// Ensure navigating to /swagger (no trailing slash) shows the UI
app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html"))
   .ExcludeFromDescription();

app.UseAuthentication();
app.UseAuthorization();

// Use conventional middleware to set tenant and manage transaction
app.UseMiddleware<TenantScopeMiddleware>();

// Middleware to set tenant from X-Tenant-Id header and set PostgreSQL local setting
app.Use(async (context, next) =>
{
    var tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(tenantId) && Guid.TryParse(tenantId, out var tid))
    {
        // Store in HttpContext for downstream usage
        context.Items["TenantId"] = tid;

        // Set the PostgreSQL local setting for RLS policies
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("SELECT app.set_tenant({0})", tid);
    }

    await next();
});

app.MapGet("/", () => Results.Ok(new { name = "appostolic-api", version = "0.0.0" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// v1 API
app.MapV1Endpoints();
app.MapDevToolsEndpoints();
app.MapDevAgentsDemoEndpoints();
app.MapAgentTasksEndpoints();

app.MapGet("/lessons", async (HttpContext ctx, AppDbContext db) =>
{
    if (!ctx.Items.TryGetValue("TenantId", out _))
        return Results.BadRequest(new { error = "Missing X-Tenant-Id header" });

    var items = await db.Lessons.AsNoTracking().ToListAsync();
    return Results.Ok(items);
});

app.MapPost("/lessons", async (HttpContext ctx, AppDbContext db, LessonCreate dto) =>
{
    if (!ctx.Items.TryGetValue("TenantId", out var v) || v is not Guid tenantId)
        return Results.BadRequest(new { error = "Missing X-Tenant-Id header" });

    var lesson = new Lesson
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Title = dto.Title,
        Status = LessonStatus.Draft,
        Audience = LessonAudience.All,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    db.Lessons.Add(lesson);
    await db.SaveChangesAsync();
    return Results.Created($"/lessons/{lesson.Id}", lesson);
});

// Auto-migrate for Development/Test
using (var scope = app.Services.CreateScope())
{
    var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
    if (env.IsDevelopment() || env.IsEnvironment("Test"))
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
    }
}

app.Run();

static string GetConnectionString(ConfigurationManager configuration)
{
    var host = configuration["POSTGRES_HOST"] ?? "localhost";
    var port = configuration["POSTGRES_PORT"] ?? "55432";
    var db = configuration["POSTGRES_DB"] ?? "appdb";
    var user = configuration["POSTGRES_USER"] ?? "appuser";
    var pass = configuration["POSTGRES_PASSWORD"] ?? "apppassword";

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = host,
        Port = int.TryParse(port, out var p) ? p : 55432,
        Database = db,
        Username = user,
        Password = pass,
        // Important for SSL-less local dev
        SslMode = SslMode.Disable
    };
    return builder.ConnectionString;
}

// DTOs
public record LessonCreate(string Title);

// EF Core DbContext and Entities
public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("app");

        // Basic minimal model to anchor migrations; real entities can be added incrementally.
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Name).HasColumnName("name").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Email).HasColumnName("email").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            b.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Membership>(b =>
        {
            b.ToTable("memberships");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.Role).HasColumnName("role");
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<Lesson>(b =>
        {
            b.ToTable("lessons");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.Title).HasColumnName("title");
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.Audience).HasColumnName("audience");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.TenantId, x.Status });
        });

        // Apply configurations for domain types (Agent runtime)
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
}

public record Tenant
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public record User
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public enum MembershipRole { Owner, Admin, Editor, Viewer }
public enum MembershipStatus { Active, Invited, Suspended, Revoked }

public record Membership
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public MembershipRole Role { get; init; }
    public MembershipStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
}

public enum LessonStatus { Draft, Published, Archived }
public enum LessonAudience { All, Adults, Children, Youth }

public record Lesson
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Title { get; init; } = string.Empty;
    public LessonStatus Status { get; init; }
    public LessonAudience Audience { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
