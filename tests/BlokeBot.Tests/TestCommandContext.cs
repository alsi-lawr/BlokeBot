using BlokeBot.Commands;

namespace BlokeBot.Tests;

internal static class TestCommandContext
{
    public static TwitchCommandContext Create(string login, string channel, string commandName)
    {
        return Create(login, channel, commandName, [], (_, _) => ValueTask.CompletedTask);
    }

    public static TwitchCommandContext Create(
        string login,
        string channel,
        string commandName,
        IReadOnlyList<string> args,
        CommandResponder respond
    )
    {
        return new()
        {
            Message = Message(login, channel, commandName, args),
            CommandName = commandName,
            Responder = respond,
        };
    }

    private static ChatMessage Message(
        string login,
        string channel,
        string commandName,
        IReadOnlyList<string> args
    )
    {
        var text = args.Count == 0 ? $"!{commandName}" : $"!{commandName} {string.Join(' ', args)}";
        return new ChatMessage(
            login,
            channel,
            text,
            $":{login}!u@h PRIVMSG #{channel} :{text}",
            new Dictionary<string, string>()
        );
    }
}
