using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Appostolic.Api.Domain.Guardrails;

namespace Appostolic.Api.Application.Guardrails;

/// <summary>
/// Configuration options controlling guardrail evaluation behavior.
/// </summary>
public sealed class GuardrailEvaluatorOptions
{
    /// <summary>
    /// Slug of the system baseline policy. Defaults to "system-core" if not provided.
    /// </summary>
    public string SystemPolicySlug { get; set; } = "system-core";

    /// <summary>
    /// Duration to cache immutable policy documents (system + denomination) in memory.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Request context for a guardrail evaluation.
/// </summary>
public sealed record GuardrailEvaluationContext
{
    public Guid TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string PolicyKey { get; init; } = "default";
    public IReadOnlyList<string> Signals { get; init; } = Array.Empty<string>();
    public string? Channel { get; init; }
    public string? PromptSummary { get; init; }
    public IReadOnlyList<string>? PresetIds { get; init; }
}

/// <summary>
/// Aggregated result of a guardrail evaluation including decision, matched rules, and policy trace.
/// </summary>
public sealed record GuardrailEvaluationResult(
    GuardrailDecision Decision,
    string ReasonCode,
    IReadOnlyList<string> MatchedSignals,
    GuardrailPolicySnapshot Snapshot,
    IReadOnlyList<GuardrailMatch> Matches,
    IReadOnlyList<GuardrailPolicyTraceEntry> Trace
);

/// <summary>
/// Final merged policy snapshot (allow/deny/escalate sets) after processing all layers.
/// </summary>
public sealed record GuardrailPolicySnapshot(
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny,
    IReadOnlyList<string> Escalate
);

/// <summary>
/// Describes the rule that influenced the outcome and where it originated.
/// </summary>
public sealed record GuardrailMatch(
    string Rule,
    string RuleType,
    GuardrailPolicySource Source,
    string SourceId,
    GuardrailPolicyLayer? Layer
);

/// <summary>
/// Trace entry capturing which rules were introduced by a particular policy layer.
/// </summary>
public sealed record GuardrailPolicyTraceEntry(
    GuardrailPolicySource Source,
    string SourceId,
    GuardrailPolicyLayer? Layer,
    IReadOnlyList<string> AddedAllow,
    IReadOnlyList<string> AddedDeny,
    IReadOnlyList<string> AddedEscalate
);

/// <summary>
/// Enumerates the policy source applied during merge.
/// </summary>
public enum GuardrailPolicySource
{
    System,
    Denomination,
    TenantBase,
    TenantOverride,
    User
}

/// <summary>
/// Parsed guardrail policy document containing allow/deny/escalate lists and any referenced preset identifiers.
/// </summary>
internal sealed record GuardrailPolicyDefinition(
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny,
    IReadOnlyList<string> Escalate,
    IReadOnlyList<string> PresetIds
);

internal sealed record ResolvedPolicy(GuardrailPolicySource Source, string SourceId, GuardrailPolicyLayer? Layer, GuardrailPolicyDefinition Definition);

/// <summary>
/// Evaluates guardrail policies by merging system, denomination, tenant, and user layers before applying signals.
/// Produces a decision (allow/escalate/deny) and supporting trace data.
/// </summary>
public sealed class GuardrailEvaluator : IGuardrailEvaluator
{
    private readonly AppDbContext _db;
    private readonly ILogger<GuardrailEvaluator> _logger;
    private readonly GuardrailEvaluatorOptions _options;
    private readonly IMemoryCache _cache;

    public GuardrailEvaluator(AppDbContext db, ILogger<GuardrailEvaluator> logger, IOptions<GuardrailEvaluatorOptions> options, IMemoryCache cache)
    {
        _db = db;
        _logger = logger;
        _options = options.Value;
        _cache = cache;
    }

    /// <summary>
    /// Merge policies and evaluate incoming signals, returning a structured guardrail decision.
    /// </summary>
    public async Task<GuardrailEvaluationResult> EvaluateAsync(GuardrailEvaluationContext context, CancellationToken ct = default)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var stopwatch = Stopwatch.StartNew();

