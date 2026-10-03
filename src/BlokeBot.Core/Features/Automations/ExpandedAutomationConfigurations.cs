namespace BlokeBot.Core.Features.Automations;

public enum CountdownLifecycle
{
    Started,
    Remaining,
    Finished,
    Cancelled,
    Reset,
}

public enum CountdownOperation
{
    Start,
    Reset,
    Cancel,
}

public enum AdTimingKind
{
    BeforeScheduled,
    Started,
    Remaining,
    ExpectedFinish,
}

public enum ScheduledTimeKind
{
    Once,
    Interval,
    Weekly,
}

public enum UptimeKind
{
    Threshold,
    Interval,
}

public enum MetadataField
{
    Any,
    Title,
    Category,
}

public enum ChatMatchKind
{
    Keyword,
    Phrase,
    Emote,
    FirstObserved,
}

public enum FeatureLifecycleKind
{
    GiveawayOpened,
    GiveawayClosed,
    GiveawayWinners,
    GuessingStarted,
    GuessingFinished,
    QueueJoined,
    QueueCalled,
}

public enum CueLifecycleKind
{
    Queued,
    Started,
    Finished,
    Interrupted,
}

public enum GoalLifecycleKind
{
    Started,
    Progressed,
    Ended,
    Milestone,
}

public enum RedemptionUpdateKind
{
    Fulfilled,
    Cancelled,
}

public sealed record CountdownSourceConfiguration(
    string Name,
    CountdownLifecycle Event,
    TimeSpan Remaining
) : AutomationConfiguration;

public sealed record CountdownActionConfiguration(
    string Name,
    TimeSpan Duration,
    CountdownOperation Operation
) : AutomationConfiguration;

public sealed record AdTimingSourceConfiguration(AdTimingKind Event, TimeSpan Offset)
    : AutomationConfiguration;

public sealed record ScheduledTimeSourceConfiguration(
    ScheduledTimeKind Kind,
    string Zone,
    DateTime LocalTime,
    DayOfWeek Day,
    TimeSpan Interval,
    bool LiveOnly
) : AutomationConfiguration;

public sealed record UptimeSourceConfiguration(UptimeKind Kind, TimeSpan Duration)
    : AutomationConfiguration;

public sealed record MetadataSourceConfiguration(MetadataField Field, string? CategoryId)
    : AutomationConfiguration;

public sealed record ChatMatchSourceConfiguration(ChatMatchKind Kind, string Match)
    : AutomationConfiguration;

public sealed record FeatureLifecycleSourceConfiguration(FeatureLifecycleKind Event)
    : AutomationConfiguration;

public sealed record CueLifecycleSourceConfiguration(
    CueLifecycleKind Event,
    string? CueId,
    string? TargetId = null
) : AutomationConfiguration;

public sealed record ManualSourceConfiguration(string Data) : AutomationConfiguration;

public sealed record OutgoingRaidSourceConfiguration : AutomationConfiguration;

public sealed record GoalSourceConfiguration(GoalLifecycleKind Event, long Milestone)
    : AutomationConfiguration;

public sealed record RedemptionUpdateSourceConfiguration(
    RedemptionUpdateKind Event,
    string? RewardId
) : AutomationConfiguration;

public sealed record ChatSettingsSourceConfiguration : AutomationConfiguration;

public sealed record ModerationSourceConfiguration(string Action) : AutomationConfiguration;
