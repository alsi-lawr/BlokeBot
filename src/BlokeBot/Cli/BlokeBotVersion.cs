using System.Reflection;

namespace BlokeBot.Cli;

internal static class BlokeBotVersion
{
    internal static string Current => Display(InformationalVersion());

    internal static string Display(string informationalVersion) =>
        informationalVersion.StartsWith("0.0.0-dev+", StringComparison.Ordinal)
            ? informationalVersion
            : informationalVersion.Split('+', 2)[0];

    private static string InformationalVersion() =>
        typeof(BlokeBotVersion)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0-dev";
}
