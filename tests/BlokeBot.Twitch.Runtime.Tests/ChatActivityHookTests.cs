using BlokeBot.Commands;
using BlokeBot.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            [new ThrowingChatMessageObserver(), new RecordingChatMessageObserver(recorder)],
            RuntimeTestObserverFanOut.Continue<
                TwitchIrcMessageObserverBoundary,
                TwitchChatMessage,
                TwitchChatObserverDeadLetter
            >(TwitchBotObserverBoundaries.IrcMessages),
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
            [new ThrowingChatMessageObserver(), new RecordingChatMessageObserver(recorder)],
            RuntimeTestObserverFanOut.Continue<
                TwitchEventSubMessageObserverBoundary,
                TwitchChatMessage,
                TwitchChatObserverDeadLetter
            >(TwitchBotObserverBoundaries.EventSubMessages),
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

    [Test]
    public async Task IrcCommandResponse_Logging_RedactsPrivateMessageContent()
    {
        const string PrivateCommand = "!ping";
        var recorder = new RuntimeHookRecorder();
        var logger = new RecordingLogger<TwitchIrcConnectionSession>();
        var session = new TwitchIrcConnectionSession(
            TwitchBotSettings.FromOptions(new TwitchBotOptions()),
            null!,
            null!,
            BuildDispatcher(recorder),
            null!,
            null!,
            new RecordingCommandResponseSender(recorder),
            new TwitchBotRuntimeStatusStore(),
            [],
            RuntimeTestObserverFanOut.Continue<
                TwitchIrcMessageObserverBoundary,
                TwitchChatMessage,
                TwitchChatObserverDeadLetter
            >(TwitchBotObserverBoundaries.IrcMessages),
            logger
        );

        await session.DispatchChatMessageAsync(
            new TwitchChatMessage(
                "viewer",
                "streamer",
                PrivateCommand,
                ":viewer!u@h PRIVMSG #streamer :!ping",
                new Dictionary<string, string>()
            ),
            CancellationToken.None
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Message.ShouldNotContain(PrivateCommand);
        entry.Message.ShouldNotContain("pong");
        entry.Properties["Channel"].ShouldBe("streamer");
        entry.Properties.ShouldNotContainKey("Reply");
        entry.Properties.ShouldNotContainKey("Text");
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

    private sealed class ThrowingChatMessageObserver : ITwitchChatMessageObserver
    {
        public ValueTask MessageReceivedAsync(
            TwitchChatMessage message,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromException(new InvalidOperationException("Observer failed."));
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

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return Scope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(
        string Message,
        IReadOnlyDictionary<string, object?> Properties
    );

    private sealed class Scope : IDisposable
    {
        internal static Scope Instance { get; } = new();

        public void Dispose() { }
    }
}
