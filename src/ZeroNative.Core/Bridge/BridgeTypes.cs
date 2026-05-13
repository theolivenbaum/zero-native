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

    /// <summary>
    /// Outcome of a dispatch call. For synchronous handlers the response is
    /// available immediately. For asynchronous handlers the response is null
    /// until the handler invokes the supplied responder.
    /// </summary>
    public readonly record struct DispatchResult(string? Response, bool IsAsync);

    public string Dispatch(string raw, BridgeSource source)
    {
        var (parsed, error, request) = TryPrepare(raw, source);
        if (!parsed) return error!;

        var sync = Registry.Find(request!.Command);
        if (sync is null)
        {
            var async = Registry.FindAsync(request.Command);
            if (async is not null)
            {
                return Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.HandlerFailed,
                    "Async handler invoked via synchronous Dispatch; use DispatchAsync or RouteAsync.");
            }
            return Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.UnknownCommand, "Bridge command is not registered");
        }

        try
        {
            var result = sync.Invoke(new BridgeInvocation(request, source));
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

    /// <summary>
    /// Dispatches a message, preferring async handlers when registered.
    /// When an async handler runs, the returned <see cref="DispatchResult"/>
    /// has a null response — the response will be delivered through the
    /// supplied <paramref name="asyncResponder"/>.
    /// </summary>
    public async ValueTask<DispatchResult> DispatchAsync(
        string raw,
        BridgeSource source,
        Func<BridgeSource, string, ValueTask>? asyncResponder = null)
    {
        var (parsed, error, request) = TryPrepare(raw, source);
        if (!parsed) return new DispatchResult(error, false);

        var async = Registry.FindAsync(request!.Command);
        if (async is not null)
        {
            if (asyncResponder is null)
            {
                return new DispatchResult(
                    Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.HandlerFailed,
                        "Async handler requires an asyncResponder."),
                    false);
            }

            try
            {
                var responder = new AsyncBridgeResponder(source, asyncResponder);
                await async.Invoke(new BridgeInvocation(request, source), responder).ConfigureAwait(false);
                return new DispatchResult(null, true);
            }
            catch (BridgeHandlerException ex)
            {
                return new DispatchResult(
                    Bridge.WriteErrorResponse(request.Id, ex.Code, ex.Message), false);
            }
            catch (Exception ex)
            {
                return new DispatchResult(
                    Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.HandlerFailed, ex.GetType().Name),
                    false);
            }
        }

        // Fall back to synchronous dispatch with the same parsed request.
        var sync = Registry.Find(request.Command);
        if (sync is null)
        {
            return new DispatchResult(
                Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.UnknownCommand, "Bridge command is not registered"),
                false);
        }

        try
        {
            var result = sync.Invoke(new BridgeInvocation(request, source));
            return new DispatchResult(
                Bridge.WriteSuccessResponse(request.Id, string.IsNullOrEmpty(result) ? "null" : result),
                false);
        }
        catch (BridgeHandlerException ex)
        {
            return new DispatchResult(
                Bridge.WriteErrorResponse(request.Id, ex.Code, ex.Message), false);
        }
        catch (Exception ex)
        {
            return new DispatchResult(
                Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.HandlerFailed, ex.GetType().Name),
                false);
        }
    }

    private (bool Parsed, string? Error, BridgeRequest? Request) TryPrepare(string raw, BridgeSource source)
    {
        if (raw.Length > BridgeLimits.MaxMessageBytes)
            return (false, Bridge.WriteErrorResponse("", BridgeErrorCode.PayloadTooLarge, "Bridge request is too large"), null);

        BridgeRequest request;
        try
        {
            request = Bridge.ParseRequest(raw);
        }
        catch (BridgeParseException)
        {
            return (false, Bridge.WriteErrorResponse("", BridgeErrorCode.InvalidRequest, "Bridge request is malformed"), null);
        }

        if (!Policy.Allows(request.Command, source.Origin))
            return (false, Bridge.WriteErrorResponse(request.Id, BridgeErrorCode.PermissionDenied, "Bridge command is not permitted"), null);

        return (true, null, request);
    }
}

public sealed class BridgeParseException : Exception
{
    public BridgeParseException(string message) : base(message) { }
}
