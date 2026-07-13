using BlokeBot.Commands;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchCommandResponseSenderSelectionTests
{
    [Test]
    public void StandalonePublicChat_Selected_ProducesPublicChatPolicy()
    {
        var policy = new TwitchCommandResponseSenderSelection()
            .UseStandalonePublicChat()
            .RequireSingle();

        policy.Kind.ShouldBe(TwitchCommandResponseSenderKind.StandalonePublicChat);
        policy.SenderType.ShouldBe(typeof(TwitchChatCommandResponseSender));
    }

    [Test]
    public void HostedWhisperSender_Selected_ProducesHostedWhisperPolicy()
    {
        var policy = new TwitchCommandResponseSenderSelection()
            .UseHostedWhisperSender<HostedWhisperSender>()
            .RequireSingle();

        policy.Kind.ShouldBe(TwitchCommandResponseSenderKind.HostedWhisper);
        policy.SenderType.ShouldBe(typeof(HostedWhisperSender));
    }

    [Test]
    public void NoResponseSender_Selected_RejectsMissingPolicy()
    {
        var selection = new TwitchCommandResponseSenderSelection();

        var exception = Should.Throw<InvalidOperationException>(selection.RequireSingle);

        exception.Message.ShouldContain("none was selected");
    }

    [Test]
    public void ConflictingResponseSenders_SelectedInEitherOrder_RejectsPolicy()
    {
        var chatThenWhisper = new TwitchCommandResponseSenderSelection()
            .UseStandalonePublicChat()
            .UseHostedWhisperSender<HostedWhisperSender>();
        var whisperThenChat = new TwitchCommandResponseSenderSelection()
            .UseHostedWhisperSender<HostedWhisperSender>()
            .UseStandalonePublicChat();

        var firstException = Should.Throw<InvalidOperationException>(chatThenWhisper.RequireSingle);
        var secondException = Should.Throw<InvalidOperationException>(
            whisperThenChat.RequireSingle
        );

        firstException.Message.ShouldContain("2 were selected");
        secondException.Message.ShouldContain("2 were selected");
    }

    private sealed class HostedWhisperSender : ITwitchCommandResponseSender
    {
        public ValueTask SendAsync(
            TwitchChatMessage sourceMessage,
            TwitchCommandResponse response,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.CompletedTask;
        }
    }
}
