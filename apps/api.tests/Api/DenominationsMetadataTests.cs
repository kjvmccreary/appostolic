using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Appostolic.Api.AuthTests; // TestAuthSeeder

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

    private async Task<HttpClient> CreateAuthedClientAsync()
    {
        var email = "denom-user@example.com";
        var tenantSlug = $"denoms-{Guid.NewGuid():N}";
        var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, tenantSlug, owner: true);
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
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
