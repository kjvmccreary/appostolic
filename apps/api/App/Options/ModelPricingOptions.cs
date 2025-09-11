namespace Appostolic.Api.App.Options;

public sealed class ModelPricingOptions
{
    public bool Enabled { get; set; } = true;
    public Dictionary<string, ModelPrice> Models { get; set; } = new();
}

public sealed class ModelPrice
{
    public decimal InputPer1K { get; set; }
    public decimal OutputPer1K { get; set; }
}
