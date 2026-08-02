using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class RuntimeSessionBoundaryTests : RuntimeSessionResilienceTestBase
{
    [Test]
    public void BoundaryClassifiers_ClassifyingHttpAndCancellation_UseExplicitCases()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var cancellation = new OperationCanceledException(canceled.Token);
        var transientHttp = new HttpRequestException(
            "service unavailable",
            null,
            System.Net.HttpStatusCode.ServiceUnavailable
        );
        var terminalHttp = new HttpRequestException(
            "unauthorized",
            null,
            System.Net.HttpStatusCode.Unauthorized
        );

        IrcSessionFailureClassifier
            .Classify(cancellation, canceled.Token)
            .ShouldBe(RuntimeSessionFailureClassification.Cancellation);
        EventSubSessionFailureClassifier
            .Classify(cancellation, CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Unexpected);
        IrcSessionFailureClassifier
            .Classify(transientHttp, CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Transient);
        EventSubSessionFailureClassifier
            .Classify(transientHttp, CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Transient);
        IrcSessionFailureClassifier
            .Classify(terminalHttp, CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Terminal);
        EventSubSessionFailureClassifier
            .Classify(terminalHttp, CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Terminal);
    }

    [Test]
    public void BoundaryClassifiers_ClassifyingTransportAndProtocolFaults_UseBoundaryCases()
    {
        IrcSessionFailureClassifier
            .Classify(new SocketException((int)SocketError.ConnectionReset), CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Transient);
        EventSubSessionFailureClassifier
            .Classify(
                new WebSocketException(WebSocketError.ConnectionClosedPrematurely),
                CancellationToken.None
            )
            .ShouldBe(RuntimeSessionFailureClassification.Transient);
        IrcSessionFailureClassifier
            .Classify(new JsonException("invalid payload"), CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Terminal);
        EventSubSessionFailureClassifier
            .Classify(new TimeoutException("establishment timeout"), CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Timeout);
    }

    [Test]
    public void StructuredHealthReport_Logging_ContainsSafeFieldsWithoutExceptionMessage()
    {
        const string Secret = "oauth:do-not-log";
        var logger = new RecordingLogger<RuntimeSessionHealthLogger>();
        var health = new RuntimeSessionHealthLogger(logger);

        health.Report(
            new RuntimeSessionHealthReport.Unhealthy
            {
                Runtime = ChatRuntime.Irc,
                Classification = RuntimeSessionFailureClassification.Unexpected,
                Attempt = 2,
                Exception = new ApplicationException(Secret),
            }
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(Secret);
        entry.Properties["Runtime"].ShouldBe(ChatRuntime.Irc);
        entry.Properties["Classification"].ShouldBe(RuntimeSessionFailureClassification.Unexpected);
        entry.Properties["Attempt"].ShouldBe(2);
        entry.Properties["FailureType"].ShouldBe(typeof(ApplicationException).FullName);
    }

    [Test]
    public void StructuredReconnectReport_Logging_ContainsSafeFieldsWithoutExceptionMessage()
    {
        const string Secret = "oauth:do-not-log";
        var logger = new RecordingLogger<RuntimeSessionHealthLogger>();
        var health = new RuntimeSessionHealthLogger(logger);

        health.Report(
            new RuntimeSessionHealthReport.ReconnectScheduled
            {
                Runtime = ChatRuntime.EventSub,
                Classification = RuntimeSessionFailureClassification.Transient,
                Attempt = 3,
                Exception = new IOException(Secret),
            }
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(Secret);
        entry.Properties["Runtime"].ShouldBe(ChatRuntime.EventSub);
        entry.Properties["Classification"].ShouldBe(RuntimeSessionFailureClassification.Transient);
        entry.Properties["Attempt"].ShouldBe(3);
        entry.Properties["FailureType"].ShouldBe(typeof(IOException).FullName);
    }
}
