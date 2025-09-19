using System.Net.Http.Json;
using Appostolic.Api.Tests;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.AuthTests;

public class LoginRolesSerializationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LoginRolesSerializationTests(WebAppFactory factory) => _factory = factory;

    private sealed record MembershipDto(Guid tenantId, string tenantSlug, string role, int? roles);
    private sealed record LoginPayload(Guid Id, string Email, MembershipDto[] memberships);

    [Fact]
    public async Task Login_IncludesRolesBitmaskOnMembership()
    {
        using var client = _factory.CreateClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email = "roleser@test.com", password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "roleser@test.com", password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginPayload>();
        payload.Should().NotBeNull();
        payload!.memberships.Should().HaveCount(1);
        var m = payload.memberships[0];
        m.roles.Should().NotBeNull();
        m.roles.Should().BeGreaterThan(0); // Owner -> derived flags should include TenantAdmin bit => at least 1
    }
}
