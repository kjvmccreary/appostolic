namespace Appostolic.Api.App.Options;

public sealed class NotificationTransportOptions
{
    // Mode: "channel" (default, in-process) or "redis"
    public string Mode { get; set; } = "channel";
    public RedisTransportOptions Redis { get; set; } = new();
}

public sealed class RedisTransportOptions
{
    // If provided, used verbatim (e.g., host:port,password=...,ssl=true,abortConnect=false)
    public string? ConnectionString { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6380;
    public string? Password { get; set; }
    public bool Ssl { get; set; } = false;
    // Pub/Sub channel to publish and subscribe notification IDs
    public string Channel { get; set; } = "app:notifications:queued";
}
