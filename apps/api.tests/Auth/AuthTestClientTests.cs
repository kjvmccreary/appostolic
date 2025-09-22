using System.Threading.Tasks;
using Xunit;
using Appostolic.Api.Tests; // WebAppFactory namespace

namespace Appostolic.Api.AuthTests;

/// <summary>
/// Tests for the AuthTestClient convenience facade ensuring it sets Authorization header correctly.
/// </summary>
public class AuthTestClientTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public AuthTestClientTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UseNeutralAsync_SetsBearerHeader()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestClient.UseNeutralAsync(client, "neutral@example.com");
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.NotNull(client.DefaultRequestHeaders.Authorization);
        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal(token, client.DefaultRequestHeaders.Authorization!.Parameter);
    }

    [Fact]
    public async Task UseAutoTenantAsync_AttachesTenantWhenAvailable()
    {
        var client = _factory.CreateClient();
        var (neutral, tenant) = await AuthTestClient.UseAutoTenantAsync(client, "autouser@example.com");
        Assert.False(string.IsNullOrWhiteSpace(neutral));
        // Accept either behavior: tenant token present or fallback neutral if multi-membership unresolved
        var attached = client.DefaultRequestHeaders.Authorization!.Parameter;
        if (tenant is not null)
        {
            Assert.Equal(tenant, attached);
        }
        else
        {
            Assert.Equal(neutral, attached);
        }
    }
}
