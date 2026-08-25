using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class PublicChatDeliveryClassifierTests
{
    [Test]
    public void CallerCancellation_ClassifyingPreparation_PropagatesCancellation()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var cancellation = new OperationCanceledException(caller.Token);

        var thrown = Should.Throw<OperationCanceledException>(() =>
            PublicChatDeliveryClassifier.ClassifyPreparationFailure(cancellation, caller.Token)
        );

        thrown.ShouldBeSameAs(cancellation);
    }

    [Test]
    public void TransportSendResults_Classifying_MapSentAndBothRejectionShapes()
    {
        _ = PublicChatDeliveryClassifier
            .ClassifySendResult(
                new ChatMessageSendResult { IsSent = true, MessageId = "provider-message-id" }
            )
            .ShouldBeOfType<PublicChatTransportSendResult.Sent>();

        var coded = PublicChatDeliveryClassifier
            .ClassifySendResult(
                new ChatMessageSendResult
                {
                    IsSent = false,
                    MessageId = string.Empty,
                    DropReason = new ChatMessageDropReason
                    {
                        Code = "followers_only",
                        Message = "provider secret response",
                    },
                }
            )
            .ShouldBeOfType<PublicChatTransportSendResult.Rejected>();
        coded.Reason.ShouldBe(
            new PublicChatRejectionReason.ProviderCode(
                new PublicChatProviderRejectionCode("followers_only")
            )
        );
        coded.ToString().ShouldNotContain("provider secret response");

        var unspecified = PublicChatDeliveryClassifier
            .ClassifySendResult(
                new ChatMessageSendResult
                {
                    IsSent = false,
                    MessageId = string.Empty,
                    DropReason = new ChatMessageDropReason
                    {
                        Code = "   ",
                        Message = "provider secret response",
                    },
                }
            )
            .ShouldBeOfType<PublicChatTransportSendResult.Rejected>();
        _ = unspecified.Reason.ShouldBeOfType<PublicChatRejectionReason.Unspecified>();
    }

    [Test]
    public void CallerCancellation_ClassifyingPostBoundary_PropagatesCancellation()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var cancellation = new OperationCanceledException(caller.Token);

        var thrown = Should.Throw<OperationCanceledException>(() =>
            PublicChatDeliveryClassifier.ClassifyPostBoundaryFailure(cancellation, caller.Token)
        );

        thrown.ShouldBeSameAs(cancellation);
    }

    [Test]
    public void SensitivePreparationValues_RenderingRecords_AreRedacted()
    {
        var exception = new InvalidOperationException("provider secret response");
        var unexpected = PublicChatDeliveryClassifier
            .ClassifyPreparationFailure(exception, CancellationToken.None)
            .ShouldBeOfType<PublicChatPreparationOutcome.Unexpected>();
        var delivery = PublicChatDeliveryClassifier
            .MapPreparationFailure(unexpected)
            .ShouldBeOfType<PublicChatDeliveryOutcome.Unexpected>();
        var prepared = Prepared("secret app token", "secret chat payload");

        delivery.Cause.ShouldBeSameAs(exception);
        unexpected.ToString().ShouldNotContain("provider secret response");
        delivery.ToString().ShouldNotContain("provider secret response");
        prepared.ToString().ShouldNotContain("secret app token");
        prepared.ToString().ShouldNotContain("secret chat payload");
    }

    private static PublicChatPreparedSend Prepared(string accessToken, string message) =>
        new()
        {
            Message = new PublicChatClaimedMessage
            {
                Id = 42,
                Channel = "streamer",
                Message = message,
                EnqueuedAt = DateTimeOffset.UnixEpoch,
                ExpiresAt = DateTimeOffset.UnixEpoch.AddSeconds(30),
                Attempt = 1,
                ClaimToken = new PublicChatClaimToken(Guid.NewGuid()),
                ClaimExpiresAt = DateTimeOffset.UnixEpoch.AddMinutes(5),
                DeduplicationKey = new PublicChatDeduplicationKey("key"),
            },
            AppAccessToken = accessToken,
            BroadcasterId = "broadcaster-id",
            BotUserId = "bot-user-id",
        };
}
