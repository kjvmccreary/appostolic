using System.Net;
using System.Net.Http.Headers;
using Appostolic.Api.Tests.AgentTasks;
using Appostolic.Api.AuthTests; // AuthTestClientFlow
using Microsoft.AspNetCore.Mvc.Testing;

namespace Appostolic.Api.Tests.Security;

public class AgentTasksAuthContractTests : IClassFixture<AgentTasksFactory>
{
    private readonly AgentTasksFactory _factory;

    public AgentTasksAuthContractTests(AgentTasksFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListEndpoint_RequiresAuthentication_JwtPath()
    {
        // Arrange
        var unauth = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        unauth.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Authenticated client: perform real password login + tenant selection to obtain a bearer token.
        var auth = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        auth.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Uses the generalized AuthTestClientFlow that seeds password (done in factory base) and calls /api/auth/login + /api/auth/select-tenant.
        await AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, auth, "dev@example.com", "acme");

        // Act - unauthenticated
        var resp = await unauth.GetAsync("/api/agent-tasks?take=1&skip=0");

        // Assert - unauthenticated is blocked (401 or 403 allowed)
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

        // Act - authenticated
        var ok = await auth.GetAsync("/api/agent-tasks?take=1&skip=0");

        // Assert - authenticated returns 200 and JSON array (JWT auth)
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ok.Content.ReadAsStringAsync()).TrimStart();
        body.Should().StartWith("[");

        // Optional sanity: swagger JSON remains publicly readable
        var swagger = await unauth.GetAsync("/swagger/v1/swagger.json");
        swagger.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
