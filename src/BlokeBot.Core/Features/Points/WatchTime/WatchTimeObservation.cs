using System.Collections.Immutable;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Points.WatchTime;

internal sealed record WatchTimeAccountSelection(bool Custom, string? UserId, string Login);

internal sealed record WatchTimeHostSnapshot(
    int HostId,
    string Login,
    string? UserId,
    HostFeatureFlags Features,
    BotChannelRuntimeState RuntimeState,
    bool Enabled,
    string? Amount,
    Guid Revision,
    Guid Generation,
    bool CustomBot,
    string? BotUserId,
    string? BotLogin
)
{
    internal WatchTimeAccountSelection Account(string defaultLogin) =>
        new(CustomBot, CustomBot ? BotUserId : null, CustomBot ? BotLogin ?? "" : defaultLogin);
}

internal sealed record WatchTimeEpoch(
    Guid Id,
    WatchTimeHostSnapshot Settings,
    BotChannelTarget Target,
    DateTimeOffset Anchor,
    long NextOrdinal
);

internal readonly record struct WatchTimeTickId(Guid Epoch, long Ordinal);

internal sealed record WatchTimeObservation(
    WatchTimeTickId Tick,
    WatchTimeEpoch Epoch,
    string BotUserId,
    string BotLogin,
    string StreamId,
    DateTimeOffset StreamStartedAt,
    DateTimeOffset OpportunityStart,
    DateTimeOffset OpportunityEnd,
    PointAmount Amount,
    ImmutableArray<HelixChatter> Targets
);

internal abstract record WatchTimeEligibility
{
    private WatchTimeEligibility() { }

    internal abstract T Match<T>(
        Func<Complete, T> complete,
        Func<Offline, T> offline,
        Func<Unavailable, T> unavailable
    );

    internal sealed record Complete(
        string BotUserId,
        string BotLogin,
        string StreamId,
        DateTimeOffset StreamStartedAt,
        ImmutableArray<HelixChatter> Targets
    ) : WatchTimeEligibility
    {
        internal override T Match<T>(
            Func<Complete, T> complete,
            Func<Offline, T> offline,
            Func<Unavailable, T> unavailable
        ) => complete(this);
    }

    internal sealed record Offline : WatchTimeEligibility
    {
        internal override T Match<T>(
            Func<Complete, T> complete,
            Func<Offline, T> offline,
            Func<Unavailable, T> unavailable
        ) => offline(this);
    }

    internal sealed record Unavailable : WatchTimeEligibility
    {
        internal override T Match<T>(
            Func<Complete, T> complete,
            Func<Offline, T> offline,
            Func<Unavailable, T> unavailable
        ) => unavailable(this);
    }
}

internal abstract record WatchTimeCreditOutcome
{
    private WatchTimeCreditOutcome() { }

    internal abstract T Match<T>(
        Func<Credited, T> credited,
        Func<AlreadyCredited, T> alreadyCredited,
        Func<NotAdmitted, T> notAdmitted,
        Func<CapExceeded, T> capExceeded,
        Func<Uncertain, T> uncertain
    );

    internal sealed record Credited : WatchTimeCreditOutcome
    {
        internal override T Match<T>(
            Func<Credited, T> credited,
            Func<AlreadyCredited, T> alreadyCredited,
            Func<NotAdmitted, T> notAdmitted,
            Func<CapExceeded, T> capExceeded,
            Func<Uncertain, T> uncertain
        ) => credited(this);
    }

    internal sealed record AlreadyCredited : WatchTimeCreditOutcome
    {
        internal override T Match<T>(
            Func<Credited, T> credited,
            Func<AlreadyCredited, T> alreadyCredited,
            Func<NotAdmitted, T> notAdmitted,
            Func<CapExceeded, T> capExceeded,
            Func<Uncertain, T> uncertain
        ) => alreadyCredited(this);
    }

    internal sealed record NotAdmitted : WatchTimeCreditOutcome
    {
        internal override T Match<T>(
            Func<Credited, T> credited,
            Func<AlreadyCredited, T> alreadyCredited,
            Func<NotAdmitted, T> notAdmitted,
            Func<CapExceeded, T> capExceeded,
            Func<Uncertain, T> uncertain
        ) => notAdmitted(this);
    }

    internal sealed record CapExceeded : WatchTimeCreditOutcome
    {
        internal override T Match<T>(
            Func<Credited, T> credited,
            Func<AlreadyCredited, T> alreadyCredited,
            Func<NotAdmitted, T> notAdmitted,
            Func<CapExceeded, T> capExceeded,
            Func<Uncertain, T> uncertain
        ) => capExceeded(this);
    }

    internal sealed record Uncertain : WatchTimeCreditOutcome
    {
        internal override T Match<T>(
            Func<Credited, T> credited,
            Func<AlreadyCredited, T> alreadyCredited,
            Func<NotAdmitted, T> notAdmitted,
            Func<CapExceeded, T> capExceeded,
            Func<Uncertain, T> uncertain
        ) => uncertain(this);
    }
}

internal enum WatchTimeStatusKind
{
    Off,
    Waiting,
    Offline,
    Unavailable,
    Active,
}

internal sealed record WatchTimeStatus(
    WatchTimeStatusKind Kind,
    string? Amount,
    DateTimeOffset? NextDue
);
