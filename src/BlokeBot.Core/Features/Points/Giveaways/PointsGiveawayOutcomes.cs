using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Points.Giveaways;

public abstract record PointsGiveawayStartOutcome
{
    private PointsGiveawayStartOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Started, TResult> started,
        Func<InvalidConfiguration, TResult> invalidConfiguration,
        Func<AlreadyActive, TResult> alreadyActive,
        Func<Cooldown, TResult> cooldown,
        Func<StreamOffline, TResult> streamOffline,
        Func<StreamLivenessUnavailable, TResult> streamLivenessUnavailable,
        Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable
    );

    public sealed record Started(PointsSettings Settings) : PointsGiveawayStartOutcome
    {
        public override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<InvalidConfiguration, TResult> invalidConfiguration,
            Func<AlreadyActive, TResult> alreadyActive,
            Func<Cooldown, TResult> cooldown,
            Func<StreamOffline, TResult> streamOffline,
            Func<StreamLivenessUnavailable, TResult> streamLivenessUnavailable,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable
        )
        {
            return started(this);
        }
    }

    public sealed record InvalidConfiguration(
        PointsSettings Settings,
        PointsConfigurationValidationError Failure
    ) : PointsGiveawayStartOutcome
    {
        public override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<InvalidConfiguration, TResult> invalidConfiguration,
            Func<AlreadyActive, TResult> alreadyActive,
            Func<Cooldown, TResult> cooldown,
            Func<StreamOffline, TResult> streamOffline,
            Func<StreamLivenessUnavailable, TResult> streamLivenessUnavailable,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable
        )
        {
            return invalidConfiguration(this);
        }
    }

    public sealed record AlreadyActive(PointsSettings Settings) : PointsGiveawayStartOutcome
    {
        public override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<InvalidConfiguration, TResult> invalidConfiguration,
            Func<AlreadyActive, TResult> alreadyActive,
            Func<Cooldown, TResult> cooldown,
            Func<StreamOffline, TResult> streamOffline,
            Func<StreamLivenessUnavailable, TResult> streamLivenessUnavailable,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable
        )
        {
            return alreadyActive(this);
        }
    }

    public sealed record Cooldown(PointsSettings Settings, TimeSpan TimeLeft)
        : PointsGiveawayStartOutcome
    {
        public override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<InvalidConfiguration, TResult> invalidConfiguration,
            Func<AlreadyActive, TResult> alreadyActive,
            Func<Cooldown, TResult> cooldown,
            Func<StreamOffline, TResult> streamOffline,
            Func<StreamLivenessUnavailable, TResult> streamLivenessUnavailable,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable
        )
        {
            return cooldown(this);
        }
    }

    public sealed record StreamOffline(PointsSettings Settings) : PointsGiveawayStartOutcome
    {
        public override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<InvalidConfiguration, TResult> invalidConfiguration,
            Func<AlreadyActive, TResult> alreadyActive,
            Func<Cooldown, TResult> cooldown,
            Func<StreamOffline, TResult> streamOffline,
            Func<StreamLivenessUnavailable, TResult> streamLivenessUnavailable,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable
        )
        {
            return streamOffline(this);
        }
    }

    public sealed record StreamLivenessUnavailable(
        PointsSettings Settings,
        HostStreamLivenessOutcome.Unavailable Failure
    ) : PointsGiveawayStartOutcome
    {
        public override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<InvalidConfiguration, TResult> invalidConfiguration,
            Func<AlreadyActive, TResult> alreadyActive,
            Func<Cooldown, TResult> cooldown,
            Func<StreamOffline, TResult> streamOffline,
            Func<StreamLivenessUnavailable, TResult> streamLivenessUnavailable,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable
        )
        {
            return streamLivenessUnavailable(this);
        }
    }

    public sealed record FollowerEligibilityUnavailable(PointsSettings Settings)
        : PointsGiveawayStartOutcome
    {
        public override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<InvalidConfiguration, TResult> invalidConfiguration,
            Func<AlreadyActive, TResult> alreadyActive,
            Func<Cooldown, TResult> cooldown,
            Func<StreamOffline, TResult> streamOffline,
            Func<StreamLivenessUnavailable, TResult> streamLivenessUnavailable,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable
        )
        {
            return followerEligibilityUnavailable(this);
        }
    }
}

