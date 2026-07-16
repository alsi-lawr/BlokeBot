using System.Reflection;

namespace BlokeBot.Cli;

internal static class BlokeBotVersion
{
    internal static string Current => Display(InformationalVersion());

    internal static string Display(string informationalVersion)
    {
        return informationalVersion.StartsWith("0.0.0-dev+", StringComparison.Ordinal)
            ? informationalVersion
            : informationalVersion.Split('+', 2)[0];
    }

    private static string InformationalVersion()
    {
        return typeof(BlokeBotVersion)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "0.0.0-dev";
    }
}
