using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Appostolic.Api.AuthTests; // real auth flow helper
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api;

public class DenominationsMetadataTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public DenominationsMetadataTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RequiresAuth()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/metadata/denominations");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // RDH Story 2: legacy dev headers removed; using JWT helper
    private const string DefaultPw = "Password123!"; // align with other migrated tests
    private async Task SeedPasswordAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var (hash, salt, _) = hasher.HashPassword(password);
        db.Users.Update(user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
    private async Task<HttpClient> CreateAuthedClientAsync()
    {
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var c = _factory.CreateClient();
        await AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, c, "kevin@example.com", "kevin-personal");
        return c;
    }

    [Fact]
    public async Task ReturnsPresetList()
    {
        var client = await CreateAuthedClientAsync();
        var resp = await client.GetAsync("/api/metadata/denominations");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("presets", out var presets).Should().BeTrue();
        presets.ValueKind.Should().Be(JsonValueKind.Array);
        presets.GetArrayLength().Should().BeGreaterThan(3);
        // basic shape check
        var first = presets[0];
        first.TryGetProperty("id", out _).Should().BeTrue();
        first.TryGetProperty("name", out _).Should().BeTrue();
    }
}
