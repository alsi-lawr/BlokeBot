using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Overlays;

internal sealed record OverlaySourceBinding(
    int HostId,
    OverlayType Type,
    OverlayConfiguration Configuration,
    OverlayRevision Revision
)
{
    internal static OverlaySourceBinding From(ResolvedOverlayInstance instance) =>
        new(instance.HostId, instance.Type, instance.Configuration, instance.Revision);
}
