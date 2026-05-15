using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Platform;

namespace ZeroNative.MacOS;

/// <summary>
/// Status bar (tray) integration via <c>NSStatusBar</c> / <c>NSStatusItem</c>.
/// Each menu item is wired to a runtime-generated NSObject subclass whose
/// <c>invoke:</c> selector forwards the click back to the supplied callback.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacTray : IDisposable
{
    private const string TargetClassName = "ZeroNativeTrayTarget";

    private static IntPtr _targetClass;
    private static InvokeMethod? _invokeImpl;
    private readonly Action<uint> _onAction;
    private readonly Dictionary<IntPtr, uint> _itemTargets = new();

    private IntPtr _statusItem;
    private IntPtr _menu;
    private IntPtr _target;

    private delegate void InvokeMethod(IntPtr self, IntPtr sel, IntPtr sender);

    public MacTray(Action<uint> onAction)
    {
        _onAction = onAction;
        EnsureTargetClass();
    }

    public void Install(TrayOptions options)
    {
        Remove();

        var statusBarClass = ObjC.GetClass("NSStatusBar");
        var bar = ObjC.MsgSend(statusBarClass, ObjC.Sel("systemStatusBar"));
        // NSVariableStatusItemLength = -1 — pass as a double via objc_msgSend variant.
        _statusItem = ObjC.MsgSend(bar, ObjC.Sel("statusItemWithLength:"), unchecked((nuint)(-1L)));
        if (_statusItem == IntPtr.Zero) return;

        var button = ObjC.MsgSend(_statusItem, ObjC.Sel("button"));
        if (button != IntPtr.Zero && !string.IsNullOrEmpty(options.Tooltip))
            ObjC.MsgSend(button, ObjC.Sel("setToolTip:"), ObjC.NSString(options.Tooltip));

        if (button != IntPtr.Zero)
        {
            if (!string.IsNullOrEmpty(options.IconPath) && File.Exists(options.IconPath))
            {
                var nsImageClass = ObjC.GetClass("NSImage");
                var imgAlloc = ObjC.MsgSend(nsImageClass, ObjC.Sel("alloc"));
                var image = ObjC.MsgSend(imgAlloc, ObjC.Sel("initWithContentsOfFile:"), ObjC.NSString(options.IconPath));
                if (image != IntPtr.Zero) ObjC.MsgSend(button, ObjC.Sel("setImage:"), image);
            }
            else
            {
                // No image: show the bundle name (or "●") as a status title.
                ObjC.MsgSend(button, ObjC.Sel("setTitle:"), ObjC.NSString("●"));
            }
        }

        _target = NewTarget();
        UpdateMenu(options.Items);
    }

    public void UpdateMenu(IReadOnlyList<TrayMenuItem> items)
    {
        if (_statusItem == IntPtr.Zero) return;

        var menuClass = ObjC.GetClass("NSMenu");
        _menu = ObjC.MsgSend(ObjC.MsgSend(menuClass, ObjC.Sel("alloc")), ObjC.Sel("init"));
        _itemTargets.Clear();

        var menuItemClass = ObjC.GetClass("NSMenuItem");
        foreach (var item in items)
        {
            IntPtr menuItem;
            if (item.Separator)
            {
                menuItem = ObjC.MsgSend(menuItemClass, ObjC.Sel("separatorItem"));
            }
            else
            {
                var alloc = ObjC.MsgSend(menuItemClass, ObjC.Sel("alloc"));
                menuItem = ObjC.MsgSend(
                    alloc,
                    ObjC.Sel("initWithTitle:action:keyEquivalent:"),
                    ObjC.NSString(item.Label ?? string.Empty),
                    ObjC.Sel("invoke:"),
                    ObjC.NSString(string.Empty));
                if (!item.Enabled) ObjC.MsgSend(menuItem, ObjC.Sel("setEnabled:"), IntPtr.Zero);
                if (_target != IntPtr.Zero) ObjC.MsgSend(menuItem, ObjC.Sel("setTarget:"), _target);
                _itemTargets[menuItem] = item.Id;
            }
            ObjC.MsgSend(_menu, ObjC.Sel("addItem:"), menuItem);
        }

        ObjC.MsgSend(_statusItem, ObjC.Sel("setMenu:"), _menu);
    }

    public void Remove()
    {
        if (_statusItem != IntPtr.Zero)
        {
            var statusBarClass = ObjC.GetClass("NSStatusBar");
            var bar = ObjC.MsgSend(statusBarClass, ObjC.Sel("systemStatusBar"));
            ObjC.MsgSend(bar, ObjC.Sel("removeStatusItem:"), _statusItem);
            _statusItem = IntPtr.Zero;
        }
        if (_target != IntPtr.Zero)
        {
            s_activeTrays.Remove(_target);
            _target = IntPtr.Zero;
        }
        _menu = IntPtr.Zero;
        _itemTargets.Clear();
    }

    public void Dispose() => Remove();

    private IntPtr NewTarget()
    {
        if (_targetClass == IntPtr.Zero) return IntPtr.Zero;
        var alloc = ObjC.MsgSend(_targetClass, ObjC.Sel("alloc"));
        var target = ObjC.MsgSend(alloc, ObjC.Sel("init"));
        s_activeTrays[target] = this;
        return target;
    }

    private void OnInvoke(IntPtr sender)
    {
        if (!_itemTargets.TryGetValue(sender, out var id)) return;
        try { _onAction(id); }
        catch { /* swallow so we don't blow up the AppKit run loop */ }
    }

    private static readonly Dictionary<IntPtr, MacTray> s_activeTrays = new();

    private static void EnsureTargetClass()
    {
        if (_targetClass != IntPtr.Zero) return;

        var existing = ObjC.GetClass(TargetClassName);
        if (existing != IntPtr.Zero) { _targetClass = existing; return; }

        _invokeImpl = TargetInvoke;
        var builder = new ObjcClassBuilder(TargetClassName, "NSObject");
        builder.AddMethod("invoke:", _invokeImpl, "v@:@");
        _targetClass = builder.Register();
    }

    private static void TargetInvoke(IntPtr self, IntPtr sel, IntPtr sender)
    {
        if (s_activeTrays.TryGetValue(self, out var tray))
            tray.OnInvoke(sender);
    }
}
