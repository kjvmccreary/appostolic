using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.Application.Privacy;

/// <summary>
/// Helper utilities to push logging scopes that carry both redacted and hashed PII forms
/// (email/phone) for correlation while avoiding raw PII in structured logs.
/// </summary>
public static class LoggingPIIScope
{
    /// <summary>
    /// Creates a logging scope containing redacted + hashed email if hashing enabled, or just redacted.
    /// Raw value is never included.
    /// </summary>
    public static IDisposable? BeginEmailScope(ILogger logger, string? email, IPIIHasher hasher, IOptions<PrivacyOptions> options)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var redacted = PIIRedactor.RedactEmail(email);
        if (!options.Value.PIIHashingEnabled)
        {
            return logger.BeginScope(new Dictionary<string, object>
            {
                ["user.email.redacted"] = redacted
            });
        }
        var hash = hasher.HashEmail(email);
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["user.email.redacted"] = redacted,
            ["user.email.hash"] = hash
        });
    }

    /// <summary>
    /// Creates a logging scope containing redacted + hashed phone if hashing enabled, or just redacted.
    /// </summary>
    public static IDisposable? BeginPhoneScope(ILogger logger, string? phone, IPIIHasher hasher, IOptions<PrivacyOptions> options)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var redacted = PIIRedactor.RedactPhone(phone);
        if (!options.Value.PIIHashingEnabled)
        {
            return logger.BeginScope(new Dictionary<string, object>
            {
                ["user.phone.redacted"] = redacted
            });
        }
        var hash = hasher.HashPhone(phone);
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["user.phone.redacted"] = redacted,
            ["user.phone.hash"] = hash
        });
    }
}
