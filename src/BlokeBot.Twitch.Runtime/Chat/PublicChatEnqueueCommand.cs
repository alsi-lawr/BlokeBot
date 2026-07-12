namespace BlokeBot.Twitch.Runtime;

internal sealed record PublicChatEnqueueCommand
{
    public required string Channel { get; init; }

    public required string Message { get; init; }
}
