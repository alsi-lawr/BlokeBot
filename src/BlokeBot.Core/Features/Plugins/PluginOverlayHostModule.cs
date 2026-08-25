using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginOverlayHostModule(
    HostFeatureService features,
    IOverlayCueAdmissionService overlays
) : IPluginHostModule
{
    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Overlay;

    public async ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        var hostId = ((PluginInvocationContext.Channel)call.Context).Host.Value;
        if (
            !Guid.TryParse(((PluginValue.String)call.Arguments[0]).Value, out var targetId)
            || !Guid.TryParse(((PluginValue.String)call.Arguments[1]).Value, out var cueId)
        )
        {
            return Failed(
                PluginHostFailureCode.InvalidArguments,
                "Overlay identifiers are invalid."
            );
        }
        if (!await features.IsEnabledAsync(hostId, HostFeatureFlags.Overlays, cancellationToken))
        {
            return Failed(PluginHostFailureCode.Unavailable, "Overlays are disabled.");
        }

        var references = await overlays.ResolveReferencesAsync(
            new(hostId, targetId, cueId),
            cancellationToken
        );
        if (references is not OverlayCueReferenceOutcome.Available)
        {
            return Failed(PluginHostFailureCode.NotFound, "Overlay cue is unavailable.");
        }
        var catalog = await overlays.QueryCatalogAsync(hostId, cancellationToken);
        var cue = catalog.Cues.SingleOrDefault(candidate => candidate.Id == cueId);
        if (cue is null)
        {
            return Failed(PluginHostFailureCode.NotFound, "Overlay cue is unavailable.");
        }
        var outcome = await overlays.AdmitAsync(
            new(
                hostId,
                targetId,
                cueId,
                cue.DefaultQueuePolicy,
                OverlayCueAdmissionOrigin.Automation,
                OverlayCueSafeContext.Empty
            ),
            cancellationToken
        );
        return outcome is OverlayCueAdmissionOutcome.Running or OverlayCueAdmissionOutcome.Queued
            ? PluginChatHostModule.Returned()
            : Failed(PluginHostFailureCode.ProviderRejected, "Overlay cue was rejected.");
    }

    private static PluginHostCallOutcome.Failed Failed(
        PluginHostFailureCode code,
        string message
    ) => new(new(code, message));
}
