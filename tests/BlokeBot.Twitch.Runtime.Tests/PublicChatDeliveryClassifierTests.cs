using System.Net;
using System.Net.Sockets;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Polly.Timeout;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class PublicChatDeliveryClassifierTests
{
    [Test]
    public void TransientPreparationFailures_Classifying_AreSafePreSendOnly()
    {
        Exception[] failures =
        [
            new HttpRequestException("connection failed"),
            new HttpRequestException(
                "request timed out",
                null,
                HttpStatusCode.RequestTimeout
            ),
            new HttpRequestException(
                "rate limited",
                null,
                HttpStatusCode.TooManyRequests
            ),
            new HttpRequestException(
                "provider unavailable",
                null,
                HttpStatusCode.ServiceUnavailable
            ),
            new SocketException((int)SocketError.ConnectionReset),
            new IOException("connection ended"),
            new TimeoutException("preparation timed out"),
            new TimeoutRejectedException("preparation timed out"),
            new OperationCanceledException("non-caller timeout"),
        ];

        foreach (var failure in failures)
        {
            var outcome = PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                failure,
                CancellationToken.None
            );

            var transient = outcome.ShouldBeOfType<
                PublicChatPreparationOutcome.SafePreSendTransient
            >();
            transient.Diagnostic.FailureType.ShouldBe(
                PublicChatFailureType.From(failure)
            );
            transient.Diagnostic.ShouldBeOfType<
                PublicChatFailureDiagnostic.Preparation
            >();
        }
    }

    [Test]
    public void TerminalPreparationFailures_Classifying_AreUnexpectedWithExactCause()
    {
        Exception[] failures =
        [
            new HttpRequestException(
                "bad request",
                null,
                HttpStatusCode.BadRequest
            ),
            new TwitchAppAccessTokenResponseException(),
            new TwitchAccessTokenUnavailableException(
                TwitchAccessTokenUnavailableReason.MissingRefreshToken,
                "credential detail"
            ),
            new InvalidOperationException("invalid identity response"),
        ];

        foreach (var failure in failures)
        {
            var unexpected = PublicChatDeliveryClassifier
                .ClassifyPreparationFailure(failure, CancellationToken.None)
                .ShouldBeOfType<PublicChatPreparationOutcome.Unexpected>();

            unexpected.Cause.ShouldBeSameAs(failure);
            unexpected.Diagnostic.FailureType.ShouldBe(
                PublicChatFailureType.From(failure)
            );
            unexpected.Diagnostic.ShouldBeOfType<
                PublicChatFailureDiagnostic.Preparation
            >();
        }
    }

    [Test]
    public void CallerCancellation_ClassifyingPreparation_PropagatesCancellation()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var cancellation = new OperationCanceledException(caller.Token);

        var thrown = Should.Throw<OperationCanceledException>(() =>
            PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                cancellation,
                caller.Token
            )
        );

        thrown.ShouldBeSameAs(cancellation);
    }

    [Test]
    public void TwitchSendResults_Classifying_MapSentAndBothRejectionShapes()
    {
        PublicChatDeliveryClassifier
            .ClassifySendResult(
                new TwitchChatMessageSendResult
                {
                    IsSent = true,
                    MessageId = "provider-message-id",
                }
            )
            .ShouldBeOfType<PublicChatTransportSendResult.Sent>();

        var coded = PublicChatDeliveryClassifier
            .ClassifySendResult(
                new TwitchChatMessageSendResult
                {
                    IsSent = false,
                    DropReason = new TwitchChatMessageDropReason
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
                new TwitchChatMessageSendResult
                {
                    IsSent = false,
                    DropReason = new TwitchChatMessageDropReason
                    {
                        Code = "   ",
                        Message = "provider secret response",
                    },
                }
            )
            .ShouldBeOfType<PublicChatTransportSendResult.Rejected>();
        unspecified.Reason.ShouldBeOfType<PublicChatRejectionReason.Unspecified>();
    }

    [Test]
    public void PostBoundaryFailures_Classifying_AreAlwaysAmbiguous()
    {
        Exception[] failures =
        [
            new HttpRequestException(
                "rejected after post",
                null,
                HttpStatusCode.BadRequest
            ),
            new HttpRequestException(
                "provider unavailable after post",
                null,
                HttpStatusCode.ServiceUnavailable
            ),
            new SocketException((int)SocketError.ConnectionReset),
            new IOException("response ended"),
            new TimeoutException("response timed out"),
            new TimeoutRejectedException("response timed out"),
            new OperationCanceledException("non-caller interruption"),
            new InvalidOperationException("invalid provider response"),
        ];

        foreach (var failure in failures)
        {
            var ambiguous = PublicChatDeliveryClassifier
                .ClassifyPostBoundaryFailure(failure, CancellationToken.None)
                .ShouldBeOfType<PublicChatDeliveryOutcome.Ambiguous>();

            ambiguous.Diagnostic.FailureType.ShouldBe(
                PublicChatFailureType.From(failure)
            );
            ambiguous.Diagnostic.ShouldBeOfType<PublicChatFailureDiagnostic.Send>();
        }
    }

    [Test]
    public void CallerCancellation_ClassifyingPostBoundary_PropagatesCancellation()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var cancellation = new OperationCanceledException(caller.Token);

        var thrown = Should.Throw<OperationCanceledException>(() =>
            PublicChatDeliveryClassifier.ClassifyPostBoundaryFailure(
                cancellation,
                caller.Token
            )
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

    private static PublicChatPreparedSend Prepared(
        string accessToken,
        string message
    )
    {
        return new()
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
}
