using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Appostolic.Api.App.Endpoints;
using Appostolic.Api.Application.Guardrails;
using Appostolic.Api.AuthTests;
using Appostolic.Api.Domain.Guardrails;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests;

/// <summary>
/// Integration tests covering the /api/guardrails/preflight endpoint and evaluator merge behavior.
/// </summary>
public class GuardrailPreflightTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public GuardrailPreflightTests(WebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preflight_Allows_WhenSignalsOnlyMatchAllow()
    {
        await ResetGuardrailTablesAsync();
        var (token, userId, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "allow@example.com", $"tenant-allow-{Guid.NewGuid():N}".Substring(0, 20));
        await SeedSystemBaselineAsync();
        await SeedTenantPolicyAsync(tenantId, GuardrailPolicyLayer.TenantBase, "{\"allow\":[\"allow:tenant-custom\"]}");

        var response = await SendPreflightAsync(token, new GuardrailPreflightRequestDto
        {
            Signals = new List<string> { "allow:tenant-custom" }
        });

        Assert.Equal(GuardrailDecision.Allow, response.Decision);
        Assert.Equal("allow:allow:tenant-custom", response.ReasonCode);
        Assert.Contains("allow:tenant-custom", response.MatchedSignals);
    }

    [Fact]
    public async Task Preflight_Denies_WhenTenantOverrideAddsDenyRule()
    {
        await ResetGuardrailTablesAsync();
        var (token, userId, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "deny@example.com", $"tenant-deny-{Guid.NewGuid():N}".Substring(0, 20));
        await SeedSystemBaselineAsync();
        await SeedTenantPolicyAsync(tenantId, GuardrailPolicyLayer.TenantBase, "{\"allow\":[\"allow:tenant-default\"]}");
        await SeedTenantPolicyAsync(tenantId, GuardrailPolicyLayer.Override, "{\"deny\":[\"deny:tenant-prohibited\"]}");

        var response = await SendPreflightAsync(token, new GuardrailPreflightRequestDto
        {
            Signals = new List<string> { "deny:tenant-prohibited" }
        });

        Assert.Equal(GuardrailDecision.Deny, response.Decision);
        Assert.Equal("deny:deny:tenant-prohibited", response.ReasonCode);
        Assert.Contains("deny:tenant-prohibited", response.MatchedSignals);
        Assert.Contains(response.Matches, m => m.Rule == "deny:tenant-prohibited" && m.Source == "tenantoverride");
    }

    [Fact]
    public async Task Preflight_Escalates_WhenPresetMatchesSignal()
    {
        await ResetGuardrailTablesAsync();
        var (token, userId, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "escalate@example.com", $"tenant-escalate-{Guid.NewGuid():N}".Substring(0, 20));
        await SeedSystemBaselineAsync();
        await SeedDenominationPresetAsync("baptist", "{\"escalate\":[\"escalate:human-review\"]}");
        await SeedTenantPolicyAsync(tenantId, GuardrailPolicyLayer.TenantBase, "{\"presets\":{\"denominations\":[\"baptist\"]}}", derivedFromPreset: "baptist");

        var response = await SendPreflightAsync(token, new GuardrailPreflightRequestDto
        {
            Signals = new List<string> { "escalate:human-review" }
        });

        Assert.Equal(GuardrailDecision.Escalate, response.Decision);
        Assert.Equal("escalate:escalate:human-review", response.ReasonCode);
        Assert.Contains(response.Matches, m => m.Source == "denomination" && m.Rule == "escalate:human-review");
    }

    private async Task<GuardrailPreflightResponseDto> SendPreflightAsync(string token, GuardrailPreflightRequestDto request)
    {
        var http = new HttpRequestMessage(HttpMethod.Post, "/api/guardrails/preflight")
        {
            Content = JsonContent.Create(request)
        };
        http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(http);
        response.EnsureSuccessStatusCode();
    var payload = await response.Content.ReadFromJsonAsync<GuardrailPreflightResponseDto>(JsonOptions);
        Assert.NotNull(payload);
        return payload!;
    }

    private async Task ResetGuardrailTablesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GuardrailSystemPolicies.RemoveRange(db.GuardrailSystemPolicies);
        db.GuardrailDenominationPolicies.RemoveRange(db.GuardrailDenominationPolicies);
        db.GuardrailTenantPolicies.RemoveRange(db.GuardrailTenantPolicies);
        db.GuardrailUserPreferences.RemoveRange(db.GuardrailUserPreferences);
        await db.SaveChangesAsync();
    }

    private async Task SeedSystemBaselineAsync()
    {
        using var scope = _factory.Services.CreateScope();
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
        using var scope = _factory.Services.CreateScope();
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
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GuardrailDenominationPolicies.Add(new GuardrailDenominationPolicy
        {
            Id = id,
            Name = id,
            Definition = ParseJson(jsonDefinition)
        });
        await db.SaveChangesAsync();
    }

    private static JsonDocument ParseJson(string json)
    {
        return JsonDocument.Parse(json);
    }
}
