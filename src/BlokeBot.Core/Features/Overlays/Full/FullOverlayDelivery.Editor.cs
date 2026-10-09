using BlokeBot.Core.Auth.Sessions;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayDelivery
{
    internal async Task ReleasePreviewAsync(
        AuthenticatedSession session,
        Guid id,
        CancellationToken ct
    )
    {
        if (!_previews.TryGetValue(id, out var preview))
        {
            return;
        }
        var authorization = await authority.AuthorizeAsync(session, ct);
        if (
            authorization is not OverlayManagementAuthorization.Granted granted
            || granted.Actor != preview.Actor
        )
        {
            return;
        }
        preview.Revoke();
        _ = _previews.TryRemove(id, out _);
        foreach (
            var lease in _renders.Values.Where(lease => ReferenceEquals(lease.Preview, preview))
        )
        {
            Close(lease);
        }
    }
}
