using System.Text.Json;
using System.Text.Json.Serialization;

namespace Appostolic.Api.Tests.TestUtilities;

public static class Json
{
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static string Stringify(object value, bool pretty = false)
        => JsonSerializer.Serialize(value, pretty ? PrettyOptions : CompactOptions);

    public static T Parse<T>(string json)
        => JsonSerializer.Deserialize<T>(json, CompactOptions)!;
}
