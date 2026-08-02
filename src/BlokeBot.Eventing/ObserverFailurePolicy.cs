namespace BlokeBot.Eventing;

public readonly record struct ObserverBoundary
{
    private ObserverBoundary(string value) => Value = value;

    public string Value { get; }

    public static ObserverBoundary Named(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ObserverBoundary(value);
    }

    public override string ToString() => Value;
}

public readonly record struct ObserverEventIdentity
{
    private ObserverEventIdentity(string value) => Value = value;

    public string Value { get; }

    public static ObserverEventIdentity Named(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ObserverEventIdentity(value);
    }

    public override string ToString() => Value;
}

public readonly record struct ObserverIdentity
{
    private ObserverIdentity(string value) => Value = value;

    public string Value { get; }

    public static ObserverIdentity Named(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ObserverIdentity(value);
    }

    public static ObserverIdentity For(Type observerType)
    {
        ArgumentNullException.ThrowIfNull(observerType);
        return Named(observerType.FullName ?? observerType.Name);
    }

    public override string ToString() => Value;
}

public readonly record struct ObserverCorrelationId
{
    private ObserverCorrelationId(string value) => Value = value;

    public string Value { get; }

    public static ObserverCorrelationId Named(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ObserverCorrelationId(value);
    }

    public override string ToString() => Value;
}

public interface IObserverDeadLetterPayload;

public interface IDurableObserverDeadLetterSink<TBoundary, TDeadLetter>
    where TDeadLetter : IObserverDeadLetterPayload
{
    ValueTask StoreAsync(
        ObserverDeadLetter<TDeadLetter> deadLetter,
        CancellationToken cancellationToken
    );
}

public abstract record ObserverFailurePolicy<TBoundary, TDeadLetter>
    where TDeadLetter : IObserverDeadLetterPayload
{
    private ObserverFailurePolicy() { }

    public required ObserverBoundary Boundary { get; init; }

    public sealed record ContinueAndReport : ObserverFailurePolicy<TBoundary, TDeadLetter>;

    public sealed record BoundedRetry : ObserverFailurePolicy<TBoundary, TDeadLetter>
    {
        /// <summary>
        /// Gets the total invocation limit, including the first attempt.
        /// </summary>
        public required int AttemptLimit
        {
            get;
            init
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value, 2);
                field = value;
            }
        }
    }

    public sealed record DeadLetter : ObserverFailurePolicy<TBoundary, TDeadLetter>
    {
        public required IDurableObserverDeadLetterSink<TBoundary, TDeadLetter> Sink { get; init; }
    }
}
