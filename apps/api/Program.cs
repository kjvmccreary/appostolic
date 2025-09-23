using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Appostolic.Api.Infrastructure.Auth.Jwt;
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
using System.Text.Json;
using StackExchange.Redis;
using Appostolic.Api.App.Notifications;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Appostolic.Api.Application.Storage;
using Microsoft.Extensions.FileProviders;
using Amazon.S3;
using Amazon;
using Microsoft.Extensions.Options;
using System.Security.Claims;

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

// Add DbContext (E2E override: allow in-memory provider when E2E_INMEM_DB=true to avoid requiring Postgres for HTTPS cookie tests)
var useE2EInMem = (Environment.GetEnvironmentVariable("E2E_INMEM_DB") ?? configuration["E2E_INMEM_DB"] ?? "false")
    .Equals("true", StringComparison.OrdinalIgnoreCase);
if (useE2EInMem)
{
    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        options.UseInMemoryDatabase("e2e-db");
    });
    Console.WriteLine("[Startup] Using InMemory DB (E2E_INMEM_DB=true)");
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        var cs = Appostolic.Api.Infrastructure.Database.ConnectionStringHelper.BuildFromEnvironment(configuration);
        options.UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema: "app"));
    });
}

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Appostolic API", Version = "v1" });

    // Use fully-qualified type names for schema Ids to avoid collisions between same-named records/classes
    // e.g., multiple InviteRequest records used in different endpoint groups
    c.CustomSchemaIds(type => type.FullName?.Replace('+', '.') ?? type.Name);


    // Bearer JWT (Story 1)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. Example: 'Authorization: Bearer {token}'"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
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

// AuthN/Z (Story 1 JWT introduction)
builder.Services.AddOptions<AuthJwtOptions>()
    .Bind(builder.Configuration.GetSection("Auth:Jwt"))
    .PostConfigure(o =>
    {
        if (string.IsNullOrWhiteSpace(o.SigningKeyBase64))
        {
            // Legacy / flat env fallback used by tests: AUTH__JWT__SIGNING_KEY
            var legacy = builder.Configuration["AUTH__JWT__SIGNING_KEY"];
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                o.SigningKeyBase64 = legacy;
            }
        }
        if (string.IsNullOrWhiteSpace(o.SigningKeyBase64))
        {
            if (builder.Environment.IsDevelopment())
            {
                var random = new byte[32];
                System.Security.Cryptography.RandomNumberGenerator.Fill(random);
                o.SigningKeyBase64 = Convert.ToBase64String(random);
                Console.WriteLine("[AuthJwt] Generated ephemeral dev signing key.");
            }
            else
            {
                throw new InvalidOperationException("Auth:Jwt:SigningKey is required in non-development environments.");
            }
        }
    });
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

var jwtEnabled = (builder.Configuration["AUTH__JWT__ENABLED"] ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);

// In Development we introduce a composite policy scheme that chooses Dev header auth when
// the x-dev-user header is present, otherwise falls back to standard Bearer (JWT). This
// removes the need to enumerate AuthenticationSchemes="Dev,Bearer" on every endpoint group
// and fixes 401 responses observed in tests that relied on dev headers for endpoints which
// previously only listed the default Bearer scheme.
var authBuilder = builder.Services.AddAuthentication(options =>
{
    // Default to Bearer everywhere; we may override with policy scheme in Development below.
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});
if (jwtEnabled)
{
    authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opts =>
    {
        opts.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        opts.SaveToken = false;
        opts.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                // Some handlers map 'sub' to ClaimTypes.NameIdentifier; attempt both before failing
                var sub = ctx.Principal?.FindFirst("sub")?.Value
                          ?? ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(sub, out var userId)) { ctx.Fail("invalid_sub"); return; }
                var tokenVersionClaim = ctx.Principal?.FindFirst("v")?.Value;
                if (!int.TryParse(tokenVersionClaim, out var tokenVersion)) tokenVersion = 0;
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var currentVersion = await db.Users.Where(u => u.Id == userId)
                    .Select(u => (int?)EF.Property<int>(u, "TokenVersion"))
                    .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted) ?? 0;
                if (tokenVersion < currentVersion)
                {
                    ctx.Fail("token_version_mismatch");
                }
            }
        };
    });
    // Resolve validation parameters lazily via IOptionsMonitor callback pattern
    builder.Services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>>(sp =>
        new PostConfigureOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            var svc = sp.GetRequiredService<IJwtTokenService>();
            o.TokenValidationParameters = svc.CreateValidationParameters();
        }));
}
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
builder.Services.Configure<Appostolic.Api.Application.Privacy.PrivacyOptions>(builder.Configuration.GetSection("Privacy"));
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
// Privacy: PII hasher (hashes emails/phones using SHA-256 + pepper)
builder.Services.AddSingleton<Appostolic.Api.Application.Privacy.IPIIHasher, Appostolic.Api.Application.Privacy.Sha256PIIHasher>();

