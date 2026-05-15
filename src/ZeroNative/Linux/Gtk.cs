using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZeroNative.Linux;

/// <summary>
/// Minimal P/Invoke bindings for GTK3/GTK4 + WebKit2GTK/WebKitGTK6.
///
/// <para>
/// GTK3 is the default on most distros and pairs with WebKit2GTK 4.0/4.1.
/// GTK4 is required when the available WebKit binding is WebKitGTK 6.0 — the
/// new ABI binds to GTK4 widgets and rejects GTK3 toplevels at load time.
/// The runtime probes both libraries lazily and dispatches each call through
/// the detected ABI; <see cref="PairWithWebKit"/> is invoked from the platform
/// after WebKit picks its own ABI so the GTK side stays consistent.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class Gtk
{
    private const string Gtk3 = "libgtk-3.so.0";
    private const string Gtk4 = "libgtk-4.so.1";
    private const string Glib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    public enum GtkAbi { Unknown, Three, Four }

    private static GtkAbi s_abi = GtkAbi.Unknown;
    private static IntPtr s_mainLoop4;

    public static GtkAbi Abi => s_abi;
    public static bool IsGtk4 => s_abi == GtkAbi.Four;

    // GTK3 "window type" constant; ignored on GTK4 (gtk_window_new() takes no args there).
    public const int GtkWindowToplevel = 0;
    public const int GdkGravityNorthWest = 1;

    /// <summary>
    /// Aligns the GTK ABI with the picked WebKit ABI: WebKitGTK 6.0 requires GTK4;
    /// the legacy 4.x WebKit chain requires GTK3. The first matching probe wins;
    /// if neither succeeds the call throws so the platform can fail loudly.
    /// </summary>
    public static void PairWithWebKit(WebKit.WebKitAbi webKitAbi)
    {
        if (s_abi != GtkAbi.Unknown) return;

        if (webKitAbi == WebKit.WebKitAbi.Six)
        {
            if (TryProbeGtk4()) { s_abi = GtkAbi.Four; return; }
            if (TryProbeGtk3()) { s_abi = GtkAbi.Three; return; }
            throw new DllNotFoundException("WebKitGTK 6.0 is present but neither libgtk-4 nor libgtk-3 could be loaded");
        }

        if (TryProbeGtk3()) { s_abi = GtkAbi.Three; return; }
        if (TryProbeGtk4()) { s_abi = GtkAbi.Four; return; }
        throw new DllNotFoundException("Neither libgtk-3 nor libgtk-4 could be loaded");
    }

    private static bool TryProbeGtk3()
    {
        try { _ = Gtk3InitCheck(); return true; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    private static bool TryProbeGtk4()
    {
        try { _ = Gtk4InitCheck(); return true; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    [LibraryImport(Gtk3, EntryPoint = "gtk_init_check")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool Gtk3InitCheck();

    [LibraryImport(Gtk4, EntryPoint = "gtk_init_check")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool Gtk4InitCheck();

    // ---- Init / main loop ----
    //
    // GTK3: gtk_init(&argc, &argv) + gtk_main() / gtk_main_quit().
    // GTK4: gtk_init() (no args) + a GMainLoop the platform spins manually.

    public static void Init(ref int argc, IntPtr argv)
    {
        switch (s_abi)
        {
            case GtkAbi.Four:
                Gtk4Init();
                break;
            default:
                Gtk3Init(ref argc, argv);
                break;
        }
    }

    public static void Main()
    {
        switch (s_abi)
        {
            case GtkAbi.Four:
                s_mainLoop4 = MainLoopNew(IntPtr.Zero, 0);
                MainLoopRun(s_mainLoop4);
                MainLoopUnref(s_mainLoop4);
                s_mainLoop4 = IntPtr.Zero;
                break;
            default:
                Gtk3Main();
                break;
        }
    }

    public static void MainQuit()
    {
        switch (s_abi)
        {
            case GtkAbi.Four:
                if (s_mainLoop4 != IntPtr.Zero) MainLoopQuit(s_mainLoop4);
                break;
            default:
                Gtk3MainQuit();
                break;
        }
    }

    [LibraryImport(Gtk3, EntryPoint = "gtk_init")]
    private static partial void Gtk3Init(ref int argc, IntPtr argv);

    [LibraryImport(Gtk4, EntryPoint = "gtk_init")]
    private static partial void Gtk4Init();

    [LibraryImport(Gtk3, EntryPoint = "gtk_main")]
    private static partial void Gtk3Main();

    [LibraryImport(Gtk3, EntryPoint = "gtk_main_quit")]
    private static partial void Gtk3MainQuit();

    [LibraryImport(Glib, EntryPoint = "g_main_loop_new")]
    private static partial IntPtr MainLoopNew(IntPtr context, int isRunning);

    [LibraryImport(Glib, EntryPoint = "g_main_loop_run")]
    private static partial void MainLoopRun(IntPtr loop);

    [LibraryImport(Glib, EntryPoint = "g_main_loop_quit")]
    private static partial void MainLoopQuit(IntPtr loop);

    [LibraryImport(Glib, EntryPoint = "g_main_loop_unref")]
    private static partial void MainLoopUnref(IntPtr loop);

    [LibraryImport(Gtk3, EntryPoint = "gtk_main_iteration_do")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int MainIterationDo([MarshalAs(UnmanagedType.U1)] bool blocking);

    [LibraryImport(Gtk3, EntryPoint = "gtk_events_pending")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int EventsPending();

    /// <summary>
    /// Iterates the default GMainContext once, blocking until at least one source
    /// dispatches. Used to drive async GLib APIs (e.g. GdkClipboard's text read,
    /// GtkFileDialog) into a synchronous return on the calling thread.
    /// </summary>
    [LibraryImport(Glib, EntryPoint = "g_main_context_iteration")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int MainContextIteration(IntPtr context, [MarshalAs(UnmanagedType.U1)] bool mayBlock);

    // ---- Windows ----

    public static IntPtr WindowNew(int type) => s_abi switch
    {
        GtkAbi.Four => Gtk4WindowNew(),
        _ => Gtk3WindowNew(type),
    };

    public static void WindowSetTitle(IntPtr window, string title)
    {
        if (s_abi == GtkAbi.Four) Gtk4WindowSetTitle(window, title);
        else Gtk3WindowSetTitle(window, title);
    }

    public static void WindowSetDefaultSize(IntPtr window, int width, int height)
    {
        if (s_abi == GtkAbi.Four) Gtk4WindowSetDefaultSize(window, width, height);
        else Gtk3WindowSetDefaultSize(window, width, height);
    }

    public static void WindowSetResizable(IntPtr window, bool resizable)
    {
        if (s_abi == GtkAbi.Four) Gtk4WindowSetResizable(window, resizable);
        else Gtk3WindowSetResizable(window, resizable);
    }

    /// <summary>
    /// Adds <paramref name="child"/> to <paramref name="window"/>.
    /// On GTK3 this is <c>gtk_container_add</c>; on GTK4 it is the single-child
    /// <c>gtk_window_set_child</c> (toplevels no longer behave as generic containers).
    /// </summary>
    public static void WindowSetChild(IntPtr window, IntPtr child)
    {
        if (s_abi == GtkAbi.Four) Gtk4WindowSetChild(window, child);
        else Gtk3ContainerAdd(window, child);
    }

    /// <summary>
    /// Backwards-compatible alias retained for callers that still think in GTK3
    /// container terms. New code should prefer <see cref="WindowSetChild"/>.
    /// </summary>
    public static void ContainerAdd(IntPtr window, IntPtr child) => WindowSetChild(window, child);

    /// <summary>
    /// Makes the window visible. GTK3 uses <c>gtk_widget_show_all</c> to recurse
    /// into the container; GTK4 windows always show their child tree so we just
    /// present the window.
    /// </summary>
    public static void WidgetShowAll(IntPtr window)
    {
        if (s_abi == GtkAbi.Four) Gtk4WindowPresent(window);
        else Gtk3WidgetShowAll(window);
    }

    public static void WindowPresent(IntPtr window)
    {
        if (s_abi == GtkAbi.Four) Gtk4WindowPresent(window);
        else Gtk3WindowPresent(window);
    }

    /// <summary>
    /// Destroys the window. On GTK4 the symbol is <c>gtk_window_destroy</c>
    /// (the GTK3 <c>gtk_widget_destroy</c> entry point is gone).
    /// </summary>
    public static void WidgetDestroy(IntPtr window)
    {
        if (s_abi == GtkAbi.Four) Gtk4WindowDestroy(window);
        else Gtk3WidgetDestroy(window);
    }

    public static void WindowMove(IntPtr window, int x, int y)
    {
        // GTK4 deliberately removed programmatic positioning; the WM owns placement now.
        if (s_abi == GtkAbi.Four) return;
        Gtk3WindowMove(window, x, y);
    }

    public static void WindowResize(IntPtr window, int width, int height)
    {
        // GTK4 has no gtk_window_resize; reuse gtk_window_set_default_size which
        // applies on the next allocation.
        if (s_abi == GtkAbi.Four) Gtk4WindowSetDefaultSize(window, width, height);
        else Gtk3WindowResize(window, width, height);
    }

    public static void WindowGetPosition(IntPtr window, out int rootX, out int rootY)
    {
        if (s_abi == GtkAbi.Four)
        {
            // GTK4 doesn't expose absolute window positions; report origin so callers
            // still see a stable rectangle.
            rootX = 0;
            rootY = 0;
            return;
        }
        Gtk3WindowGetPosition(window, out rootX, out rootY);
    }

    public static void WindowGetSize(IntPtr window, out int width, out int height)
    {
        if (s_abi == GtkAbi.Four)
        {
            width = Gtk4WidgetGetWidth(window);
            height = Gtk4WidgetGetHeight(window);
            return;
        }
        Gtk3WindowGetSize(window, out width, out height);
    }

    public static int WidgetGetScaleFactor(IntPtr widget) => s_abi switch
    {
        GtkAbi.Four => Gtk4WidgetGetScaleFactor(widget),
        _ => Gtk3WidgetGetScaleFactor(widget),
    };

    // GTK3 window symbols.
    [LibraryImport(Gtk3, EntryPoint = "gtk_window_new")]
    private static partial IntPtr Gtk3WindowNew(int type);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_set_title", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void Gtk3WindowSetTitle(IntPtr window, string title);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_set_default_size")]
    private static partial void Gtk3WindowSetDefaultSize(IntPtr window, int width, int height);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_set_resizable")]
    private static partial void Gtk3WindowSetResizable(IntPtr window, [MarshalAs(UnmanagedType.U1)] bool resizable);

    [LibraryImport(Gtk3, EntryPoint = "gtk_container_add")]
    private static partial void Gtk3ContainerAdd(IntPtr container, IntPtr child);

    [LibraryImport(Gtk3, EntryPoint = "gtk_widget_show_all")]
    private static partial void Gtk3WidgetShowAll(IntPtr widget);

    [LibraryImport(Gtk3, EntryPoint = "gtk_widget_destroy")]
    private static partial void Gtk3WidgetDestroy(IntPtr widget);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_get_position")]
    private static partial void Gtk3WindowGetPosition(IntPtr window, out int rootX, out int rootY);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_get_size")]
    private static partial void Gtk3WindowGetSize(IntPtr window, out int width, out int height);

    [LibraryImport(Gtk3, EntryPoint = "gtk_widget_get_scale_factor")]
    private static partial int Gtk3WidgetGetScaleFactor(IntPtr widget);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_present")]
    private static partial void Gtk3WindowPresent(IntPtr window);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_move")]
    private static partial void Gtk3WindowMove(IntPtr window, int x, int y);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_resize")]
    private static partial void Gtk3WindowResize(IntPtr window, int width, int height);

    // GTK4 window symbols.
    [LibraryImport(Gtk4, EntryPoint = "gtk_window_new")]
    private static partial IntPtr Gtk4WindowNew();

    [LibraryImport(Gtk4, EntryPoint = "gtk_window_set_title", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void Gtk4WindowSetTitle(IntPtr window, string title);

    [LibraryImport(Gtk4, EntryPoint = "gtk_window_set_default_size")]
    private static partial void Gtk4WindowSetDefaultSize(IntPtr window, int width, int height);

    [LibraryImport(Gtk4, EntryPoint = "gtk_window_set_resizable")]
    private static partial void Gtk4WindowSetResizable(IntPtr window, [MarshalAs(UnmanagedType.U1)] bool resizable);

    [LibraryImport(Gtk4, EntryPoint = "gtk_window_set_child")]
    private static partial void Gtk4WindowSetChild(IntPtr window, IntPtr child);

    [LibraryImport(Gtk4, EntryPoint = "gtk_window_present")]
    private static partial void Gtk4WindowPresent(IntPtr window);

    [LibraryImport(Gtk4, EntryPoint = "gtk_window_destroy")]
    private static partial void Gtk4WindowDestroy(IntPtr window);

    [LibraryImport(Gtk4, EntryPoint = "gtk_widget_get_width")]
    private static partial int Gtk4WidgetGetWidth(IntPtr widget);

    [LibraryImport(Gtk4, EntryPoint = "gtk_widget_get_height")]
    private static partial int Gtk4WidgetGetHeight(IntPtr widget);

    [LibraryImport(Gtk4, EntryPoint = "gtk_widget_get_scale_factor")]
    private static partial int Gtk4WidgetGetScaleFactor(IntPtr widget);

    /// <summary>
    /// GTK4 read for <c>GtkWindow:is-active</c>. Used by the notify::is-active
    /// callback to suppress focus-loss events.
    /// </summary>
    [LibraryImport(Gtk4, EntryPoint = "gtk_window_is_active")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool Gtk4WindowIsActive(IntPtr window);

    // ---- Signals (shared GObject path) ----

    [LibraryImport(GObject, EntryPoint = "g_signal_connect_data", StringMarshalling = StringMarshalling.Utf8)]
    public static partial ulong SignalConnectData(
        IntPtr instance, string signalName, IntPtr handler, IntPtr data, IntPtr destroyData, int flags);

    // ---- Clipboard ----
    //
    // GTK3 exposes the synchronous GtkClipboard API; GTK4 removed it in favour of
    // GdkClipboard (only set is sync; read is async via GAsyncReadyCallback).
    // The GTK4 read path below pumps the main context until the callback fires.

    [LibraryImport(Gtk3, EntryPoint = "gdk_atom_intern", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr AtomIntern(string atomName, [MarshalAs(UnmanagedType.U1)] bool onlyIfExists);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_get")]
    public static partial IntPtr ClipboardGet(IntPtr selection);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_set_text", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void ClipboardSetText(IntPtr clipboard, string text, int len);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_wait_for_text")]
    public static partial IntPtr ClipboardWaitForText(IntPtr clipboard);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_wait_for_uris")]
    public static partial IntPtr ClipboardWaitForUris(IntPtr clipboard);

    // GTK4 (and GTK3-with-GDK4) clipboard surface. The display getters live in
    // libgtk-4 in modern distros (GDK is folded into the same so).
    [LibraryImport(Gtk4, EntryPoint = "gdk_display_get_default")]
    public static partial IntPtr Gdk4DisplayGetDefault();

    [LibraryImport(Gtk4, EntryPoint = "gdk_display_get_clipboard")]
    public static partial IntPtr Gdk4DisplayGetClipboard(IntPtr display);

    [LibraryImport(Gtk4, EntryPoint = "gdk_clipboard_set_text", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Gdk4ClipboardSetText(IntPtr clipboard, string text);

    [LibraryImport(Gtk4, EntryPoint = "gdk_clipboard_read_text_async")]
    public static partial void Gdk4ClipboardReadTextAsync(IntPtr clipboard, IntPtr cancellable, IntPtr callback, IntPtr userData);

    [LibraryImport(Gtk4, EntryPoint = "gdk_clipboard_read_text_finish")]
    public static partial IntPtr Gdk4ClipboardReadTextFinish(IntPtr clipboard, IntPtr result, IntPtr error);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_set_with_data")]
    public static partial int ClipboardSetWithData(IntPtr clipboard, IntPtr targets, uint nTargets, IntPtr getFunc, IntPtr clearFunc, IntPtr userData);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_store")]
    public static partial void ClipboardStore(IntPtr clipboard);

    // GTK3 image clipboard. wait_for_image returns a transfer-full GdkPixbuf*
    // (must be unref'd by the caller); set_image takes a borrowed reference and
    // the clipboard installs its own owner.
    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_wait_for_image")]
    public static partial IntPtr ClipboardWaitForImage(IntPtr clipboard);

    [LibraryImport(Gtk3, EntryPoint = "gtk_clipboard_set_image")]
    public static partial void ClipboardSetImage(IntPtr clipboard, IntPtr pixbuf);

    // gdk-pixbuf serialization. save_to_buffer writes a newly allocated buffer
    // that must be released with g_free. The matching loader_new/write/close path
    // decodes any format the system understands (PNG, JPEG, ...).
    private const string GdkPixbuf = "libgdk_pixbuf-2.0.so.0";

    [LibraryImport(GdkPixbuf, EntryPoint = "gdk_pixbuf_save_to_buffer", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GdkPixbufSaveToBuffer(IntPtr pixbuf, out IntPtr buffer, out UIntPtr bufferSize, string type, IntPtr error, IntPtr sentinel);

    [LibraryImport(GdkPixbuf, EntryPoint = "gdk_pixbuf_loader_new")]
    public static partial IntPtr GdkPixbufLoaderNew();

    [LibraryImport(GdkPixbuf, EntryPoint = "gdk_pixbuf_loader_write")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GdkPixbufLoaderWrite(IntPtr loader, IntPtr data, UIntPtr count, IntPtr error);

    [LibraryImport(GdkPixbuf, EntryPoint = "gdk_pixbuf_loader_close")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GdkPixbufLoaderClose(IntPtr loader, IntPtr error);

    [LibraryImport(GdkPixbuf, EntryPoint = "gdk_pixbuf_loader_get_pixbuf")]
    public static partial IntPtr GdkPixbufLoaderGetPixbuf(IntPtr loader);

    [LibraryImport(Glib, EntryPoint = "g_strfreev")]
    public static partial void StringArrayFree(IntPtr stringArray);

    [LibraryImport(Glib, EntryPoint = "g_free")]
    public static partial void GFree(IntPtr ptr);

    // ---- Dialog APIs (GTK3-only; GTK4 replaces these with the GtkFileDialog async APIs) ----
    public const int ResponseAccept = -3;
    public const int ResponseCancel = -6;
    public const int ResponseOk = -5;
    public const int ResponseClose = -7;
    public const int ResponseYes = -8;
    public const int ResponseNo = -9;

    public const int FileChooserActionOpen = 0;
    public const int FileChooserActionSave = 1;
    public const int FileChooserActionSelectFolder = 2;

    public const int MessageInfo = 0;
    public const int MessageWarning = 1;
    public const int MessageQuestion = 2;
    public const int MessageError = 3;

    public const int ButtonsOk = 1;
    public const int ButtonsClose = 2;
    public const int ButtonsCancel = 3;
    public const int ButtonsYesNo = 4;
    public const int ButtonsOkCancel = 5;
    public const int ButtonsNone = 0;

    public const int DialogModal = 1;

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_chooser_dialog_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr FileChooserDialogNew(string? title, IntPtr parent, int action, IntPtr nullSentinel);

    [LibraryImport(Gtk3, EntryPoint = "gtk_dialog_add_button", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DialogAddButton(IntPtr dialog, string buttonText, int responseId);

    [LibraryImport(Gtk3, EntryPoint = "gtk_dialog_run")]
    public static partial int DialogRun(IntPtr dialog);

    [LibraryImport(Gtk3, EntryPoint = "gtk_widget_destroy")]
    public static partial void DialogDestroy(IntPtr widget);

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_chooser_get_filename")]
    public static partial IntPtr FileChooserGetFilename(IntPtr chooser);

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_chooser_get_filenames")]
    public static partial IntPtr FileChooserGetFilenames(IntPtr chooser);

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_chooser_set_select_multiple")]
    public static partial void FileChooserSetSelectMultiple(IntPtr chooser, [MarshalAs(UnmanagedType.U1)] bool select);

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_chooser_set_current_folder", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FileChooserSetCurrentFolder(IntPtr chooser, string folder);

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_chooser_set_current_name", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void FileChooserSetCurrentName(IntPtr chooser, string name);

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_chooser_set_do_overwrite_confirmation")]
    public static partial void FileChooserSetOverwriteConfirmation(IntPtr chooser, [MarshalAs(UnmanagedType.U1)] bool confirm);

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_chooser_add_filter")]
    public static partial void FileChooserAddFilter(IntPtr chooser, IntPtr filter);

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_filter_new")]
    public static partial IntPtr FileFilterNew();

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_filter_set_name", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void FileFilterSetName(IntPtr filter, string name);

    [LibraryImport(Gtk3, EntryPoint = "gtk_file_filter_add_pattern", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void FileFilterAddPattern(IntPtr filter, string pattern);

    [LibraryImport(Gtk3, EntryPoint = "gtk_message_dialog_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr MessageDialogNew(IntPtr parent, int flags, int type, int buttons, string? message);

    [LibraryImport(Gtk3, EntryPoint = "gtk_message_dialog_format_secondary_text", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void MessageDialogFormatSecondary(IntPtr dialog, string? message);

    [LibraryImport(Gtk3, EntryPoint = "gtk_window_set_title", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DialogSetTitle(IntPtr dialog, string title);

    // GList helpers (used to walk gtk_file_chooser_get_filenames results).
    [LibraryImport(Glib, EntryPoint = "g_list_length")]
    public static partial uint ListLength(IntPtr list);

    [LibraryImport(Glib, EntryPoint = "g_list_nth_data")]
    public static partial IntPtr ListNthData(IntPtr list, uint n);

    [LibraryImport(Glib, EntryPoint = "g_list_free")]
    public static partial void ListFree(IntPtr list);

    [LibraryImport(Glib, EntryPoint = "g_slist_length")]
    public static partial uint SListLength(IntPtr list);

    [LibraryImport(Glib, EntryPoint = "g_slist_nth_data")]
    public static partial IntPtr SListNthData(IntPtr list, uint n);

    [LibraryImport(Glib, EntryPoint = "g_slist_free")]
    public static partial void SListFree(IntPtr list);

    // ---- GTK4 file / alert dialogs (async APIs) ----
    //
    // GTK4 replaced GtkFileChooserDialog with GtkFileDialog and GtkMessageDialog
    // with GtkAlertDialog. Both expose only async entry points; the platform
    // drives them via g_main_context_iteration in the same pattern as the GTK4
    // clipboard read.

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_new")]
    public static partial IntPtr Gtk4FileDialogNew();

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_set_title", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Gtk4FileDialogSetTitle(IntPtr dialog, string title);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_set_initial_folder")]
    public static partial void Gtk4FileDialogSetInitialFolder(IntPtr dialog, IntPtr folder);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_set_initial_name", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Gtk4FileDialogSetInitialName(IntPtr dialog, string name);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_set_filters")]
    public static partial void Gtk4FileDialogSetFilters(IntPtr dialog, IntPtr filters);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_open")]
    public static partial void Gtk4FileDialogOpen(IntPtr dialog, IntPtr parent, IntPtr cancellable, IntPtr callback, IntPtr userData);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_open_multiple")]
    public static partial void Gtk4FileDialogOpenMultiple(IntPtr dialog, IntPtr parent, IntPtr cancellable, IntPtr callback, IntPtr userData);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_save")]
    public static partial void Gtk4FileDialogSave(IntPtr dialog, IntPtr parent, IntPtr cancellable, IntPtr callback, IntPtr userData);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_select_folder")]
    public static partial void Gtk4FileDialogSelectFolder(IntPtr dialog, IntPtr parent, IntPtr cancellable, IntPtr callback, IntPtr userData);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_open_finish")]
    public static partial IntPtr Gtk4FileDialogOpenFinish(IntPtr dialog, IntPtr result, IntPtr error);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_open_multiple_finish")]
    public static partial IntPtr Gtk4FileDialogOpenMultipleFinish(IntPtr dialog, IntPtr result, IntPtr error);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_save_finish")]
    public static partial IntPtr Gtk4FileDialogSaveFinish(IntPtr dialog, IntPtr result, IntPtr error);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_dialog_select_folder_finish")]
    public static partial IntPtr Gtk4FileDialogSelectFolderFinish(IntPtr dialog, IntPtr result, IntPtr error);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_filter_new")]
    public static partial IntPtr Gtk4FileFilterNew();

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_filter_set_name", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Gtk4FileFilterSetName(IntPtr filter, string name);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_filter_add_pattern", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Gtk4FileFilterAddPattern(IntPtr filter, string pattern);

    [LibraryImport(Gtk4, EntryPoint = "gtk_file_filter_get_type")]
    public static partial IntPtr Gtk4FileFilterGetType();

    // GIO helpers used by GtkFileDialog (GFile / GListStore / GListModel).
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_list_store_new")]
    public static partial IntPtr GListStoreNew(IntPtr itemType);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_list_store_append")]
    public static partial void GListStoreAppend(IntPtr store, IntPtr item);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_list_model_get_n_items")]
    public static partial uint GListModelGetNItems(IntPtr model);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_list_model_get_item")]
    public static partial IntPtr GListModelGetItem(IntPtr model, uint position);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_new_for_path", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GFileNewForPath(string path);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_get_path")]
    public static partial IntPtr GFileGetPath(IntPtr file);

    [LibraryImport(GObject, EntryPoint = "g_object_unref")]
    public static partial void GObjectUnref(IntPtr obj);

    // GtkAlertDialog.
    [LibraryImport(Gtk4, EntryPoint = "gtk_alert_dialog_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr Gtk4AlertDialogNew(string format);

    [LibraryImport(Gtk4, EntryPoint = "gtk_alert_dialog_set_detail", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Gtk4AlertDialogSetDetail(IntPtr dialog, string detail);

    [LibraryImport(Gtk4, EntryPoint = "gtk_alert_dialog_set_buttons")]
    public static partial void Gtk4AlertDialogSetButtons(IntPtr dialog, IntPtr buttons);

    [LibraryImport(Gtk4, EntryPoint = "gtk_alert_dialog_choose")]
    public static partial void Gtk4AlertDialogChoose(IntPtr dialog, IntPtr parent, IntPtr cancellable, IntPtr callback, IntPtr userData);

    [LibraryImport(Gtk4, EntryPoint = "gtk_alert_dialog_choose_finish")]
    public static partial int Gtk4AlertDialogChooseFinish(IntPtr dialog, IntPtr result, IntPtr error);
}

[SupportedOSPlatform("linux")]
internal static partial class WebKit
{
    // Library names in preference order: WebKitGTK 6.0 (GTK4, modern distros),
    // 4.1 (GTK3, late 2010s+), 4.0 (older GTK3 distros).
    private const string WebKit60 = "libwebkitgtk-6.0.so.4";
    private const string WebKit41 = "libwebkit2gtk-4.1.so.0";
    private const string WebKit40 = "libwebkit2gtk-4.0.so.37";

    /// <summary>
    /// Detected WebKitGTK ABI, picked once via a probe call (<see cref="WebKitNew60"/>
    /// → <see cref="WebView_New41"/> → <see cref="WebView_New40"/>) so subsequent
    /// calls dispatch without try/catch overhead.
    /// </summary>
    public enum WebKitAbi { Unknown, Six, FourOne, FourZero }

    private static WebKitAbi s_abi = WebKitAbi.Unknown;

    public static WebKitAbi Abi => s_abi;

    /// <summary>
    /// Non-invasive ABI probe: tries to load each WebKitGTK soname via
    /// <see cref="NativeLibrary.TryLoad(string, out IntPtr)"/> and remembers the
    /// first hit. The .NET P/Invoke loader doesn't unload the handle so the
    /// subsequent <c>LibraryImport</c> calls reuse the cached binding.
    /// Calling this before <c>gtk_init</c> lets the host pair the GTK ABI
    /// against the WebKit one without creating any widgets first.
    /// </summary>
    public static WebKitAbi Probe()
    {
        if (s_abi != WebKitAbi.Unknown) return s_abi;
        if (NativeLibrary.TryLoad(WebKit41, out _)) { s_abi = WebKitAbi.FourOne; return s_abi; }
        if (NativeLibrary.TryLoad(WebKit40, out _)) { s_abi = WebKitAbi.FourZero; return s_abi; }
        if (NativeLibrary.TryLoad(WebKit60, out _)) { s_abi = WebKitAbi.Six; return s_abi; }
        return WebKitAbi.Unknown;
    }

    public static IntPtr WebViewNew()
    {
        // The first call probes which ABI is available and remembers it.
        // Order matters: 4.1 and 4.0 are GTK3-compatible and so pair with the
        // GTK3 host this platform builds against. 6.0 is GTK4-only and is
        // probed last as a safety net (a real GTK4 host pipeline still needs
        // separate wiring; this entry covers the case where a system installs
        // 6.0 alongside 4.x and the 4.x soname has shifted).
        if (s_abi == WebKitAbi.Unknown)
        {
            try { var p = WebView_New41(); s_abi = WebKitAbi.FourOne; return p; }
            catch (DllNotFoundException) { }
            try { var p = WebView_New40(); s_abi = WebKitAbi.FourZero; return p; }
            catch (DllNotFoundException) { }
            var p60 = WebView_New60();
            s_abi = WebKitAbi.Six;
            return p60;
        }
        return s_abi switch
        {
            WebKitAbi.Six => WebView_New60(),
            WebKitAbi.FourOne => WebView_New41(),
            _ => WebView_New40(),
        };
    }

    public static void LoadHtml(IntPtr webview, string content, string? baseUri)
    {
        switch (s_abi)
        {
            case WebKitAbi.Six: WebView_LoadHtml60(webview, content, baseUri); break;
            case WebKitAbi.FourOne: WebView_LoadHtml41(webview, content, baseUri); break;
            default: WebView_LoadHtml40(webview, content, baseUri); break;
        }
    }

    public static void LoadUri(IntPtr webview, string uri)
    {
        switch (s_abi)
        {
            case WebKitAbi.Six: WebView_LoadUri60(webview, uri); break;
            case WebKitAbi.FourOne: WebView_LoadUri41(webview, uri); break;
            default: WebView_LoadUri40(webview, uri); break;
        }
    }

    public static void RunJavaScript(IntPtr webview, string js)
    {
        switch (s_abi)
        {
            case WebKitAbi.Six: WebView_RunJs60(webview, js, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); break;
            case WebKitAbi.FourOne: WebView_RunJs41(webview, js, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); break;
            default: WebView_RunJs40(webview, js, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); break;
        }
    }

    public static IntPtr GetUserContentManager(IntPtr webview) => s_abi switch
    {
        WebKitAbi.Six => WebView_UCM60(webview),
        WebKitAbi.FourOne => WebView_UCM41(webview),
        _ => WebView_UCM40(webview),
    };

    public static void AddUserScript(IntPtr ucm, string sourceJs)
    {
        var script = NewUserScript(sourceJs);
        switch (s_abi)
        {
            case WebKitAbi.Six: UCM_AddScript60(ucm, script); break;
            case WebKitAbi.FourOne: UCM_AddScript41(ucm, script); break;
            default: UCM_AddScript40(ucm, script); break;
        }
    }

    public static bool RegisterScriptMessageHandler(IntPtr ucm, string name) => s_abi switch
    {
        // WebKitGTK 6.0 added a world-name argument; passing IntPtr.Zero matches "main world".
        WebKitAbi.Six => UCM_RegisterHandler60(ucm, name, IntPtr.Zero),
        WebKitAbi.FourOne => UCM_RegisterHandler41(ucm, name, IntPtr.Zero),
        _ => UCM_RegisterHandler40(ucm, name),
    };

    private static IntPtr NewUserScript(string source) => s_abi switch
    {
        WebKitAbi.Six => UserScript_New60(source, 0, 0, IntPtr.Zero, IntPtr.Zero),
        WebKitAbi.FourOne => UserScript_New41(source, 0, 0, IntPtr.Zero, IntPtr.Zero),
        _ => UserScript_New40(source, 0, 0, IntPtr.Zero, IntPtr.Zero),
    };

    [LibraryImport(WebKit60, EntryPoint = "webkit_web_view_get_user_content_manager")]
    private static partial IntPtr WebView_UCM60(IntPtr webview);

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_get_user_content_manager")]
    private static partial IntPtr WebView_UCM41(IntPtr webview);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_get_user_content_manager")]
    private static partial IntPtr WebView_UCM40(IntPtr webview);

    [LibraryImport(WebKit60, EntryPoint = "webkit_user_script_new", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr UserScript_New60(string source, int injectedFrames, int injectionTime, IntPtr allowList, IntPtr blockList);

    [LibraryImport(WebKit41, EntryPoint = "webkit_user_script_new", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr UserScript_New41(string source, int injectedFrames, int injectionTime, IntPtr allowList, IntPtr blockList);

    [LibraryImport(WebKit40, EntryPoint = "webkit_user_script_new", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr UserScript_New40(string source, int injectedFrames, int injectionTime, IntPtr allowList, IntPtr blockList);

    [LibraryImport(WebKit60, EntryPoint = "webkit_user_content_manager_add_script")]
    private static partial void UCM_AddScript60(IntPtr ucm, IntPtr script);

    [LibraryImport(WebKit41, EntryPoint = "webkit_user_content_manager_add_script")]
    private static partial void UCM_AddScript41(IntPtr ucm, IntPtr script);

    [LibraryImport(WebKit40, EntryPoint = "webkit_user_content_manager_add_script")]
    private static partial void UCM_AddScript40(IntPtr ucm, IntPtr script);

    [LibraryImport(WebKit60, EntryPoint = "webkit_user_content_manager_register_script_message_handler", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool UCM_RegisterHandler60(IntPtr ucm, string name, IntPtr worldName);

    [LibraryImport(WebKit41, EntryPoint = "webkit_user_content_manager_register_script_message_handler", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool UCM_RegisterHandler41(IntPtr ucm, string name, IntPtr worldName);

    [LibraryImport(WebKit40, EntryPoint = "webkit_user_content_manager_register_script_message_handler", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool UCM_RegisterHandler40(IntPtr ucm, string name);

    /// <summary>
    /// Reads a string from a <c>WebKitJavascriptResult</c> (4.x) or a <c>JSCValue</c>
    /// passed directly (6.0+ — the script-message-received signal changed shape).
    /// </summary>
    public static string? ReadJavascriptResultString(IntPtr jsResult) => s_abi switch
    {
        // WebKitGTK 6.0 callbacks already receive a JSCValue directly, so skip the unwrap.
        WebKitAbi.Six => Marshal.PtrToStringUTF8(JscValueToString60(jsResult)),
        WebKitAbi.FourOne => ReadJsResult41(jsResult),
        _ => ReadJsResult40(jsResult),
    };

    [LibraryImport(WebKit41, EntryPoint = "webkit_javascript_result_get_js_value")]
    private static partial IntPtr JsResultGetValue41(IntPtr jsResult);

    [LibraryImport(WebKit40, EntryPoint = "webkit_javascript_result_get_js_value")]
    private static partial IntPtr JsResultGetValue40(IntPtr jsResult);

    [LibraryImport("libjavascriptcoregtk-6.0.so.1", EntryPoint = "jsc_value_to_string")]
    private static partial IntPtr JscValueToString60(IntPtr value);

    [LibraryImport("libjavascriptcoregtk-4.1.so.0", EntryPoint = "jsc_value_to_string")]
    private static partial IntPtr JscValueToString41(IntPtr value);

    [LibraryImport("libjavascriptcoregtk-4.0.so.18", EntryPoint = "jsc_value_to_string")]
    private static partial IntPtr JscValueToString40(IntPtr value);

    private static string? ReadJsResult41(IntPtr jsResult)
    {
        var value = JsResultGetValue41(jsResult);
        if (value == IntPtr.Zero) return null;
        var strPtr = JscValueToString41(value);
        return Marshal.PtrToStringUTF8(strPtr);
    }

    private static string? ReadJsResult40(IntPtr jsResult)
    {
        var value = JsResultGetValue40(jsResult);
        if (value == IntPtr.Zero) return null;
        var strPtr = JscValueToString40(value);
        return Marshal.PtrToStringUTF8(strPtr);
    }

    [LibraryImport(WebKit60, EntryPoint = "webkit_web_view_new")]
    private static partial IntPtr WebView_New60();

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_new")]
    private static partial IntPtr WebView_New41();

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_new")]
    private static partial IntPtr WebView_New40();

    [LibraryImport(WebKit60, EntryPoint = "webkit_web_view_load_html", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_LoadHtml60(IntPtr webview, string content, string? baseUri);

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_load_html", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_LoadHtml41(IntPtr webview, string content, string? baseUri);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_load_html", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_LoadHtml40(IntPtr webview, string content, string? baseUri);

    [LibraryImport(WebKit60, EntryPoint = "webkit_web_view_load_uri", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_LoadUri60(IntPtr webview, string uri);

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_load_uri", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_LoadUri41(IntPtr webview, string uri);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_load_uri", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_LoadUri40(IntPtr webview, string uri);

    [LibraryImport(WebKit60, EntryPoint = "webkit_web_view_evaluate_javascript", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_RunJs60(IntPtr webview, string js, IntPtr lengthOrNeg1, IntPtr worldName, IntPtr sourceUri);

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_run_javascript", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_RunJs41(IntPtr webview, string js, IntPtr cancellable, IntPtr callback, IntPtr user_data);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_run_javascript", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void WebView_RunJs40(IntPtr webview, string js, IntPtr cancellable, IntPtr callback, IntPtr user_data);

    public static IntPtr GetWebContext(IntPtr webview) => s_abi switch
    {
        WebKitAbi.Six => WebView_GetContext60(webview),
        WebKitAbi.FourOne => WebView_GetContext41(webview),
        _ => WebView_GetContext40(webview),
    };

    public static void RegisterUriScheme(IntPtr context, string scheme, IntPtr callback, IntPtr userData)
    {
        switch (s_abi)
        {
            case WebKitAbi.Six: Context_RegisterScheme60(context, scheme, callback, userData, IntPtr.Zero); break;
            case WebKitAbi.FourOne: Context_RegisterScheme41(context, scheme, callback, userData, IntPtr.Zero); break;
            default: Context_RegisterScheme40(context, scheme, callback, userData, IntPtr.Zero); break;
        }
    }

    public static string? GetUriSchemeRequestUri(IntPtr request) => s_abi switch
    {
        WebKitAbi.Six => Marshal.PtrToStringUTF8(SchemeRequest_GetUri60(request)),
        WebKitAbi.FourOne => Marshal.PtrToStringUTF8(SchemeRequest_GetUri41(request)),
        _ => Marshal.PtrToStringUTF8(SchemeRequest_GetUri40(request)),
    };

    public static void FinishUriSchemeRequest(IntPtr request, IntPtr stream, long streamLength, string contentType)
    {
        switch (s_abi)
        {
            case WebKitAbi.Six: SchemeRequest_Finish60(request, stream, streamLength, contentType); break;
            case WebKitAbi.FourOne: SchemeRequest_Finish41(request, stream, streamLength, contentType); break;
            default: SchemeRequest_Finish40(request, stream, streamLength, contentType); break;
        }
    }

    public static IntPtr CreateMemoryInputStream(byte[] data)
    {
        // g_bytes_new copies the data internally, so we can free the unmanaged buffer
        // immediately. The resulting GBytes owns the copy; we ref-transfer it to the
        // input stream and unref our reference.
        IntPtr unmanaged = IntPtr.Zero;
        try
        {
            unmanaged = Marshal.AllocHGlobal(Math.Max(1, data.Length));
            if (data.Length > 0) Marshal.Copy(data, 0, unmanaged, data.Length);
            var bytes = BytesNew(unmanaged, (IntPtr)data.Length);
            var stream = MemoryStream_NewFromBytes(bytes);
            BytesUnref(bytes);
            return stream;
        }
        finally
        {
            if (unmanaged != IntPtr.Zero) Marshal.FreeHGlobal(unmanaged);
        }
    }

    [LibraryImport(WebKit60, EntryPoint = "webkit_web_view_get_context")]
    private static partial IntPtr WebView_GetContext60(IntPtr webview);

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_view_get_context")]
    private static partial IntPtr WebView_GetContext41(IntPtr webview);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_view_get_context")]
    private static partial IntPtr WebView_GetContext40(IntPtr webview);

    [LibraryImport(WebKit60, EntryPoint = "webkit_web_context_register_uri_scheme", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void Context_RegisterScheme60(IntPtr context, string scheme, IntPtr callback, IntPtr userData, IntPtr destroy);

    [LibraryImport(WebKit41, EntryPoint = "webkit_web_context_register_uri_scheme", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void Context_RegisterScheme41(IntPtr context, string scheme, IntPtr callback, IntPtr userData, IntPtr destroy);

    [LibraryImport(WebKit40, EntryPoint = "webkit_web_context_register_uri_scheme", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void Context_RegisterScheme40(IntPtr context, string scheme, IntPtr callback, IntPtr userData, IntPtr destroy);

    [LibraryImport(WebKit60, EntryPoint = "webkit_uri_scheme_request_get_uri")]
    private static partial IntPtr SchemeRequest_GetUri60(IntPtr request);

    [LibraryImport(WebKit41, EntryPoint = "webkit_uri_scheme_request_get_uri")]
    private static partial IntPtr SchemeRequest_GetUri41(IntPtr request);

    [LibraryImport(WebKit40, EntryPoint = "webkit_uri_scheme_request_get_uri")]
    private static partial IntPtr SchemeRequest_GetUri40(IntPtr request);

    [LibraryImport(WebKit60, EntryPoint = "webkit_uri_scheme_request_finish", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void SchemeRequest_Finish60(IntPtr request, IntPtr stream, long streamLength, string contentType);

    [LibraryImport(WebKit41, EntryPoint = "webkit_uri_scheme_request_finish", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void SchemeRequest_Finish41(IntPtr request, IntPtr stream, long streamLength, string contentType);

    [LibraryImport(WebKit40, EntryPoint = "webkit_uri_scheme_request_finish", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void SchemeRequest_Finish40(IntPtr request, IntPtr stream, long streamLength, string contentType);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_memory_input_stream_new_from_bytes")]
    private static partial IntPtr MemoryStream_NewFromBytes(IntPtr bytes);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_bytes_new")]
    private static partial IntPtr BytesNew(IntPtr data, IntPtr size);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_bytes_unref")]
    private static partial void BytesUnref(IntPtr bytes);

    // Navigation policy: subscribe to the WebKit "decide-policy" signal and inspect
    // the WebKitNavigationPolicyDecision / WebKitNavigationAction / WebKitURIRequest.
    public const int PolicyDecisionTypeNavigationAction = 0;
    public const int PolicyDecisionTypeNewWindowAction = 1;
    public const int PolicyDecisionTypeResponse = 2;

    public static IntPtr GetNavigationRequestUri(IntPtr decision)
    {
        IntPtr action, request;
        switch (s_abi)
        {
            case WebKitAbi.Six:
                action = NavigationPolicyDecisionGetAction60(decision);
                if (action == IntPtr.Zero) return IntPtr.Zero;
                request = NavigationActionGetRequest60(action);
                if (request == IntPtr.Zero) return IntPtr.Zero;
                return UriRequestGetUri60(request);
            case WebKitAbi.FourOne:
                action = NavigationPolicyDecisionGetAction41(decision);
                if (action == IntPtr.Zero) return IntPtr.Zero;
                request = NavigationActionGetRequest41(action);
                if (request == IntPtr.Zero) return IntPtr.Zero;
                return UriRequestGetUri41(request);
            default:
                action = NavigationPolicyDecisionGetAction40(decision);
                if (action == IntPtr.Zero) return IntPtr.Zero;
                request = NavigationActionGetRequest40(action);
                if (request == IntPtr.Zero) return IntPtr.Zero;
                return UriRequestGetUri40(request);
        }
    }

    public static void IgnoreDecision(IntPtr decision)
    {
        switch (s_abi)
        {
            case WebKitAbi.Six: PolicyDecisionIgnore60(decision); break;
            case WebKitAbi.FourOne: PolicyDecisionIgnore41(decision); break;
            default: PolicyDecisionIgnore40(decision); break;
        }
    }

    public static void UseDecision(IntPtr decision)
    {
        switch (s_abi)
        {
            case WebKitAbi.Six: PolicyDecisionUse60(decision); break;
            case WebKitAbi.FourOne: PolicyDecisionUse41(decision); break;
            default: PolicyDecisionUse40(decision); break;
        }
    }

    [LibraryImport(WebKit60, EntryPoint = "webkit_navigation_policy_decision_get_navigation_action")]
    private static partial IntPtr NavigationPolicyDecisionGetAction60(IntPtr decision);

    [LibraryImport(WebKit41, EntryPoint = "webkit_navigation_policy_decision_get_navigation_action")]
    private static partial IntPtr NavigationPolicyDecisionGetAction41(IntPtr decision);

    [LibraryImport(WebKit40, EntryPoint = "webkit_navigation_policy_decision_get_navigation_action")]
    private static partial IntPtr NavigationPolicyDecisionGetAction40(IntPtr decision);

    [LibraryImport(WebKit60, EntryPoint = "webkit_navigation_action_get_request")]
    private static partial IntPtr NavigationActionGetRequest60(IntPtr action);

    [LibraryImport(WebKit41, EntryPoint = "webkit_navigation_action_get_request")]
    private static partial IntPtr NavigationActionGetRequest41(IntPtr action);

    [LibraryImport(WebKit40, EntryPoint = "webkit_navigation_action_get_request")]
    private static partial IntPtr NavigationActionGetRequest40(IntPtr action);

    [LibraryImport(WebKit60, EntryPoint = "webkit_uri_request_get_uri")]
    private static partial IntPtr UriRequestGetUri60(IntPtr request);

    [LibraryImport(WebKit41, EntryPoint = "webkit_uri_request_get_uri")]
    private static partial IntPtr UriRequestGetUri41(IntPtr request);

    [LibraryImport(WebKit40, EntryPoint = "webkit_uri_request_get_uri")]
    private static partial IntPtr UriRequestGetUri40(IntPtr request);

    [LibraryImport(WebKit60, EntryPoint = "webkit_policy_decision_ignore")]
    private static partial void PolicyDecisionIgnore60(IntPtr decision);

    [LibraryImport(WebKit41, EntryPoint = "webkit_policy_decision_ignore")]
    private static partial void PolicyDecisionIgnore41(IntPtr decision);

    [LibraryImport(WebKit40, EntryPoint = "webkit_policy_decision_ignore")]
    private static partial void PolicyDecisionIgnore40(IntPtr decision);

    [LibraryImport(WebKit60, EntryPoint = "webkit_policy_decision_use")]
    private static partial void PolicyDecisionUse60(IntPtr decision);

    [LibraryImport(WebKit41, EntryPoint = "webkit_policy_decision_use")]
    private static partial void PolicyDecisionUse41(IntPtr decision);

    [LibraryImport(WebKit40, EntryPoint = "webkit_policy_decision_use")]
    private static partial void PolicyDecisionUse40(IntPtr decision);
}
