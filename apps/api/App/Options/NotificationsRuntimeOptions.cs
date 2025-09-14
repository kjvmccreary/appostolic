namespace Appostolic.Api.App.Options;

public sealed class NotificationsRuntimeOptions
{
    public bool RunDispatcher { get; set; } = true;
    public bool RunLegacyEmailDispatcher { get; set; } = true;
}
