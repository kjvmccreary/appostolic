namespace Appostolic.Api.App.Options;

public sealed class ToolsOptions
{
    public const string SectionName = "Tools";

    public string? FsRoot { get; set; }
    public int MaxBytes { get; set; } = 1_048_576; // default 1MB
}
