using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Competitions;

public sealed class CompetitionFeatureObserver(CompetitionService competitions)
    : IHostFeatureActivationObserver
{
    public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
        HostFeatureActivationChange change,
        CancellationToken cancellationToken
    )
    {
        if (
            change.Feature == HostFeatureFlags.Competitions
            && change.State is HostFeatureActivationState.Disabled
        )
        {
            _ = await competitions.SuppressDueRemindersAsync(change.HostId, cancellationToken);
        }

        return new HostFeatureAutomaticWorkResult.Complete();
    }
}
