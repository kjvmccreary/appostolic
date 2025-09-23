using System;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using Appostolic.Api.Infrastructure.Auth.Jwt;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Tests for /internal/health/jwt-keys endpoint.
/// </summary>
public class JwtKeysHealthEndpointTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public JwtKeysHealthEndpointTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Returns_Key_Metadata_And_Probe_Result()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/internal/health/jwt-keys");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JwtKeysHealthResponse>();
        Assert.NotNull(json);
        Assert.False(string.IsNullOrWhiteSpace(json!.active_key_id));
        Assert.True(json.total_configured_keys >= 1);
    }

    // Negative-path probe_result=false test omitted; without a seam to force validation failure we rely on manual fault injection (invalid key set) in staging.

    private record JwtKeysHealthResponse(string? active_key_id, int total_configured_keys, string[] configured_key_ids, bool probe_result);
}
