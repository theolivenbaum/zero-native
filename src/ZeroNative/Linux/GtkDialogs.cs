using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Platform;

namespace ZeroNative.Linux;

/// <summary>
/// GTK3 implementations of the standard file/save/message dialogs
/// via <c>GtkFileChooserDialog</c> and <c>GtkMessageDialog</c>.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class GtkDialogs
{
    public static OpenDialogResult ShowOpen(IntPtr parent, OpenDialogOptions options)
    {
        var action = options.AllowDirectories ? Gtk.FileChooserActionSelectFolder : Gtk.FileChooserActionOpen;
        var dialog = Gtk.FileChooserDialogNew(
            string.IsNullOrEmpty(options.Title) ? null : options.Title,
            parent,
            action,
            IntPtr.Zero);
        if (dialog == IntPtr.Zero) return new OpenDialogResult(Array.Empty<string>());

        try
        {
            Gtk.DialogAddButton(dialog, "_Cancel", Gtk.ResponseCancel);
            Gtk.DialogAddButton(dialog, "_Open", Gtk.ResponseAccept);

            if (options.AllowMultiple)
                Gtk.FileChooserSetSelectMultiple(dialog, true);

            if (!string.IsNullOrEmpty(options.DefaultPath))
                Gtk.FileChooserSetCurrentFolder(dialog, options.DefaultPath);

            ApplyFilters(dialog, options.Filters);

            if (Gtk.DialogRun(dialog) != Gtk.ResponseAccept)
                return new OpenDialogResult(Array.Empty<string>());

            if (options.AllowMultiple)
            {
                var list = Gtk.FileChooserGetFilenames(dialog);
                if (list == IntPtr.Zero) return new OpenDialogResult(Array.Empty<string>());
                try
                {
                    var len = (int)Gtk.SListLength(list);
                    var paths = new List<string>(len);
                    for (var i = 0; i < len; i++)
                    {
                        var ptr = Gtk.SListNthData(list, (uint)i);
                        if (ptr == IntPtr.Zero) continue;
                        var s = Marshal.PtrToStringUTF8(ptr);
                        if (!string.IsNullOrEmpty(s)) paths.Add(s);
                        Gtk.GFree(ptr);
                    }
                    return new OpenDialogResult(paths);
                }
                finally { Gtk.SListFree(list); }
            }
            else
            {
                var ptr = Gtk.FileChooserGetFilename(dialog);
                if (ptr == IntPtr.Zero) return new OpenDialogResult(Array.Empty<string>());
                try
                {
                    var s = Marshal.PtrToStringUTF8(ptr);
                    return string.IsNullOrEmpty(s)
                        ? new OpenDialogResult(Array.Empty<string>())
                        : new OpenDialogResult(new[] { s });
                }
                finally { Gtk.GFree(ptr); }
            }
        }
        finally
        {
            Gtk.DialogDestroy(dialog);
        }
    }

    public static string? ShowSave(IntPtr parent, SaveDialogOptions options)
    {
        var dialog = Gtk.FileChooserDialogNew(
            string.IsNullOrEmpty(options.Title) ? null : options.Title,
            parent,
            Gtk.FileChooserActionSave,
            IntPtr.Zero);
        if (dialog == IntPtr.Zero) return null;

        try
        {
            Gtk.DialogAddButton(dialog, "_Cancel", Gtk.ResponseCancel);
            Gtk.DialogAddButton(dialog, "_Save", Gtk.ResponseAccept);
            Gtk.FileChooserSetOverwriteConfirmation(dialog, true);

            if (!string.IsNullOrEmpty(options.DefaultPath))
                Gtk.FileChooserSetCurrentFolder(dialog, options.DefaultPath);

            if (!string.IsNullOrEmpty(options.DefaultName))
                Gtk.FileChooserSetCurrentName(dialog, options.DefaultName);

            ApplyFilters(dialog, options.Filters);

            if (Gtk.DialogRun(dialog) != Gtk.ResponseAccept)
                return null;

            var ptr = Gtk.FileChooserGetFilename(dialog);
            if (ptr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(ptr); }
            finally { Gtk.GFree(ptr); }
        }
        finally
        {
            Gtk.DialogDestroy(dialog);
        }
    }

    public static MessageDialogResult ShowMessage(IntPtr parent, MessageDialogOptions options)
    {
        var type = options.Style switch
        {
            MessageDialogStyle.Warning => Gtk.MessageWarning,
            MessageDialogStyle.Critical => Gtk.MessageError,
            _ => Gtk.MessageInfo,
        };

        var dialog = Gtk.MessageDialogNew(parent, Gtk.DialogModal, type, Gtk.ButtonsNone, options.Message);
        if (dialog == IntPtr.Zero) return MessageDialogResult.Primary;

        try
        {
            if (!string.IsNullOrEmpty(options.Title))
                Gtk.DialogSetTitle(dialog, options.Title);
            if (!string.IsNullOrEmpty(options.InformativeText))
                Gtk.MessageDialogFormatSecondary(dialog, options.InformativeText);

            var primary = string.IsNullOrEmpty(options.PrimaryButton) ? "OK" : options.PrimaryButton;
            Gtk.DialogAddButton(dialog, primary, 1);
            if (!string.IsNullOrEmpty(options.SecondaryButton))
                Gtk.DialogAddButton(dialog, options.SecondaryButton, 2);
            if (!string.IsNullOrEmpty(options.TertiaryButton))
                Gtk.DialogAddButton(dialog, options.TertiaryButton, 3);

            var result = Gtk.DialogRun(dialog);
            return result switch
            {
                1 => MessageDialogResult.Primary,
                2 => MessageDialogResult.Secondary,
                3 => MessageDialogResult.Tertiary,
                _ => MessageDialogResult.Primary,
            };
        }
        finally
        {
            Gtk.DialogDestroy(dialog);
        }
    }

    private static void ApplyFilters(IntPtr dialog, IReadOnlyList<FileFilter> filters)
    {
        if (filters.Count == 0) return;
        foreach (var filter in filters)
        {
            var gtkFilter = Gtk.FileFilterNew();
            if (gtkFilter == IntPtr.Zero) continue;
            Gtk.FileFilterSetName(gtkFilter, filter.Name);
            foreach (var ext in filter.Extensions)
            {
                var pattern = "*." + ext.TrimStart('.');
                Gtk.FileFilterAddPattern(gtkFilter, pattern);
            }
            Gtk.FileChooserAddFilter(dialog, gtkFilter);
        }
    }
}