// Storage: conditional S3/MinIO vs local filesystem
var storageMode = (builder.Configuration["Storage:Mode"] ?? "local").ToLowerInvariant();
if (storageMode == "s3")
{
    builder.Services.Configure<S3StorageOptions>(builder.Configuration.GetSection("Storage:S3"));
    builder.Services.AddSingleton<IAmazonS3>(sp =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<S3StorageOptions>>().Value;
        var config = new AmazonS3Config
        {
            ForcePathStyle = opts.PathStyle,
        };
        if (!string.IsNullOrWhiteSpace(opts.ServiceURL))
        {
            config.ServiceURL = opts.ServiceURL;
        }
        if (!string.IsNullOrWhiteSpace(opts.RegionEndpoint))
        {
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(opts.RegionEndpoint);
        }
        if (!string.IsNullOrWhiteSpace(opts.AccessKey) && !string.IsNullOrWhiteSpace(opts.SecretKey))
        {
            return new AmazonS3Client(opts.AccessKey, opts.SecretKey, config);
        }
        return new AmazonS3Client(config);
    });
    builder.Services.AddSingleton<IObjectStorageService, S3ObjectStorageService>();
}
else
{
    builder.Services.Configure<LocalFileStorageOptions>(builder.Configuration.GetSection("Storage:Local"));
    builder.Services.AddSingleton<IObjectStorageService, LocalFileStorageService>();
}

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
        // Allow disabling noisy console exporters in Development with QUIET_CONSOLE_TELEMETRY=true
        var quietConsole = (builder.Configuration["QUIET_CONSOLE_TELEMETRY"] ?? Environment.GetEnvironmentVariable("QUIET_CONSOLE_TELEMETRY") ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        if (builder.Environment.IsDevelopment() && !quietConsole)
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
        var quietConsole = (builder.Configuration["QUIET_CONSOLE_TELEMETRY"] ?? Environment.GetEnvironmentVariable("QUIET_CONSOLE_TELEMETRY") ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        if (builder.Environment.IsDevelopment() && !quietConsole)
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

    var quietConsole = (builder.Configuration["QUIET_CONSOLE_TELEMETRY"] ?? Environment.GetEnvironmentVariable("QUIET_CONSOLE_TELEMETRY") ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
    if (builder.Environment.IsDevelopment() && !quietConsole)
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

// Reduce noisy category logging in Development (Kestrel routing/matching spam, EF Core info)
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Warning);
    builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
}

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

// (Relocated) Swagger middleware: register AFTER endpoint mappings to avoid any timing issues
// where the OpenAPI generator inspects the endpoint data source before all minimal APIs are added.
// While Swashbuckle typically resolves endpoints lazily, relocating removes ambiguity behind the observed 404 for /swagger/v1/swagger.json.
// We also set an explicit RouteTemplate for clarity.
// Swagger middleware (must be registered before app.Run and before authentication for full endpoint metadata visibility)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    // Serve the UI at /swagger/ (with or without trailing slash).
    // Setting RoutePrefix = "swagger" normally serves index at /swagger; the extra redirect we previously had
    // caused some clients to land on the JSON when path logic collided. Keep it simple: no custom redirect.
    c.RoutePrefix = "swagger"; // UI root: /swagger/
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Appostolic API v1");
    c.DocumentTitle = "Appostolic API Docs";
});
// Remove custom redirect that interfered with UI loading (browser now hits /swagger or /swagger/ and gets index.html)


