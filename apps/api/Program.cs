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
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
using System.Diagnostics;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using Appostolic.Api.App.Notifications;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var configuration = builder.Configuration;

// SendGrid API key compatibility shim and production guard
// 1) Compatibility: allow legacy env var SENDGRID_API_KEY to populate SendGrid:ApiKey
// 2) Guard: in Production, if Email:Provider=sendgrid then require SendGrid:ApiKey
{
    var compatKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
    var configuredKey = builder.Configuration["SendGrid:ApiKey"];
    if (!string.IsNullOrWhiteSpace(compatKey) && string.IsNullOrWhiteSpace(configuredKey))
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SendGrid:ApiKey"] = compatKey
        });
    }

    var provider = builder.Configuration["Email:Provider"]; // expected values: smtp | sendgrid
    if (builder.Environment.IsProduction() && string.Equals(provider, "sendgrid", StringComparison.OrdinalIgnoreCase))
    {
        var apiKey = builder.Configuration["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Email:Provider=sendgrid but SendGrid:ApiKey is not configured. Set environment variable SendGrid__ApiKey or configure SendGrid:ApiKey.");
        }
    }
}

// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = Appostolic.Api.Infrastructure.Database.ConnectionStringHelper.BuildFromEnvironment(configuration);
    options.UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema: "app"));
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Appostolic API", Version = "v1" });

    // Use fully-qualified type names for schema Ids to avoid collisions between same-named records/classes
    // e.g., multiple InviteRequest records used in different endpoint groups
    c.CustomSchemaIds(type => type.FullName?.Replace('+', '.') ?? type.Name);

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

    // Include XML comments generated from triple-slash docs (///) in Swagger descriptions
    try
    {
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
        }
    }
    catch { /* best-effort; ignore file resolution errors */ }
});

// AuthN/Z
builder.Services
    .AddAuthentication(DevHeaderAuthHandler.DevScheme)
    .AddScheme<AuthenticationSchemeOptions, DevHeaderAuthHandler>(DevHeaderAuthHandler.DevScheme, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuthorizationHandler, Appostolic.Api.Infrastructure.Auth.RoleAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, Appostolic.Api.Infrastructure.Auth.ProblemDetailsAuthorizationResultHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TenantAdmin", p => p.AddRequirements(new Appostolic.Api.Infrastructure.Auth.RoleRequirement(Roles.TenantAdmin)));
    options.AddPolicy("Approver",     p => p.AddRequirements(new Appostolic.Api.Infrastructure.Auth.RoleRequirement(Roles.Approver)));
    options.AddPolicy("Creator",      p => p.AddRequirements(new Appostolic.Api.Infrastructure.Auth.RoleRequirement(Roles.Creator)));
    options.AddPolicy("Learner",      p => p.AddRequirements(new Appostolic.Api.Infrastructure.Auth.RoleRequirement(Roles.Learner)));
});

// Agent Tools
builder.Services.AddSingleton<ITool, WebSearchTool>();
builder.Services.AddSingleton<ITool, DbQueryTool>();
builder.Services.AddSingleton<ITool, FsWriteTool>();
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddScoped<Appostolic.Api.Application.Agents.AgentStore>();

// Options
builder.Services.Configure<ToolsOptions>(builder.Configuration.GetSection("Tools"));
builder.Services.Configure<ModelPricingOptions>(builder.Configuration.GetSection("ModelPricing"));
builder.Services.Configure<Appostolic.Api.App.Options.EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<Appostolic.Api.App.Options.SendGridOptions>(builder.Configuration.GetSection("SendGrid"));
builder.Services.Configure<Appostolic.Api.App.Options.SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<Appostolic.Api.App.Options.NotificationOptions>(builder.Configuration.GetSection("Notifications"));
builder.Services.Configure<Appostolic.Api.App.Options.NotificationTransportOptions>(builder.Configuration.GetSection("Notifications:Transport"));
// JSON: prefer string values for enums for both input binding and output serialization
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
// Provide Development-friendly defaults for SMTP if not configured (Mailhog)
if (builder.Environment.IsDevelopment())
{
    builder.Services.PostConfigure<Appostolic.Api.App.Options.SmtpOptions>(o =>
    {
        o.Host = string.IsNullOrWhiteSpace(o.Host) ? "127.0.0.1" : o.Host;
        o.Port = o.Port <= 0 ? 1025 : o.Port;
    });
}

// Notifications runtime (shared registration)
builder.Services.AddNotificationsRuntime(builder.Configuration, builder.Environment);

// Auth: password hasher
builder.Services.AddSingleton<Appostolic.Api.Application.Auth.IPasswordHasher, Appostolic.Api.Application.Auth.Argon2PasswordHasher>();

