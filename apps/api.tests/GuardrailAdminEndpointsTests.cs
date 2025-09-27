using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Appostolic.Api.App.Endpoints;
using Appostolic.Api.AuthTests;
using Appostolic.Api.Domain.Guardrails;
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
    payload.Audits.Should().NotBeNull();
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

    var audits = await db.GuardrailPolicyAudits.Where(a => a.TenantPolicyId == basePolicy.Id).ToListAsync();
    audits.Should().NotBeEmpty();
    audits.Should().Contain(a => a.Action == GuardrailPolicyAuditActions.TenantPublish);
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

    var audits = await db.GuardrailPolicyAudits.Where(a => a.TenantPolicyId == basePolicy.Id).ToListAsync();
    audits.Should().NotBeEmpty();
    audits.Should().Contain(a => a.Action == GuardrailPolicyAuditActions.TenantReset);
    }

    [Fact]
    public async Task SuperadminState_RequiresSuperClaim()
    {
        await SeedGuardrailDataAsync("tenant-super-check", seedDraft: true);
        var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "tenant-user@example.com", "tenant-super-check", owner: true);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/guardrails/admin/super/state");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuperadminState_ReturnsAggregatedData()
    {
        var tenantSlug = $"super-state-{Guid.NewGuid():N}".Substring(0, 20);
        await SeedGuardrailDataAsync(tenantSlug, seedDraft: true);

        var extraClaims = new[] { new Claim("superadmin", "true") };
        var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "super@example.com", tenantSlug, owner: true, extraClaims: extraClaims);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var systemPayload = new
        {
            name = "System Extra",
            description = "Additional baseline",
            definition = new
            {
                allow = new[] { "allow:extra" },
                deny = Array.Empty<string>(),
                escalate = Array.Empty<string>()
            }
        };
        var systemResponse = await _client.PutAsJsonAsync("/api/guardrails/admin/super/system/system-extra", systemPayload, JsonOptions);
        systemResponse.EnsureSuccessStatusCode();

        var presetPayload = new
        {
            name = "Extended",
            notes = "Extended preset",
            definition = new
            {
                deny = new[] { "deny:extended" }
            }
        };
        var presetResponse = await _client.PutAsJsonAsync("/api/guardrails/admin/super/presets/preset-extended", presetPayload, JsonOptions);
        presetResponse.EnsureSuccessStatusCode();

        var response = await _client.GetAsync("/api/guardrails/admin/super/state");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GuardrailSuperadminSummaryDto>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.SystemPolicies.Should().NotBeEmpty();
        payload.Presets.Should().NotBeEmpty();
        payload.Activity.Should().NotBeEmpty();
        payload.Activity.Should().AllSatisfy(a => a.Scope.Should().NotBeNullOrWhiteSpace());
        payload.SystemPolicies.Select(s => s.Slug).Should().Contain(new[] { "system-core", "system-extra" });
    }

    [Fact]
    public async Task UpsertSystemPolicy_CreatesAndUpdates()
    {
        await SeedGuardrailDataAsync("tenant-system-upsert", seedDraft: false);
        var extraClaims = new[] { new Claim("superadmin", "true") };
        var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "system-admin@example.com", "tenant-system-upsert", owner: true, extraClaims: extraClaims);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var createPayload = new
        {
            name = "Global Core",
            description = "Global baseline",
            definition = new
            {
                allow = new[] { "allow:global" },
                deny = Array.Empty<string>()
            }
        };

        var createResponse = await _client.PutAsJsonAsync("/api/guardrails/admin/super/system/global-core", createPayload, JsonOptions);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<GuardrailSystemPolicyDto>(JsonOptions);
        created.Should().NotBeNull();
        created!.Slug.Should().Be("global-core");
        created.Version.Should().Be(1);

        var updatePayload = new
        {
            name = "Global Core",
            description = "Updated global baseline",
            definition = new
            {
                allow = new[] { "allow:global", "allow:extra" },
                deny = new[] { "deny:global" }
            }
        };

        var updateResponse = await _client.PutAsJsonAsync("/api/guardrails/admin/super/system/global-core", updatePayload, JsonOptions);
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<GuardrailSystemPolicyDto>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Version.Should().Be(2);
        updated.Description.Should().Be("Updated global baseline");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.GuardrailSystemPolicies.AsNoTracking().FirstAsync(p => p.Slug == "global-core");
        persisted.Version.Should().Be(2);
    }

    [Fact]
    public async Task UpsertPreset_CreatesAndUpdates()
    {
        await SeedGuardrailDataAsync("tenant-preset-upsert", seedDraft: false);
        var extraClaims = new[] { new Claim("superadmin", "true") };
        var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "preset-admin@example.com", "tenant-preset-upsert", owner: true, extraClaims: extraClaims);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var createPayload = new
        {
            name = "Youth Extended",
            notes = "Draft preset",
            definition = new
            {
                allow = new[] { "allow:youth" }
            }
        };

        var createResponse = await _client.PutAsJsonAsync("/api/guardrails/admin/super/presets/preset-youth-extended", createPayload, JsonOptions);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<GuardrailDenominationPolicyDto>(JsonOptions);
        created.Should().NotBeNull();
        created!.Id.Should().Be("preset-youth-extended");
        created.Version.Should().Be(1);

        var updatePayload = new
        {
            name = "Youth Extended",
            notes = "Updated preset",
            definition = new
            {
                allow = new[] { "allow:youth" },
                deny = new[] { "deny:youth" }
            }
        };

        var updateResponse = await _client.PutAsJsonAsync("/api/guardrails/admin/super/presets/preset-youth-extended", updatePayload, JsonOptions);
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<GuardrailDenominationPolicyDto>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Version.Should().Be(2);
        updated.Notes.Should().Be("Updated preset");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var preset = await db.GuardrailDenominationPolicies.AsNoTracking().FirstAsync(p => p.Id == "preset-youth-extended");
        preset.Version.Should().Be(2);

        var audits = await db.GuardrailPolicyAudits.Where(a => a.PresetId == "preset-youth-extended").ToListAsync();
        audits.Should().NotBeEmpty();
        audits.Should().Contain(a => a.Action == GuardrailPolicyAuditActions.PresetUpsert);
    }

    [Fact]
    public async Task TenantAuditsEndpoint_ReturnsAuditEntries()
    {
        var tenantSlug = $"tenant-audit-{Guid.NewGuid():N}".Substring(0, 20);
        await SeedGuardrailDataAsync(tenantSlug, seedDraft: true);
        var (token, _, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "auditor@example.com", tenantSlug, owner: true);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Publish draft to create audit entry
        await _client.PostAsync($"/api/guardrails/admin/tenant/default/publish", null);

        var response = await _client.GetAsync("/api/guardrails/admin/tenant/audits");
        response.EnsureSuccessStatusCode();
    var audits = await response.Content.ReadFromJsonAsync<List<GuardrailPolicyAuditDto>>(JsonOptions);
    audits.Should().NotBeNull();
    var auditList = audits!;
    auditList.Should().NotBeEmpty();
    auditList.Should().Contain(a => a.Action == GuardrailPolicyAuditActions.TenantPublish);

    var auditId = auditList.First().Id;
        var download = await _client.GetAsync($"/api/guardrails/admin/tenant/audits/{auditId}/snapshot");
        download.EnsureSuccessStatusCode();
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var payload = await download.Content.ReadAsStringAsync();
        payload.Should().NotBeNullOrWhiteSpace();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditEntities = await db.GuardrailPolicyAudits.Where(a => a.TenantId == tenantId).ToListAsync();
        auditEntities.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SuperAuditsEndpoint_AllowsFiltering()
    {
        var tenantSlug = $"super-audit-{Guid.NewGuid():N}".Substring(0, 18);
        await SeedGuardrailDataAsync(tenantSlug, seedDraft: true);

        var extraClaims = new[] { new Claim("superadmin", "true") };
        var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, "super-auditor@example.com", tenantSlug, owner: true, extraClaims: extraClaims);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Trigger system and preset changes
        var systemPayload = new
        {
            name = "Audit Core",
            description = "Audit description",
            definition = new { allow = new[] { "allow:audit" } }
        };
        await _client.PutAsJsonAsync("/api/guardrails/admin/super/system/audit-core", systemPayload, JsonOptions);

        var presetPayload = new
        {
            name = "Audit Preset",
            notes = "Audit note",
            definition = new { deny = new[] { "deny:audit" } }
        };
        await _client.PutAsJsonAsync("/api/guardrails/admin/super/presets/audit-preset", presetPayload, JsonOptions);

        var response = await _client.GetAsync("/api/guardrails/admin/super/audits?scope=system");
        response.EnsureSuccessStatusCode();
    var audits = await response.Content.ReadFromJsonAsync<List<GuardrailPolicyAuditDto>>(JsonOptions);
    audits.Should().NotBeNull();
    var auditList = audits!;
    auditList.Should().NotBeEmpty();
    auditList.Should().OnlyContain(a => a.Scope == GuardrailPolicyAuditScopes.System);
    }

    private async Task SeedGuardrailDataAsync(string tenantSlug, bool seedDraft)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GuardrailSystemPolicies.RemoveRange(db.GuardrailSystemPolicies);
        db.GuardrailDenominationPolicies.RemoveRange(db.GuardrailDenominationPolicies);
        db.GuardrailTenantPolicies.RemoveRange(db.GuardrailTenantPolicies);
        db.GuardrailUserPreferences.RemoveRange(db.GuardrailUserPreferences);
    db.GuardrailPolicyAudits.RemoveRange(db.GuardrailPolicyAudits);
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

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        for (var i = options.Converters.Count - 1; i >= 0; i--)
        {
            if (options.Converters[i] is JsonStringEnumConverter)
            {
                options.Converters.RemoveAt(i);
            }
        }

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static JsonDocument ParseJson(string json) => JsonDocument.Parse(json);
}
