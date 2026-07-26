namespace BlokeBot.Twitch;

public sealed record EventSubSubscriptionRequest(
    string Type,
    string Version,
    IReadOnlyDictionary<string, string> Condition,
    string SessionId
);
