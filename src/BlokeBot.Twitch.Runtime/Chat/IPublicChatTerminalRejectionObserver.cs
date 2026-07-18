namespace BlokeBot.Twitch.Runtime;

public sealed record PublicChatTerminalRejection(string Channel, string ProviderCode);

public interface IPublicChatTerminalRejectionObserver
{
    ValueTask TerminalRejectionAsync(
        PublicChatTerminalRejection rejection,
        CancellationToken cancellationToken
    );
}
