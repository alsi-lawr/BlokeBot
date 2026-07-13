using BlokeBot.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchChatCommandResponseSenderTests
{
    [Test]
    public async Task ChatResponse_SendingStandalone_DeliversToSourceChannel()
    {
        var chat = new RecordingChatSender();
        var sender = new TwitchChatCommandResponseSender(
            chat,
            NullLogger<TwitchChatCommandResponseSender>.Instance
        );

        await sender.SendAsync(
            SourceMessage(),
            TwitchCommandResponse.Chat("public response"),
            CancellationToken.None
        );

        chat.Channels.ShouldBe(["streamer"]);
        chat.Messages.ShouldBe(["public response"]);
        chat.Deadlines.ShouldHaveSingleItem()
            .ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
    }

    [Test]
    public async Task WhisperResponse_SendingStandalone_FallsBackToSourceChannel()
    {
        var chat = new RecordingChatSender();
        var sender = new TwitchChatCommandResponseSender(
            chat,
            NullLogger<TwitchChatCommandResponseSender>.Instance
        );

        await sender.SendAsync(
            SourceMessage(),
            TwitchCommandResponse.Whisper("private response"),
            CancellationToken.None
        );

        chat.Channels.ShouldBe(["streamer"]);
        chat.Messages.ShouldBe(["private response"]);
        chat.Deadlines.ShouldHaveSingleItem()
            .ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
    }

    private static TwitchChatMessage SourceMessage()
    {
        return new(
            "viewer",
            "streamer",
            "!points",
            ":viewer!u@h PRIVMSG #streamer :!points",
            new Dictionary<string, string>()
        );
    }

    private sealed class RecordingChatSender : ITwitchChatMessageSender
    {
        public List<string> Channels { get; } = [];

        public List<string> Messages { get; } = [];

        public List<PublicChatDeliveryDeadline> Deadlines { get; } = [];

        public Task SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Channels.Add(channel);
            Messages.Add(message);
            Deadlines.Add(deadline);
            return Task.CompletedTask;
        }
    }
}
