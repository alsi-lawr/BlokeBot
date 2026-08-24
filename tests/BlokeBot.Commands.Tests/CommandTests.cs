using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Commands.Tests;

public sealed class CommandTests
{
    [Test]
    public async Task CallbackCommand_DispatchingCaseInsensitiveRoute_PassesArgumentsAndReplies()
    {
        List<CommandResponse> responses = [];
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

        responses.ShouldBe([CommandResponse.Chat("alice:12:extra")]);
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
    public async Task ContextualBuiltInUnavailable_Dispatching_UsesCustomBeforePlugin()
    {
        List<string> calls = [];
        var dispatcher = BuildDispatcher(builder =>
            builder.AddCommands(commands =>
            {
                _ = commands.MapContextual(
                    new FixedChatCommandRoute("shared"),
                    (_, _, _) =>
                    {
                        calls.Add("built-in");
                        return ValueTask.FromResult<CommandHandlingOutcome>(
                            new CommandHandlingOutcome.Unhandled()
                        );
                    }
                );
                _ = commands.MapDynamic(
                    (context, _, _) =>
                    {
                        if (context.CommandName != "shared")
                        {
                            return ValueTask.FromResult<CommandHandlingOutcome>(
                                new CommandHandlingOutcome.Unhandled()
                            );
                        }

                        calls.Add("custom");
                        return ValueTask.FromResult<CommandHandlingOutcome>(
                            new CommandHandlingOutcome.Handled()
                        );
                    }
                );
                _ = commands.MapDynamic(
                    (_, _, _) =>
                    {
                        calls.Add("plugin");
                        return ValueTask.FromResult<CommandHandlingOutcome>(
                            new CommandHandlingOutcome.Handled()
                        );
                    }
                );
            })
        );

        await dispatcher.DispatchResponsesAsync(
            Message("alice", "!shared"),
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );

        calls.ShouldBe(["built-in", "custom"]);
    }

    [Test]
    public async Task ContextualBuiltInHandledNoEffect_Dispatching_DoesNotFallThrough()
    {
        var fallbackCalls = 0;
        var dispatcher = BuildDispatcher(builder =>
            builder.AddCommands(commands =>
            {
                _ = commands.MapContextual(
                    new FixedChatCommandRoute("shared"),
                    (_, _, _) =>
                        ValueTask.FromResult<CommandHandlingOutcome>(
                            new CommandHandlingOutcome.Handled()
                        )
                );
                _ = commands.MapDynamic(
                    (_, _, _) =>
                    {
                        fallbackCalls++;
                        return ValueTask.FromResult<CommandHandlingOutcome>(
                            new CommandHandlingOutcome.Handled()
                        );
                    }
                );
            })
        );

        await dispatcher.DispatchResponsesAsync(
            Message("alice", "!shared invalid"),
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );

        fallbackCalls.ShouldBe(0);
    }

    private static ChatCommandDispatcher BuildDispatcher(Action<IChatBotBuilder> configure)
    {
        var services = new ServiceCollection();
        var builder = services.AddChatCommands();
        configure(builder);
        return services.BuildServiceProvider().GetRequiredService<ChatCommandDispatcher>();
    }

    private static ChatMessage Message(string login, string text) =>
        new(
            login,
            "channel",
            text,
            $":{login}!u@h PRIVMSG #channel :{text}",
            new Dictionary<string, string>()
        );

    private static CommandResponder RecordResponses(List<CommandResponse> responses) =>
        (response, _) =>
        {
            responses.Add(response);
            return ValueTask.CompletedTask;
        };
}
