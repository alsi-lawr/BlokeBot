using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Features.Points.Giveaways;

public sealed class PointsGiveawayEligibilityPolicy(
    HostBotStatusService botStatus,
    ILogger<PointsGiveawayEligibilityPolicy> log
)
{
    public async Task<HostStreamLivenessOutcome> GetStreamLivenessAsync(
        string hostLogin,
        CancellationToken ct
    )
    {
        try
        {
            return await botStatus.GetStreamLivenessAsync(hostLogin, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogCritical(
                "Points giveaway stream-liveness evaluation failed unexpectedly for host {HostLogin} with {FailureType}; the operation will be escalated.",
                hostLogin,
                exception.GetType().FullName
            );
            throw new PointsGiveawayStreamLivenessException(hostLogin, exception);
        }
    }

    public async Task<bool> IsFollowerEligibilityAvailableAsync(
        string hostLogin,
        PointsSettings settings,
        CancellationToken ct
    )
    {
        if (settings.GiveawayEligibility != PointsEligibilityMode.Followers)
            return true;

        var status = await botStatus.GetStatusAsync(hostLogin, ct);
        return status.ModeratorState == HostBotModeratorState.IsModerator;
    }

    public async Task<FollowerCheckResult> CheckJoinEligibilityAsync(
        PointsSettings settings,
        string hostLogin,
        string login,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
        return settings.GiveawayEligibility switch
        {
            PointsEligibilityMode.Subscribers => HasSubscriberBadge(tags)
                ? FollowerCheckResult.Eligible
                : FollowerCheckResult.NotEligible,
            PointsEligibilityMode.Followers => await botStatus.IsFollowerAsync(
                hostLogin,
                login,
                ct
            ),
            _ => FollowerCheckResult.Eligible,
        };
    }

    private static bool HasSubscriberBadge(IReadOnlyDictionary<string, string> tags)
    {
        if (!tags.TryGetValue("badges", out var badges))
            return false;

        return badges
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x.StartsWith("subscriber/", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class PointsGiveawayStreamLivenessException(
    string hostLogin,
    Exception innerException
) : Exception("Points giveaway stream-liveness evaluation failed unexpectedly.", innerException)
{
    internal string HostLogin { get; } = hostLogin;
}
