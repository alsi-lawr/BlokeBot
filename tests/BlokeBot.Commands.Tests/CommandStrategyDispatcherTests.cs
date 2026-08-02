using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Commands.Tests;

public sealed class CommandStrategyDispatcherTests
{
    private enum TestKind
    {
        Public,
        Moderator,
    }

    [Test]
    public async Task ResolvedDynamicRoute_Dispatching_ExecutesMatchingStrategy()
    {
        List<string> replies = [];
        var services = BuildServices(new TestResolver(TestKind.Public, "state"));
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();

        await dispatcher.DispatchResponsesAsync(
            Message("alice", "!dynamic value"),
            ReplyTo(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["Public:state:value"]);
    }

    [Test]
    public async Task ModeratorOnlyRoute_DispatchingViewer_ReturnsModeratorReply()
    {
        List<string> replies = [];
        var services = BuildServices(new TestResolver(TestKind.Moderator, "state"));
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();

        await dispatcher.DispatchResponsesAsync(
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
            new TestStrategy(
                TestKind.Public,
                new CommandStrategyAccess<TestKind, string>.Everyone()
            )
        );
        services.AddSingleton<ICommandStrategy<TestKind, string>>(
            new TestStrategy(
                TestKind.Moderator,
                new CommandStrategyAccess<TestKind, string>.ModeratorOnly(ModeratorResponse)
            )
        );
        services.AddSingleton<CommandStrategyCatalog<TestKind, string>>();
        services.AddSingleton<CommandStrategyDispatcher<TestKind, string>>();
        services.AddChatCommands().AddCommandModule<CommandStrategyModule<TestKind, string>>();
        return services.BuildServiceProvider();
    }

    private sealed class TestResolver(TestKind kind, string state)
        : ICommandRouteResolver<TestKind, string>
    {
        public ValueTask<CommandRouteResolution<TestKind, string>> ResolveAsync(
            ChatCommandContext context,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<CommandRouteResolution<TestKind, string>>(
                new CommandRouteResolution<TestKind, string>.Resolved(
                    new CommandRoute<TestKind, string>(kind, state)
                )
            );
    }

    private sealed class TestStrategy(TestKind kind, CommandStrategyAccess<TestKind, string> access)
        : ICommandStrategy<TestKind, string>
    {
        public TestKind Kind { get; } = kind;

        public IReadOnlyList<string> DefaultAliases { get; } = [kind.ToString().ToLowerInvariant()];

        public CommandStrategyAccess<TestKind, string> Access { get; } = access;

        public async ValueTask ExecuteAsync(
            CommandStrategyContext<TestKind, string> context,
            CancellationToken cancellationToken
        ) =>
            await context.Command.ReplyAsync(
                $"{Kind}:{context.State}:{context.Args[0]}",
                cancellationToken
            );
    }

    private static ValueTask<CommandResponse> ModeratorResponse(
        CommandStrategyContext<TestKind, string> context,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(CommandResponse.Chat("mods only"));

    private static ChatMessage Message(string login, string text) =>
        new(
            login,
            "channel",
            text,
            $":{login}!u@h PRIVMSG #channel :{text}",
            new Dictionary<string, string>()
        );

    private static CommandResponder ReplyTo(List<string> replies) =>
        (response, _) =>
        {
            replies.Add(response.Message);
            return ValueTask.CompletedTask;
        };
}
