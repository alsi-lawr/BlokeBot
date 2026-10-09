namespace BlokeBot.Core.Features.Overlays.Full;

// Existing feed-owner changes, not document selection changes. Delivery subscribes by this
// identity and reads ProjectFullAsync; notification never consumes or advances the queue.
internal sealed record FullOverlayEventFeedIdentity(int HostId, Guid OverlayId, Guid BindingId);
