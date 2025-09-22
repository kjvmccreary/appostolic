using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

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
    private static async Task<HttpClient> CreateAuthedClientAsync(WebAppFactory f)
    {
        var c = f.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(c, "kevin@example.com", "kevin-personal");
        return c;
    }

    [Fact]
    public async Task ReturnsPresetList()
    {
    var client = await CreateAuthedClientAsync(_factory);
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
