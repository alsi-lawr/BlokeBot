using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class OutboundMessageQueueTests
{
    [Test]
    public void Split_prefers_line_sentence_then_word_breaks()
    {
        TwitchChatMessageSplitter
            .Split("first line\nsecond line", 12)
            .ShouldBe(["first line", "second line"]);

        TwitchChatMessageSplitter
            .Split("First sentence. Second one.", 20)
            .ShouldBe(["First sentence.", "Second one."]);

        TwitchChatMessageSplitter.Split("alpha beta gamma", 10).ShouldBe(["alpha", "beta gamma"]);
    }

    [Test]
    public async Task Long_messages_are_split_and_sent_in_order()
    {
        var queue = new TwitchOutboundMessageQueue(
            Options.Create(
                new TwitchBotOptions
                {
                    ChatMessageSendIntervalSeconds = 0,
                    DuplicateChatMessageCooldownSeconds = 0,
                    MaxChatMessageLength = 10,
                }
            ),
            NullLogger<TwitchOutboundMessageQueue>.Instance
        );
        List<string> sent = [];

        await queue.SendAsync(
            "channel",
            "alpha beta gamma",
            (message, _) =>
            {
                sent.Add(message.Message);
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        sent.ShouldBe(["alpha", "beta gamma"]);
    }

    [Test]
    public async Task Duplicate_messages_wait_without_blocking_different_messages()
    {
        var queue = new TwitchOutboundMessageQueue(
            Options.Create(
                new TwitchBotOptions
                {
                    ChatMessageSendIntervalSeconds = 0,
                    DuplicateChatMessageCooldownSeconds = 1,
                }
            ),
            NullLogger<TwitchOutboundMessageQueue>.Instance
        );
        List<string> sent = [];

        await queue.SendAsync("channel", "same", SendAsync, CancellationToken.None);
        var duplicate = queue.SendAsync("channel", "same", SendAsync, CancellationToken.None);
        var different = queue.SendAsync("channel", "different", SendAsync, CancellationToken.None);

        var completed = await Task.WhenAny(different, Task.Delay(TimeSpan.FromMilliseconds(250)));
        completed.ShouldBe(different);
        duplicate.IsCompleted.ShouldBeFalse();

        await duplicate;
        sent.ShouldBe(["same", "different", "same"]);
        return;

        Task SendAsync(TwitchOutboundChatMessage message, CancellationToken _)
        {
            sent.Add(message.Message);
            return Task.CompletedTask;
        }
    }
}
