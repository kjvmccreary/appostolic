using Appostolic.Api.App.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;

namespace Appostolic.Api.App.Notifications;

public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationsRuntime(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
    {
        // Options binding
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.Configure<SendGridOptions>(configuration.GetSection("SendGrid"));
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
        services.Configure<NotificationTransportOptions>(configuration.GetSection("Notifications:Transport"));
        services.Configure<NotificationsRuntimeOptions>(configuration.GetSection("Notifications:Runtime"));

        // Email queue
        services.AddSingleton<IEmailQueue, EmailQueue>();

        // Field cipher (encryption) selection
        services.AddSingleton<IFieldCipher>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NotificationOptions>>().Value;
            if (!opts.EncryptFields || string.IsNullOrWhiteSpace(opts.EncryptionKeyBase64)) return new NullFieldCipher();
            try
            {
                var key = Convert.FromBase64String(opts.EncryptionKeyBase64);
                return new AesGcmFieldCipher(key);
            }
            catch
            {
                // Fallback to null cipher on invalid key
                return new NullFieldCipher();
            }
        });

        // Outbox (scoped)
        services.AddScoped<INotificationOutbox, EfNotificationOutbox>();
        services.AddSingleton<INotificationIdQueue, NotificationIdQueue>();

        // Transport selection
        var transportMode = (configuration["Notifications:Transport:Mode"] ?? "channel").ToLowerInvariant();
        if (transportMode == "redis")
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NotificationTransportOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(opts.Redis.ConnectionString))
                {
                    return ConnectionMultiplexer.Connect(opts.Redis.ConnectionString);
                }
                var parts = new List<string> { $"{opts.Redis.Host}:{opts.Redis.Port}", "abortConnect=false" };
                if (!string.IsNullOrWhiteSpace(opts.Redis.Password)) parts.Add($"password={opts.Redis.Password}");
                if (opts.Redis.Ssl) parts.Add("ssl=true");
                var cs = string.Join(',', parts);
                return ConnectionMultiplexer.Connect(cs);
            });

            services.AddSingleton<RedisTransportDiagnostics>();
            services.AddSingleton<INotificationTransport, RedisNotificationTransport>();
            services.AddHostedService<RedisNotificationSubscriberHostedService>();
        }
        else
        {
            services.AddSingleton<RedisTransportDiagnostics>();
            services.AddSingleton<INotificationTransport, ChannelNotificationTransport>();
        }

        // Template renderer and providers
        services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();
        services.AddHttpClient("sendgrid");
        services.AddSingleton<SendGridEmailSender>();
        services.AddSingleton<ISmtpClientFactory, DefaultSmtpClientFactory>();
        services.AddSingleton<SmtpEmailSender>();

        // Provider selection (smtp in Dev by default, sendgrid otherwise when unset)
        var provider = configuration["Email:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = env.IsDevelopment() ? "smtp" : "sendgrid";
        }
        if (string.Equals(provider, "sendgrid", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<SendGridEmailSender>());
        }
        else if (string.Equals(provider, "smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<SmtpEmailSender>());
        }
        else
        {
            services.AddSingleton<IEmailSender, NoopEmailSender>();
        }

        // Runtime services (gated via Notifications:Runtime)
        services.AddSingleton<IEmailDedupeStore, InMemoryEmailDedupeStore>();
        services.AddSingleton<INotificationsPurger, NotificationsPurger>();
        services.AddHostedService<NotificationsPurgeHostedService>();

        services.AddSingleton<IAutoResendScanner, AutoResendScanner>();
        services.AddHostedService<AutoResendHostedService>();

        // Conditionally add dispatchers
        services.AddOptions<NotificationsRuntimeOptions>();

        // Register concrete hosted services so they can be resolved when enabled
        services.AddSingleton<EmailDispatcherHostedService>();
        services.AddSingleton<NotificationDispatcherHostedService>();

        // Conditionally register IHostedService wrappers that either delegate to the real service or no-op
        services.AddSingleton<IHostedService>(sp =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NotificationsRuntimeOptions>>().Value;
            return o.RunLegacyEmailDispatcher
                ? sp.GetRequiredService<EmailDispatcherHostedService>()
                : new DisabledHostedService("EmailDispatcher");
        });

        services.AddSingleton<IHostedService>(sp =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NotificationsRuntimeOptions>>().Value;
            return o.RunDispatcher
                ? sp.GetRequiredService<NotificationDispatcherHostedService>()
                : new DisabledHostedService("NotificationDispatcher");
        });

        // Enqueuer
        services.AddScoped<INotificationEnqueuer, NotificationEnqueuer>();

        return services;
    }

    // A no-op hosted service wrapper to conditionally disable a hosted service via DI factory.
    private sealed class DisabledHostedService : IHostedService
    {
        private readonly string _name;
        public DisabledHostedService(string name) => _name = name;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override string ToString() => $"DisabledHostedService({_name})";
    }
}
