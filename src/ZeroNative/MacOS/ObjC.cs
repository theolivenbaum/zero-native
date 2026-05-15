using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZeroNative.MacOS;

/// <summary>
/// Bare-bones Objective-C runtime interop. Enough to host AppKit/WebKit on macOS,
/// including registering custom subclasses that conform to delegate protocols
/// (WKScriptMessageHandler, NSWindowDelegate, NSApplicationDelegate, WKURLSchemeHandler).
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class ObjC
{
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    [LibraryImport(ObjCLib, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetClass(string name);

    [LibraryImport(ObjCLib, EntryPoint = "objc_getProtocol", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetProtocol(string name);

    [LibraryImport(ObjCLib, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr Sel(string name);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend(IntPtr target, IntPtr sel);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend(IntPtr target, IntPtr sel, IntPtr arg1);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend(IntPtr target, IntPtr sel, IntPtr arg1, IntPtr arg2);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend(IntPtr target, IntPtr sel, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend(IntPtr target, IntPtr sel, IntPtr arg1, IntPtr arg2, IntPtr arg3, IntPtr arg4);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool MsgSendBool(IntPtr target, IntPtr sel);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool MsgSendBool(IntPtr target, IntPtr sel, IntPtr arg1);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial long MsgSendLong(IntPtr target, IntPtr sel);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial nint MsgSendNInt(IntPtr target, IntPtr sel);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial double MsgSendDouble(IntPtr target, IntPtr sel);

    // Class machinery used to define custom delegate classes.
    [LibraryImport(ObjCLib, EntryPoint = "objc_allocateClassPair", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr AllocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [LibraryImport(ObjCLib, EntryPoint = "objc_registerClassPair")]
    public static partial void RegisterClassPair(IntPtr cls);

    [LibraryImport(ObjCLib, EntryPoint = "class_addMethod", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ClassAddMethod(IntPtr cls, IntPtr selector, IntPtr imp, string typeEncoding);

    [LibraryImport(ObjCLib, EntryPoint = "class_addProtocol")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ClassAddProtocol(IntPtr cls, IntPtr protocol);

    [LibraryImport(ObjCLib, EntryPoint = "class_addIvar", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ClassAddIvar(IntPtr cls, string name, nuint size, byte alignment, string typeEncoding);

    // CGRect/CGPoint/CGSize use natural alignment on every macOS ABI.
    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public double X, Y, Width, Height;
        public CGRect(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NSRange
    {
        public nuint Location, Length;
    }

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend_CGRect(IntPtr target, IntPtr sel, CGRect frame);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend_CGRect_NSUInteger(IntPtr target, IntPtr sel, CGRect frame, nuint mask, nuint backing, [MarshalAs(UnmanagedType.U1)] bool defer);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend_CGRect_IntPtr(IntPtr target, IntPtr sel, CGRect frame, IntPtr arg);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend_CGRect_Bool(IntPtr target, IntPtr sel, CGRect frame, [MarshalAs(UnmanagedType.U1)] bool display);

    // CGRect-returning message send. Both x86_64 and arm64 macOS return CGRect by value
    // through the standard struct-return convention so the regular objc_msgSend works.
    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial CGRect MsgSend_RetCGRect(IntPtr target, IntPtr sel);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend(IntPtr target, IntPtr sel, nuint arg1);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend(IntPtr target, IntPtr sel, IntPtr arg1, nuint arg2);

    public static IntPtr NSString(string s)
    {
        var cls = GetClass("NSString");
        var sel = Sel("stringWithUTF8String:");
        var bytes = System.Text.Encoding.UTF8.GetBytes(s + '\0');
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        try
        {
            return MsgSend(cls, sel, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Reads a UTF-8 string out of an NSString (or any string-coercible object).</summary>
    public static string? ReadNSString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return null;
        var utf8 = MsgSend(nsString, Sel("UTF8String"));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    /// <summary>Reads bytes out of an NSData.</summary>
    public static byte[] ReadNSData(IntPtr nsData)
    {
        if (nsData == IntPtr.Zero) return Array.Empty<byte>();
        var length = (long)MsgSendNInt(nsData, Sel("length"));
        var bytes = MsgSend(nsData, Sel("bytes"));
        if (bytes == IntPtr.Zero || length <= 0) return Array.Empty<byte>();
        var buffer = new byte[length];
        Marshal.Copy(bytes, buffer, 0, (int)length);
        return buffer;
    }

    /// <summary>Creates an NSData from the given byte slice (the data is copied).</summary>
    public static IntPtr NSData(ReadOnlySpan<byte> data)
    {
        var cls = GetClass("NSData");
        var sel = Sel("dataWithBytes:length:");
        unsafe
        {
            fixed (byte* p = data)
            {
                return MsgSend(cls, sel, (IntPtr)p, (nuint)data.Length);
            }
        }
    }

    /// <summary>Reads an NSURL's absoluteString as a managed string.</summary>
    public static string? ReadNSUrl(IntPtr nsUrl)
    {
        if (nsUrl == IntPtr.Zero) return null;
        var s = MsgSend(nsUrl, Sel("absoluteString"));
        return ReadNSString(s);
    }
}
