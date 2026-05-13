namespace ZeroNative.Automation;

/// <summary>
/// File-based automation server. Publishes snapshot artifacts under
/// <see cref="Directory"/> and consumes a single-line <c>command.txt</c>
/// produced by the harness.
/// </summary>
public sealed class AutomationServer
{
    public string Directory { get; }
    public string Title { get; }

    public AutomationServer(string? directory = null, string title = "zero-native")
    {
        Directory = directory ?? AutomationProtocol.DefaultDirectory;
        Title = title;
    }

    public void Publish(AutomationInput input)
    {
        EnsureDirectory();
        File.WriteAllText(PathFor("snapshot.txt"), AutomationSnapshot.WriteText(input));
        File.WriteAllText(PathFor("accessibility.txt"), AutomationSnapshot.WriteAccessibility(input));
        File.WriteAllText(PathFor("windows.txt"), AutomationSnapshot.WriteWindows(input));
    }

    public void PublishBridgeResponse(string response)
    {
        EnsureDirectory();
        File.WriteAllText(PathFor("bridge-response.txt"), response);
    }

    /// <summary>
    /// Reads and consumes <c>command.txt</c>. Returns <c>null</c> when the
    /// file doesn't exist, is empty, contains <c>done</c>, or fails to parse;
    /// otherwise the parsed command and the file is rewritten with <c>done\n</c>
    /// to acknowledge consumption.
    /// </summary>
    public AutomationCommand? TakeCommand()
    {
        var path = PathFor("command.txt");
        if (!File.Exists(path)) return null;

        string contents;
        try { contents = File.ReadAllText(path); }
        catch (IOException) { return null; }

        if (contents.Length > AutomationProtocol.MaxCommandBytes)
            throw new AutomationCommandTooLargeException();

        var line = contents.Trim(' ', '\n', '\r', '\t');
        if (line.Length == 0 || line == "done") return null;

        AutomationCommand command;
        try { command = AutomationProtocol.Parse(line); }
        catch (AutomationProtocolException) { return null; }

        File.WriteAllText(path, "done\n");
        return command;
    }

    public string PathFor(string name) => Path.Combine(Directory, name);

    private void EnsureDirectory()
    {
        if (!System.IO.Directory.Exists(Directory))
            System.IO.Directory.CreateDirectory(Directory);
    }
}
