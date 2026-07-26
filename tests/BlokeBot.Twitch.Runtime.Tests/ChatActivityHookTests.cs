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
        var session = new IrcConnectionSession(
            BotSettings.FromOptions(new BotOptions()),
            null!,
            null!,
            dispatcher,
            null!,
            null!,
            null!,
            new RecordingCommandResponseSender(recorder),
            new BotRuntimeStatusStore(),
            [new ThrowingChatMessageObserver(), new RecordingChatMessageObserver(recorder)],
            RuntimeTestObserverFanOut.Continue<
                IrcMessageObserverBoundary,
                ChatMessage,
                ChatObserverDeadLetter
            >(BotObserverBoundaries.IrcMessages),
            NullLogger<IrcConnectionSession>.Instance
        );

        await session.DispatchChatMessageAsync(
            new ChatMessage(
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
        var session = new EventSubConnectionSession(
            null!,
            null!,
            dispatcher,
            new RecordingCommandResponseSender(recorder),
            new BotRuntimeStatusStore(),
            [new ThrowingChatMessageObserver(), new RecordingChatMessageObserver(recorder)],
            RuntimeTestObserverFanOut.Continue<
                EventSubMessageObserverBoundary,
                ChatMessage,
                ChatObserverDeadLetter
            >(BotObserverBoundaries.EventSubMessages),
            NullLogger<EventSubConnectionSession>.Instance
        ,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        await session.DispatchChatMessageAsync(
            new EventSubChatMessageEvent
            {
                BroadcasterUserLogin = "streamer",
                ChatterUserLogin = "viewer",
                Message = new EventSubChatMessage { Text = "!ping" },
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
        var logger = new RecordingLogger<IrcConnectionSession>();
        var session = new IrcConnectionSession(
            BotSettings.FromOptions(new BotOptions()),
            null!,
            null!,
            BuildDispatcher(recorder),
            null!,
            null!,
            null!,
            new RecordingCommandResponseSender(recorder),
            new BotRuntimeStatusStore(),
            [],
            RuntimeTestObserverFanOut.Continue<
                IrcMessageObserverBoundary,
                ChatMessage,
                ChatObserverDeadLetter
            >(BotObserverBoundaries.IrcMessages),
            logger
        );

        await session.DispatchChatMessageAsync(
            new ChatMessage(
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

    internal static ChatCommandDispatcher BuildDispatcher(RuntimeHookRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddChatCommands().AddCommandModule<RecordingCommandModule>();
        return services.BuildServiceProvider().GetRequiredService<ChatCommandDispatcher>();
    }

    private sealed class RecordingCommandModule(RuntimeHookRecorder recorder) : IChatCommandModule
    {
        public void AddCommands(IChatCommandBuilder commands)
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
        : IChatMessageObserver
    {
        public ValueTask MessageReceivedAsync(
            ChatMessage message,
            CancellationToken cancellationToken
        )
        {
            recorder.Events.Add("activity");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingChatMessageObserver : IChatMessageObserver
    {
        public ValueTask MessageReceivedAsync(
            ChatMessage message,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromException(new InvalidOperationException("Observer failed."));
        }
    }

    private sealed class RecordingCommandResponseSender(RuntimeHookRecorder recorder)
        : ICommandResponseSender
    {
        public ValueTask SendAsync(
            ChatMessage sourceMessage,
            CommandResponse response,
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

    private sealed record LogEntry(string Message, IReadOnlyDictionary<string, object?> Properties);

    private sealed class Scope : IDisposable
    {
        internal static Scope Instance { get; } = new();

        public void Dispose() { }
    }
}
