using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Platform;

namespace ZeroNative.MacOS;

/// <summary>
/// AppKit implementations of file/save/message dialogs via NSOpenPanel,
/// NSSavePanel, and NSAlert. All panels are run as modal sheets via
/// <c>runModal</c> so the caller blocks until the user picks something.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacDialogs
{
    public static OpenDialogResult ShowOpen(OpenDialogOptions options)
    {
        var panelClass = ObjC.GetClass("NSOpenPanel");
        var panel = ObjC.MsgSend(panelClass, ObjC.Sel("openPanel"));
        if (panel == IntPtr.Zero) return new OpenDialogResult(Array.Empty<string>());

        if (!string.IsNullOrEmpty(options.Title))
            ObjC.MsgSend(panel, ObjC.Sel("setTitle:"), ObjC.NSString(options.Title));

        ObjC.MsgSend(panel, ObjC.Sel("setCanChooseFiles:"), (IntPtr)(options.AllowDirectories ? 0 : 1));
        ObjC.MsgSend(panel, ObjC.Sel("setCanChooseDirectories:"), (IntPtr)(options.AllowDirectories ? 1 : 0));
        ObjC.MsgSend(panel, ObjC.Sel("setAllowsMultipleSelection:"), (IntPtr)(options.AllowMultiple ? 1 : 0));

        if (!string.IsNullOrEmpty(options.DefaultPath))
        {
            var nsUrlClass = ObjC.GetClass("NSURL");
            var url = ObjC.MsgSend(nsUrlClass, ObjC.Sel("fileURLWithPath:"), ObjC.NSString(options.DefaultPath));
            ObjC.MsgSend(panel, ObjC.Sel("setDirectoryURL:"), url);
        }

        ApplyFilters(panel, options.Filters);

        var response = (long)ObjC.MsgSendNInt(panel, ObjC.Sel("runModal"));
        if (response != AppKit.NSModalResponseOK) return new OpenDialogResult(Array.Empty<string>());

        var urls = ObjC.MsgSend(panel, ObjC.Sel("URLs"));
        if (urls == IntPtr.Zero) return new OpenDialogResult(Array.Empty<string>());
        var count = (long)ObjC.MsgSendNInt(urls, ObjC.Sel("count"));
        var paths = new List<string>((int)count);
        for (long i = 0; i < count; i++)
        {
            var url = ObjC.MsgSend(urls, ObjC.Sel("objectAtIndex:"), (nuint)i);
            var path = ObjC.MsgSend(url, ObjC.Sel("path"));
            var s = ObjC.ReadNSString(path);
            if (!string.IsNullOrEmpty(s)) paths.Add(s);
        }
        return new OpenDialogResult(paths);
    }

    public static string? ShowSave(SaveDialogOptions options)
    {
        var panelClass = ObjC.GetClass("NSSavePanel");
        var panel = ObjC.MsgSend(panelClass, ObjC.Sel("savePanel"));
        if (panel == IntPtr.Zero) return null;

        if (!string.IsNullOrEmpty(options.Title))
            ObjC.MsgSend(panel, ObjC.Sel("setTitle:"), ObjC.NSString(options.Title));
        if (!string.IsNullOrEmpty(options.DefaultName))
            ObjC.MsgSend(panel, ObjC.Sel("setNameFieldStringValue:"), ObjC.NSString(options.DefaultName));
        if (!string.IsNullOrEmpty(options.DefaultPath))
        {
            var nsUrlClass = ObjC.GetClass("NSURL");
            var url = ObjC.MsgSend(nsUrlClass, ObjC.Sel("fileURLWithPath:"), ObjC.NSString(options.DefaultPath));
            ObjC.MsgSend(panel, ObjC.Sel("setDirectoryURL:"), url);
        }

        ApplyFilters(panel, options.Filters);

        var response = (long)ObjC.MsgSendNInt(panel, ObjC.Sel("runModal"));
        if (response != AppKit.NSModalResponseOK) return null;

        var resultUrl = ObjC.MsgSend(panel, ObjC.Sel("URL"));
        if (resultUrl == IntPtr.Zero) return null;
        var path = ObjC.MsgSend(resultUrl, ObjC.Sel("path"));
        return ObjC.ReadNSString(path);
    }

    public static MessageDialogResult ShowMessage(MessageDialogOptions options)
    {
        var alertClass = ObjC.GetClass("NSAlert");
        var alert = ObjC.MsgSend(ObjC.MsgSend(alertClass, ObjC.Sel("alloc")), ObjC.Sel("init"));
        if (alert == IntPtr.Zero) return MessageDialogResult.Primary;

        var style = options.Style switch
        {
            MessageDialogStyle.Warning => AppKit.NSAlertStyleWarning,
            MessageDialogStyle.Critical => AppKit.NSAlertStyleCritical,
            _ => AppKit.NSAlertStyleInformational,
        };
        ObjC.MsgSend(alert, ObjC.Sel("setAlertStyle:"), (IntPtr)(nint)style);

        if (!string.IsNullOrEmpty(options.Message))
            ObjC.MsgSend(alert, ObjC.Sel("setMessageText:"), ObjC.NSString(options.Message));
        if (!string.IsNullOrEmpty(options.InformativeText))
            ObjC.MsgSend(alert, ObjC.Sel("setInformativeText:"), ObjC.NSString(options.InformativeText));

        var primary = string.IsNullOrEmpty(options.PrimaryButton) ? "OK" : options.PrimaryButton;
        ObjC.MsgSend(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.NSString(primary));
        if (!string.IsNullOrEmpty(options.SecondaryButton))
            ObjC.MsgSend(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.NSString(options.SecondaryButton));
        if (!string.IsNullOrEmpty(options.TertiaryButton))
            ObjC.MsgSend(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.NSString(options.TertiaryButton));

        var response = (long)ObjC.MsgSendNInt(alert, ObjC.Sel("runModal"));
        return response switch
        {
            AppKit.NSAlertFirstButtonReturn => MessageDialogResult.Primary,
            AppKit.NSAlertSecondButtonReturn => MessageDialogResult.Secondary,
            AppKit.NSAlertThirdButtonReturn => MessageDialogResult.Tertiary,
            _ => MessageDialogResult.Primary,
        };
    }

    private static void ApplyFilters(IntPtr panel, IReadOnlyList<FileFilter> filters)
    {
        if (filters.Count == 0) return;

        var nsArrayClass = ObjC.GetClass("NSMutableArray");
        var array = ObjC.MsgSend(ObjC.MsgSend(nsArrayClass, ObjC.Sel("alloc")), ObjC.Sel("init"));

        foreach (var filter in filters)
        {
            foreach (var ext in filter.Extensions)
            {
                var clean = ext.TrimStart('.');
                if (string.IsNullOrEmpty(clean) || clean == "*") continue;
                ObjC.MsgSend(array, ObjC.Sel("addObject:"), ObjC.NSString(clean));
            }
        }

        // `allowedFileTypes:` is deprecated on Big Sur+ in favor of
        // `allowedContentTypes:` (UTType), but it still works and avoids
        // requiring a UniformTypeIdentifiers dependency.
        ObjC.MsgSend(panel, ObjC.Sel("setAllowedFileTypes:"), array);
    }
}
