using System.Globalization;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Bounties;

/// <summary>
/// Read-only wording and class selection shared by the moderator board and the public board. The
/// money sentences are derived from <see cref="BountyLifecycle"/> so a Failed bounty whose policy
/// spends pledges never claims a refund.
/// </summary>
internal static class BountyPresentation
{
    private static readonly TimeSpan _soonThreshold = TimeSpan.FromHours(24);

    public static bool IsTerminal(BountyStatus status) =>
        status
            is BountyStatus.Completed
                or BountyStatus.Failed
                or BountyStatus.Expired
                or BountyStatus.Cancelled;

    public static string StatusPillClass(BountyStatus status) =>
        status switch
        {
            BountyStatus.Funding => "status-pill status-pill--blue",
            BountyStatus.Accepted => "status-pill status-pill--violet",
            BountyStatus.Completed => "status-pill status-pill--green",
            BountyStatus.Failed => "status-pill status-pill--red",
            BountyStatus.Expired => "status-pill status-pill--amber",
            _ => "status-pill status-pill--slate",
        };

    public static string OutcomeBandClass(BountyStatus status) =>
        status switch
        {
            BountyStatus.Completed => "bounty-outcome bounty-outcome--green",
            BountyStatus.Failed => "bounty-outcome bounty-outcome--red",
            BountyStatus.Expired => "bounty-outcome bounty-outcome--amber",
            _ => "bounty-outcome bounty-outcome--slate",
        };

    public static string Countdown(BountyView bounty, DateTime nowUtc) =>
        Countdown(bounty.ExpiresAtUtc, nowUtc);

    public static bool DeadlineIsSoon(BountyView bounty, DateTime nowUtc) =>
        bounty.ExpiresAtUtc - nowUtc < _soonThreshold;

    public static string CountdownClass(BountyView bounty, DateTime nowUtc) =>
        DeadlineIsSoon(bounty, nowUtc)
            ? "text-[var(--tone-amber-fg)]"
            : "text-[var(--app-text-strong)]";

    private static string Countdown(DateTime expiresAtUtc, DateTime nowUtc) =>
        Countdown(expiresAtUtc - nowUtc);

    private static string Countdown(TimeSpan remaining) =>
        remaining <= TimeSpan.Zero ? "Past the deadline"
        : remaining.TotalDays >= 1 ? $"{(int)remaining.TotalDays}d {remaining.Hours}h left"
        : remaining.TotalHours >= 1 ? $"{remaining.Hours}h {remaining.Minutes}m left"
        : $"{Math.Max(1, remaining.Minutes)}m left";

    public static string AbsoluteUtc(DateTime value) =>
        string.Format(CultureInfo.InvariantCulture, "{0:ddd d MMM} · {0:HH:mm} UTC", value);

    public static string ShortDate(DateTime value) =>
        value.ToString("d MMM", CultureInfo.InvariantCulture);

    public static int FundingPercent(BountyView bounty)
    {
        if (bounty.FundingTarget.IsZero)
        {
            return bounty.PledgedAmount.IsZero ? 0 : 100;
        }

        var scaled = bounty.PledgedAmount.Value * 100 / bounty.FundingTarget.Value;
        return scaled >= 100 ? 100 : (int)scaled;
    }

    public static bool TargetIsMet(BountyView bounty) =>
        bounty.PledgedAmount >= bounty.FundingTarget;

    public static string MeterClass(BountyView bounty) =>
        TargetIsMet(bounty) ? "meter meter--full" : "meter";

    public static string PledgedSummary(BountyView bounty) =>
        $"/ {bounty.FundingTarget.ToDisplayString()} points pledged";

    public static string FundingProgressSummary(BountyView bounty) =>
        TargetIsMet(bounty)
            ? "Target met · pledges reserved"
            : $"{FundingPercent(bounty)}% · {Remaining(bounty).ToDisplayString()} to go";

    public static string ContributorSummary(BountyView bounty) =>
        bounty.ContributorCount == 1 ? "1 contributor" : $"{bounty.ContributorCount} contributors";

    public static string BonusSummary(BountyView bounty) =>
        bounty.CompletionReward.IsZero
            ? "No completion bonus"
            : $"Completion bonus {bounty.CompletionReward.ToDisplayString()} · {DistributionWord(bounty.RewardDistribution)} split";

