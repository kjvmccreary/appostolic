namespace Appostolic.Api.App.Options;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1025;
    public string? User { get; set; }
    public string? Pass { get; set; }
}
