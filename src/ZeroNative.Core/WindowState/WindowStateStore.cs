using System.Text.Json;
using ZeroNative.Platform;
using ZeroNative.Primitives;

namespace ZeroNative.WindowStateStores;

/// <summary>
/// Persists window geometry/state across runs so apps can restore where the user left them.
/// </summary>
public interface IWindowStateStore
{
    /// <summary>Loads the persisted state for a single labeled window, or null if absent.</summary>
    Platform.WindowState? LoadWindow(string label);

    /// <summary>Loads all persisted window states.</summary>
    IReadOnlyList<Platform.WindowState> LoadWindows();

    /// <summary>Persists the given window state, replacing any prior record with the same label or id.</summary>
    void SaveWindow(Platform.WindowState state);

    /// <summary>Removes a persisted record (does nothing if absent).</summary>
    void RemoveWindow(string label);
}

/// <summary>
/// JSON-backed window-state store. State is written to a single file (default: <c>windows.json</c>)
/// inside a configurable state directory, typically obtained from <see cref="AppDirs"/>.
/// </summary>
public sealed class JsonWindowStateStore : IWindowStateStore
{
    private readonly string _filePath;
    private readonly string _stateDir;
    private readonly object _lock = new();

    public JsonWindowStateStore(string stateDir, string fileName = "windows.json")
    {
        _stateDir = Path.GetFullPath(stateDir);
        _filePath = Path.Combine(_stateDir, fileName);
    }

    public string FilePath => _filePath;

    public Platform.WindowState? LoadWindow(string label)
    {
        if (string.IsNullOrEmpty(label)) return null;
        return LoadWindows().FirstOrDefault(w => w.Label == label);
    }

    public IReadOnlyList<Platform.WindowState> LoadWindows()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath)) return Array.Empty<Platform.WindowState>();
            try
            {
                using var stream = File.OpenRead(_filePath);
                var dto = JsonSerializer.Deserialize<WindowFileDto>(stream);
                if (dto?.Windows is null) return Array.Empty<Platform.WindowState>();
                return dto.Windows
                    .Where(w => !string.IsNullOrEmpty(w.Label))
                    .Select(w => w.ToWindowState())
                    .ToList();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return Array.Empty<Platform.WindowState>();
            }
        }
    }

    public void SaveWindow(Platform.WindowState state)
    {
        if (string.IsNullOrEmpty(state.Label)) return;

        lock (_lock)
        {
            var existing = LoadWindows().ToList();
            var idx = existing.FindIndex(w =>
                (state.Label.Length > 0 && w.Label == state.Label) ||
                (state.Id != 0 && w.Id == state.Id));

            if (idx >= 0) existing[idx] = state;
            else existing.Add(state);

            Directory.CreateDirectory(_stateDir);
            using var stream = File.Create(_filePath);
            JsonSerializer.Serialize(stream, new WindowFileDto
            {
                Windows = existing.Select(WindowStateDto.From).ToList(),
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    public void RemoveWindow(string label)
    {
        if (string.IsNullOrEmpty(label)) return;
        lock (_lock)
        {
            var existing = LoadWindows().Where(w => w.Label != label).ToList();
            Directory.CreateDirectory(_stateDir);
            using var stream = File.Create(_filePath);
            JsonSerializer.Serialize(stream, new WindowFileDto
            {
                Windows = existing.Select(WindowStateDto.From).ToList(),
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>Builds a default store path under the OS-appropriate state directory.</summary>
    public static JsonWindowStateStore CreateForApp(string appName, string fileName = "windows.json")
        => new(AppDirs.GetStateDirectory(appName), fileName);

    private sealed class WindowFileDto
    {
        public List<WindowStateDto> Windows { get; set; } = new();
    }

    private sealed class WindowStateDto
    {
        public ulong Id { get; set; } = 1;
        public string Label { get; set; } = "main";
        public string Title { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; } = 720;
        public float Height { get; set; } = 480;
        public float Scale { get; set; } = 1;
        public bool Open { get; set; } = true;
        public bool Focused { get; set; }
        public bool Maximized { get; set; }
        public bool Fullscreen { get; set; }

        public static WindowStateDto From(Platform.WindowState s) => new()
        {
            Id = s.Id,
            Label = s.Label,
            Title = s.Title,
            X = s.Frame.X,
            Y = s.Frame.Y,
            Width = s.Frame.Width,
            Height = s.Frame.Height,
            Scale = s.ScaleFactor,
            Open = s.Open,
            Focused = s.Focused,
            Maximized = s.Maximized,
            Fullscreen = s.Fullscreen,
        };

        public Platform.WindowState ToWindowState() => new()
        {
            Id = Id,
            Label = Label,
            Title = Title,
            Frame = new RectF(X, Y, Width, Height),
            ScaleFactor = Scale,
            Open = Open,
            Focused = Focused,
            Maximized = Maximized,
            Fullscreen = Fullscreen,
        };
    }
}
