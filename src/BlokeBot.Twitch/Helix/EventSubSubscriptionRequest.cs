namespace BlokeBot.Twitch;

public sealed record EventSubSubscriptionRequest(
    string Type,
    string Version,
    IReadOnlyDictionary<string, string> Condition
);

/// <summary>
/// Manages the transport used for transport-neutral EventSub subscription definitions.
/// </summary>
public interface IEventSubSubscriptionTransport
{
    Task ResetAsync(string clientId, CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> ListEnabledOwnedIdsAsync(
        string clientId,
        CancellationToken cancellationToken
    );

    Task<string> CreateAsync(
        string clientId,
        EventSubSubscriptionRequest subscription,
        CancellationToken cancellationToken
    );

    Task DeleteAsync(string clientId, string subscriptionId, CancellationToken cancellationToken);
}

/// <summary>Coordinates direct-webhook callback verification with subscription creation.</summary>
public interface IEventSubSubscriptionVerification
{
    Task WaitAsync(string subscriptionId, CancellationToken cancellationToken);

    void Confirm(string subscriptionId);
}
