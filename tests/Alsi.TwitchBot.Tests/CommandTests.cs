using Alsi.TwitchBot;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Alsi.TwitchBot.Tests;

public sealed class CommandTests
{
    [Test]
    public async Task Dispatches_callback_commands_with_case_insensitive_route_and_args()
    {
        List<string> replies = [];
        IReadOnlyList<string> capturedArgs = [];

        var dispatcher = BuildDispatcher(builder =>
            builder.AddCommands(commands =>
                commands.Map(
                    "deaths",
                    async (ctx, args, ct) =>
                    {
                        capturedArgs = args;
                        await ctx.ReplyAsync($"{ctx.Message.Login}:{args[0]}:{args[1]}", ct);
                    }
                )
            )
        );

        await dispatcher.DispatchAsync(
            Message("alice", "!DEATHS 12 extra"),
            ReplyTo(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["alice:12:extra"]);
        capturedArgs.ShouldBe(["12", "extra"]);
    }

    [Test]
    public async Task Dispatch_ignores_unknown_or_non_command_messages()
    {
        var calls = 0;
        var dispatcher = BuildDispatcher(builder =>
            builder.AddCommands(commands =>
                commands.Map(
                    "known",
                    (_, _, _) =>
                    {
                        calls++;
                        return ValueTask.CompletedTask;
                    }
                )
            )
        );

        await dispatcher.DispatchAsync(
            Message("alice", "!missing"),
            (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );
        await dispatcher.DispatchAsync(
            Message("alice", "known"),
            (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );

        calls.ShouldBe(0);
    }

    [Test]
    public async Task Filters_can_deny_commands()
    {
        var calls = 0;
        var dispatcher = BuildDispatcher(builder =>
            builder.AddCommands(commands =>
                commands
                    .UseFilter<DenyAllFilter>()
                    .Map(
                        "known",
                        (_, _, _) =>
                        {
                            calls++;
                            return ValueTask.CompletedTask;
                        }
                    )
            )
        );

        await dispatcher.DispatchAsync(
            Message("alice", "!known"),
            (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );

        calls.ShouldBe(0);
    }

    [Test]
    public async Task Dispatches_module_commands()
    {
        List<string> replies = [];
        var dispatcher = BuildDispatcher(builder => builder.AddCommandModule<TestModule>());

        await dispatcher.DispatchAsync(
            Message("alice", "!module value"),
            ReplyTo(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["value"]);
    }

    private static TwitchCommandDispatcher BuildDispatcher(Action<ITwitchBotBuilder> configure)
    {
        var services = new ServiceCollection();
        var builder = services.AddTwitchBot(options =>
        {
            options.Identity.BotUsername = "bot";
            options.Identity.ClientId = "client";
            options.Identity.ClientSecret = "secret";
            options.Identity.RedirectUri = "http://localhost/callback";
        });
        services.AddSingleton<ITwitchBotChannelProvider>(new TestChannelProvider("channel"));
        configure(builder);
        return services.BuildServiceProvider().GetRequiredService<TwitchCommandDispatcher>();
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

    private sealed class TestChannelProvider(string channel) : ITwitchBotChannelProvider
    {
        public ValueTask<IReadOnlyList<string>> GetChannelsAsync(
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<IReadOnlyList<string>>([channel]);
    }
}
