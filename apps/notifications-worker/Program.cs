using Appostolic.Api;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Appostolic.Api.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Configuration & environment
var configuration = builder.Configuration;
var env = builder.Environment;

// Options bindings for notifications runtime
// We reuse the extension method from the API to wire notifications services.
// We override runtime options to ensure the dispatcher runs in the worker, and is disabled in API via config if desired.
builder.Services.Configure<NotificationsRuntimeOptions>(configuration.GetSection("Notifications:Runtime"));

// DbContext (needed for outbox and EF models)
builder.Services.AddDbContext<Appostolic.Api.AppDbContext>(options =>
{
    var cs = ConnectionStringHelper.BuildFromEnvironment(configuration);
    options.UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema: "app"));
});

// Notifications runtime registration (shared)
builder.Services.AddNotificationsRuntime(configuration, env);

// OpenTelemetry (match API defaults lightly)
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Appostolic.Notifications.Worker"))
    .WithTracing(t =>
    {
        t.AddHttpClientInstrumentation();
        if (env.IsDevelopment()) t.AddConsoleExporter();
        var otlp = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlp))
        {
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
        }
    })
    .WithMetrics(m =>
    {
        m.AddRuntimeInstrumentation();
        m.AddMeter("Appostolic.Metrics");
        if (env.IsDevelopment()) m.AddConsoleExporter();
        var otlp = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlp))
        {
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
        }
    });

var app = builder.Build();

// Auto-migrate in Development/Test for relational providers (reusing API convention)
using (var scope = app.Services.CreateScope())
{
    var scopeEnv = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
    if (scopeEnv.IsDevelopment() || scopeEnv.IsEnvironment("Test"))
    {
        var db = scope.ServiceProvider.GetRequiredService<Appostolic.Api.AppDbContext>();
        if (db.Database.IsRelational())
        {
            db.Database.Migrate();
        }
    }
}

await app.RunAsync();
