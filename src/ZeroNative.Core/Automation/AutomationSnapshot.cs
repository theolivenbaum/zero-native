using System.Globalization;
using System.Text;
using ZeroNative.Platform;
using ZeroNative.Primitives;

namespace ZeroNative.Automation;

public sealed record AutomationWindow(
    ulong Id = 1,
    string Title = "",
    RectF Bounds = default,
    bool Focused = true);

public sealed record AutomationDiagnostics(ulong FrameIndex = 0, int CommandCount = 0);

public sealed record AutomationInput(
    IReadOnlyList<AutomationWindow> Windows,
    AutomationDiagnostics Diagnostics,
    WebViewSource? Source = null)
{
    public AutomationInput(IReadOnlyList<AutomationWindow> windows) : this(windows, new AutomationDiagnostics(), null) { }
}

public static class AutomationSnapshot
{
    public static string WriteText(AutomationInput input)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append("ready=true frame=").Append(input.Diagnostics.FrameIndex)
          .Append(" commands=").Append(input.Diagnostics.CommandCount).Append('\n');

        foreach (var window in input.Windows)
        {
            sb.Append("window @w").Append(window.Id)
              .Append(" \"").Append(window.Title).Append('"')
              .Append(" bounds=(").Append(window.Bounds.X.ToString(inv))
              .Append(',').Append(window.Bounds.Y.ToString(inv))
              .Append(' ').Append(window.Bounds.Width.ToString(inv))
              .Append('x').Append(window.Bounds.Height.ToString(inv))
              .Append(')')
              .Append(" focused=").Append(window.Focused ? "true" : "false")
              .Append(" frame=").Append(input.Diagnostics.FrameIndex)
              .Append(" commands=").Append(input.Diagnostics.CommandCount)
              .Append('\n');
        }

        if (input.Source is { } source)
        {
            sb.Append("  source kind=").Append(SourceKindText(source.Kind))
              .Append(" bytes=").Append(source.Body.Length).Append('\n');
        }

        return sb.ToString();
    }

    public static string WriteAccessibility(AutomationInput input)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append("a11y root=@w1 nodes=").Append(input.Windows.Count).Append('\n');
        foreach (var window in input.Windows)
        {
            sb.Append('@').Append('w').Append(window.Id)
              .Append(" role=window name=\"").Append(window.Title).Append('"')
              .Append(" bounds=(").Append(window.Bounds.X.ToString(inv))
              .Append(',').Append(window.Bounds.Y.ToString(inv))
              .Append(' ').Append(window.Bounds.Width.ToString(inv))
              .Append('x').Append(window.Bounds.Height.ToString(inv))
              .Append(")\n");
        }
        return sb.ToString();
    }

    public static string WriteWindows(AutomationInput input)
    {
        var sb = new StringBuilder();
        foreach (var window in input.Windows)
        {
            sb.Append("window @w").Append(window.Id)
              .Append(" \"").Append(window.Title).Append('"')
              .Append(" focused=").Append(window.Focused ? "true" : "false")
              .Append('\n');
        }
        return sb.ToString();
    }

    private static string SourceKindText(WebViewSourceKind kind) => kind switch
    {
        WebViewSourceKind.Html => "html",
        WebViewSourceKind.Url => "url",
        WebViewSourceKind.Assets => "assets",
        _ => "unknown",
    };
}
