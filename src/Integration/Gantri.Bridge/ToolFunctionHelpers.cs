using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Gantri.Bridge;

/// <summary>
/// Shared helpers for normalizing AI function arguments and building JSON schemas.
/// Used by <see cref="PluginActionFunction"/> and <see cref="McpToolFunction"/>.
/// </summary>
internal static class ToolFunctionHelpers
{
    public static Dictionary<string, object?> NormalizeArguments(AIFunctionArguments arguments)
    {
        var normalized = new Dictionary<string, object?>(arguments.Count);
        foreach (var (key, value) in arguments)
        {
            normalized[key] = value is JsonElement je ? UnwrapJsonElement(je) : value;
        }
        return normalized;
    }

    public static object? UnwrapJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Undefined => null,
        _ => element.GetRawText()
    };

    public static JsonElement BuildFunctionSchema(string name, string description, JsonElement schema)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("title", name);
            writer.WriteString("description", description);

            if (schema.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in schema.EnumerateObject())
                {
                    prop.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }
}
