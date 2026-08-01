using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Core.Features.Points.Giveaways;

public sealed class PointsGiveawayEligibilityPolicy(
    HostBotStatusService botStatus,
    ILogger<PointsGiveawayEligibilityPolicy> log
)
{
    public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string hostLogin) =>
        IO<HostStreamLivenessOutcome, Never>.Create(async ct =>
        {
            try
            {
                var result = await botStatus.GetStreamLiveness(hostLogin).ExecuteAsync(ct);
                return result.Match(
                    Result<HostStreamLivenessOutcome, Never>.Success,
                    _ => throw new UnreachableException()
                );
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
        });

    public async Task<bool> IsFollowerEligibilityAvailableAsync(
        string hostLogin,
        PointsSettings settings,
        CancellationToken ct
    )
    {
        if (settings.GiveawayEligibility != PointsEligibilityMode.Followers)
        {
            return true;
        }

        var result = await botStatus.GetStatus(hostLogin).ExecuteAsync(ct);
        return result.Match(status => status.IsModerator, _ => throw new UnreachableException());
    }

    public IO<FollowerCheckOutcome, Never> CheckJoinEligibility(
        PointsSettings settings,
        string hostLogin,
        string login,
        IReadOnlyDictionary<string, string> tags
    ) =>
        settings.GiveawayEligibility switch
        {
            PointsEligibilityMode.Subscribers => Immediate(
                HasSubscriberBadge(tags)
                    ? new FollowerCheckOutcome.Eligible()
                    : new FollowerCheckOutcome.NotEligible()
            ),
            PointsEligibilityMode.Followers => botStatus.CheckFollower(hostLogin, login),
            _ => Immediate(new FollowerCheckOutcome.Eligible()),
        };

    private static IO<FollowerCheckOutcome, Never> Immediate(FollowerCheckOutcome outcome) =>
        IO<FollowerCheckOutcome, Never>.Create(_ =>
            ValueTask.FromResult(Result<FollowerCheckOutcome, Never>.Success(outcome))
        );

    private static bool HasSubscriberBadge(IReadOnlyDictionary<string, string> tags)
    {
        if (!tags.TryGetValue("badges", out var badges))
        {
            return false;
        }

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
