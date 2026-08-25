using System.Text;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class IrcConnectionSessionTests
{
    [Test]
    public async Task ReplacedChannelSession_PartsOldIdentityAndStartsNewIdentity()
    {
        var channels = new StaticChannelProvider(["Channel"]);
        var lifecycle = new RecordingLifecycleNotifier();
        var session = new IrcConnectionSession(
            BotSettings.FromOptions(new BotOptions()),
            channels,
            null!,
            null!,
            lifecycle,
            new StaticStartupMessageProvider(new StartupChatMessage.Disabled()),
            new RejectingChatSender(),
            null!,
            new BotRuntimeStatusStore(),
            [],
            null!,
            new RecordingLogger<IrcConnectionSession>()
        );
        var joinedChannels = new Dictionary<string, BotChannelSessionIdentity>(
            StringComparer.OrdinalIgnoreCase
        );
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true
        )
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };

        await session.SyncJoinedChannelsAsync(writer, joinedChannels, CancellationToken.None);
        var first = lifecycle.StartedTargets.ShouldHaveSingleItem();
        var second = channels.Replace("channel");

        await session.SyncJoinedChannelsAsync(writer, joinedChannels, CancellationToken.None);

        ReferenceEquals(first.SessionIdentity, second.SessionIdentity).ShouldBeFalse();
        ReferenceEquals(
                lifecycle.StoppedTargets.ShouldHaveSingleItem().SessionIdentity,
                first.SessionIdentity
            )
            .ShouldBeTrue();
        ReferenceEquals(lifecycle.StartedTargets[1].SessionIdentity, second.SessionIdentity)
            .ShouldBeTrue();
    }

    [Test]
    public async Task StartupMessageDisabled_FreshSessionsJoinWithoutChatAttempt()
    {
        var channels = new StaticChannelProvider(["Channel"]);
        var lifecycle = new RecordingLifecycleNotifier();
        var chat = new RejectingChatSender();
        var session = new IrcConnectionSession(
            BotSettings.FromOptions(new BotOptions()),
            channels,
            null!,
            null!,
            lifecycle,
            new StaticStartupMessageProvider(new StartupChatMessage.Disabled()),
            chat,
            null!,
            new BotRuntimeStatusStore(),
            [],
            null!,
            new RecordingLogger<IrcConnectionSession>()
        );
        var joinedChannels = new Dictionary<string, BotChannelSessionIdentity>(
            StringComparer.OrdinalIgnoreCase
        );
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true
        )
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };

        await session.SyncJoinedChannelsAsync(writer, joinedChannels, CancellationToken.None);

        var reconnectChannels = new Dictionary<string, BotChannelSessionIdentity>(
            StringComparer.OrdinalIgnoreCase
        );
        await session.SyncJoinedChannelsAsync(writer, reconnectChannels, CancellationToken.None);

        chat.Messages.ShouldBeEmpty();
        joinedChannels.Keys.ShouldBe(["channel"]);
        reconnectChannels.Keys.ShouldBe(["channel"]);
        lifecycle.StartedChannels.ShouldBe(["channel", "channel"]);
    }

    [Test]
    public async Task StartupMessageRejected_SynchronizingChannel_JoinsAndStartsLifecycleOnce()
    {
        const string PrivateStartupMessage = "private startup payload";
        var channels = new StaticChannelProvider(["Channel"]);
        var lifecycle = new RecordingLifecycleNotifier();
        var chat = new RejectingChatSender();
        var status = new BotRuntimeStatusStore();
        var logger = new RecordingLogger<IrcConnectionSession>();
        var session = new IrcConnectionSession(
            BotSettings.FromOptions(new BotOptions { StartupMessage = PrivateStartupMessage }),
            channels,
            null!,
            null!,
            lifecycle,
            new StaticStartupMessageProvider(new StartupChatMessage.Enabled(PrivateStartupMessage)),
            chat,
            null!,
            status,
            [],
            null!,
            logger
        );
        var joinedChannels = new Dictionary<string, BotChannelSessionIdentity>(
            StringComparer.OrdinalIgnoreCase
        );
        await using var stream = new MemoryStream();

        await using (
            var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1024,
                leaveOpen: true
            )
            {
                NewLine = "\r\n",
                AutoFlush = true,
            }
        )
        {
            await session.SyncJoinedChannelsAsync(writer, joinedChannels, CancellationToken.None);
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        (await reader.ReadToEndAsync()).ShouldBe("JOIN #channel\r\n");
        joinedChannels.Keys.ShouldBe(["channel"]);
        lifecycle.StartedChannels.ShouldBe(["channel"]);
        lifecycle.StoppedChannels.ShouldBeEmpty();
        status.Current.ShouldBeOfType<BotRuntimeStatus.Connected>().Channels.ShouldBe(["channel"]);
        chat.Messages.ShouldBe([new SentMessage("channel", PrivateStartupMessage)]);
        _ = chat
            .Deadlines.ShouldHaveSingleItem()
            .ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
        var warning = logger.Entries.Single(static entry => entry.Level == LogLevel.Warning);
        warning.Exception.ShouldBeNull();
        warning.Message.ShouldContain("rejected");
        warning.Message.ShouldNotContain(PrivateStartupMessage);
        warning.Properties["Channel"].ShouldBe("channel");
        logger.Entries.ShouldNotContain(static entry => entry.Message.Contains("accepted"));
    }

    [Test]
    public async Task EnabledStartupMessage_FreshSessionsDeliverOnceAndDuplicateSyncDoesNotResend()
    {
        var channels = new StaticChannelProvider(["Channel"]);
        var lifecycle = new RecordingLifecycleNotifier();
        var chat = new AcceptingChatSender();
        var session = new IrcConnectionSession(
            BotSettings.FromOptions(new BotOptions()),
            channels,
            null!,
            null!,
            lifecycle,
            new StaticStartupMessageProvider(new StartupChatMessage.Enabled("Hello")),
            chat,
            null!,
            new BotRuntimeStatusStore(),
            [],
            null!,
            new RecordingLogger<IrcConnectionSession>()
        );
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true
        )
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };
        var initialSessionChannels = new Dictionary<string, BotChannelSessionIdentity>(
            StringComparer.OrdinalIgnoreCase
        );

        await session.SyncJoinedChannelsAsync(
            writer,
            initialSessionChannels,
            CancellationToken.None
        );
        await session.SyncJoinedChannelsAsync(
            writer,
            initialSessionChannels,
            CancellationToken.None
        );

        chat.Messages.ShouldBe([new SentMessage("channel", "Hello")]);
        lifecycle.StartedChannels.ShouldBe(["channel"]);

        var reconnectSessionChannels = new Dictionary<string, BotChannelSessionIdentity>(
            StringComparer.OrdinalIgnoreCase
        );
        await session.SyncJoinedChannelsAsync(
            writer,
            reconnectSessionChannels,
            CancellationToken.None
        );

        chat.Messages.ShouldBe([
            new SentMessage("channel", "Hello"),
            new SentMessage("channel", "Hello"),
        ]);
        lifecycle.StartedChannels.ShouldBe(["channel", "channel"]);
    }

    private sealed class StaticChannelProvider(IReadOnlyList<string> channels) : IBotChannelProvider
    {
        private IReadOnlyList<BotChannelTarget> _targets =
        [
            .. channels.Select(channel => new BotChannelTarget(
                channel,
                BotChannelSessionIdentity.Create()
            )),
        ];

        internal BotChannelTarget Replace(string channel)
        {
            var target = new BotChannelTarget(channel, BotChannelSessionIdentity.Create());
            _targets = [target];
            return target;
        }

        public ValueTask<IReadOnlyList<BotChannelTarget>> GetChannelsAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_targets);
        }
    }

    private sealed class StaticStartupMessageProvider(StartupChatMessage message)
        : IStartupChatMessageProvider
    {
        public ValueTask<StartupChatMessage> GetAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(message);
        }
    }

    private sealed class RecordingLifecycleNotifier : IBotChannelLifecycleNotifier
    {
        private readonly List<BotChannelTarget> _started = [];

        private readonly List<BotChannelTarget> _stopped = [];

        internal IReadOnlyList<string> StartedChannels =>
            _started.Select(static target => target.Channel).ToArray();

        internal IReadOnlyList<string> StoppedChannels =>
            _stopped.Select(static target => target.Channel).ToArray();

        internal IReadOnlyList<BotChannelTarget> StartedTargets => _started;

        internal IReadOnlyList<BotChannelTarget> StoppedTargets => _stopped;

        public Task ChannelStartedAsync(
            BotChannelTarget target,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            _started.Add(target);
            return Task.CompletedTask;
        }

        public Task ChannelStoppedAsync(
            BotChannelTarget target,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            _stopped.Add(target);
            return Task.CompletedTask;
        }
    }

    private sealed class RejectingChatSender : IPublicChatMessageSender
    {
        internal List<SentMessage> Messages { get; } = [];

        internal List<PublicChatDeliveryDeadline> Deadlines { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(new SentMessage(channel, message));
            Deadlines.Add(deadline);
            return ValueTask.FromResult<PublicChatSendOutcome>(
                new PublicChatSendOutcome.Rejected()
            );
        }
    }

    private sealed class AcceptingChatSender : IPublicChatMessageSender
    {
        internal List<SentMessage> Messages { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(new SentMessage(channel, message));
            return ValueTask.FromResult<PublicChatSendOutcome>(
                new PublicChatSendOutcome.Accepted()
            );
        }
    }

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(static pair => pair.Key, static pair => pair.Value)
                : [];
            Entries.Add(new(logLevel, formatter(state, exception), exception, properties));
        }
    }

    private sealed record SentMessage(string Channel, string Message);

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties
    );
}