    public static IReadOnlyList<BountyTimelinePoint> Timeline(BountyView bounty)
    {
        var points = new List<BountyTimelinePoint>(3)
        {
            new("Created", ShortDate(bounty.CreatedAtUtc)),
        };
        if (bounty.AcceptedAtUtc is { } accepted)
        {
            points.Add(new("Accepted", ShortDate(accepted)));
        }

        if (bounty.ResolvedAtUtc is { } resolved)
        {
            points.Add(new(bounty.Status.ToString(), ShortDate(resolved)));
        }

        return points;
    }

    public static string OutcomeSentence(BountyView bounty)
    {
        var when = ShortDate(ResolvedAt(bounty));
        var pledged = bounty.PledgedAmount;
        if (bounty.Status == BountyStatus.Completed)
        {
            var spent = pledged.IsZero
                ? $"Completed {when} · no points were pledged"
                : $"Completed {when} · {pledged.ToDisplayString()} pledged points spent";
            return $"{spent}{CompletionBonusClause(bounty)}.";
        }

        var verb = bounty.Status.ToString();
        return pledged.IsZero ? $"{verb} {when} · no points were pledged."
            : BountyLifecycle.RefundsPledges(bounty.Status, bounty.FailurePledgePolicy)
                ? $"{verb} {when} · all {pledged.ToDisplayString()} pledged points refunded in full."
            : $"{verb} {when} · all {pledged.ToDisplayString()} pledged points spent under this bounty's failure policy.";
    }

    public static string OutcomeMeta(BountyView bounty)
    {
        var when = ShortDate(ResolvedAt(bounty));
        if (bounty.Status == BountyStatus.Completed)
        {
            var bonus = CompletionBonusMeta(bounty);
            var pledges = bounty.PledgedAmount.IsZero
                ? "no pledges"
                : $"{bounty.PledgedAmount.ToDisplayString()} spent";
            return $"{when} · {pledges}{bonus}";
        }

        return bounty.PledgedAmount.IsZero ? $"{when} · no pledges"
            : BountyLifecycle.RefundsPledges(bounty.Status, bounty.FailurePledgePolicy)
                ? $"{when} · every pledge refunded"
            : $"{when} · every pledge spent";
    }

    public static string AuditActionLabel(BountyAuditAction action) =>
        action switch
        {
            BountyAuditAction.Created => "Created",
            BountyAuditAction.FundingOpened => "Opened for funding",
            BountyAuditAction.Accepted => "Accepted",
            BountyAuditAction.Completed => "Completed",
            BountyAuditAction.Failed => "Failed",
            BountyAuditAction.Cancelled => "Cancelled",
            BountyAuditAction.Rejected => "Rejected",
            BountyAuditAction.Extended => "Extended",
            BountyAuditAction.PauseAdjusted => "Pause adjusted",
            BountyAuditAction.Expired => "Expired",
        };

    public static string ShortId(Guid id) => id.ToString("N")[..8];

    private static PointAmount Remaining(BountyView bounty) =>
        TargetIsMet(bounty)
            ? PointAmount.Zero
            : new PointAmount(bounty.FundingTarget.Value - bounty.PledgedAmount.Value);

    private static DateTime ResolvedAt(BountyView bounty) =>
        bounty.ResolvedAtUtc ?? bounty.UpdatedAtUtc;

    private static string CompletionBonusClause(BountyView bounty) =>
        bounty.CompletionReward.IsZero ? " · no completion bonus"
        : bounty.PledgedAmount.IsZero
            ? $" · completion bonus {bounty.CompletionReward.ToDisplayString()} not distributed because there were no contributors"
        : $" · completion bonus {bounty.CompletionReward.ToDisplayString()} distributed, {DistributionWord(bounty.RewardDistribution)} split";

    private static string CompletionBonusMeta(BountyView bounty) =>
        bounty.CompletionReward.IsZero ? string.Empty
        : bounty.PledgedAmount.IsZero ? " · no bonus distributed"
        : $" · bonus {bounty.CompletionReward.ToDisplayString()} distributed";

    private static string DistributionWord(BountyRewardDistribution distribution) =>
        distribution == BountyRewardDistribution.Equal ? "equal" : "proportional";
}

internal sealed record BountyTimelinePoint(string Label, string When);