// Enforce HTTPS + HSTS in non-Development/Test environments. This keeps local dev/test flexible (plain HTTP)
// while ensuring production traffic is secured and browsers remember to prefer HTTPS.
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test"))
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// Story 3: Dev headers deprecation (reject legacy headers early when flag disabled)
app.UseMiddleware<Appostolic.Api.App.Middleware.DevHeadersDeprecationMiddleware>();

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

// Static files for media (avatars/logos) under /media
var mediaBase = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "web.out", "media");
Directory.CreateDirectory(mediaBase);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaBase),
    RequestPath = "/media"
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

// E2E helper endpoint for HTTPS cookie attribute validation (Story 5b)
if (useE2EInMem)
{
    app.MapGet("/e2e/issue-cookie", (HttpContext http) =>
    {
        var expires = DateTimeOffset.UtcNow.AddDays(30);
        http.Response.Cookies.Append("rt", Guid.NewGuid().ToString("N"), new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires
        });
        return Results.Ok(new { issued = true, expires });
    }).WithTags("e2e").WithDescription("Issues a dummy refresh cookie for E2E HTTPS Secure attribute validation (InMemory mode only).");
}

// Auth smoke endpoint (Story 1) - requires authentication (JWT or dev headers)
app.MapGet("/auth-smoke/ping", (ClaimsPrincipal user) =>
{
    var sub = user.FindFirst("sub")?.Value
              ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
              ?? user.Identity?.Name
              ?? "anonymous";
    return Results.Ok(new { pong = true, sub });
}).RequireAuthorization();

