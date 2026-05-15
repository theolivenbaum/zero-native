using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZeroNative.MacOS;

/// <summary>
/// Helper to define a custom Objective-C class at runtime via
/// <c>objc_allocateClassPair</c>/<c>class_addMethod</c>. Used to implement
/// the WKScriptMessageHandler / NSWindowDelegate / NSApplicationDelegate /
/// WKURLSchemeHandler protocols from managed code.
///
/// Caller responsibilities:
/// - Keep each delegate alive in a field (Marshal.GetFunctionPointerForDelegate
///   does not pin the delegate; the GC can collect it if no managed reference
///   remains).
/// - Provide a correct Objective-C type encoding for each selector signature.
///   Common encodings:
///     - <c>v@:</c>          : void method, no args
///     - <c>v@:@</c>         : void method, one id arg
///     - <c>v@:@@</c>        : void method, two id args
///     - <c>v@:@@@</c>       : void method, three id args
///     - <c>c@:</c>          : returns BOOL
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class ObjcClassBuilder
{
    private readonly IntPtr _class;
    private readonly List<Delegate> _imps = new();

    public IntPtr ClassHandle => _class;

    public ObjcClassBuilder(string name, IntPtr superclass)
    {
        _class = ObjC.AllocateClassPair(superclass, name, 0);
        if (_class == IntPtr.Zero)
        {
            // Class may already be registered from a previous session; fall back to lookup.
            _class = ObjC.GetClass(name);
        }
    }

    public ObjcClassBuilder(string name, string superclassName)
        : this(name, ObjC.GetClass(superclassName)) { }

    public ObjcClassBuilder AddProtocol(string protocolName)
    {
        var protocol = ObjC.GetProtocol(protocolName);
        if (protocol != IntPtr.Zero && _class != IntPtr.Zero)
            ObjC.ClassAddProtocol(_class, protocol);
        return this;
    }

    /// <summary>
    /// Adds an instance method. The delegate must match the selector signature; the
    /// first two parameters are always <c>(IntPtr self, IntPtr selector)</c>.
    /// </summary>
    public ObjcClassBuilder AddMethod(string selectorName, Delegate impl, string typeEncoding)
    {
        if (_class == IntPtr.Zero) return this;
        _imps.Add(impl); // Hold a managed reference so the GC doesn't collect the delegate.
        var sel = ObjC.Sel(selectorName);
        var imp = Marshal.GetFunctionPointerForDelegate(impl);
        ObjC.ClassAddMethod(_class, sel, imp, typeEncoding);
        return this;
    }

    public IntPtr Register()
    {
        if (_class != IntPtr.Zero)
        {
            try { ObjC.RegisterClassPair(_class); }
            catch { /* idempotent if already registered */ }
        }
        return _class;
    }

    /// <summary>Allocates an instance via <c>[[Cls alloc] init]</c>.</summary>
    public IntPtr NewInstance()
    {
        if (_class == IntPtr.Zero) return IntPtr.Zero;
        var alloc = ObjC.MsgSend(_class, ObjC.Sel("alloc"));
        return ObjC.MsgSend(alloc, ObjC.Sel("init"));
    }
}
