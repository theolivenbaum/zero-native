using System.Text;
using System.Text.Json;
using ZeroNative.Primitives;

namespace ZeroNative.Bridge;

public static class Bridge
{
    private const string NullJson = "null";

    public static BridgeRequest ParseRequest(string raw)
    {
        if (raw is null) throw new BridgeParseException("null raw");
        if (raw.Length > BridgeLimits.MaxMessageBytes) throw new BridgeParseException("payload too large");

        using var doc = TryParseDoc(raw);
        if (doc is null) throw new BridgeParseException("invalid json");
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new BridgeParseException("not an object");

        if (!doc.RootElement.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            throw new BridgeParseException("missing id");
        if (!doc.RootElement.TryGetProperty("command", out var cmdEl) || cmdEl.ValueKind != JsonValueKind.String)
            throw new BridgeParseException("missing command");

        var id = idEl.GetString()!;
        var command = cmdEl.GetString()!;
        if (!ValidId(id) || !ValidCommand(command))
            throw new BridgeParseException("invalid id or command");

        string payload = NullJson;
        if (doc.RootElement.TryGetProperty("payload", out var payloadEl))
        {
            payload = payloadEl.GetRawText();
        }

        return new BridgeRequest(id, command, payload);
    }

    private static JsonDocument? TryParseDoc(string raw)
    {
        try { return JsonDocument.Parse(raw); }
        catch (JsonException) { return null; }
    }

    public static string WriteSuccessResponse(string id, string result)
    {
        var value = string.IsNullOrEmpty(result) ? NullJson : result;
        if (!JsonUtilities.IsValidValue(value))
            return WriteErrorResponse(id, BridgeErrorCode.HandlerFailed, "Bridge command returned invalid JSON");

        var sb = new StringBuilder(value.Length + id.Length + 32);
        sb.Append("{\"id\":");
        JsonUtilities.AppendString(sb, id);
        sb.Append(",\"ok\":true,\"result\":");
        sb.Append(value);
        sb.Append('}');
        return sb.ToString();
    }

    public static string WriteErrorResponse(string id, BridgeErrorCode code, string message)
    {
        var sb = new StringBuilder(message.Length + id.Length + 64);
        sb.Append("{\"id\":");
        JsonUtilities.AppendString(sb, id);
        sb.Append(",\"ok\":false,\"error\":{\"code\":");
        JsonUtilities.AppendString(sb, code.JsonName());
        sb.Append(",\"message\":");
        JsonUtilities.AppendString(sb, message);
        sb.Append("}}");
        return sb.ToString();
    }

    public static bool IsValidJsonValue(string raw) => JsonUtilities.IsValidValue(raw);

    private static bool ValidId(string value)
    {
        if (value.Length == 0 || value.Length > BridgeLimits.MaxIdBytes) return false;
        foreach (var ch in value)
        {
            if (ch <= 0x1f || ch == '"' || ch == '\\') return false;
        }
        return true;
    }

    private static bool ValidCommand(string value)
    {
        if (value.Length == 0 || value.Length > BridgeLimits.MaxCommandBytes) return false;
        foreach (var ch in value)
        {
            if (ch <= 0x1f || ch == '"' || ch == '\\' || ch == '/' || ch == ' ') return false;
        }
        return true;
    }
}
