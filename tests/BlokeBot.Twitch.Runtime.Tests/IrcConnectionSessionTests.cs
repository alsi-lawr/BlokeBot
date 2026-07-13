using System.Text;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class IrcConnectionSessionTests
{
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
            chat,
            null!,
            status,
            [],
            null!,
            logger
        );
        var joinedChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        joinedChannels.ShouldBe(["channel"]);
        lifecycle.StartedChannels.ShouldBe(["channel"]);
        lifecycle.StoppedChannels.ShouldBeEmpty();
        status.Current.IsConnected.ShouldBeTrue();
        status.Current.ConnectedChannels.ShouldBe(["channel"]);
        chat.Messages.ShouldBe([new SentMessage("channel", PrivateStartupMessage)]);
        chat.Deadlines.ShouldHaveSingleItem()
            .ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
        var warning = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        warning.Exception.ShouldBeNull();
        warning.Message.ShouldContain("rejected");
        warning.Message.ShouldNotContain(PrivateStartupMessage);
        warning.Properties["Channel"].ShouldBe("channel");
        logger.Entries.ShouldNotContain(entry => entry.Message.Contains("accepted"));
    }

    private sealed class StaticChannelProvider(IReadOnlyList<string> channels) : IBotChannelProvider
    {
        public ValueTask<IReadOnlyList<string>> GetChannelsAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(channels);
        }
    }

    private sealed class RecordingLifecycleNotifier : IBotChannelLifecycleNotifier
    {
        internal List<string> StartedChannels { get; } = [];

        internal List<string> StoppedChannels { get; } = [];

        public Task ChannelStartedAsync(string channel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartedChannels.Add(channel);
            return Task.CompletedTask;
        }

        public Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoppedChannels.Add(channel);
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

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
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
