using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Appostolic.Api.App.Endpoints;
using Appostolic.Api.AuthTests;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests;

public class GuardrailAdminEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    private readonly HttpClient _client;

    public GuardrailAdminEndpointsTests(WebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task GetTenantPolicies_ReturnsActiveAndDraftLayers()
    {
        var tenantSlug = $"tenant-admin-{Guid.NewGuid():N}".Substring(0, 24);
        await SeedGuardrailDataAsync(tenantSlug, seedDraft: true);

        var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "admin@example.com", tenantSlug, owner: true);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync($"/api/guardrails/admin/tenant?policyKey=default");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TenantGuardrailSummaryDto>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Key.Should().Be("default");
        payload.Policies.Should().HaveCount(2);
        payload.Policies.Select(p => p.Layer).Should().Contain(new[] { "tenantbase", "draft" });
        payload.Snapshot.Decision.Should().Be(Appostolic.Api.Application.Guardrails.GuardrailDecision.Allow);
        payload.Presets.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UpsertDraft_CreatesDraftLayer()
    {
        var tenantSlug = $"tenant-draft-{Guid.NewGuid():N}".Substring(0, 24);
        await SeedGuardrailDataAsync(tenantSlug, seedDraft: false);
        var (token, _, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "drafter@example.com", tenantSlug, owner: true);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            definition = new
            {
                allow = new[] { "allow:qna" },
                deny = Array.Empty<string>(),
                escalate = new[] { "escalate:review" }
            },
            derivedFromPresetId = "preset-core"
        };

        var response = await _client.PutAsJsonAsync($"/api/guardrails/admin/tenant/default/draft", payload, JsonOptions);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<TenantGuardrailPolicyDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.Layer.Should().Be("draft");
        dto.DerivedFromPresetId.Should().Be("preset-core");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var draft = await db.GuardrailTenantPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Key == "default" && p.Layer == Appostolic.Api.Domain.Guardrails.GuardrailPolicyLayer.Draft);
        draft.Should().NotBeNull();
        draft!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task PublishDraft_PromotesDefinitionToBase()
    {
        var tenantSlug = $"tenant-publish-{Guid.NewGuid():N}".Substring(0, 24);
        await SeedGuardrailDataAsync(tenantSlug, seedDraft: true);
        var (token, _, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "publisher@example.com", tenantSlug, owner: true);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var publishResponse = await _client.PostAsync($"/api/guardrails/admin/tenant/default/publish", null);
        publishResponse.EnsureSuccessStatusCode();
        var dto = await publishResponse.Content.ReadFromJsonAsync<TenantGuardrailPolicyDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.Layer.Should().Be("tenantbase");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var basePolicy = await db.GuardrailTenantPolicies.AsNoTracking()
            .FirstAsync(p => p.TenantId == tenantId && p.Key == "default" && p.Layer == Appostolic.Api.Domain.Guardrails.GuardrailPolicyLayer.TenantBase);
        basePolicy.Version.Should().BeGreaterThan(0);
        basePolicy.DerivedFromPresetId.Should().Be("preset-core");
    }

    [Fact]
    public async Task ResetToPreset_ReplacesBasePolicy()
    {
        var tenantSlug = $"tenant-reset-{Guid.NewGuid():N}".Substring(0, 24);
        await SeedGuardrailDataAsync(tenantSlug, seedDraft: false);
        var (token, _, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "reseter@example.com", tenantSlug, owner: true);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resetPayload = new { presetId = "preset-core" };
        var resetResponse = await _client.PostAsJsonAsync($"/api/guardrails/admin/tenant/default/reset", resetPayload, JsonOptions);
        resetResponse.EnsureSuccessStatusCode();
        var dto = await resetResponse.Content.ReadFromJsonAsync<TenantGuardrailPolicyDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.DerivedFromPresetId.Should().Be("preset-core");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var basePolicy = await db.GuardrailTenantPolicies.AsNoTracking()
            .FirstAsync(p => p.TenantId == tenantId && p.Key == "default" && p.Layer == Appostolic.Api.Domain.Guardrails.GuardrailPolicyLayer.TenantBase);
        basePolicy.DerivedFromPresetId.Should().Be("preset-core");
        var draftCount = await db.GuardrailTenantPolicies.CountAsync(p => p.TenantId == tenantId && p.Key == "default" && p.Layer == Appostolic.Api.Domain.Guardrails.GuardrailPolicyLayer.Draft);
        draftCount.Should().Be(0);
    }

    private async Task SeedGuardrailDataAsync(string tenantSlug, bool seedDraft)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GuardrailSystemPolicies.RemoveRange(db.GuardrailSystemPolicies);
        db.GuardrailDenominationPolicies.RemoveRange(db.GuardrailDenominationPolicies);
        db.GuardrailTenantPolicies.RemoveRange(db.GuardrailTenantPolicies);
        db.GuardrailUserPreferences.RemoveRange(db.GuardrailUserPreferences);
        await db.SaveChangesAsync();

        db.GuardrailSystemPolicies.Add(new Appostolic.Api.Domain.Guardrails.GuardrailSystemPolicy
        {
            Id = Guid.NewGuid(),
            Slug = "system-core",
            Name = "System Baseline",
            Definition = ParseJson("{\"allow\":[\"allow:system\"],\"deny\":[],\"escalate\":[]}")
        });

        db.GuardrailDenominationPolicies.Add(new Appostolic.Api.Domain.Guardrails.GuardrailDenominationPolicy
        {
            Id = "preset-core",
            Name = "Core",
            Notes = "Baseline preset",
            Definition = ParseJson("{\"allow\":[\"allow:preset\"]}")
        });

        var (token, userId, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "seeder@example.com", tenantSlug, owner: true);
        _ = token; // token unused; seeding ensures tenant exists

        db.GuardrailTenantPolicies.Add(new Appostolic.Api.Domain.Guardrails.GuardrailTenantPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Key = "default",
            Layer = Appostolic.Api.Domain.Guardrails.GuardrailPolicyLayer.TenantBase,
            Definition = ParseJson("{\"allow\":[\"allow:tenant\"]}"),
            IsActive = true,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            PublishedAt = DateTime.UtcNow
        });

        if (seedDraft)
        {
            db.GuardrailTenantPolicies.Add(new Appostolic.Api.Domain.Guardrails.GuardrailTenantPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Key = "default",
                Layer = Appostolic.Api.Domain.Guardrails.GuardrailPolicyLayer.Draft,
                Definition = ParseJson("{\"allow\":[\"allow:draft\"],\"presets\":{\"denominations\":[\"preset-core\"]}}"),
                DerivedFromPresetId = "preset-core",
                IsActive = false,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static JsonDocument ParseJson(string json) => JsonDocument.Parse(json);
}
