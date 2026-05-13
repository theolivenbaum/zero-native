using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZeroNative.MacOS;

/// <summary>
/// Bare-bones Objective-C runtime interop. Enough to host AppKit/WebKit on macOS.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class ObjC
{
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(ObjCLib, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetClass(string name);

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
    public static partial long MsgSendLong(IntPtr target, IntPtr sel);

    // CGRect on x86_64 and arm64 macOS is passed in registers; matrix is identical for both abis.
    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public double X, Y, Width, Height;
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

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend_CGRect(IntPtr target, IntPtr sel, CGRect frame);

    [LibraryImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr MsgSend_CGRect_NSUInteger(IntPtr target, IntPtr sel, CGRect frame, nuint mask, nuint backing, [MarshalAs(UnmanagedType.U1)] bool defer);

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
}
