using BlokeBot.Core.Features.Plugins;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayDelivery
{
    private static string ResourceBase(FullOverlayRenderLease lease) =>
        $"/full-overlay/resources/{lease.Id}";

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    internal async Task<PluginAssetContentResolution> PluginAssetAsync(
        Guid resourceId,
        Guid widgetId,
        Guid assetId,
        string path,
        CancellationToken ct
    )
    {
        if (
            !_renders.TryGetValue(resourceId, out var lease)
            || !lease.DeclaredPlugins.TryGetValue(widgetId, out var declared)
            || declared.Id != assetId
            || !await RefreshAuthorityAsync(lease, ct)
        )
        {
            return new PluginAssetContentResolution.NotFound();
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            lease.Cancellation
        );
        ct = lifetime.Token;
        var context = Context(lease);
        var asset = await widgets.ResolvePluginAssetAsync(
            context,
            new(widgetId),
            declared.Endpoint,
            path,
            ct
        );
        return await ContextCurrentAsync(lease, context, ct)
            ? asset
            : new PluginAssetContentResolution.NotFound();
    }

    internal async Task<OverlayMediaContent?> MediaAsync(
        Guid resourceId,
        Guid assetId,
        int revision,
        CancellationToken ct
    )
    {
        if (
            !_renders.TryGetValue(resourceId, out var lease)
            || !await RefreshAuthorityAsync(lease, ct)
        )
        {
            return null;
        }
        var context = Context(lease);
        if (!lease.DeclaredMedia.Contains(new(assetId, revision)))
        {
            return null;
        }
        var content = await media.ResolveContentAsync(lease.HostId, assetId, revision, ct);
        return await ContextCurrentAsync(lease, context, ct) ? content : null;
    }

    internal async Task<bool> CompleteAsync(
        PublishedFullOverlay selected,
        Guid connectionId,
        Guid runId,
        CancellationToken ct
    )
    {
        var lease = _renders.Values.FirstOrDefault(lease =>
            lease.ConnectionId == connectionId
            && lease.Selected is { } current
            && current.HostId == selected.HostId
            && current.OverlayId == selected.OverlayId
            && current.Generation == selected.Generation
        );
        if (lease is null || !await RefreshAuthorityAsync(lease, ct))
        {
            return false;
        }
        _ = await cues.CompleteFullAsync(
            lease.HostId,
            lease.OverlayId,
            lease.Selected!.Generation,
            runId,
            ct
        );
        return true;
    }
}
