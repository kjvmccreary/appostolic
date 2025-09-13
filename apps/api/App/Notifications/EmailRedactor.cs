namespace Appostolic.Api.App.Notifications;

public static class EmailRedactor
{
    public static string Redact(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return email;
        var at = email.IndexOf('@');
        if (at <= 1) return "***" + email; // fallback
        var local = email[..at];
        var domain = email[(at + 1)..];
        var head = local[0];
        return $"{head}***@{domain}";
    }
}
