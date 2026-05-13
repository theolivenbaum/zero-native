using ZeroNative.Platform;

namespace ZeroNative.Runtime;

/// <summary>
/// Application entry. Describes the WebView source and optional lifecycle hooks.
/// Use <see cref="AppBuilder"/> or a subclass.
/// </summary>
public class App
{
    public string Name { get; init; } = "zero-native-app";
    public WebViewSource Source { get; init; } = WebViewSource.Html("<!doctype html><title>zero-native</title>");

    public Func<Runtime, WebViewSource>? SourceFactory { get; init; }
    public Action<Runtime>? OnStart { get; init; }
    public Action<Runtime, RuntimeEvent>? OnEvent { get; init; }
    public Action<Runtime>? OnStop { get; init; }

    public virtual void Start(Runtime runtime) => OnStart?.Invoke(runtime);

    public virtual void HandleEvent(Runtime runtime, RuntimeEvent ev) => OnEvent?.Invoke(runtime, ev);

    public virtual WebViewSource GetSource(Runtime runtime)
        => SourceFactory is not null ? SourceFactory(runtime) : Source;

    public virtual void Stop(Runtime runtime) => OnStop?.Invoke(runtime);
}

public sealed class AppBuilder
{
    private string _name = "zero-native-app";
    private WebViewSource _source = WebViewSource.Html("<!doctype html><title>zero-native</title>");
    private Func<Runtime, WebViewSource>? _sourceFactory;
    private Action<Runtime>? _onStart;
    private Action<Runtime, RuntimeEvent>? _onEvent;
    private Action<Runtime>? _onStop;

    public AppBuilder Named(string name) { _name = name; return this; }
    public AppBuilder WithSource(WebViewSource source) { _source = source; return this; }
    public AppBuilder WithSourceFactory(Func<Runtime, WebViewSource> factory) { _sourceFactory = factory; return this; }
    public AppBuilder OnStart(Action<Runtime> handler) { _onStart = handler; return this; }
    public AppBuilder OnEvent(Action<Runtime, RuntimeEvent> handler) { _onEvent = handler; return this; }
    public AppBuilder OnStop(Action<Runtime> handler) { _onStop = handler; return this; }

    public App Build() => new()
    {
        Name = _name,
        Source = _source,
        SourceFactory = _sourceFactory,
        OnStart = _onStart,
        OnEvent = _onEvent,
        OnStop = _onStop,
    };
}
