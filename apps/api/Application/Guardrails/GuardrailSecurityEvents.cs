using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.Application.Guardrails;

/// <summary>
/// Writes structured guardrail security events (schema v1) to the logging pipeline when evaluations deny or escalate content.
/// Mirrors the Auth security event writer while keeping guardrail-specific vocabularies isolated.
/// </summary>
public interface IGuardrailSecurityEventWriter
{
    GuardrailSecurityEvent Create(string type, Action<GuardrailSecurityEventBuilder> configure);
    void Emit(GuardrailSecurityEvent evt);
}

/// <summary>
/// Fluent builder used to construct guardrail security events with bounded fields.
/// </summary>
public sealed class GuardrailSecurityEventBuilder
{
    private readonly GuardrailSecurityEvent _evt = new();
    internal GuardrailSecurityEvent Build() => _evt;

    public GuardrailSecurityEventBuilder Tenant(Guid tenantId) { _evt.TenantId = tenantId; return this; }
    public GuardrailSecurityEventBuilder User(Guid userId) { _evt.UserId = userId; return this; }
    public GuardrailSecurityEventBuilder Channel(string channel) { _evt.Channel = channel; return this; }
    public GuardrailSecurityEventBuilder Decision(string decision) { _evt.Decision = decision; return this; }
    public GuardrailSecurityEventBuilder Rule(string ruleType, string ruleCode)
    {
        _evt.RuleType = ruleType;
        _evt.RuleCode = ruleCode;
        return this;
    }
    public GuardrailSecurityEventBuilder Source(string source)
    {
        _evt.Source = source;
        return this;
    }
    public GuardrailSecurityEventBuilder Reason(string reason)
    {
        _evt.Reason = reason;
        return this;
    }
    public GuardrailSecurityEventBuilder Meta(string key, string value)
    {
        _evt.Meta ??= new();
        _evt.Meta[key] = value;
        return this;
    }
}

/// <summary>
/// Serializable guardrail security event payload.
/// </summary>
public sealed record GuardrailSecurityEvent
{
    public int V { get; init; } = 1;
    public DateTime Ts { get; init; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? TenantId { get; set; }

    [JsonPropertyName("user_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? UserId { get; set; }

    [JsonPropertyName("channel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Channel { get; set; }

    [JsonPropertyName("decision"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Decision { get; set; }

    [JsonPropertyName("rule_type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleType { get; set; }

    [JsonPropertyName("rule_code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleCode { get; set; }

    [JsonPropertyName("source"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    [JsonPropertyName("reason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }

    [JsonPropertyName("meta"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Meta { get; set; }
}

/// <summary>
/// Concrete writer for guardrail security events. Handles enablement flag validation and ensures vocabularies remain bounded.
/// </summary>
public sealed class GuardrailSecurityEventWriter : IGuardrailSecurityEventWriter
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "guardrail_denied",
        "guardrail_escalated"
    };

    private static readonly HashSet<string> AllowedDecisions = new(StringComparer.OrdinalIgnoreCase)
    {
        "deny",
        "escalate",
        "allow"
    };

    private static readonly HashSet<string> AllowedReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "policy_match",
        "missing_policy"
    };

    private readonly ILogger _logger;
    private readonly bool _enabled;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GuardrailSecurityEventWriter(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _logger = loggerFactory.CreateLogger("Security.Guardrails");
        _enabled = (configuration["GUARDRAILS__SECURITY_EVENTS__ENABLED"] ?? "false")
            .Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public GuardrailSecurityEvent Create(string type, Action<GuardrailSecurityEventBuilder> configure)
    {
        if (!AllowedTypes.Contains(type))
            throw new ArgumentException($"Unsupported guardrail security event type '{type}'", nameof(type));

        var builder = new GuardrailSecurityEventBuilder();
        configure(builder);
        var evt = builder.Build();
        evt.Type = type;
        if (evt.Decision != null && !AllowedDecisions.Contains(evt.Decision))
            throw new ArgumentException($"Unsupported guardrail decision '{evt.Decision}'", nameof(type));
        if (evt.Reason != null && !AllowedReasons.Contains(evt.Reason))
            throw new ArgumentException($"Unsupported guardrail reason '{evt.Reason}'", nameof(type));
        return evt;
    }

    public void Emit(GuardrailSecurityEvent evt)
    {
        if (!_enabled) return;
        var json = JsonSerializer.Serialize(evt, JsonOptions);
        _logger.LogInformation("SECURITY_EVENT {json}", json);
        GuardrailMetrics.RecordSecurityEvent(evt.Type);
    }
}
