using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.Application.Auth;

/* Story 6: Structured security event writer (schema v1). Emits JSON line events via ILogger with category "Security.Auth".
    Ensures bounded vocabulary & no PII (no emails, no plaintext tokens). */
public interface ISecurityEventWriter
{
    void Emit(SecurityEvent evt);
    SecurityEvent Create(string type, Action<SecurityEventBuilder> configure);
}

// Internal builder to enforce controlled construction of events.
public class SecurityEventBuilder
{
    private readonly SecurityEvent _evt = new();
    internal SecurityEvent Build() => _evt;

    public SecurityEventBuilder User(Guid userId) { _evt.UserId = userId; return this; }
    public SecurityEventBuilder Tenant(Guid tenantId) { _evt.TenantId = tenantId; return this; }
    public SecurityEventBuilder Refresh(Guid refreshId) { _evt.RefreshId = refreshId; return this; }
    public SecurityEventBuilder Ip(string ip) { _evt.Ip = ip; return this; }
    public SecurityEventBuilder Reason(string reason) { _evt.Reason = reason; return this; }
    public SecurityEventBuilder Meta(string key, string value)
    {
        _evt.Meta ??= new();
        _evt.Meta[key] = value;
        return this;
    }
}

// Security event schema (v=1). Only GUIDs & bounded machine codes; meta is small string map.
public record SecurityEvent
{
    public int V { get; init; } = 1;
    public DateTime Ts { get; init; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty; // login_failure, refresh_reuse, etc.

    [JsonPropertyName("user_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? UserId { get; set; }
    [JsonPropertyName("tenant_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? TenantId { get; set; }
    [JsonPropertyName("refresh_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? RefreshId { get; set; }
    [JsonPropertyName("ip"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ip { get; set; }
    [JsonPropertyName("reason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
    [JsonPropertyName("meta"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string,string>? Meta { get; set; } = new();
}

// Concrete writer. Handles enable flag + vocabulary validation.
public class SecurityEventWriter : ISecurityEventWriter
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "login_failure", "refresh_reuse", "refresh_expired", "refresh_invalid", "refresh_rate_limited", "logout_all_user", "logout_all_tenant", "session_revoked_single"
    };
    private static readonly HashSet<string> AllowedReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        // login
        "invalid_credentials", "unknown_user", "missing_fields",
        // refresh
        "refresh_reuse", "refresh_expired", "refresh_invalid", "refresh_rate_limited",
        // logout / session
        "user_requested", "admin_forced"
    };

    private readonly ILogger _logger;
    private readonly bool _enabled;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SecurityEventWriter(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _logger = loggerFactory.CreateLogger("Security.Auth");
        _enabled = (configuration["AUTH__SECURITY_EVENTS__ENABLED"] ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public SecurityEvent Create(string type, Action<SecurityEventBuilder> configure)
    {
        if (!AllowedTypes.Contains(type)) throw new ArgumentException($"Unsupported security event type '{type}'", nameof(type));
        var b = new SecurityEventBuilder();
        configure(b);
        var evt = b.Build();
        evt.Type = type;
        if (evt.Reason != null && !AllowedReasons.Contains(evt.Reason))
        {
            throw new ArgumentException($"Unsupported security event reason '{evt.Reason}'", nameof(type));
        }
        return evt;
    }

    public void Emit(SecurityEvent evt)
    {
        if (!_enabled) return;
        // PII guard: basic heuristics (no '@' symbol inside reason or meta values)
        if ((evt.Reason != null && evt.Reason.Contains('@')) || (evt.Meta != null && evt.Meta.Values.Any(v => v.Contains('@'))))
        {
            throw new InvalidOperationException("PII detected in security event payload (email-like token)");
        }
        var json = JsonSerializer.Serialize(evt, JsonOptions);
        _logger.LogInformation("SECURITY_EVENT {json}", json);
        AuthMetrics.IncrementSecurityEvent(evt.Type);
    }
}