public abstract record PointsGiveawayJoinOutcome
{
    private PointsGiveawayJoinOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Joined, TResult> joined,
        Func<NotActive, TResult> notActive,
        Func<DuplicateJoin, TResult> duplicateJoin,
        Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable,
        Func<NotEligible, TResult> notEligible
    );

    public sealed record Joined(PointsSettings Settings, string User) : PointsGiveawayJoinOutcome
    {
        public override TResult Match<TResult>(
            Func<Joined, TResult> joined,
            Func<NotActive, TResult> notActive,
            Func<DuplicateJoin, TResult> duplicateJoin,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable,
            Func<NotEligible, TResult> notEligible
        )
        {
            return joined(this);
        }
    }

    public sealed record NotActive(PointsSettings Settings, string User) : PointsGiveawayJoinOutcome
    {
        public override TResult Match<TResult>(
            Func<Joined, TResult> joined,
            Func<NotActive, TResult> notActive,
            Func<DuplicateJoin, TResult> duplicateJoin,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable,
            Func<NotEligible, TResult> notEligible
        )
        {
            return notActive(this);
        }
    }

    public sealed record DuplicateJoin(PointsSettings Settings, string User)
        : PointsGiveawayJoinOutcome
    {
        public override TResult Match<TResult>(
            Func<Joined, TResult> joined,
            Func<NotActive, TResult> notActive,
            Func<DuplicateJoin, TResult> duplicateJoin,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable,
            Func<NotEligible, TResult> notEligible
        )
        {
            return duplicateJoin(this);
        }
    }

    public sealed record FollowerEligibilityUnavailable(PointsSettings Settings, string User)
        : PointsGiveawayJoinOutcome
    {
        public override TResult Match<TResult>(
            Func<Joined, TResult> joined,
            Func<NotActive, TResult> notActive,
            Func<DuplicateJoin, TResult> duplicateJoin,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable,
            Func<NotEligible, TResult> notEligible
        )
        {
            return followerEligibilityUnavailable(this);
        }
    }

    public sealed record NotEligible(PointsSettings Settings, string User)
        : PointsGiveawayJoinOutcome
    {
        public override TResult Match<TResult>(
            Func<Joined, TResult> joined,
            Func<NotActive, TResult> notActive,
            Func<DuplicateJoin, TResult> duplicateJoin,
            Func<FollowerEligibilityUnavailable, TResult> followerEligibilityUnavailable,
            Func<NotEligible, TResult> notEligible
        )
        {
            return notEligible(this);
        }
    }
}

public sealed record PointsGiveawayWinnerPayout(string Login, PointAmount Payout);

public abstract record PointsGiveawayDrawOutcome
{
    private PointsGiveawayDrawOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Missing, TResult> missing,
        Func<NotActive, TResult> notActive,
        Func<NoEntrants, TResult> noEntrants,
        Func<PayoutFailed, TResult> payoutFailed,
        Func<Winners, TResult> winners
    );

    public sealed record Missing : PointsGiveawayDrawOutcome
    {
        public override TResult Match<TResult>(
            Func<Missing, TResult> missing,
            Func<NotActive, TResult> notActive,
            Func<NoEntrants, TResult> noEntrants,
            Func<PayoutFailed, TResult> payoutFailed,
            Func<Winners, TResult> winners
        )
        {
            return missing(this);
        }
    }

    public sealed record NotActive(PointsSettings Settings) : PointsGiveawayDrawOutcome
    {
        public override TResult Match<TResult>(
            Func<Missing, TResult> missing,
            Func<NotActive, TResult> notActive,
            Func<NoEntrants, TResult> noEntrants,
            Func<PayoutFailed, TResult> payoutFailed,
            Func<Winners, TResult> winners
        )
        {
            return notActive(this);
        }
    }

    public sealed record NoEntrants(PointsSettings Settings) : PointsGiveawayDrawOutcome
    {
        public override TResult Match<TResult>(
            Func<Missing, TResult> missing,
            Func<NotActive, TResult> notActive,
            Func<NoEntrants, TResult> noEntrants,
            Func<PayoutFailed, TResult> payoutFailed,
            Func<Winners, TResult> winners
        )
        {
            return noEntrants(this);
        }
    }

    public sealed record PayoutFailed(PointsSettings Settings, PointBalanceMutationFailure Failure)
        : PointsGiveawayDrawOutcome
    {
        public override TResult Match<TResult>(
            Func<Missing, TResult> missing,
            Func<NotActive, TResult> notActive,
            Func<NoEntrants, TResult> noEntrants,
            Func<PayoutFailed, TResult> payoutFailed,
            Func<Winners, TResult> winners
        )
        {
            return payoutFailed(this);
        }
    }

    public sealed record Winners : PointsGiveawayDrawOutcome
    {
        public Winners(PointsSettings settings, IEnumerable<PointsGiveawayWinnerPayout> winners)
        {
            Settings = settings;
            Payouts = winners.ToImmutableArray();
            if (Payouts.IsEmpty)
            {
                throw new ArgumentException(
                    "A winners outcome requires at least one winner.",
                    nameof(winners)
                );
            }
        }

        public PointsSettings Settings { get; }

        public ImmutableArray<PointsGiveawayWinnerPayout> Payouts { get; }

        public override TResult Match<TResult>(
            Func<Missing, TResult> missing,
            Func<NotActive, TResult> notActive,
            Func<NoEntrants, TResult> noEntrants,
            Func<PayoutFailed, TResult> payoutFailed,
            Func<Winners, TResult> winners
        )
        {
            return winners(this);
        }
    }
}

public abstract record PointsGiveawayCancelOutcome
{
    private PointsGiveawayCancelOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Cancelled, TResult> cancelled,
        Func<NotActive, TResult> notActive
    );

    public sealed record Cancelled(PointsSettings Settings) : PointsGiveawayCancelOutcome
    {
        public override TResult Match<TResult>(
            Func<Cancelled, TResult> cancelled,
            Func<NotActive, TResult> notActive
        )
        {
            return cancelled(this);
        }
    }

    public sealed record NotActive(PointsSettings Settings) : PointsGiveawayCancelOutcome
    {
        public override TResult Match<TResult>(
            Func<Cancelled, TResult> cancelled,
            Func<NotActive, TResult> notActive
        )
        {
            return notActive(this);
        }
    }
}
