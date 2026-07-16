using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Points.Giveaways;

public abstract record PointsGiveawayLifecycle
{
    private PointsGiveawayLifecycle() { }

    public abstract TResult Match<TResult>(
        Func<Active, TResult> active,
        Func<Completed, TResult> completed,
        Func<Cancelled, TResult> cancelled,
        Func<Expired, TResult> expired
    );

    internal static PointsGiveawayLifecycle FromPersistence(
        PointsGiveawayStatus status,
        DateTime startedAtUtc,
        DateTime? completedAtUtc
    )
    {
        return status switch
        {
            PointsGiveawayStatus.Active when completedAtUtc is null => new Active(),
            PointsGiveawayStatus.Completed
                when completedAtUtc is { } completed && completed >= startedAtUtc => new Completed(
                completed
            ),
            PointsGiveawayStatus.Cancelled
                when completedAtUtc is { } cancelled && cancelled >= startedAtUtc => new Cancelled(
                cancelled
            ),
            PointsGiveawayStatus.Expired
                when completedAtUtc is { } expired && expired >= startedAtUtc => new Expired(
                expired
            ),
            _ => throw new PersistenceDataIntegrityException(typeof(PointsGiveaway)),
        };
    }

    public sealed record Active : PointsGiveawayLifecycle
    {
        public override TResult Match<TResult>(
            Func<Active, TResult> active,
            Func<Completed, TResult> completed,
            Func<Cancelled, TResult> cancelled,
            Func<Expired, TResult> expired
        )
        {
            return active(this);
        }
    }

    public sealed record Completed : PointsGiveawayLifecycle
    {
        internal Completed(DateTime completedAtUtc)
        {
            CompletedAtUtc = completedAtUtc;
        }

        public DateTime CompletedAtUtc { get; }

        public override TResult Match<TResult>(
            Func<Active, TResult> active,
            Func<Completed, TResult> completed,
            Func<Cancelled, TResult> cancelled,
            Func<Expired, TResult> expired
        )
        {
            return completed(this);
        }
    }

    public sealed record Cancelled : PointsGiveawayLifecycle
    {
        internal Cancelled(DateTime completedAtUtc)
        {
            CompletedAtUtc = completedAtUtc;
        }

        public DateTime CompletedAtUtc { get; }

        public override TResult Match<TResult>(
            Func<Active, TResult> active,
            Func<Completed, TResult> completed,
            Func<Cancelled, TResult> cancelled,
            Func<Expired, TResult> expired
        )
        {
            return cancelled(this);
        }
    }

    public sealed record Expired : PointsGiveawayLifecycle
    {
        internal Expired(DateTime completedAtUtc)
        {
            CompletedAtUtc = completedAtUtc;
        }

        public DateTime CompletedAtUtc { get; }

        public override TResult Match<TResult>(
            Func<Active, TResult> active,
            Func<Completed, TResult> completed,
            Func<Cancelled, TResult> cancelled,
            Func<Expired, TResult> expired
        )
        {
            return expired(this);
        }
    }
}
