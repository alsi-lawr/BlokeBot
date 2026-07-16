using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Guessing.Rounds;

public abstract record GuessRoundLifecycle
{
    private GuessRoundLifecycle() { }

    public abstract DateTime StartedAtUtc { get; }

    public abstract TResult Match<TResult>(
        Func<Open, TResult> open,
        Func<Closed, TResult> closed,
        Func<Completed, TResult> completed
    );

    internal static GuessRoundLifecycle FromPersistence(
        GuessRoundStatus status,
        DateTime startedAtUtc,
        DateTime? closedAtUtc,
        string? winningName
    )
    {
        return status switch
        {
            GuessRoundStatus.Open when closedAtUtc is null && winningName is null => new Open(
                startedAtUtc
            ),
            GuessRoundStatus.Closed
                when closedAtUtc is { } closed && closed >= startedAtUtc && winningName is null =>
                new Closed(startedAtUtc, closed),
            GuessRoundStatus.Completed
                when closedAtUtc is { } completedAt
                    && completedAt >= startedAtUtc
                    && !string.IsNullOrWhiteSpace(winningName) => new Completed(
                startedAtUtc,
                completedAt,
                winningName
            ),
            _ => throw new PersistenceDataIntegrityException(typeof(GuessRound)),
        };
    }

    public sealed record Open : GuessRoundLifecycle
    {
        internal Open(DateTime startedAtUtc)
        {
            StartedAtUtc = startedAtUtc;
        }

        public override DateTime StartedAtUtc { get; }

        public override TResult Match<TResult>(
            Func<Open, TResult> open,
            Func<Closed, TResult> closed,
            Func<Completed, TResult> completed
        )
        {
            return open(this);
        }
    }

    public sealed record Closed : GuessRoundLifecycle
    {
        internal Closed(DateTime startedAtUtc, DateTime closedAtUtc)
        {
            StartedAtUtc = startedAtUtc;
            ClosedAtUtc = closedAtUtc;
        }

        public override DateTime StartedAtUtc { get; }

        public DateTime ClosedAtUtc { get; }

        public override TResult Match<TResult>(
            Func<Open, TResult> open,
            Func<Closed, TResult> closed,
            Func<Completed, TResult> completed
        )
        {
            return closed(this);
        }
    }

    public sealed record Completed : GuessRoundLifecycle
    {
        internal Completed(DateTime startedAtUtc, DateTime closedAtUtc, string winningName)
        {
            StartedAtUtc = startedAtUtc;
            ClosedAtUtc = closedAtUtc;
            WinningName = winningName;
        }

        public override DateTime StartedAtUtc { get; }

        public DateTime ClosedAtUtc { get; }

        public string WinningName { get; }

        public override TResult Match<TResult>(
            Func<Open, TResult> open,
            Func<Closed, TResult> closed,
            Func<Completed, TResult> completed
        )
        {
            return completed(this);
        }
    }
}
