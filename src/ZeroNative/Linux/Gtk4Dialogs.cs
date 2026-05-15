using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZeroNative.Platform;

namespace ZeroNative.Linux;

/// <summary>
/// GTK4 implementations of the open/save/message dialogs via the async
/// <c>GtkFileDialog</c> / <c>GtkAlertDialog</c> APIs. GTK4 removed the
/// synchronous <c>gtk_dialog_run</c> variants from GTK3, so each call here
/// drives the request through <c>g_main_context_iteration</c> until the
/// <c>GAsyncReadyCallback</c> fires and the <c>*_finish</c> getter pulls the
/// result out.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class Gtk4Dialogs
{
    // Keep one delegate per finish-shape so the JITted thunks stay alive for
    // the lifetime of the process (GTK retains the function pointer between
    // *_async and *_finish).
    private delegate void GAsyncReadyCallback(IntPtr source, IntPtr result, IntPtr userData);

    private static readonly GAsyncReadyCallback _openCallback = OnOpenReady;
    private static readonly GAsyncReadyCallback _openMultipleCallback = OnOpenMultipleReady;
    private static readonly GAsyncReadyCallback _saveCallback = OnSaveReady;
    private static readonly GAsyncReadyCallback _selectFolderCallback = OnSelectFolderReady;
    private static readonly GAsyncReadyCallback _chooseCallback = OnChooseReady;

    public static OpenDialogResult ShowOpen(IntPtr parent, OpenDialogOptions options)
    {
        var dialog = Gtk.Gtk4FileDialogNew();
        if (dialog == IntPtr.Zero) return new OpenDialogResult(Array.Empty<string>());

        try
        {
            if (!string.IsNullOrEmpty(options.Title))
                Gtk.Gtk4FileDialogSetTitle(dialog, options.Title);

            ApplyInitialFolder(dialog, options.DefaultPath);
            ApplyFilters(dialog, options.Filters);

            // Folder picker (no equivalent to "AllowDirectories=true" with file
            // selection mixed in on GTK4 — there's a dedicated select_folder API).
            if (options.AllowDirectories)
            {
                var folderState = RunAsync(handle => Gtk.Gtk4FileDialogSelectFolder(dialog, parent, IntPtr.Zero, GetCallback(_selectFolderCallback), handle));
                return folderState.Paths.Count == 0
                    ? new OpenDialogResult(Array.Empty<string>())
                    : new OpenDialogResult(folderState.Paths);
            }

            DialogState state;
            if (options.AllowMultiple)
                state = RunAsync(handle => Gtk.Gtk4FileDialogOpenMultiple(dialog, parent, IntPtr.Zero, GetCallback(_openMultipleCallback), handle));
            else
                state = RunAsync(handle => Gtk.Gtk4FileDialogOpen(dialog, parent, IntPtr.Zero, GetCallback(_openCallback), handle));

            return state.Paths.Count == 0
                ? new OpenDialogResult(Array.Empty<string>())
                : new OpenDialogResult(state.Paths);
        }
        finally
        {
            Gtk.GObjectUnref(dialog);
        }
    }

    public static string? ShowSave(IntPtr parent, SaveDialogOptions options)
    {
        var dialog = Gtk.Gtk4FileDialogNew();
        if (dialog == IntPtr.Zero) return null;

        try
        {
            if (!string.IsNullOrEmpty(options.Title))
                Gtk.Gtk4FileDialogSetTitle(dialog, options.Title);

            ApplyInitialFolder(dialog, options.DefaultPath);
            if (!string.IsNullOrEmpty(options.DefaultName))
                Gtk.Gtk4FileDialogSetInitialName(dialog, options.DefaultName);

            ApplyFilters(dialog, options.Filters);

            var state = RunAsync(handle => Gtk.Gtk4FileDialogSave(dialog, parent, IntPtr.Zero, GetCallback(_saveCallback), handle));
            return state.Paths.Count == 0 ? null : state.Paths[0];
        }
        finally
        {
            Gtk.GObjectUnref(dialog);
        }
    }

    public static MessageDialogResult ShowMessage(IntPtr parent, MessageDialogOptions options)
    {
        var dialog = Gtk.Gtk4AlertDialogNew(options.Message ?? "");
        if (dialog == IntPtr.Zero) return MessageDialogResult.Primary;

        try
        {
            if (!string.IsNullOrEmpty(options.InformativeText))
                Gtk.Gtk4AlertDialogSetDetail(dialog, options.InformativeText);

            // gtk_alert_dialog_set_buttons takes a NULL-terminated char**.
            var labels = new List<string>(3) { string.IsNullOrEmpty(options.PrimaryButton) ? "OK" : options.PrimaryButton };
            if (!string.IsNullOrEmpty(options.SecondaryButton)) labels.Add(options.SecondaryButton);
            if (!string.IsNullOrEmpty(options.TertiaryButton)) labels.Add(options.TertiaryButton);

            var buttons = AllocateNullTerminatedUtf8Array(labels);
            try
            {
                Gtk.Gtk4AlertDialogSetButtons(dialog, buttons);
                var state = RunAsync(handle => Gtk.Gtk4AlertDialogChoose(dialog, parent, IntPtr.Zero, GetCallback(_chooseCallback), handle));
                return state.ButtonIndex switch
                {
                    1 => MessageDialogResult.Secondary,
                    2 => MessageDialogResult.Tertiary,
                    _ => MessageDialogResult.Primary,
                };
            }
            finally
            {
                FreeNullTerminatedUtf8Array(buttons);
            }
        }
        finally
        {
            Gtk.GObjectUnref(dialog);
        }
    }

    /// <summary>
    /// Tracks the async dialog result across the GAsyncReadyCallback boundary.
    /// Pinned via a GCHandle whose IntPtr is passed as the callback's user_data.
    /// </summary>
    private sealed class DialogState
    {
        public bool Done;
        public readonly List<string> Paths = new();
        public int ButtonIndex;
    }

    private static IntPtr GetCallback(GAsyncReadyCallback cb) => Marshal.GetFunctionPointerForDelegate(cb);

    private static DialogState RunAsync(Action<IntPtr> launch)
    {
        var state = new DialogState();
        var handle = GCHandle.Alloc(state);
        try
        {
            launch(GCHandle.ToIntPtr(handle));
            // Drain the default main context until the callback flips Done. The
            // GTK runtime's other sources (paint, input, etc.) get serviced as a
            // side effect, which keeps the parent window responsive while the
            // dialog is up.
            while (!state.Done) Gtk.MainContextIteration(IntPtr.Zero, true);
            return state;
        }
        finally
        {
            handle.Free();
        }
    }

    private static DialogState? StateFromUserData(IntPtr userData)
    {
        if (userData == IntPtr.Zero) return null;
        try { return GCHandle.FromIntPtr(userData).Target as DialogState; }
        catch { return null; }
    }

    private static void OnOpenReady(IntPtr source, IntPtr result, IntPtr userData)
    {
        var state = StateFromUserData(userData);
        if (state is null) return;
        try
        {
            var gfile = Gtk.Gtk4FileDialogOpenFinish(source, result, IntPtr.Zero);
            AppendPathFromGFile(state.Paths, gfile);
        }
        finally { state.Done = true; }
    }

    private static void OnOpenMultipleReady(IntPtr source, IntPtr result, IntPtr userData)
    {
        var state = StateFromUserData(userData);
        if (state is null) return;
        try
        {
            var model = Gtk.Gtk4FileDialogOpenMultipleFinish(source, result, IntPtr.Zero);
            if (model == IntPtr.Zero) return;
            try
            {
                var n = Gtk.GListModelGetNItems(model);
                for (uint i = 0; i < n; i++)
                {
                    var gfile = Gtk.GListModelGetItem(model, i);
                    AppendPathFromGFile(state.Paths, gfile);
                }
            }
            finally { Gtk.GObjectUnref(model); }
        }
        finally { state.Done = true; }
    }

    private static void OnSaveReady(IntPtr source, IntPtr result, IntPtr userData)
    {
        var state = StateFromUserData(userData);
        if (state is null) return;
        try
        {
            var gfile = Gtk.Gtk4FileDialogSaveFinish(source, result, IntPtr.Zero);
            AppendPathFromGFile(state.Paths, gfile);
        }
        finally { state.Done = true; }
    }

    private static void OnSelectFolderReady(IntPtr source, IntPtr result, IntPtr userData)
    {
        var state = StateFromUserData(userData);
        if (state is null) return;
        try
        {
            var gfile = Gtk.Gtk4FileDialogSelectFolderFinish(source, result, IntPtr.Zero);
            AppendPathFromGFile(state.Paths, gfile);
        }
        finally { state.Done = true; }
    }

    private static void OnChooseReady(IntPtr source, IntPtr result, IntPtr userData)
    {
        var state = StateFromUserData(userData);
        if (state is null) return;
        try
        {
            state.ButtonIndex = Gtk.Gtk4AlertDialogChooseFinish(source, result, IntPtr.Zero);
        }
        catch { /* leave ButtonIndex at 0 -> Primary */ }
        finally { state.Done = true; }
    }

    /// <summary>
    /// Reads the local path off a GFile returned by gtk_file_dialog_*_finish and
    /// unrefs the GFile in one place. Skips entries whose path can't be resolved
    /// (e.g. remote / virtual files surfaced by the portal).
    /// </summary>
    private static void AppendPathFromGFile(List<string> sink, IntPtr gfile)
    {
        if (gfile == IntPtr.Zero) return;
        try
        {
            var pathPtr = Gtk.GFileGetPath(gfile);
            if (pathPtr == IntPtr.Zero) return;
            try
            {
                var s = Marshal.PtrToStringUTF8(pathPtr);
                if (!string.IsNullOrEmpty(s)) sink.Add(s);
            }
            finally { Gtk.GFree(pathPtr); }
        }
        finally { Gtk.GObjectUnref(gfile); }
    }

    private static void ApplyInitialFolder(IntPtr dialog, string? defaultPath)
    {
        if (string.IsNullOrEmpty(defaultPath)) return;
        var folder = Gtk.GFileNewForPath(defaultPath);
        if (folder == IntPtr.Zero) return;
        try { Gtk.Gtk4FileDialogSetInitialFolder(dialog, folder); }
        finally { Gtk.GObjectUnref(folder); }
    }

    private static void ApplyFilters(IntPtr dialog, IReadOnlyList<FileFilter> filters)
    {
        if (filters.Count == 0) return;

        var filterType = Gtk.Gtk4FileFilterGetType();
        if (filterType == IntPtr.Zero) return;

        var store = Gtk.GListStoreNew(filterType);
        if (store == IntPtr.Zero) return;
        try
        {
            foreach (var filter in filters)
            {
                var gfilter = Gtk.Gtk4FileFilterNew();
                if (gfilter == IntPtr.Zero) continue;
                Gtk.Gtk4FileFilterSetName(gfilter, filter.Name);
                foreach (var ext in filter.Extensions)
                    Gtk.Gtk4FileFilterAddPattern(gfilter, "*." + ext.TrimStart('.'));
                Gtk.GListStoreAppend(store, gfilter);
                Gtk.GObjectUnref(gfilter);
            }
            Gtk.Gtk4FileDialogSetFilters(dialog, store);
        }
        finally
        {
            Gtk.GObjectUnref(store);
        }
    }

    /// <summary>
    /// Builds a NULL-terminated <c>char**</c> from a managed list of strings.
    /// The caller owns both the outer array and the per-entry UTF-8 buffers
    /// (freed via <see cref="FreeNullTerminatedUtf8Array"/>).
    /// </summary>
    private static IntPtr AllocateNullTerminatedUtf8Array(IReadOnlyList<string> values)
    {
        var ptrSize = IntPtr.Size;
        var array = Marshal.AllocHGlobal(ptrSize * (values.Count + 1));
        for (var i = 0; i < values.Count; i++)
            Marshal.WriteIntPtr(array, i * ptrSize, Marshal.StringToCoTaskMemUTF8(values[i]));
        Marshal.WriteIntPtr(array, values.Count * ptrSize, IntPtr.Zero);
        return array;
    }

    private static void FreeNullTerminatedUtf8Array(IntPtr array)
    {
        if (array == IntPtr.Zero) return;
        var ptrSize = IntPtr.Size;
        for (var i = 0; ; i++)
        {
            var p = Marshal.ReadIntPtr(array, i * ptrSize);
            if (p == IntPtr.Zero) break;
            Marshal.FreeCoTaskMem(p);
        }
        Marshal.FreeHGlobal(array);
    }
}
