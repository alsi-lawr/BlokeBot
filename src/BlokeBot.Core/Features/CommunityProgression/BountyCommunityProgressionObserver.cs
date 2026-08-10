using BlokeBot.Core.Features.Bounties;

namespace BlokeBot.Core.Features.CommunityProgression;

internal sealed class BountyCommunityProgressionObserver(
    CommunityProgressionService progression,
    ILogger<BountyCommunityProgressionObserver> log
) : IBountyCompletionObserver
{
    public async Task BountyCompletedAsync(
        int hostId,
        Guid bountyPublicId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken
    )
    {
        try
        {
            _ = await progression.ProcessEventAsync(
                hostId,
                new CommunitySourceEvent.BountyCompleted(
                    bountyPublicId.ToString("N"),
                    completedAtUtc
                ),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogError(
                "Bounty completion progression dispatch failed with {FailureType}.",
                exception.GetType().Name
            );
        }
    }
}
