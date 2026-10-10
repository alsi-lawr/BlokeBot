using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.Guessing.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public class DashboardGuessingRepliesTests
{
    [Test]
    public async Task SuccessReply_PreservesNativeMetadata_AdmissionFailureIsSeparateAndNeverRetries()
    {
        var queue = new CapturingQueue();
        var replies = new DashboardGuessingReplies(
            queue,
            NullLogger<DashboardGuessingReplies>.Instance
        );
        var pin = new PublicChatPinIntent(7, 44, "guessing", "start", 60, true);
        var native = new GuessingOperationOutcome.Succeeded(
            "Native configured reply",
            CommandResponseTarget.Chat,
            pin
        );
        var warning = await replies.QueueAsync("selected-host", native);
        _ = warning.ShouldNotBeNull();
        queue.Messages.ShouldBe([("selected-host", native.Message, pin)]);
        _ = native.ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        queue.Throw = true;
        _ = (await replies.QueueAsync("selected-host", native)).ShouldNotBeNull();
        queue.Messages.Count.ShouldBe(2);
        (
            await replies.QueueAsync(
                "selected-host",
                new GuessingOperationOutcome.Rejected("Native rejection")
            )
        ).ShouldBeNull();
        queue.Messages.Count.ShouldBe(2);
        _ = (
            await replies.QueueAsync(
                "selected-host",
                native with
                {
                    Target = CommandResponseTarget.Whisper,
                }
            )
        ).ShouldNotBeNull();
        queue.Messages.Count.ShouldBe(2);
    }

    private sealed class CapturingQueue : IPublicChatMessageSender
    {
        internal bool Throw { get; set; }
        internal List<(
            string Channel,
            string Message,
            PublicChatPinIntent? Pin
        )> Messages { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken ct
        ) => SendAsync(channel, message, deadline, null!, ct);

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            PublicChatPinIntent pin,
            CancellationToken ct
        )
        {
            _ = deadline.ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
            ct.ShouldBe(CancellationToken.None);
            Messages.Add((channel, message, pin));
            return Throw
                ? ValueTask.FromException<PublicChatSendOutcome>(
                    new IOException("Finite queue failure")
                )
                : ValueTask.FromResult<PublicChatSendOutcome>(new PublicChatSendOutcome.Rejected());
        }
    }
}
