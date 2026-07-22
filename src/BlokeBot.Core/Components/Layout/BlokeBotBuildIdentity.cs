using System.Reflection;

namespace BlokeBot.Core.Components.Layout;

internal sealed record BlokeBotBuildIdentity(string InformationalVersion)
{
    internal static BlokeBotBuildIdentity Current { get; } =
        new(
            typeof(BlokeBotBuildIdentity)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? "0.0.0-dev"
        );
}
