using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Giveaways;

public sealed class PointsGiveawayEligibilityPolicy(HostBotStatusService botStatus)
{
    public async Task<bool> IsStreamLiveAsync(string hostLogin, CancellationToken ct)
    {
        try
        {
            return await botStatus.IsStreamLiveAsync(hostLogin, ct);
        }
        catch
        {
            return false;
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
