using BlokeBot.Commands;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Commands.Tests;

public sealed class CommandTests
{
    [Test]
    public async Task CallbackCommand_DispatchingCaseInsensitiveRoute_PassesArgumentsAndReplies()
    {
        List<TwitchCommandResponse> responses = [];
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

        await dispatcher.DispatchResponsesAsync(
            Message("alice", "!DEATHS 12 extra"),
            RecordResponses(responses),
            CancellationToken.None
        );

        responses.ShouldBe([TwitchCommandResponse.Chat("alice:12:extra")]);
        capturedArgs.ShouldBe(["12", "extra"]);
    }

    [Test]
    public async Task UnknownOrPlainMessage_Dispatching_DoesNotInvokeHandler()
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

        await dispatcher.DispatchResponsesAsync(
            Message("alice", "!missing"),
            (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("alice", "known"),
            (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );

        calls.ShouldBe(0);
    }

    [Test]
    public async Task DenyingFilter_Dispatching_PreventsHandlerExecution()
    {
        var calls = 0;
        var dispatcher = BuildDispatcher(builder =>
            builder
                .AddCommandFilter<DenyAllFilter>()
                .AddCommands(commands =>
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

        await dispatcher.DispatchResponsesAsync(
            Message("alice", "!known"),
            (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );

        calls.ShouldBe(0);
    }

    [Test]
    public async Task RegisteredCommandModule_Dispatching_ExecutesModuleHandler()
    {
        List<TwitchCommandResponse> responses = [];
        var dispatcher = BuildDispatcher(builder => builder.AddCommandModule<TestModule>());

        await dispatcher.DispatchResponsesAsync(
            Message("alice", "!module value"),
            RecordResponses(responses),
            CancellationToken.None
        );

        responses.ShouldBe([TwitchCommandResponse.Chat("value")]);
    }

    private static TwitchCommandDispatcher BuildDispatcher(Action<ITwitchBotBuilder> configure)
    {
        var services = new ServiceCollection();
        var builder = services.AddTwitchCommands();
        configure(builder);
        return services.BuildServiceProvider().GetRequiredService<TwitchCommandDispatcher>();
    }

    private static TwitchChatMessage Message(string login, string text)
    {
        return new(
            login,
            "channel",
            text,
            $":{login}!u@h PRIVMSG #channel :{text}",
            new Dictionary<string, string>()
        );
    }

    private static TwitchCommandResponder RecordResponses(
        List<TwitchCommandResponse> responses
    )
    {
        return (response, _) =>
        {
            responses.Add(response);
            return ValueTask.CompletedTask;
        };
    }
}
