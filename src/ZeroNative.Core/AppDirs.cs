using System.Runtime.InteropServices;

namespace ZeroNative;

/// <summary>
/// Resolves OS-appropriate per-application directories (state, config, data, cache).
/// Mirrors the layout described by the XDG Base Directory Specification on Linux, with
/// equivalents for Windows (LocalAppData) and macOS (Application Support / Library).
/// </summary>
public static class AppDirs
{
    public static string GetStateDirectory(string appName) =>
        Combine(StateRoot(), appName);

    public static string GetConfigDirectory(string appName) =>
        Combine(ConfigRoot(), appName);

    public static string GetDataDirectory(string appName) =>
        Combine(DataRoot(), appName);

    public static string GetCacheDirectory(string appName) =>
        Combine(CacheRoot(), appName);

    private static string StateRoot()
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (OperatingSystem.IsMacOS())
            return Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        // Linux & other Unix: XDG_STATE_HOME, else ~/.local/state
        var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrEmpty(xdg)) return xdg;
        return Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
    }

    private static string ConfigRoot()
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (OperatingSystem.IsMacOS())
            return Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences");
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg)) return xdg;
        return Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }

    private static string DataRoot()
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (OperatingSystem.IsMacOS())
            return Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdg)) return xdg;
        return Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }

    private static string CacheRoot()
    {
        if (OperatingSystem.IsWindows())
            return Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cache");
        if (OperatingSystem.IsMacOS())
            return Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches");
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrEmpty(xdg)) return xdg;
        return Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
    }

    private static string Combine(params string[] parts) => Path.Combine(parts);
}
