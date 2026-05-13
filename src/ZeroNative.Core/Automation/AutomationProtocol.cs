namespace ZeroNative.Automation;

public enum AutomationAction
{
    Reload,
    Wait,
    Bridge,
}

public sealed record AutomationCommand(AutomationAction Action, string Value = "");

public class AutomationProtocolException : Exception
{
    public AutomationProtocolException(string message) : base(message) { }
}

public class AutomationInvalidCommandException : AutomationProtocolException
{
    public AutomationInvalidCommandException() : base("invalid automation command") { }
    public AutomationInvalidCommandException(string message) : base(message) { }
}

public class AutomationCommandTooLargeException : AutomationProtocolException
{
    public AutomationCommandTooLargeException() : base("automation command too large") { }
}

public static class AutomationProtocol
{
    public const string DefaultDirectory = ".zero-native-automation";
    public const int MaxCommandBytes = 16 * 1024 + 64;

    public static AutomationCommand Parse(string line)
    {
        var trimmed = line.Trim(' ', '\n', '\r', '\t');
        if (trimmed.Length == 0) throw new AutomationInvalidCommandException();

        var separator = trimmed.IndexOf(' ');
        var action = separator < 0 ? trimmed : trimmed[..separator];
        var value = separator < 0 ? "" : trimmed[(separator + 1)..].Trim(' ', '\n', '\r', '\t');

        return action switch
        {
            "reload" => new AutomationCommand(AutomationAction.Reload),
            "wait" => new AutomationCommand(AutomationAction.Wait, value),
            "bridge" when value.Length > 0 => new AutomationCommand(AutomationAction.Bridge, value),
            _ => throw new AutomationInvalidCommandException($"unknown automation action: {action}"),
        };
    }

    public static string FormatLine(string action, string value)
    {
        if (action.Length + value.Length + 2 > MaxCommandBytes)
            throw new AutomationCommandTooLargeException();
        return value.Length > 0 ? $"{action} {value}\n" : $"{action}\n";
    }
}
