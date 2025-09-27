using System.Text.Json;
using System.Linq;
using FluentAssertions;
using Appostolic.Api.Application.Guardrails;
using Appostolic.Api.Domain.Agents;
using Appostolic.Api.Domain.Guardrails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.AgentTasks;

public class AgentTasksGuardrailTests : AgentTasksTestBase
{
    public AgentTasksGuardrailTests(AgentTasksFactory factory) : base(factory) { }

    [Fact]
    public async Task CreateTask_WithDenyGuardrail_FailsAndPersistsDecision()
    {
        await ResetGuardrailsAsync();
        var tenantId = await GetTenantIdAsync();
        await SeedSystemBaselineAsync();
        await SeedTenantPolicyAsync(tenantId, GuardrailPolicyLayer.Override, "{\"deny\":[\"deny:tenant-prohibited\"]}");

        var guardrails = new
        {
            signals = new[] { "deny:tenant-prohibited" },
            policyKey = "default",
            channel = "agent.runtime",
            promptSummary = "guarded"
        };

        var id = await CreateTaskAsync(ResearchAgentId, new { topic = "guard" }, guardrails: guardrails);
        var (status, _, _) = await GetTaskAsync(id);
        status.Should().Be("Failed");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.Set<AgentTask>().AsNoTracking().SingleAsync(t => t.Id == id);
        task.GuardrailDecision.Should().Be(GuardrailDecision.Deny);
        task.GuardrailMetadataJson.Should().NotBeNull();
        task.StartedAt.Should().BeNull();
        task.ErrorMessage.Should().Contain("Guardrail denied");
    }

    [Fact]
    public async Task GuardrailMetadata_ReturnedInDetails()
    {
        await ResetGuardrailsAsync();
        var tenantId = await GetTenantIdAsync();
        await SeedSystemBaselineAsync();
        await SeedDenominationPresetAsync("baptist", "{\"escalate\":[\"escalate:human-review\"]}");
        await SeedTenantPolicyAsync(tenantId, GuardrailPolicyLayer.TenantBase, "{\"presets\":{\"denominations\":[\"baptist\"]}}", derivedFromPreset: "baptist");

        var guardrails = new
        {
            signals = new[] { "escalate:human-review" },
            policyKey = "default",
            channel = "agent.runtime",
            promptSummary = "needs review"
        };

        var id = await CreateTaskAsync(ResearchAgentId, new { topic = "review" }, guardrails: guardrails);

        var resp = await Client.GetAsync($"/api/agent-tasks/{id}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var element = doc.RootElement;
        if (element.TryGetProperty("task", out var taskElem))
        {
            element = taskElem;
        }

        element.GetProperty("status").GetString().Should().Be("Failed");
        element.GetProperty("guardrailDecision").GetString().Should().Be("Escalate");
        var metadata = element.GetProperty("guardrailMetadata");
        metadata.ValueKind.Should().Be(JsonValueKind.Object);
        metadata.GetProperty("result").GetProperty("decision").GetString().Should().Be("Escalate");
        metadata.GetProperty("context").GetProperty("signals").EnumerateArray().Select(s => s.GetString()).Should().Contain("escalate:human-review");
    }

    private async Task ResetGuardrailsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GuardrailSystemPolicies.RemoveRange(db.GuardrailSystemPolicies);
        db.GuardrailDenominationPolicies.RemoveRange(db.GuardrailDenominationPolicies);
        db.GuardrailTenantPolicies.RemoveRange(db.GuardrailTenantPolicies);
        db.GuardrailUserPreferences.RemoveRange(db.GuardrailUserPreferences);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> GetTenantIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Name == "acme");
        return tenant.Id;
    }

    private async Task SeedSystemBaselineAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GuardrailSystemPolicies.Add(new GuardrailSystemPolicy
        {
            Id = Guid.NewGuid(),
            Slug = "system-core",
            Name = "System Baseline",
            Definition = ParseJson("{\"allow\":[\"allow:creedal-core\"],\"deny\":[\"deny:heretical-content\"],\"escalate\":[\"escalate:ambiguous-case\"]}")
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedTenantPolicyAsync(Guid tenantId, GuardrailPolicyLayer layer, string jsonDefinition, string? derivedFromPreset = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GuardrailTenantPolicies.Add(new GuardrailTenantPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Layer = layer,
            Key = "default",
            Definition = ParseJson(jsonDefinition),
            DerivedFromPresetId = derivedFromPreset,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedDenominationPresetAsync(string id, string jsonDefinition)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GuardrailDenominationPolicies.Add(new GuardrailDenominationPolicy
        {
            Id = id,
            Name = id,
            Definition = ParseJson(jsonDefinition)
        });
        await db.SaveChangesAsync();
    }

    private static JsonDocument ParseJson(string json) => JsonDocument.Parse(json);
}
