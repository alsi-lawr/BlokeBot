using BlokeBot.Commands;

namespace BlokeBot.Tests;

internal static class TestCommandContext
{
    private static readonly IServiceProvider Services = new EmptyServiceProvider();

    public static TwitchCommandContext Create(
        string login,
        string channel,
        string commandName
    ) =>
        Create(
            login,
            channel,
            commandName,
            [],
            (string _, CancellationToken _) => ValueTask.CompletedTask
        );

    public static TwitchCommandContext Create(
        string login,
        string channel,
        string commandName,
        IReadOnlyList<string> args,
        Func<string, CancellationToken, ValueTask> reply
    ) =>
        new(
            Message(login, channel, commandName, args),
            commandName,
            Services,
            reply
        );

    public static TwitchCommandContext Create(
        string login,
        string channel,
        string commandName,
        IReadOnlyList<string> args,
        Func<TwitchCommandResponse, CancellationToken, ValueTask> respond
    ) =>
        new(
            Message(login, channel, commandName, args),
            commandName,
            Services,
            respond,
            resolveReplyTarget: false
        );

    private static TwitchChatMessage Message(
        string login,
        string channel,
        string commandName,
        IReadOnlyList<string> args
    )
    {
        var text = args.Count == 0
            ? $"!{commandName}"
            : $"!{commandName} {string.Join(' ', args)}";
        return new TwitchChatMessage(
            login,
            channel,
            text,
            $":{login}!u@h PRIVMSG #{channel} :{text}",
            new Dictionary<string, string>()
        );
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