// Orchestration services
builder.Services.AddScoped<ITraceWriter, Appostolic.Api.Application.Agents.Runtime.TraceWriter>();
builder.Services.AddScoped<IAgentOrchestrator, Appostolic.Api.Application.Agents.Runtime.AgentOrchestrator>();
builder.Services.AddSingleton<IModelAdapter, MockModelAdapter>();
// Queue: ensure single shared instance for both interface and concrete
builder.Services.AddSingleton<InMemoryAgentTaskQueue>();
builder.Services.AddSingleton<IAgentTaskQueue>(sp => sp.GetRequiredService<InMemoryAgentTaskQueue>());
// Worker
builder.Services.AddHostedService<Appostolic.Api.Application.Agents.Queue.AgentTaskWorker>();
builder.Services.AddSingleton<Appostolic.Api.Application.Agents.Queue.AgentTaskCancelRegistry>();

// OpenTelemetry: resource, traces, metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Appostolic.Api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation()
         .AddSource("Appostolic.AgentRuntime", "Appostolic.Tools");

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (builder.Environment.IsDevelopment())
        {
            t.AddConsoleExporter();
        }
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            t.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation()
         .AddRuntimeInstrumentation()
         .AddMeter("Appostolic.Metrics");

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (builder.Environment.IsDevelopment())
        {
            m.AddConsoleExporter();
        }
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            m.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }
    });

// Logging: keep existing console logger, also export logs via OTEL providers
var logOtlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
builder.Logging.AddOpenTelemetry(logOptions =>
{
    logOptions.IncludeFormattedMessage = true;
    logOptions.ParseStateValues = true;
    logOptions.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Appostolic.Api"));

    if (builder.Environment.IsDevelopment())
    {
        logOptions.AddConsoleExporter();
    }
    if (!string.IsNullOrWhiteSpace(logOtlpEndpoint))
    {
        logOptions.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(logOtlpEndpoint);
        });
    }
});

// Remove scoped registration for TenantScopeMiddleware (not needed with UseMiddleware)
// builder.Services.AddScoped<TenantScopeMiddleware>();

var app = builder.Build();

// Startup DB guard: ensure the notification_dedupes TTL table exists (idempotent)
// This protects against earlier empty migrations or out-of-order application.
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var sql = @"CREATE SCHEMA IF NOT EXISTS app;
CREATE TABLE IF NOT EXISTS app.notification_dedupes (
    dedupe_key varchar(200) PRIMARY KEY,
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    updated_at timestamptz NOT NULL DEFAULT timezone('utc', now())
);
CREATE INDEX IF NOT EXISTS ix_notification_dedupes_expires ON app.notification_dedupes(expires_at);";
    db.Database.ExecuteSqlRaw(sql);
}
catch (Exception ex)
{
    // In Production we still proceed; errors will surface when enqueue attempts happen.
    Console.WriteLine($"[Startup] Warning: failed to ensure app.notification_dedupes exists: {ex.Message}");
}

// Swagger middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    // Ensure the UI is hosted at /swagger and points to the v1 JSON
    c.RoutePrefix = "swagger";
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Appostolic API v1");
});

// Ensure navigating to /swagger (no trailing slash) shows the Swagger UI (with trailing slash)
app.MapGet("/swagger", () => Results.Redirect("/swagger/", permanent: false))
   .ExcludeFromDescription();

app.UseAuthentication();
app.UseAuthorization();

// Use conventional middleware to set tenant and manage transaction
app.UseMiddleware<TenantScopeMiddleware>();

// Development dev-header correlation: propagate tenant/user via Activity baggage and tags
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var devTenant = context.Request.Headers["x-tenant"].FirstOrDefault();
    var devUser = context.Request.Headers["x-dev-user"].FirstOrDefault();

    if (!string.IsNullOrWhiteSpace(devTenant))
    {
        Activity.Current?.AddBaggage("tenant", devTenant);
        Activity.Current?.SetTag("tenant", devTenant);
    }
    if (!string.IsNullOrWhiteSpace(devUser))
    {
        Activity.Current?.AddBaggage("user", devUser);
        Activity.Current?.SetTag("user", devUser);
    }

    await next();
});

