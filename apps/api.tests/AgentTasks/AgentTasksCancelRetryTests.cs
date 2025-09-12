using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Appostolic.Api.Tests.AgentTasks;

public class AgentTasksCancelRetryTests : AgentTasksTestBase
{
    public AgentTasksCancelRetryTests(AgentTasksFactory factory) : base(factory) { }

    [Fact(Timeout = 10000)]
    public async Task Cancel_Pending_Task_IsCanceledImmediately()
    {
    // Create a task but suppress enqueue to keep it Pending deterministically
    var id = await CreateTaskAsync(ResearchAgentId, new { topic = "ShortCancel" }, suppressEnqueue: true);

        var resp = await Client.PostAsync($"/api/agent-tasks/{id}/cancel", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("id").GetGuid().Should().Be(id);
        payload.GetProperty("status").GetString().Should().Be("Canceled");

        // Details reflect terminal Canceled with FinishedAt and ErrorMessage
        var get = await Client.GetAsync($"/api/agent-tasks/{id}");
        get.EnsureSuccessStatusCode();
        var detail = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var status = detail.RootElement.GetProperty("status").GetString();
        status.Should().Be("Canceled");
        detail.RootElement.TryGetProperty("finishedAt", out var finishedAt).Should().BeTrue();
        finishedAt.ValueKind.Should().NotBe(JsonValueKind.Null);
        if (detail.RootElement.TryGetProperty("errorMessage", out var err))
        {
            err.GetString().Should().Contain("Canceled");
        }
    }

    [Fact(Timeout = 10000)]
    public async Task Cancel_Terminal_Task_Returns409()
    {
        var id = await CreateTaskAsync(ResearchAgentId, new { topic = "Terminal" });
        // Wait for terminal (Succeeded under mock model)
        await WaitUntilAsync(id, s => s is "Succeeded" or "Failed" or "Canceled", TimeSpan.FromSeconds(10));

        var resp = await Client.PostAsync($"/api/agent-tasks/{id}/cancel", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("message").GetString().Should().Contain("Already terminal");
    }

    [Fact(Timeout = 10000)]
    public async Task Retry_From_Succeeded_Creates_New_Task_And_Runs()
    {
        var id = await CreateTaskAsync(ResearchAgentId, new { topic = "RetryMe" });
        await WaitUntilAsync(id, s => s is "Succeeded" or "Failed" or "Canceled", TimeSpan.FromSeconds(10));

        var retry = await Client.PostAsync($"/api/agent-tasks/{id}/retry", content: null);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.Headers.Location.Should().NotBeNull();
        var summary = await retry.Content.ReadFromJsonAsync<JsonElement>();
        var newId = summary.GetProperty("id").GetGuid();
        newId.Should().NotBe(Guid.Empty);
        newId.Should().NotBe(id);

        // New task should run to a terminal state
        await WaitUntilAsync(newId, s => s is "Succeeded" or "Failed" or "Canceled", TimeSpan.FromSeconds(10));
        var (status, _, _) = await GetTaskAsync(newId);
        status.Should().BeOneOf("Succeeded", "Failed", "Canceled");
    }
}
