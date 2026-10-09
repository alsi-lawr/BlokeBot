using System.Globalization;
using System.Numerics;

namespace BlokeBot.Persistence;

public readonly record struct PointBalanceTarget(int HostId, string Login);

public readonly record struct CanonicalPointInteger
{
    private CanonicalPointInteger(string text) => Text = text;

    public string Text { get; }

    public static CanonicalPointInteger From(BigInteger value) =>
        new(value.ToString(CultureInfo.InvariantCulture));

    public static bool TryCreate(string text, out CanonicalPointInteger value)
    {
        if (
            BigInteger.TryParse(
                text,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed
            )
            && parsed.ToString(CultureInfo.InvariantCulture) == text
        )
        {
            value = new(text);
            return true;
        }
        value = default;
        return false;
    }

    public BigInteger ToBigInteger() => BigInteger.Parse(Text, CultureInfo.InvariantCulture);
}

public readonly record struct PointDeltaBounds(
    CanonicalPointInteger MinimumCurrent,
    CanonicalPointInteger MaximumAfter
);

public abstract record PointBalanceRead
{
    private PointBalanceRead() { }

    public abstract T Match<T>(Func<Found, T> found, Func<Missing, T> missing);

    public sealed record Found(CanonicalPointInteger Amount) : PointBalanceRead
    {
        public override T Match<T>(Func<Found, T> found, Func<Missing, T> missing) => found(this);
    }

    public sealed record Missing : PointBalanceRead
    {
        public override T Match<T>(Func<Found, T> found, Func<Missing, T> missing) => missing(this);
    }
}

public abstract record PointDeltaSqlOutcome
{
    private PointDeltaSqlOutcome() { }

    public abstract T Match<T>(Func<Applied, T> applied, Func<Rejected, T> rejected);

    public sealed record Applied(CanonicalPointInteger Before, CanonicalPointInteger After)
        : PointDeltaSqlOutcome
    {
        public override T Match<T>(Func<Applied, T> applied, Func<Rejected, T> rejected) =>
            applied(this);
    }

    public sealed record Rejected(PointBalanceRead Current) : PointDeltaSqlOutcome
    {
        public override T Match<T>(Func<Applied, T> applied, Func<Rejected, T> rejected) =>
            rejected(this);
    }
}

public abstract record PointDeleteSqlOutcome
{
    private PointDeleteSqlOutcome() { }

    public abstract T Match<T>(Func<Deleted, T> deleted, Func<Missing, T> missing);

    public sealed record Deleted(CanonicalPointInteger Before) : PointDeleteSqlOutcome
    {
        public override T Match<T>(Func<Deleted, T> deleted, Func<Missing, T> missing) =>
            deleted(this);
    }

    public sealed record Missing : PointDeleteSqlOutcome
    {
        public override T Match<T>(Func<Deleted, T> deleted, Func<Missing, T> missing) =>
            missing(this);
    }
}

public readonly record struct WatchTimeLedgerWrite(
    PointBalanceTarget Target,
    CanonicalPointInteger Delta,
    CanonicalPointInteger BalanceAfter,
    string OperationKey,
    DateTime CreatedAtUtc
);

public abstract record WatchTimeLedgerInsertOutcome
{
    private WatchTimeLedgerInsertOutcome() { }

    public abstract T Match<T>(Func<Inserted, T> inserted, Func<ExistingKey, T> existingKey);

    public sealed record Inserted : WatchTimeLedgerInsertOutcome
    {
        public override T Match<T>(Func<Inserted, T> inserted, Func<ExistingKey, T> existingKey) =>
            inserted(this);
    }

    public sealed record ExistingKey : WatchTimeLedgerInsertOutcome
    {
        public override T Match<T>(Func<Inserted, T> inserted, Func<ExistingKey, T> existingKey) =>
            existingKey(this);
    }
}
