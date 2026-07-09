namespace BlokeBot.Commands;

public interface ITwitchCommandResponseSender
{
    ValueTask SendAsync(
        TwitchChatMessage sourceMessage,
        TwitchCommandResponse response,
        CancellationToken cancellationToken
    );
}
