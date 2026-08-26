using System.Collections.Immutable;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

internal sealed record PluginMarketplaceRuntimeContext(
    PluginHostCompatibilityTarget Target,
    IPluginHostCallDispatcher HostCalls,
    ILogger<PluginWorkerClient> WorkerLogger
)
{
    internal static PluginMarketplaceRuntimeContext Create(
        BlokeBotBuildIdentity build,
        IEnumerable<IPluginHostModule> modules,
        IPluginHostCallDispatcher hostCalls,
        ILogger<PluginWorkerClient> workerLogger
    )
    {
        var versionText = build.InformationalVersion.Split('+', 2)[0];
        if (!SemanticVersion.TryCreate(versionText, out var version))
        {
            throw new InvalidOperationException(
                "BlokeBot informational version is not a semantic version."
            );
        }

        if (!PluginRuntimeIdentifierResolver.TryResolveCurrent(out var runtimeIdentifier))
        {
            throw new InvalidOperationException(
                "The current platform is not a supported plugin target."
            );
        }

        var target = new PluginHostCompatibilityTarget(
            version,
            PluginRuntimeContract.Current.HostApiVersion,
            runtimeIdentifier,
            modules
                .Select(static module => module.Descriptor)
                .OrderBy(static descriptor => descriptor.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray()
        );
        return new(target, hostCalls, workerLogger);
    }
}
