using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.BlokeRaid;

public static class BlokeRaidLimits
{
    public const int MinimumHealth = 100;
    public const int MaximumHealth = 10_000_000;
    public const int MaximumWard = 1_000_000;
    public const int MaximumActionOutcome = 100_000;
    public const int MaximumCooldownSeconds = 86_400;
    public const int MaximumPerStreamLimit = 10_000;
    public const int MaximumCampaignDurationHours = 24 * 365;
    public const int MaximumHistoryCount = 20;
    public const int MaximumEventReadCount = 200;
}

public sealed record BlokeRaidViewer(string TwitchUserId, string Login, string DisplayName);

public sealed record BlokeRaidActor(string TwitchUserId, string Login);

public sealed record BlokeRaidConfigurationDraft(
    int Revision,
    string BossName,
    int MaximumHealth,
    int MaximumWard,
    int CampaignDurationHours,
    int AttackMinimum,
    int AttackMaximum,
    int AttackCooldownSeconds,
    int AttackPerStreamLimit,
    int MendMinimum,
    int MendMaximum,
    int MendCooldownSeconds,
    int MendPerStreamLimit,
    int SpecialMinimum,
    int SpecialMaximum,
    int SpecialCooldownSeconds,
    int SpecialPerStreamLimit,
    PointAmount SpecialPointCost,
    int CorrectGuessDamage,
    PointAmount VictoryPointReward,
    int PhaseTwoHealthPercent,
    int PhaseThreeHealthPercent,
    string PhaseOneResponse,
    string PhaseTwoResponse,
    string PhaseThreeResponse,
    string VictoryResponse,
    string ExpiryResponse,
    BlokeRaidResetPolicy ResetPolicy,
    DayOfWeek WeeklyResetDay,
    int WeeklyResetHourUtc
);

public sealed record BlokeRaidConfigurationView(
    int Revision,
    string BossName,
    int MaximumHealth,
    int MaximumWard,
    int CampaignDurationHours,
    BlokeRaidActionRuleView Attack,
    BlokeRaidActionRuleView Mend,
    BlokeRaidActionRuleView Special,
    int CorrectGuessDamage,
    PointAmount VictoryPointReward,
    int PhaseTwoHealthPercent,
    int PhaseThreeHealthPercent,
    string PhaseOneResponse,
    string PhaseTwoResponse,
    string PhaseThreeResponse,
    string VictoryResponse,
    string ExpiryResponse,
    BlokeRaidResetPolicy ResetPolicy,
    DayOfWeek WeeklyResetDay,
    int WeeklyResetHourUtc,
    DateTime? NextWeeklyResetAtUtc
);

public sealed record BlokeRaidActionRuleView(
    int Minimum,
    int Maximum,
    int CooldownSeconds,
    int PerStreamLimit,
    PointAmount PointCost
);

public sealed record BlokeRaidCampaignCommand(
    string OperationKey,
    BlokeRaidActor Actor,
    string PrivateReason
);

public sealed record BlokeRaidActionCommand(
    string OperationKey,
    BlokeRaidActionKind Kind,
    BlokeRaidViewer Viewer,
    string StreamKey
);

public sealed record BlokeRaidGuessingResult(
    int RoundId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<BlokeRaidViewer> CorrectGuessers
);

public sealed record BlokeRaidActionView(
    long Id,
    BlokeRaidActionKind Kind,
    BlokeRaidActionSource Source,
    BlokeRaidViewer? Viewer,
    int Outcome,
    PointAmount PointCost,
    int BossHealthBefore,
    int BossHealthAfter,
    int WardBefore,
    int WardAfter,
    int PhaseAfter,
    int? GuessRoundId,
    string Response,
    DateTime OccurredAtUtc
);

public sealed record BlokeRaidContributionView(
    BlokeRaidViewer Viewer,
    int Damage,
    int WardRestored,
    int ActionCount,
    int SpecialCount,
    int CorrectGuessCount,
    DateTime LastContributedAtUtc
)
{
    public int Total => Damage + WardRestored;
}

public sealed record BlokeRaidEventView(
    long Id,
    Guid CampaignId,
    BlokeRaidEventKind Kind,
    string PublicPayload,
    DateTime OccurredAtUtc
);

public sealed record BlokeRaidCampaignView(
    Guid Id,
    BlokeRaidCampaignStatus Status,
    string BossName,
    int MaximumHealth,
    int CurrentHealth,
    int MaximumWard,
    int CurrentWard,
    int CurrentPhase,
    PointAmount VictoryPointReward,
    BlokeRaidResetPolicy ResetPolicy,
    DateTime StartedAtUtc,
    DateTime EndsAtUtc,
    DateTime? CompletedAtUtc,
    bool VictoryRewarded,
    int Revision,
    IReadOnlyList<BlokeRaidContributionView> Contributions,
    IReadOnlyList<BlokeRaidActionView> RecentActions
);

public sealed record BlokeRaidModeratorView(
    BlokeRaidConfigurationView Configuration,
    BlokeRaidCampaignView? ActiveCampaign,
    IReadOnlyList<BlokeRaidCampaignView> History
);

public sealed record BlokeRaidPublicView(
    string HostLogin,
    string HostDisplayName,
    BlokeRaidCampaignView? ActiveCampaign,
    BlokeRaidCampaignView? CompletedRecap
);

public abstract record BlokeRaidConfigurationOutcome
{
    private BlokeRaidConfigurationOutcome() { }

    public sealed record Saved(BlokeRaidConfigurationView Configuration)
        : BlokeRaidConfigurationOutcome;

    public sealed record FeatureDisabled : BlokeRaidConfigurationOutcome;

    public sealed record Conflict(string Message) : BlokeRaidConfigurationOutcome;

    public sealed record Invalid(string Message) : BlokeRaidConfigurationOutcome;
}

public abstract record BlokeRaidCampaignOutcome
{
    private BlokeRaidCampaignOutcome() { }

    public sealed record Succeeded(BlokeRaidCampaignView Campaign, bool WasIdempotent = false)
        : BlokeRaidCampaignOutcome;

    public sealed record FeatureDisabled : BlokeRaidCampaignOutcome;

    public sealed record NoActiveCampaign : BlokeRaidCampaignOutcome;

    public sealed record Conflict(string Message) : BlokeRaidCampaignOutcome;

    public sealed record Invalid(string Message) : BlokeRaidCampaignOutcome;
}

public abstract record BlokeRaidActionOutcome
{
    private BlokeRaidActionOutcome() { }

    public sealed record Succeeded(
        BlokeRaidActionView Action,
        BlokeRaidCampaignView Campaign,
        bool WasIdempotent = false
    ) : BlokeRaidActionOutcome;

    public sealed record FeatureDisabled : BlokeRaidActionOutcome;

    public sealed record NoActiveCampaign : BlokeRaidActionOutcome;

    public sealed record Cooldown(TimeSpan Remaining) : BlokeRaidActionOutcome;

    public sealed record PerStreamLimitReached : BlokeRaidActionOutcome;

    public sealed record InsufficientPoints(PointAmount Balance, PointAmount Cost)
        : BlokeRaidActionOutcome;

    public sealed record PointCapacityExceeded : BlokeRaidActionOutcome;

    public sealed record SourceSuppressed : BlokeRaidActionOutcome;

    public sealed record Invalid(string Message) : BlokeRaidActionOutcome;
}

public interface IBlokeRaidRandom
{
    int NextInclusive(int minimum, int maximum);
}

public sealed class BlokeRaidRandom : IBlokeRaidRandom
{
    public int NextInclusive(int minimum, int maximum) => Random.Shared.Next(minimum, maximum + 1);
}
