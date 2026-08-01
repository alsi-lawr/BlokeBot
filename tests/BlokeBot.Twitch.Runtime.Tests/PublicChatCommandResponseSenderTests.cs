using BlokeBot.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class PublicChatCommandResponseSenderTests
{
    [Test]
    public async Task ChatResponse_SendingStandalone_DeliversToSourceChannel()
    {
        var chat = new RecordingChatSender();
        var sender = new PublicChatCommandResponseSender(
            chat,
            NullLogger<PublicChatCommandResponseSender>.Instance
        );

        await sender.SendAsync(
            SourceMessage(),
            CommandResponse.Chat("public response"),
            CancellationToken.None
        );

        chat.Channels.ShouldBe(["streamer"]);
        chat.Messages.ShouldBe(["public response"]);
        chat.Deadlines.ShouldHaveSingleItem()
            .ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
    }

    [Test]
    public async Task WhisperResponse_SendingStandalone_DoesNotUsePublicDelivery()
    {
        var chat = new RecordingChatSender();
        var sender = new PublicChatCommandResponseSender(
            chat,
            NullLogger<PublicChatCommandResponseSender>.Instance
        );

        await sender.SendAsync(
            SourceMessage(),
            CommandResponse.Whisper("private response"),
            CancellationToken.None
        );

        chat.Channels.ShouldBeEmpty();
        chat.Messages.ShouldBeEmpty();
        chat.Deadlines.ShouldBeEmpty();
    }

    [Test]
    public async Task RejectedChatResponse_SendingStandalone_ReportsRedactedNoDelivery()
    {
        var chat = new RecordingChatSender(new PublicChatSendOutcome.Rejected());
        var logger = new RecordingLogger<PublicChatCommandResponseSender>();
        var sender = new PublicChatCommandResponseSender(chat, logger);

        await sender.SendAsync(
            SourceMessage(),
            CommandResponse.Chat("private response payload"),
            CancellationToken.None
        );

        chat.Channels.ShouldBe(["streamer"]);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain("rejected");
        entry.Message.ShouldNotContain("private response payload");
        entry.Properties["HostChannel"].ShouldBe("streamer");
    }

    private static ChatMessage SourceMessage() =>
        new(
            "viewer",
            "streamer",
            "!points",
            ":viewer!u@h PRIVMSG #streamer :!points",
            new Dictionary<string, string>()
        );

    private sealed class RecordingChatSender(PublicChatSendOutcome? outcome = null)
        : IPublicChatMessageSender
    {
        public List<string> Channels { get; } = [];

        public List<string> Messages { get; } = [];

        public List<PublicChatDeliveryDeadline> Deadlines { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Channels.Add(channel);
            Messages.Add(message);
            Deadlines.Add(deadline);
            return ValueTask.FromResult<PublicChatSendOutcome>(
                outcome ?? new PublicChatSendOutcome.Accepted()
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
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new(logLevel, formatter(state, exception), exception, properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties
    );
}
