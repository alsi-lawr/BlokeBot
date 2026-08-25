using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginTwitchHostModule(IClipMarkerDashboardOperations clipsMarkers)
    : IPluginHostModule
{
    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Twitch;

    public async ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        var hostId = ((PluginInvocationContext.Channel)call.Context).Host.Value;
        var outcome = await clipsMarkers.CreateMarkerAsync(
            hostId,
            ((PluginValue.String)call.Arguments[0]).Value,
            cancellationToken
        );
        return outcome is ClipMarkerOperationOutcome.MarkerCreated
            ? PluginChatHostModule.Returned()
            : new PluginHostCallOutcome.Failed(
                new(PluginHostFailureCode.ProviderRejected, "Twitch marker was rejected.")
            );
    }
}
