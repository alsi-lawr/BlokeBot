using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Functional;
using BlokeBot.Persistence;

namespace BlokeBot.Core.Features.Points.WatchTime;

public enum WatchTimeConfigurationError
{
    AmountRequired,
    PositiveWholeAmountRequired,
    AmountOutOfRange,
}

public abstract record WatchTimeConfiguration
{
    private WatchTimeConfiguration() { }

    public abstract T Match<T>(Func<Disabled, T> disabled, Func<Enabled, T> enabled);

    public sealed record Disabled : WatchTimeConfiguration
    {
        internal Disabled(Option<PointAmount> amount) => Amount = amount;

        public Option<PointAmount> Amount { get; }

        public override T Match<T>(Func<Disabled, T> disabled, Func<Enabled, T> enabled) =>
            disabled(this);
    }

    public sealed record Enabled : WatchTimeConfiguration
    {
        internal Enabled(PointAmount amount) => Amount = amount;

        public PointAmount Amount { get; }

        public override T Match<T>(Func<Disabled, T> disabled, Func<Enabled, T> enabled) =>
            enabled(this);
    }

    public static Result<WatchTimeConfiguration, WatchTimeConfigurationError> Create(
        bool enabled,
        string? amount
    ) =>
        string.IsNullOrWhiteSpace(amount)
            ? enabled
                ? Result<WatchTimeConfiguration, WatchTimeConfigurationError>.Error(
                    WatchTimeConfigurationError.AmountRequired
                )
                : Result<WatchTimeConfiguration, WatchTimeConfigurationError>.Success(
                    new Disabled(Option<PointAmount>.None)
                )
            : PointAmount
                .ParseNonNegativeAbsolute(amount)
                .Match(
                    value =>
                        value.IsZero
                            ? Result<WatchTimeConfiguration, WatchTimeConfigurationError>.Error(
                                WatchTimeConfigurationError.PositiveWholeAmountRequired
                            )
                            : Result<WatchTimeConfiguration, WatchTimeConfigurationError>.Success(
                                enabled
                                    ? new Enabled(value)
                                    : new Disabled(Option<PointAmount>.Some(value))
                            ),
                    error =>
                        Result<WatchTimeConfiguration, WatchTimeConfigurationError>.Error(
                            error == PointAmountParseError.AmountOutOfRange
                                ? WatchTimeConfigurationError.AmountOutOfRange
                                : WatchTimeConfigurationError.PositiveWholeAmountRequired
                        )
                );
}

internal sealed record WatchTimeConfigurationWrite(WatchTimeConfiguration Configuration);

public interface IWatchTimeSettingsCommitObserver
{
    void SettingsCommitted(
        int hostId,
        WatchTimeSettingsWriteResult result,
        DateTimeOffset receiptUtc
    );
}
