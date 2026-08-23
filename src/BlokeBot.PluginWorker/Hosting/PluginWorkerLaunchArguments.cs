namespace BlokeBot.PluginWorker;

internal sealed record PluginWorkerLaunchArguments(
    string PipeName,
    string PackageRoot,
    string StateRoot
);

internal abstract record PluginWorkerLaunchArgumentOutcome
{
    private PluginWorkerLaunchArgumentOutcome() { }

    internal sealed record Accepted(PluginWorkerLaunchArguments Arguments)
        : PluginWorkerLaunchArgumentOutcome;

    internal sealed record Rejected : PluginWorkerLaunchArgumentOutcome;
}

internal static class PluginWorkerLaunchArgumentParser
{
    internal static PluginWorkerLaunchArgumentOutcome Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length != 6)
        {
            return new PluginWorkerLaunchArgumentOutcome.Rejected();
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length; index += 2)
        {
            if (!values.TryAdd(arguments[index], arguments[index + 1]))
            {
                return new PluginWorkerLaunchArgumentOutcome.Rejected();
            }
        }

        return
            values.TryGetValue("--pipe", out var pipe)
            && pipe is { Length: >= 1 and <= 128 }
            && pipe.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
            )
            && values.TryGetValue("--package", out var package)
            && !string.IsNullOrWhiteSpace(package)
            && values.TryGetValue("--state", out var state)
            && !string.IsNullOrWhiteSpace(state)
            ? new PluginWorkerLaunchArgumentOutcome.Accepted(
                new(pipe, Path.GetFullPath(package), Path.GetFullPath(state))
            )
            : new PluginWorkerLaunchArgumentOutcome.Rejected();
    }
}
