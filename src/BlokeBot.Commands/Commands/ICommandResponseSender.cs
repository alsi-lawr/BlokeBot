namespace BlokeBot.Commands;

public interface ICommandResponseSender
{
    ValueTask SendAsync(
        ChatMessage sourceMessage,
        CommandResponse response,
        CancellationToken cancellationToken
    );
}
