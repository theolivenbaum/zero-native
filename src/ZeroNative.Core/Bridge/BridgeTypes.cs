using System.Text;
using System.Text.Json;
using ZeroNative.Primitives;
using ZeroNative.Security;

namespace ZeroNative.Bridge;

public static class BridgeLimits
{
    public const int MaxMessageBytes = 1024 * 1024;
    public const int MaxResponseBytes = 1024 * 1024;
    public const int MaxResultBytes = 1024 * 1024;
    public const int MaxIdBytes = 64;
    public const int MaxCommandBytes = 128;
}

public enum BridgeErrorCode
{
    InvalidRequest,
    UnknownCommand,
    PermissionDenied,
    HandlerFailed,
    PayloadTooLarge,
    InternalError,
}

public static class BridgeErrorCodeExtensions
{
    public static string JsonName(this BridgeErrorCode code) => code switch
    {
        BridgeErrorCode.InvalidRequest => "invalid_request",
        BridgeErrorCode.UnknownCommand => "unknown_command",
        BridgeErrorCode.PermissionDenied => "permission_denied",
        BridgeErrorCode.HandlerFailed => "handler_failed",
        BridgeErrorCode.PayloadTooLarge => "payload_too_large",
        BridgeErrorCode.InternalError => "internal_error",
        _ => "internal_error",
    };
}

public readonly record struct BridgeSource(string Origin = "", ulong WindowId = 1);

public sealed record BridgeRequest(string Id, string Command, string Payload = "null");

public sealed record BridgeInvocation(BridgeRequest Request, BridgeSource Source);

public sealed record BridgeCommandPolicy(
    string Name,
    IReadOnlyList<string>? Permissions = null,
    IReadOnlyList<string>? Origins = null)
{
    public IReadOnlyList<string> Permissions { get; init; } = Permissions ?? Array.Empty<string>();
    public IReadOnlyList<string> Origins { get; init; } = Origins ?? Array.Empty<string>();
}

public sealed record BridgePolicy(
    bool Enabled = false,
    IReadOnlyList<string>? Permissions = null,
    IReadOnlyList<BridgeCommandPolicy>? Commands = null)
{
    public IReadOnlyList<string> Permissions { get; init; } = Permissions ?? Array.Empty<string>();
    public IReadOnlyList<BridgeCommandPolicy> Commands { get; init; } = Commands ?? Array.Empty<BridgeCommandPolicy>();

    public BridgeCommandPolicy? Find(string command)
        => Commands.FirstOrDefault(c => c.Name == command);

    public bool Allows(string command, string origin)
    {
        if (!Enabled) return false;
        var policy = Find(command);
        if (policy is null) return false;
        if (!SecurityPolicy.HasPermissions(Permissions, policy.Permissions)) return false;
        if (policy.Origins.Count == 0) return true;
        foreach (var allowed in policy.Origins)
        {
            if (allowed == "*") return true;
            if (allowed == origin) return true;
        }
        return false;
    }
}

public class BridgeHandlerException : Exception
{
    public BridgeErrorCode Code { get; }

    public BridgeHandlerException(string message, BridgeErrorCode code = BridgeErrorCode.HandlerFailed)
        : base(message)
    {
        Code = code;
    }
}

public abstract record BridgeHandlerBase(string Name);

public sealed record BridgeHandler(
    string Name,
    Func<BridgeInvocation, string> Invoke) : BridgeHandlerBase(Name);

public sealed record AsyncBridgeHandler(
    string Name,
    Func<BridgeInvocation, AsyncBridgeResponder, ValueTask> Invoke) : BridgeHandlerBase(Name);

public sealed class AsyncBridgeResponder
{
    private readonly BridgeSource _source;
    private readonly Func<BridgeSource, string, ValueTask> _respond;

    public AsyncBridgeResponder(BridgeSource source, Func<BridgeSource, string, ValueTask> respond)
    {
        _source = source;
        _respond = respond;
    }

    public BridgeSource Source => _source;

    public ValueTask Respond(string response) => _respond(_source, response);

    public ValueTask Success(string id, string result)
        => Respond(Bridge.WriteSuccessResponse(id, result));

    public ValueTask Fail(string id, BridgeErrorCode code, string message)
        => Respond(Bridge.WriteErrorResponse(id, code, message));
}

public sealed class BridgeRegistry
{
    private readonly Dictionary<string, BridgeHandler> _sync = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AsyncBridgeHandler> _async = new(StringComparer.Ordinal);

    public BridgeRegistry Register(BridgeHandler handler)
    {
        _sync[handler.Name] = handler;
        return this;
    }

    public BridgeRegistry Register(AsyncBridgeHandler handler)
    {
        _async[handler.Name] = handler;
        return this;
    }

    public BridgeHandler? Find(string command) =>
        _sync.TryGetValue(command, out var h) ? h : null;

    public AsyncBridgeHandler? FindAsync(string command) =>
        _async.TryGetValue(command, out var h) ? h : null;
}

public sealed class BridgeDispatcher
{
    public BridgePolicy Policy { get; init; } = new();
    public BridgeRegistry Registry { get; init; } = new();

    public string Dispatch(string raw, BridgeSource source)
    {
        if (raw.Length > BridgeLimits.MaxMessageBytes)
            return Bridge.WriteErrorResponse("", BridgeErrorCode.PayloadTooLarge, "Bridge request is too large");

        BridgeRequest request;
        try
        {
            request = Bridge.ParseRequest(raw);
        }
        catch (BridgeParseException)
        {
            return Bridge.WriteErrorResponse("", BridgeErrorCode.InvalidRequest, "Bridge request is malformed");
        }

        if (!Policy.Allows(request.Command, source.Origin))
            return Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.PermissionDenied, "Bridge command is not permitted");

        var handler = Registry.Find(request.Command);
        if (handler is null)
            return Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.UnknownCommand, "Bridge command is not registered");

        try
        {
            var result = handler.Invoke(new BridgeInvocation(request, source));
            return Bridge.WriteSuccessResponse(request.Id, string.IsNullOrEmpty(result) ? "null" : result);
        }
        catch (BridgeHandlerException ex)
        {
            return Bridge.WriteErrorResponse(request.Id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.HandlerFailed, ex.GetType().Name);
        }
    }
}

public sealed class BridgeParseException : Exception
{
    public BridgeParseException(string message) : base(message) { }
}
