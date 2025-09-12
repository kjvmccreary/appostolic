using System.Net;
using System.Net.Http.Headers;
using Appostolic.Api.Tests.AgentTasks;
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
    public async Task ListEndpoint_RequiresDevHeaders()
    {
        // Arrange
        var unauth = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        unauth.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var auth = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        auth.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        auth.DefaultRequestHeaders.Add("x-dev-user", "dev@example.com");
        auth.DefaultRequestHeaders.Add("x-tenant", "acme");

        // Act - unauthenticated
        var resp = await unauth.GetAsync("/api/agent-tasks?take=1&skip=0");

        // Assert - unauthenticated is blocked (401 or 403 allowed)
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

        // Act - authenticated
        var ok = await auth.GetAsync("/api/agent-tasks?take=1&skip=0");

        // Assert - authenticated returns 200 and JSON array
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ok.Content.ReadAsStringAsync()).TrimStart();
        body.Should().StartWith("[");

        // Optional sanity: swagger JSON remains publicly readable
        var swagger = await unauth.GetAsync("/swagger/v1/swagger.json");
        swagger.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
