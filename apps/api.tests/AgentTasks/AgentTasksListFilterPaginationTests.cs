using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Appostolic.Api.Tests.AgentTasks;

public class AgentTasksListFilterPaginationTests : AgentTasksTestBase
{
    public AgentTasksListFilterPaginationTests(AgentTasksFactory factory) : base(factory) { }

    [Fact(Timeout = 10000)]
    public async Task Paging_Returns_XTotalCount_And_DescOrdering()
    {
        await ClearAllTasksAsync();
        var a = await CreateTaskAsync(ResearchAgentId, new { topic = "A-alpha" });
        await Task.Delay(50);
        var b = await CreateTaskAsync(ResearchAgentId, new { topic = "B-beta" });
        await Task.Delay(50);
        var c = await CreateTaskAsync(ResearchAgentId, new { topic = "C-charlie" });

        // page 1
        var r1 = await Client.GetAsync("/api/agent-tasks?take=2&skip=0");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r1.Headers.TryGetValues("X-Total-Count", out var v1).Should().BeTrue();
        int.Parse(v1!.Single()).Should().Be(3);
        var arr1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        arr1.ValueKind.Should().Be(JsonValueKind.Array);
        arr1.GetArrayLength().Should().Be(2);
        var first = arr1[0].GetProperty("id").GetGuid();
        var second = arr1[1].GetProperty("id").GetGuid();
        // Desc by createdAt means C then B
        new[] { first, second }.Should().BeEquivalentTo(new[] { c, b }, opts => opts.WithStrictOrdering());

        // page 2
        var r2 = await Client.GetAsync("/api/agent-tasks?take=2&skip=2");
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.Headers.TryGetValues("X-Total-Count", out var v2).Should().BeTrue();
        int.Parse(v2!.Single()).Should().Be(3);
        var arr2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        arr2.GetArrayLength().Should().Be(1);
        arr2[0].GetProperty("id").GetGuid().Should().Be(a);
    }

    [Fact(Timeout = 10000)]
    public async Task Status_Filter_Is_CaseInsensitive()
    {
        var id = await CreateTaskAsync(ResearchAgentId, new { topic = "StatusCase" });
        await WaitUntilAsync(id, s => s is "Succeeded" or "Failed" or "Canceled", TimeSpan.FromSeconds(10));

        var r1 = await Client.GetAsync("/api/agent-tasks?status=succeeded");
        r1.EnsureSuccessStatusCode();
        var arr1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        arr1.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Contain(id);

        var r2 = await Client.GetAsync("/api/agent-tasks?status=RUNNING");
        r2.EnsureSuccessStatusCode();
        var arr2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        // Likely empty unless timing hits Running
        arr2.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact(Timeout = 10000)]
    public async Task Agent_Filter_Returns_Only_Matching()
    {
        var a1 = await CreateTaskAsync(ResearchAgentId, new { topic = "Agent1-A" });
        var a2 = await CreateTaskAsync(ResearchAgentId, new { topic = "Agent1-B" });
        var b1 = await CreateTaskAsync(FilesAgentId, new { path = "/tmp", content = "hi" });

        var r = await Client.GetAsync($"/api/agent-tasks?agentId={ResearchAgentId}&take=10&skip=0");
        r.EnsureSuccessStatusCode();
        r.Headers.TryGetValues("X-Total-Count", out var vals).Should().BeTrue();
        var count = int.Parse(vals!.Single());
        count.Should().BeGreaterOrEqualTo(2);
        var arr = await r.Content.ReadFromJsonAsync<JsonElement>();
        var ids = arr.EnumerateArray().Select(e => e.GetProperty("agentId").GetGuid()).Distinct().ToArray();
        ids.Should().AllBeEquivalentTo(ResearchAgentId);
    }

    [Fact(Timeout = 10000)]
    public async Task Date_Range_Filter_On_CreatedAt()
    {
        var id = await CreateTaskAsync(ResearchAgentId, new { topic = "DateRange" });
        var (_, _, createdAt) = await GetTaskAsync(id);
        var before = createdAt.AddSeconds(-1).ToUniversalTime().ToString("O");
        var after = createdAt.AddSeconds(1).ToUniversalTime().ToString("O");

        var ex1 = await Client.GetAsync($"/api/agent-tasks?from={after}"); // after created → excludes
        var ex1Arr = await ex1.Content.ReadFromJsonAsync<JsonElement>();
        ex1Arr.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().NotContain(id);

        var ex2 = await Client.GetAsync($"/api/agent-tasks?to={before}"); // before created → excludes
        var ex2Arr = await ex2.Content.ReadFromJsonAsync<JsonElement>();
        ex2Arr.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inc = await Client.GetAsync($"/api/agent-tasks?from={before}&to={after}"); // bracket → includes
        var incArr = await inc.Content.ReadFromJsonAsync<JsonElement>();
        incArr.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact(Timeout = 10000)]
    public async Task FreeText_Q_Search_On_Input_And_User()
    {
        await ClearAllTasksAsync();
        var x = await CreateTaskAsync(ResearchAgentId, new { topic = "Beatitudes Alpha" });
        var y = await CreateTaskAsync(ResearchAgentId, new { topic = "Zacchaeus Beta" });

        var r1 = await Client.GetAsync("/api/agent-tasks?q=Beatitudes");
        r1.EnsureSuccessStatusCode();
        var arr1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var ids1 = arr1.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToArray();
        ids1.Should().Contain(x);
        ids1.Should().NotContain(y);

        var r2 = await Client.GetAsync("/api/agent-tasks?q=dev@example.com"); // RequestUser from dev header
        r2.EnsureSuccessStatusCode();
        var arr2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        arr2.ValueKind.Should().Be(JsonValueKind.Array);
        r2.Headers.TryGetValues("X-Total-Count", out var v2).Should().BeTrue();
        int.Parse(v2!.Single()).Should().BeGreaterOrEqualTo(2);
    }
}