// v1 API
app.MapV1Endpoints();
app.MapDevToolsEndpoints();
app.MapDevAgentsEndpoints(app.Services.GetRequiredService<IHostEnvironment>());
app.MapDevAgentsDemoEndpoints();
app.MapDevNotificationsEndpoints();
app.MapNotificationsWebhookEndpoints();
// Development-only auth helper endpoints for Swagger (login -> token -> tenant selection)
app.MapDevAuthEndpoints();
app.MapNotificationsAdminEndpoints();
app.MapAgentTasksEndpoints();
app.MapAgentTasksExportEndpoints();
app.MapAgentsEndpoints();
app.MapUserProfileEndpoints();
app.MapTenantSettingsEndpoints();

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
            // JSONB settings blob for org-level configuration (branding, contact, social, privacy toggles)
            b.Property(x => x.Settings).HasColumnName("settings").HasColumnType("jsonb").IsRequired(false);
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
            // JSONB profile blob (name/contact/social/avatar/bio/guardrails/preferences)
            b.Property(x => x.Profile).HasColumnName("profile").HasColumnType("jsonb").IsRequired(false);
            b.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Membership>(b =>
        {
            b.ToTable("memberships");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
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

        // Audit entries for membership role changes (IAM 3.3)
        modelBuilder.Entity<Audit>(b =>
        {
            b.ToTable("audits");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id").IsRequired(false);
            b.Property(x => x.ChangedByEmail).HasColumnName("changed_by_email").IsRequired(false);
            b.Property(x => x.OldRoles).HasColumnName("old_roles");
            b.Property(x => x.NewRoles).HasColumnName("new_roles");
            b.Property(x => x.ChangedAt).HasColumnName("changed_at").HasDefaultValueSql("timezone('utc', now())");

            // Helpful index for recent changes per tenant
            b.HasIndex(x => new { x.TenantId, x.ChangedAt }).HasDatabaseName("ix_audits_tenant_changed");

            // FKs (optional, not enforced to keep write path simple)
            b.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
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

        // Refresh Tokens (Auth-JWT-02)
        modelBuilder.Entity<Appostolic.Api.Infrastructure.Auth.Jwt.RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            b.Property(x => x.Purpose).HasColumnName("purpose").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            b.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired(false);
            b.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_refresh_tokens_user_created");
            b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
            // Future: partial index for active tokens (revoked_at IS NULL) if needed for performance
            b.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Apply configurations for domain types (Agent runtime)
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Test support: EF InMemory provider cannot map JsonDocument; apply converters when detected
        // Provider name for InMemory: "Microsoft.EntityFrameworkCore.InMemory"
        if (Database.ProviderName != null && Database.ProviderName.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            // Use string storage for JsonDocument properties under InMemory via helper methods
            System.Linq.Expressions.Expression<Func<JsonDocument?, string?>> toString = v => SerializeNullable(v);
            System.Linq.Expressions.Expression<Func<string?, JsonDocument?>> toJson = s => ParseNullable(s);
            var converter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<JsonDocument?, string?>(toString, toJson);

            modelBuilder.Entity<Tenant>().Property(x => x.Settings).HasConversion(converter);
            modelBuilder.Entity<User>().Property(x => x.Profile).HasConversion(converter);
        }
    }

    private static string? SerializeNullable(JsonDocument? doc) => doc == null ? null : doc.RootElement.GetRawText();
    private static JsonDocument? ParseNullable(string? s) => s == null ? null : JsonDocument.Parse(s);

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();
    public DbSet<Audit> Audits => Set<Audit>();
}

public record Tenant
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public JsonDocument? Settings { get; init; }
}

public record User
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public byte[]? PasswordHash { get; init; }
    public byte[]? PasswordSalt { get; init; }
    public DateTime? PasswordUpdatedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public JsonDocument? Profile { get; init; }
    /// <summary>
    /// Monotonically increasing token version used to invalidate outstanding access tokens (claim 'v').
    /// </summary>
    public int TokenVersion { get; init; } = 0;
}

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

/// <summary>
/// Represents a user's membership within a tenant. Roles is intentionally mutable because role assignments evolve
/// over time (e.g. a user may later be promoted to TenantAdmin). All role changes SHOULD produce an <see cref="Audit"/> record.
/// </summary>
public record Membership
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    /// <summary>
    /// Granular roles bitfield. Mutable by design ("pencil" model). We allow in-place updates because
    /// role assignments change frequently and EF tracked-entity mutation avoids duplicate-tracking issues
    /// encountered with immutable record replacement. All changes MUST flow through <see cref="ApplyRoleChange"/>
    /// to centralize audit creation and keep a consistent trail of old→new flags.
    /// </summary>
    public Roles Roles { get; set; }
    public MembershipStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Apply a roles change in-memory, returning an <see cref="Audit"/> object if a change occurred, else null.
    /// Caller is responsible for adding the audit to the DbContext and saving changes.
    /// </summary>
    public Audit? ApplyRoleChange(Roles newRoles, string? changedByEmail, Guid? changedByUserId)
    {
        if (newRoles == Roles) return null;
        var audit = new Audit
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = UserId,
            ChangedByUserId = changedByUserId,
            ChangedByEmail = changedByEmail,
            OldRoles = Roles,
            NewRoles = newRoles,
            ChangedAt = DateTime.UtcNow
        };
        Roles = newRoles;
        return audit;
    }
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
    // New in IAM 2.2 — granular roles flags captured at invite time
    public Roles Roles { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public Guid? InvitedByUserId { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Audit record for membership roles changes under a tenant scope.
/// Captures target user, who performed the change, and old/new Roles bitfields.
/// </summary>
public record Audit
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public Guid? ChangedByUserId { get; init; }
    public string? ChangedByEmail { get; init; }
    public Roles OldRoles { get; init; }
    public Roles NewRoles { get; init; }
    public DateTime ChangedAt { get; init; }
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
