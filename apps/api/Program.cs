using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var configuration = builder.Configuration;

// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = GetConnectionString(configuration);
    options.UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema: "app"));
});

var app = builder.Build();

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
public class AppDbContext : DbContext
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
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            b.HasIndex(x => new { x.TenantId, x.Status });
        });
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
