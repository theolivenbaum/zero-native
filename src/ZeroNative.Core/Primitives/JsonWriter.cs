using System.Buffers;
using System.Text;

namespace ZeroNative.Primitives;

/// <summary>
/// Lightweight JSON writer/utility helpers matching the Zig reference.
/// Uses System.Text.Json under the hood where reasonable.
/// </summary>
public static class JsonUtilities
{
    /// <summary>Encodes a string value into a JSON string literal (with surrounding quotes).</summary>
    public static string EncodeString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        AppendString(sb, value);
        return sb.ToString();
    }

    public static void AppendString(StringBuilder sb, ReadOnlySpan<char> value)
    {
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        sb.Append('"');
    }

    /// <summary>Returns true if the given JSON value is well-formed and self-contained.</summary>
    public static bool IsValidValue(ReadOnlySpan<char> raw)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw.ToString());
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    public static bool IsValidValue(ReadOnlySpan<byte> raw)
    {
        try
        {
            var reader = new System.Text.Json.Utf8JsonReader(raw, isFinalBlock: true, state: default);
            while (reader.Read())
            {
                // consume everything; if it parses we're good
            }
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads a top-level string field (no nested traversal).</summary>
    public static string? StringField(string payload, string field)
        => TopLevelField(payload, field, System.Text.Json.JsonValueKind.String) is { } element
           ? element.GetString()
           : null;

    public static float? NumberField(string payload, string field)
        => TopLevelField(payload, field, System.Text.Json.JsonValueKind.Number) is { } element
           ? (float)element.GetDouble()
           : null;

    public static ulong? UnsignedField(string payload, string field)
        => TopLevelField(payload, field, System.Text.Json.JsonValueKind.Number) is { } element
           ? element.GetUInt64()
           : null;

    public static bool? BoolField(string payload, string field)
    {
        var element = TopLevelField(payload, field, null);
        return element switch
        {
            { ValueKind: System.Text.Json.JsonValueKind.True } => true,
            { ValueKind: System.Text.Json.JsonValueKind.False } => false,
            _ => null,
        };
    }

    private static System.Text.Json.JsonElement? TopLevelField(string payload, string field, System.Text.Json.JsonValueKind? expectedKind)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(field, out var element)) return null;
            if (expectedKind is { } kind && element.ValueKind != kind) return null;
            return element.Clone();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
