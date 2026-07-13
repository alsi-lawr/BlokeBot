namespace BlokeBot.Functional.Tests.Examples;

public abstract record SubmissionOutcome
{
    private SubmissionOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Accepted, TResult> accepted,
        Func<Deferred, TResult> deferred,
        Func<Rejected, TResult> rejected
    );

    public sealed record Accepted : SubmissionOutcome
    {
        public Accepted(string receipt)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(receipt);
            Receipt = receipt;
        }

        public string Receipt { get; }

        public override TResult Match<TResult>(
            Func<Accepted, TResult> accepted,
            Func<Deferred, TResult> deferred,
            Func<Rejected, TResult> rejected
        )
        {
            return accepted(this);
        }
    }

    public sealed record Deferred : SubmissionOutcome
    {
        public Deferred(TimeSpan retryAfter)
        {
            if (retryAfter <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(retryAfter),
                    retryAfter,
                    "Retry delay must be positive."
                );
            }

            RetryAfter = retryAfter;
        }

        public TimeSpan RetryAfter { get; }

        public override TResult Match<TResult>(
            Func<Accepted, TResult> accepted,
            Func<Deferred, TResult> deferred,
            Func<Rejected, TResult> rejected
        )
        {
            return deferred(this);
        }
    }

    public sealed record Rejected : SubmissionOutcome
    {
        public Rejected(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        public string Reason { get; }

        public override TResult Match<TResult>(
            Func<Accepted, TResult> accepted,
            Func<Deferred, TResult> deferred,
            Func<Rejected, TResult> rejected
        )
        {
            return rejected(this);
        }
    }
}

public static class SubmissionOutcomeDescription
{
    public static string Describe(SubmissionOutcome outcome)
    {
        return outcome.Match(
            accepted => $"Accepted: {accepted.Receipt}",
            deferred => $"Deferred: {deferred.RetryAfter.TotalMinutes:0} minutes",
            rejected => $"Rejected: {rejected.Reason}"
        );
    }
}