        var normalizedSignals = context.Signals
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var presetIds = new HashSet<string>(context.PresetIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var policies = new List<ResolvedPolicy>();

        // System policy
        var systemPolicy = await LoadSystemPolicyAsync(ct);
        if (systemPolicy != null)
        {
            policies.Add(systemPolicy);
        }
        else
        {
            _logger.LogWarning("System guardrail policy '{Slug}' not found.", _options.SystemPolicySlug);
        }

        // Tenant policies (base + overrides)
        var tenantPolicies = await _db.GuardrailTenantPolicies.AsNoTracking()
            .Where(p => p.TenantId == context.TenantId && p.Key == context.PolicyKey && p.IsActive)
            .ToListAsync(ct);

        var orderedTenantPolicies = tenantPolicies
            .Where(p => p.Layer != GuardrailPolicyLayer.Draft)
            .OrderBy(p => p.Layer)
            .ThenBy(p => p.PublishedAt ?? p.UpdatedAt ?? p.CreatedAt)
            .ToList();

        foreach (var tenantPolicy in orderedTenantPolicies)
        {
            var definition = ParseDefinition(tenantPolicy.Definition);
            foreach (var preset in definition.PresetIds)
            {
                presetIds.Add(preset);
            }
            if (!string.IsNullOrWhiteSpace(tenantPolicy.DerivedFromPresetId))
            {
                presetIds.Add(tenantPolicy.DerivedFromPresetId);
            }

            var source = tenantPolicy.Layer == GuardrailPolicyLayer.TenantBase
                ? GuardrailPolicySource.TenantBase
                : GuardrailPolicySource.TenantOverride;

            policies.Add(new ResolvedPolicy(source, tenantPolicy.Id.ToString(), tenantPolicy.Layer, definition));
        }

        // User preferences (if provided)
        GuardrailUserPreference? userPreference = null;
        if (context.UserId.HasValue)
        {
            userPreference = await _db.GuardrailUserPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantId == context.TenantId && p.UserId == context.UserId.Value, ct);
        }

        if (userPreference != null)
        {
            var definition = ParseDefinition(userPreference.Preferences);
            foreach (var preset in definition.PresetIds)
            {
                presetIds.Add(preset);
            }
            policies.Add(new ResolvedPolicy(GuardrailPolicySource.User, userPreference.Id.ToString(), null, definition));
        }

        // Denomination presets referenced either explicitly or via policy metadata
        if (presetIds.Count > 0)
        {
            var resolved = await LoadPresetPoliciesAsync(presetIds, ct);
            foreach (var preset in resolved)
            {
                policies.Insert(systemPolicy != null ? 1 : 0, preset);
            }
        }

        // Merge policies in sequence and collect trace
        var accumulator = new GuardrailRuleAccumulator();
        var trace = new List<GuardrailPolicyTraceEntry>();
        foreach (var policy in policies)
        {
            var (addedAllow, addedDeny, addedEscalate) = accumulator.Merge(policy.Definition);
            if (addedAllow.Count == 0 && addedDeny.Count == 0 && addedEscalate.Count == 0)
            {
                continue;
            }
            trace.Add(new GuardrailPolicyTraceEntry(
                policy.Source,
                policy.SourceId,
                policy.Layer,
                addedAllow,
                addedDeny,
                addedEscalate
            ));
        }

        var snapshot = new GuardrailPolicySnapshot(
            accumulator.Allow.ToArray(),
            accumulator.Deny.ToArray(),
            accumulator.Escalate.ToArray()
        );

        // Evaluate signals
    var signalMatches = EvaluateSignals(normalizedSignals, trace, snapshot);
    var decision = DetermineDecision(signalMatches, out var reasonCode, out var matchedSignals, out var decisionMatches);

        stopwatch.Stop();
        GuardrailMetrics.RecordPreflight(stopwatch.Elapsed.TotalMilliseconds, decision, context.Channel);
        foreach (var match in decisionMatches)
        {
            GuardrailMetrics.RecordRuleMatch(match.RuleType, match.Source.ToString().ToLowerInvariant());
        }

