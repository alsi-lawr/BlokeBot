using BlokeBot.Commands;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Commands.Tests;

public sealed class CommandStrategyDispatcherTests
{
    private enum TestKind
    {
        Public,
        Moderator,
    }

    [Test]
    public async Task Strategy_module_executes_resolved_dynamic_route()
    {
        List<string> replies = [];
        var services = BuildServices(new TestResolver(TestKind.Public, "state"));
        var dispatcher = services.GetRequiredService<TwitchCommandDispatcher>();

        await dispatcher.DispatchAsync(
            Message("alice", "!dynamic value"),
            ReplyTo(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["Public:state:value"]);
    }

    [Test]
    public async Task Strategy_dispatcher_applies_moderator_gate()
    {
        List<string> replies = [];
        var services = BuildServices(new TestResolver(TestKind.Moderator, "state"));
        var dispatcher = services.GetRequiredService<TwitchCommandDispatcher>();

        await dispatcher.DispatchAsync(
            Message("viewer", "!mod"),
            ReplyTo(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["mods only"]);
    }

    private static ServiceProvider BuildServices(ICommandRouteResolver<TestKind, string> resolver)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        services.AddSingleton<ICommandStrategy<TestKind, string>>(
            new TestStrategy(TestKind.Public, requiresModerator: false)
        );
        services.AddSingleton<ICommandStrategy<TestKind, string>>(
            new TestStrategy(TestKind.Moderator, requiresModerator: true)
        );
        services.AddSingleton<CommandStrategyCatalog<TestKind, string>>();
        services.AddSingleton<CommandStrategyDispatcher<TestKind, string>>();
        services.AddTwitchCommands().AddCommandModule<CommandStrategyModule<TestKind, string>>();
        return services.BuildServiceProvider();
    }

    private sealed class TestResolver(TestKind kind, string state)
        : ICommandRouteResolver<TestKind, string>
    {
        public ValueTask<CommandRoute<TestKind, string>?> ResolveAsync(
            TwitchCommandContext context,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<CommandRoute<TestKind, string>?>(
                new CommandRoute<TestKind, string>(kind, state)
            );
    }

    private sealed class TestStrategy(TestKind kind, bool requiresModerator)
        : ICommandStrategy<TestKind, string>
    {
        public TestKind Kind { get; } = kind;

        public IReadOnlyList<string> DefaultAliases { get; } = [kind.ToString().ToLowerInvariant()];

        public bool RequiresModerator { get; } = requiresModerator;

        public ValueTask<string> ModeratorOnlyReplyAsync(
            CommandStrategyContext<TestKind, string> context,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult("mods only");

        public async ValueTask ExecuteAsync(
            CommandStrategyContext<TestKind, string> context,
            CancellationToken cancellationToken
        )
        {
            await context.Command.ReplyAsync(
                $"{Kind}:{context.State}:{context.Args[0]}",
                cancellationToken
            );
        }
    }

    private static TwitchChatMessage Message(string login, string text) =>
        new(
            login,
            "channel",
            text,
            $":{login}!u@h PRIVMSG #channel :{text}",
            new Dictionary<string, string>()
        );

    private static Func<string, CancellationToken, ValueTask> ReplyTo(List<string> replies) =>
        (message, _) =>
        {
            replies.Add(message);
            return ValueTask.CompletedTask;
        };
}
