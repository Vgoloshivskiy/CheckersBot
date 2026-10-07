using System.Text.Json;
using System.Text.Json.Serialization;

namespace Checkers.Engine.Protocol;

/// <summary>JSON used on the line protocol: camelCase, nulls omitted, one compact object per line.</summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
