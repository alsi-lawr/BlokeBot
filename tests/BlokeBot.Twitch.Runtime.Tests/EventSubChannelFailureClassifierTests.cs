using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubChannelFailureClassifierTests : EventSubChannelRecoveryTestBase
{
    [Test]
    public void ChannelFailureClassifier_ClassifyingBoundaryFailures_UsesChannelSemantics()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var cancellation = EventSubChannelFailureClassifier.Classify(
            new OperationCanceledException(canceled.Token),
            EventSubChannelPhase.AccountResolution,
            canceled.Token
        );
        var transientSetup = EventSubChannelFailureClassifier.Classify(
            new EventSubChannelOperationException(
                EventSubChannelPhase.SubscriptionSetup,
                new HttpRequestException(
                    "service unavailable",
                    null,
                    System.Net.HttpStatusCode.ServiceUnavailable
                )
            ),
            EventSubChannelPhase.AccountResolution,
            CancellationToken.None
        );
        var unexpected = EventSubChannelFailureClassifier.Classify(
            new ApplicationException("programmer defect"),
            EventSubChannelPhase.Reconciliation,
            CancellationToken.None
        );
        var deletionCause = new IOException("delete failed");
        var deletionFailure = EventSubChannelFailureClassifier.Classify(
            deletionCause,
            EventSubChannelPhase.SubscriptionDeletion,
            CancellationToken.None
        );

        cancellation.Classification.ShouldBe(EventSubChannelFailureClassification.Cancellation);
        transientSetup.Phase.ShouldBe(EventSubChannelPhase.SubscriptionSetup);
        transientSetup.Classification.ShouldBe(EventSubChannelFailureClassification.Transient);
        unexpected.Classification.ShouldBe(EventSubChannelFailureClassification.Unexpected);
        deletionFailure.Phase.ShouldBe(EventSubChannelPhase.SubscriptionDeletion);
        deletionFailure.Classification.ShouldBe(EventSubChannelFailureClassification.Transient);
        deletionFailure.Exception.ShouldBeSameAs(deletionCause);
    }
}
