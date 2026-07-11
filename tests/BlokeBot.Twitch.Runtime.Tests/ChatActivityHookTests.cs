using BlokeBot.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class ChatActivityHookTests
{
    [Test]
    public async Task IrcChatMessage_Dispatching_RunsActivityBeforeCommandAndResponse()
    {
        var recorder = new RuntimeHookRecorder();
        var dispatcher = BuildDispatcher(recorder);
        var session = new TwitchIrcConnectionSession(
            TwitchBotSettings.FromOptions(new TwitchBotOptions()),
            null!,
            null!,
            dispatcher,
            null!,
            null!,
            new RecordingCommandResponseSender(recorder),
            new TwitchBotRuntimeStatusStore(),
            [new RecordingChatMessageObserver(recorder)],
            NullLogger<TwitchIrcConnectionSession>.Instance
        );

        await session.DispatchChatMessageAsync(
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
    public async Task EventSubChatMessage_Dispatching_RunsActivityBeforeCommandAndResponse()
    {
        var recorder = new RuntimeHookRecorder();
        var dispatcher = BuildDispatcher(recorder);
        var session = new TwitchEventSubConnectionSession(
            null!,
            null!,
            dispatcher,
            new RecordingCommandResponseSender(recorder),
            new TwitchBotRuntimeStatusStore(),
            [new RecordingChatMessageObserver(recorder)],
            NullLogger<TwitchEventSubConnectionSession>.Instance
        );

        await session.DispatchChatMessageAsync(
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

    internal static TwitchCommandDispatcher BuildDispatcher(RuntimeHookRecorder recorder)
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

    internal sealed class RuntimeHookRecorder
    {
        public List<string> Events { get; } = [];
    }
}
