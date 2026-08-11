using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Competitions;

public sealed class CompetitionFeatureObserver(CompetitionService competitions)
    : IHostFeatureChangeObserver
{
    public async ValueTask FeatureChangedAsync(
        int hostId,
        HostFeatureFlags feature,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        if (feature == HostFeatureFlags.Competitions && !enabled)
        {
            _ = await competitions.SuppressDueRemindersAsync(hostId, cancellationToken);
        }
    }
}
