using Xunit;
using ZeroNative.Automation;
using ZeroNative.Platform;
using ZeroNative.Primitives;

namespace ZeroNative.Tests;

public class AutomationSnapshotTests
{
    [Fact]
    public void WriteText_EmitsReadyHeaderAndWindowRows()
    {
        var input = new AutomationInput(
            new[] { new AutomationWindow(1, "Test", new RectF(0, 0, 100, 100)) },
            new AutomationDiagnostics(7, 3),
            WebViewSource.Html("<h1>Hello</h1>"));
        var text = AutomationSnapshot.WriteText(input);

        Assert.Contains("ready=true frame=7 commands=3", text);
        Assert.Contains("window @w1 \"Test\"", text);
        Assert.Contains("bounds=(0,0 100x100)", text);
        Assert.Contains("focused=true", text);
        Assert.Contains("source kind=html", text);
    }

    [Fact]
    public void WriteText_OmitsSourceLineWhenAbsent()
    {
        var input = new AutomationInput(new[] { new AutomationWindow(1, "Test", new RectF(0, 0, 100, 100)) });
        var text = AutomationSnapshot.WriteText(input);
        Assert.DoesNotContain("source kind=", text);
    }

    [Fact]
    public void WriteAccessibility_ListsWindowsAsA11yNodes()
    {
        var input = new AutomationInput(
            new[]
            {
                new AutomationWindow(1, "First", new RectF(0, 0, 100, 100)),
                new AutomationWindow(2, "Second", new RectF(10, 10, 200, 100)),
            });

        var text = AutomationSnapshot.WriteAccessibility(input);
        Assert.Contains("a11y root=@w1 nodes=2", text);
        Assert.Contains("@w1 role=window name=\"First\"", text);
        Assert.Contains("@w2 role=window name=\"Second\"", text);
    }

    [Fact]
    public void WriteWindows_EmitsCompactFocusList()
    {
        var input = new AutomationInput(
            new[]
            {
                new AutomationWindow(1, "Main", new RectF(0, 0, 100, 100), Focused: true),
                new AutomationWindow(2, "Side", new RectF(0, 0, 100, 100), Focused: false),
            });
        var text = AutomationSnapshot.WriteWindows(input);
        Assert.Contains("window @w1 \"Main\" focused=true", text);
        Assert.Contains("window @w2 \"Side\" focused=false", text);
    }
}
