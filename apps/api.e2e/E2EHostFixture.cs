using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Appostolic.Api.E2E;

/// <summary>
/// Launches the real API with Kestrel HTTPS on a chosen (or dynamic) port so tests can assert transport-dependent cookie attributes.
/// </summary>
public sealed class E2EHostFixture : IAsyncLifetime
{
    private IHost? _host;
    public int Port { get; private set; }
    public required Uri BaseAddress { get; init; }
    public required HttpClient Client { get; init; }

    public static async Task<E2EHostFixture> LaunchAsync(int? preferredPort = null)
    {
        var port = preferredPort ?? FindFreePort();
        var baseAddress = new Uri($"https://localhost:{port}");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        // Generate self-signed cert and wire Kestrel explicitly (bypasses need for dev-certs)
        var cert = CreateSelfSignedCert();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(port, listen => listen.UseHttps(cert));
        });

        var app = builder.Build();

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
        });

        app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine($"[E2E] Listening {baseAddress}"));
        await app.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        var client = new HttpClient(handler) { BaseAddress = baseAddress };

        return new E2EHostFixture
        {
            _host = app,
            Port = port,
            BaseAddress = baseAddress,
            Client = client
        };
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        try { if (_host != null) await _host.StopAsync(); } catch { /* ignore */ }
        _host?.Dispose();
        Client.Dispose();
    }

    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=localhost", ecdsa, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }
}
