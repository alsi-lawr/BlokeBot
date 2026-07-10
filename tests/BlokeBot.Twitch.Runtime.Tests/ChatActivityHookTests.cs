using BlokeBot.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class ChatActivityHookTests
{
    [Test]
    public async Task Irc_chat_activity_runs_before_command_dispatch()
    {
        var recorder = new RuntimeHookRecorder();
        var dispatcher = BuildDispatcher(recorder);
        var runtime = new TwitchIrcRuntime(
            Options.Create(new TwitchBotOptions()),
            null!,
            null!,
            dispatcher,
            null!,
            null!,
            new RecordingCommandResponseSender(recorder),
            new TwitchBotRuntimeStatusStore(),
            [new RecordingChatMessageObserver(recorder)],
            NullLogger<TwitchIrcRuntime>.Instance
        );

        await runtime.DispatchChatMessageAsync(
            new TwitchChatMessage(
                "viewer",
                "streamer",
                "!ping",
                ":viewer!u@h PRIVMSG #streamer :!ping",
                new Dictionary<string, string>()
            ),
            CancellationToken.None
        );

        recorder.Events.ShouldBe(["activity", "dispatch", "response"]);
    }

    [Test]
    public async Task EventSub_chat_activity_runs_before_command_dispatch()
    {
        var recorder = new RuntimeHookRecorder();
        var dispatcher = BuildDispatcher(recorder);
        var runtime = new TwitchEventSubRuntime(
            Options.Create(new TwitchBotOptions()),
            null!,
            null!,
            dispatcher,
            null!,
            null!,
            new RecordingCommandResponseSender(recorder),
            null!,
            new TwitchBotRuntimeStatusStore(),
            [new RecordingChatMessageObserver(recorder)],
            NullLogger<TwitchEventSubRuntime>.Instance
        );

        await runtime.DispatchChatMessageAsync(
            new TwitchEventSubChatMessageEvent
            {
                BroadcasterUserLogin = "streamer",
                ChatterUserLogin = "viewer",
                Message = new TwitchEventSubChatMessage { Text = "!ping" },
            },
            "{}",
            CancellationToken.None
        );

        recorder.Events.ShouldBe(["activity", "dispatch", "response"]);
    }

    private static TwitchCommandDispatcher BuildDispatcher(RuntimeHookRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddTwitchCommands().AddCommandModule<RecordingCommandModule>();
        return services.BuildServiceProvider().GetRequiredService<TwitchCommandDispatcher>();
    }

    private sealed class RecordingCommandModule(RuntimeHookRecorder recorder) : ITwitchCommandModule
    {
        public void AddCommands(ITwitchCommandBuilder commands)
        {
            commands.Map(
                "ping",
                async (context, _, cancellationToken) =>
                {
                    recorder.Events.Add("dispatch");
                    await context.ReplyAsync("pong", cancellationToken);
                }
            );
        }
    }

    private sealed class RecordingChatMessageObserver(RuntimeHookRecorder recorder)
        : ITwitchChatMessageObserver
    {
        public ValueTask MessageReceivedAsync(
            TwitchChatMessage message,
            CancellationToken cancellationToken
        )
        {
            recorder.Events.Add("activity");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingCommandResponseSender(RuntimeHookRecorder recorder)
        : ITwitchCommandResponseSender
    {
        public ValueTask SendAsync(
            TwitchChatMessage sourceMessage,
            TwitchCommandResponse response,
            CancellationToken cancellationToken
        )
        {
            recorder.Events.Add("response");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RuntimeHookRecorder
    {
        public List<string> Events { get; } = [];
    }
}
