using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public sealed record PluginLifecycleOptions(TimeSpan DrainTimeout, TimeSpan RestartBackoff)
{
    public static PluginLifecycleOptions Default { get; } =
        new(
            TimeSpan.FromMilliseconds(PluginWorkerLimits.CancellationGraceMilliseconds),
            TimeSpan.FromSeconds(1)
        );
}
