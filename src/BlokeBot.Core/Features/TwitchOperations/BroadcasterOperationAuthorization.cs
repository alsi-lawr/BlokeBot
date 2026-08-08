using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations;

/// <summary>
/// Shared broadcaster readiness for the native Twitch operations: resolves the broadcaster token
/// against the milestone scopes and raises the single reconnect durable alert when it is not
/// ready. Every native operation service authorizes provider calls through this one path.
/// </summary>
public sealed class BroadcasterOperationAuthorization(
    IHostBroadcasterTokenStatusProvider broadcasters,
    DurableAlertService alerts
)
{
    public async Task<string?> ReadyTokenAsync(int hostId, CancellationToken cancellationToken)
    {
        var status = await broadcasters.GetTokenStatusAsync(
            hostId,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            cancellationToken
        );
        if (status is TokenStatus.Ready ready)
        {
            return ready.AccessToken;
        }

        _ = await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Warning,
                "twitch-broadcaster-authorization",
                "reauthorize-v1",
                "Reconnect Twitch integration",
                "Reconnect the selected channel's Twitch integration and approve all requested permissions.",
                "/twitch-operations"
            )
            .ExecuteAsync(cancellationToken);
        return null;
    }
}
