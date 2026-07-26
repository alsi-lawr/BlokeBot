namespace BlokeBot.Twitch.Runtime;

public enum EventSubCredentialKind
{
    ConfiguredBot,
    Broadcaster,
}

public sealed record EventSubAuthorizationContext(EventSubCredentialKind CredentialKind)
{
    public static EventSubAuthorizationContext ConfiguredBot { get; } =
        new(EventSubCredentialKind.ConfiguredBot);

    public static EventSubAuthorizationContext Broadcaster { get; } =
        new(EventSubCredentialKind.Broadcaster);
}
