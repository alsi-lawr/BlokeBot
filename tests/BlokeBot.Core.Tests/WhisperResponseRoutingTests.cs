using System.Net;
using System.Text;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Core.Identity;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class WhisperResponseRoutingTests : WhisperResponseTestBase
{
    [Test]
    public async Task HandlerFailure_HandlingPrivateFailure_EscalatesWithoutDeliveryOrRecursion()
    {
        var handlerFailure = new SensitiveWhisperException("telemetry infrastructure failed");
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.BadRequest,
            handlerException: handlerFailure
        );

        var action = async () =>
            await harness.Sender.SendAsync(
                harness.Source(),
                CommandResponse.Whisper("sensitive private response"),
                CancellationToken.None
            );

        var escalation = await action.ShouldThrowAsync<PrivateDeliveryFailureHandlingException>();
        escalation.InnerException.ShouldBeSameAs(handlerFailure);
        escalation.DeliveryError.ShouldBeOfType<PrivateDeliveryError.Rejected>();
        escalation.Context.HostChannel.ShouldBe("streamer");
        harness.FailureHandler.Failures.Count.ShouldBe(1);
        harness.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task SuccessfulPrivateResponse_SendingCommandResponse_DoesNotUsePublicDelivery()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);

        await harness.Sender.SendAsync(
            harness.Source(),
            CommandResponse.Whisper("private response"),
            CancellationToken.None
        );

        harness.Http.WhisperRequestCount.ShouldBe(1);
        harness.FailureHandler.Failures.ShouldBeEmpty();
        harness.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task CallerCancellationDuringFailureHandling_PropagatesWithoutEscalation()
    {
        using var cancellation = new CancellationTokenSource();
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.BadRequest,
            cancelOnHandling: cancellation
        );

        var action = async () =>
            await harness.Sender.SendAsync(
                harness.Source(),
                CommandResponse.Whisper("private response"),
                cancellation.Token
            );

        await action.ShouldThrowAsync<OperationCanceledException>();
        harness.FailureHandler.Failures.Count.ShouldBe(1);
        harness.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task PublicTarget_SendingCommandResponse_UsesExistingPublicDeliveryPath()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);

        await harness.Sender.SendAsync(
            harness.Source(),
            CommandResponse.Chat("public response"),
            CancellationToken.None
        );

        harness.Chat.Messages.ShouldBe([new SentChatMessage("streamer", "public response")]);
        harness.FailureHandler.Failures.ShouldBeEmpty();
        harness.Http.ValidationRequestCount.ShouldBe(0);
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task RejectedPublicTarget_SendingCommandResponse_ReportsRedactedNoDelivery()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            publicChatOutcome: new PublicChatSendOutcome.Rejected()
        );

        await harness.Sender.SendAsync(
            harness.Source(),
            CommandResponse.Chat("private public response"),
            CancellationToken.None
        );

        harness.Chat.Messages.ShouldBe([
            new SentChatMessage("streamer", "private public response"),
        ]);
        var entry = harness.PublicChatLogger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain("rejected");
        entry.Message.ShouldNotContain("private public response");
        entry.Properties["Channel"].ShouldBe("streamer");
        harness.FailureHandler.Failures.ShouldBeEmpty();
        harness.Http.ValidationRequestCount.ShouldBe(0);
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task PrivateFailureTelemetry_Handling_RecordsOnlyRedactedContext()
    {
        var logger = new RecordingLogger<PrivateDeliveryFailureTelemetryHandler>();
        var handler = new PrivateDeliveryFailureTelemetryHandler(logger);
        var error = new PrivateDeliveryError.Unexpected(
            new SensitiveWhisperException("sensitive exception message")
        );
        var context = new PrivateDeliveryFailureContext { HostChannel = "streamer" };

        await handler.HandleAsync(error, context, CancellationToken.None);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain("Private command response delivery");
        entry.Message.ShouldContain("streamer");
        entry.Message.ShouldContain(nameof(PrivateDeliveryError.Unexpected));
        entry.Message.ShouldNotContain("sensitive exception message");
        entry.Message.ShouldNotContain("access-token");
        entry.Message.ShouldNotContain("private response");
        entry.Message.ShouldNotContain("viewer");
        entry.Properties["HostChannel"].ShouldBe("streamer");
        entry.Properties["Classification"].ShouldBe(nameof(PrivateDeliveryError.Unexpected));
    }
}
