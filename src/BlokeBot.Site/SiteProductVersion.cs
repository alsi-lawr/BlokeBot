using System.Reflection;

namespace BlokeBot.Site;

internal sealed record SiteProductVersion(string Value)
{
    internal static SiteProductVersion Current { get; } =
        new(
            Display(
                typeof(SiteProductVersion)
                    .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    ?? "0.0.0-dev"
            )
        );

    internal static string Display(string informationalVersion) =>
        informationalVersion.StartsWith("0.0.0-dev+", StringComparison.Ordinal)
            ? informationalVersion
            : informationalVersion.Split('+', 2)[0];
}
