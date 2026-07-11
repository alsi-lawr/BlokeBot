namespace BlokeBot.Eventing;

public readonly record struct ObserverFailurePolicyKey
{
    public required string Value { get; init; }

    public static ObserverFailurePolicyKey Named(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ObserverFailurePolicyKey { Value = value };
    }

    public override string ToString() => Value;
}

/// <summary>
/// Explicitly selects attempt-once, structured-reporting isolation for one fan-out boundary.
/// </summary>
public sealed record ContinueAndReportObserverPolicy
{
    public required ObserverFailurePolicyKey Boundary { get; init; }
}