// Uniform 403 responses (both policy and manual Results.Forbid) as RFC7807 problem+json
app.Use(async (context, next) =>
{
    await next();
    if (context.Response.StatusCode == StatusCodes.Status403Forbidden && !context.Response.HasStarted)
    {
        // If an explicit problem+json was already written (from policy handler), do nothing
        if (!string.IsNullOrWhiteSpace(context.Response.ContentType) && context.Response.ContentType.Contains("application/problem+json", StringComparison.OrdinalIgnoreCase))
            return;

        // Build minimal problem details
        string? tenantIdStr = null;
        if (context.Items.TryGetValue("TenantId", out var tid) && tid is Guid gid)
            tenantIdStr = gid.ToString();
        else if (Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out var claimTid))
            tenantIdStr = claimTid.ToString();
        else if (Guid.TryParse(context.Request.Headers["X-Tenant-Id"].FirstOrDefault(), out var headerTid))
            tenantIdStr = headerTid.ToString();

        var requiredRoles = context.Items.TryGetValue("AuthRequiredRoles", out var rr) ? rr?.ToString() : null;

        var payload = new
        {
            type = "https://httpstatuses.com/403",
            title = "Forbidden",
            status = 403,
            detail = string.IsNullOrWhiteSpace(requiredRoles) ? "You do not have permission to perform this action." : $"Missing required role: {requiredRoles}",
            extensions = new { tenantId = tenantIdStr, requiredRoles }
        };

        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(payload);
    }
});

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
app.MapDevAgentsEndpoints(app.Services.GetRequiredService<IHostEnvironment>());
app.MapDevAgentsDemoEndpoints();
app.MapDevNotificationsEndpoints();
app.MapNotificationsWebhookEndpoints();
app.MapNotificationsAdminEndpoints();
app.MapAgentTasksEndpoints();
app.MapAgentTasksExportEndpoints();
app.MapAgentsEndpoints();

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

// Auto-migrate for Development/Test (only for relational providers)
using (var scope = app.Services.CreateScope())
{
    var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
    if (env.IsDevelopment() || env.IsEnvironment("Test"))
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational())
        {
            db.Database.Migrate();
        }
    }
}

app.Run();

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
            // Treat Name as a slug; enforce uniqueness to support natural-key lookups
            b.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Email).HasColumnName("email").IsRequired();
            b.Property(x => x.PasswordHash).HasColumnName("password_hash").HasColumnType("bytea").IsRequired(false);
            b.Property(x => x.PasswordSalt).HasColumnName("password_salt").HasColumnType("bytea").IsRequired(false);
            b.Property(x => x.PasswordUpdatedAt).HasColumnName("password_updated_at").IsRequired(false);
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
            b.Property(x => x.Roles).HasColumnName("roles");
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();

            // Enforce referential integrity
            b.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
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

            // FK: lessons.tenant_id → tenants.id
            b.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Invitations (Auth-01)
        modelBuilder.Entity<Invitation>(b =>
        {
            b.ToTable("invitations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.Email).HasColumnName("email").IsRequired();
            b.Property(x => x.Role).HasColumnName("role");
            // New in IAM 2.2 — granular roles flags on invitations
            b.Property(x => x.Roles).HasColumnName("roles");
            b.Property(x => x.Token).HasColumnName("token").IsRequired();
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.InvitedByUserId).HasColumnName("invited_by_user_id");
            b.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            // Referential integrity
            b.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.InvitedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            // Note: Case-insensitive unique index on (tenant_id, lower(email)) added via migration SQL.
            b.HasIndex(x => x.Token).IsUnique();
        });

        // Magic Link Login Tokens (Auth-ML-01)
        modelBuilder.Entity<LoginToken>(b =>
        {
            b.ToTable("login_tokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Email).HasColumnName("email").IsRequired().HasColumnType("citext");
            b.Property(x => x.TokenHash).HasColumnName("token_hash").HasColumnType("varchar(128)").IsRequired();
            b.Property(x => x.Purpose).HasColumnName("purpose").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
            // Unique token hash; support lookups by email and created_at for rate-limiting
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => new { x.Email, x.CreatedAt }).HasDatabaseName("ix_login_tokens_email_created");
        });

        // Apply configurations for domain types (Agent runtime)
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();
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
    public byte[]? PasswordHash { get; init; }
    public byte[]? PasswordSalt { get; init; }
    public DateTime? PasswordUpdatedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public enum MembershipRole { Owner, Admin, Editor, Viewer }
public enum MembershipStatus { Active, Invited, Suspended, Revoked }

[Flags]
public enum Roles
{
    None = 0,
    TenantAdmin = 1 << 0,
    Approver    = 1 << 1,
    Creator     = 1 << 2,
    Learner     = 1 << 3,
}

public record Membership
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public MembershipRole Role { get; init; }
    public Roles Roles { get; init; }
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

public record Invitation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public MembershipRole Role { get; set; }
    // New in IAM 2.2 — granular roles flags captured at invite time
    public Roles Roles { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public Guid? InvitedByUserId { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public record LoginToken
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string Purpose { get; set; } = "magic";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

// Expose Program for WebApplicationFactory in tests
public partial class Program { }