        return new GuardrailEvaluationResult(
            decision,
            reasonCode,
            matchedSignals,
            snapshot,
            decisionMatches,
            trace
        );
    }

    private async Task<ResolvedPolicy?> LoadSystemPolicyAsync(CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(_options.SystemPolicySlug) ? "system-core" : _options.SystemPolicySlug;
        var cacheKey = $"guardrail/system/{slug}";
        if (_cache.TryGetValue(cacheKey, out ResolvedPolicy? cached))
        {
            return cached;
        }

        var entity = await _db.GuardrailSystemPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);
        if (entity is null) return null;

        var definition = ParseDefinition(entity.Definition);
        var resolved = new ResolvedPolicy(GuardrailPolicySource.System, entity.Slug, null, definition);
        _cache.Set(cacheKey, resolved, _options.CacheDuration);
        return resolved;
    }

    private async Task<List<ResolvedPolicy>> LoadPresetPoliciesAsync(IEnumerable<string> presetIds, CancellationToken ct)
    {
        var resolved = new List<ResolvedPolicy>();
        var normalized = presetIds
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0) return resolved;

        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in normalized)
        {
            var cacheKey = $"guardrail/preset/{id.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out ResolvedPolicy? cached) && cached is not null)
            {
                resolved.Add(cached);
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count != 0)
        {
            var entities = await _db.GuardrailDenominationPolicies.AsNoTracking()
                .Where(p => missing.Contains(p.Id))
                .ToListAsync(ct);
            foreach (var preset in entities)
            {
                var definition = ParseDefinition(preset.Definition);
                var resolvedPolicy = new ResolvedPolicy(GuardrailPolicySource.Denomination, preset.Id, null, definition);
                resolved.Add(resolvedPolicy);
                _cache.Set($"guardrail/preset/{preset.Id.ToLowerInvariant()}", resolvedPolicy, _options.CacheDuration);
            }
        }

        // Maintain deterministic ordering (alphabetical)
        return resolved
            .OrderBy(r => r.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static GuardrailPolicyDefinition ParseDefinition(JsonDocument document)
    {
        var root = document.RootElement;
        var allow = ExtractArray(root, "allow");
        var deny = ExtractArray(root, "deny");
        var escalate = ExtractArray(root, "escalate");
        var presets = ExtractPresets(root);
        return new GuardrailPolicyDefinition(allow, deny, escalate, presets);
    }

    private static List<string> ExtractArray(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array)
        {
            return property.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!.Trim().ToLowerInvariant())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();
        }
        return new List<string>();
    }

    private static List<string> ExtractPresets(JsonElement root)
    {
        var presets = new List<string>();
        if (root.TryGetProperty("presets", out var presetsNode) && presetsNode.ValueKind == JsonValueKind.Object)
        {
            if (presetsNode.TryGetProperty("denominations", out var denomNode) && denomNode.ValueKind == JsonValueKind.Array)
            {
                presets.AddRange(denomNode.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }
        if (root.TryGetProperty("inherits", out var inheritsNode) && inheritsNode.ValueKind == JsonValueKind.String)
        {
            var inheritValue = inheritsNode.GetString();
            if (!string.IsNullOrWhiteSpace(inheritValue)) presets.Add(inheritValue.Trim());
        }
        return presets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static GuardrailDecision DetermineDecision(
        GuardrailSignalMatches matches,
        out string reasonCode,
        out List<string> matchedSignals,
        out List<GuardrailMatch> decisionMatches)
    {
        if (matches.DenyMatches.Count > 0)
        {
            var first = matches.DenyMatches[0];
            reasonCode = $"deny:{first.Rule}";
            matchedSignals = matches.DenySignals;
            decisionMatches = matches.DenyMatches;
            return GuardrailDecision.Deny;
        }
        if (matches.EscalateMatches.Count > 0)
        {
            var first = matches.EscalateMatches[0];
            reasonCode = $"escalate:{first.Rule}";
            matchedSignals = matches.EscalateSignals;
            decisionMatches = matches.EscalateMatches;
            return GuardrailDecision.Escalate;
        }
        if (matches.AllowMatches.Count > 0)
        {
            var first = matches.AllowMatches[0];
            reasonCode = $"allow:{first.Rule}";
            matchedSignals = matches.AllowSignals;
            decisionMatches = matches.AllowMatches;
            return GuardrailDecision.Allow;
        }
        reasonCode = "allow:default";
        matchedSignals = new List<string>();
        decisionMatches = new List<GuardrailMatch>();
        return GuardrailDecision.Allow;
    }

    private static GuardrailSignalMatches EvaluateSignals(IReadOnlyList<string> signals, IReadOnlyList<GuardrailPolicyTraceEntry> trace, GuardrailPolicySnapshot snapshot)
    {
        var denyMatches = new List<GuardrailMatch>();
        var escalateMatches = new List<GuardrailMatch>();
        var allowMatches = new List<GuardrailMatch>();

        var denySignals = new List<string>();
        var escalateSignals = new List<string>();
        var allowSignals = new List<string>();

        foreach (var signal in signals)
        {
            if (snapshot.Deny.Contains(signal))
            {
                denySignals.Add(signal);
                if (TryLocateMatch(trace, signal, "deny", out var match))
                {
                    denyMatches.Add(match);
                }
                continue;
            }
            if (snapshot.Escalate.Contains(signal))
            {
                escalateSignals.Add(signal);
                if (TryLocateMatch(trace, signal, "escalate", out var match))
                {
                    escalateMatches.Add(match);
                }
                continue;
            }
            if (snapshot.Allow.Contains(signal))
            {
                allowSignals.Add(signal);
                if (TryLocateMatch(trace, signal, "allow", out var match))
                {
                    allowMatches.Add(match);
                }
            }
        }

        return new GuardrailSignalMatches(
            denyMatches, denySignals,
            escalateMatches, escalateSignals,
            allowMatches, allowSignals
        );
    }

    private static bool TryLocateMatch(IReadOnlyList<GuardrailPolicyTraceEntry> trace, string rule, string ruleType, out GuardrailMatch match)
    {
        foreach (var entry in trace)
        {
            IReadOnlyList<string> set = ruleType switch
            {
                "deny" => entry.AddedDeny,
                "escalate" => entry.AddedEscalate,
                _ => entry.AddedAllow
            };
            if (set.Contains(rule))
            {
                match = new GuardrailMatch(rule, ruleType, entry.Source, entry.SourceId, entry.Layer);
                return true;
            }
        }
        match = null!;
        return false;
    }

    private sealed record GuardrailSignalMatches(
        List<GuardrailMatch> DenyMatches,
        List<string> DenySignals,
        List<GuardrailMatch> EscalateMatches,
        List<string> EscalateSignals,
        List<GuardrailMatch> AllowMatches,
        List<string> AllowSignals
    );

    private sealed class GuardrailRuleAccumulator
    {
        public HashSet<string> Allow { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Deny { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Escalate { get; } = new(StringComparer.OrdinalIgnoreCase);

        public (IReadOnlyList<string> AddedAllow, IReadOnlyList<string> AddedDeny, IReadOnlyList<string> AddedEscalate) Merge(GuardrailPolicyDefinition definition)
        {
            var addedAllow = MergeInto(Allow, definition.Allow);
            var addedDeny = MergeInto(Deny, definition.Deny);
            var addedEscalate = MergeInto(Escalate, definition.Escalate);
            return (addedAllow, addedDeny, addedEscalate);
        }

        private static IReadOnlyList<string> MergeInto(HashSet<string> target, IReadOnlyList<string> incoming)
        {
            if (incoming.Count == 0) return Array.Empty<string>();
            var added = new List<string>();
            foreach (var item in incoming)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                if (target.Add(item))
                {
                    added.Add(item);
                }
            }
            return added;
        }
    }
}

/// <summary>
/// Contract for guardrail evaluator implementations.
/// </summary>
public interface IGuardrailEvaluator
{
    Task<GuardrailEvaluationResult> EvaluateAsync(GuardrailEvaluationContext context, CancellationToken ct = default);
}
