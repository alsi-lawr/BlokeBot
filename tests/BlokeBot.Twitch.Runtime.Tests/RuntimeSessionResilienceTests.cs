using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Polly;
using Polly.Timeout;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class RuntimeSessionResilienceTests
{
    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task FirstSessionAttempt_Succeeding_CompletesWithoutFailureReport(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        harness.Session.Enqueue(_ =>
        {
            harness.Status.SetConnected(true, ["channel"]);
            return Task.CompletedTask;
        });

        var outcome = await harness.RunSessionAsync(CancellationToken.None);

        outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Completed>();
        harness.Session.CallCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
        harness.Status.Current.IsConnected.ShouldBeTrue();
        harness.Status.Current.ConnectedChannels.ShouldBe(["channel"]);
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task TransientFailureThenSuccess_RunningSession_RetriesAndResetsLifecycle(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new IOException("transport unavailable");
        var connectedTransitions = new List<bool>();
        harness.Status.Changed += () =>
            connectedTransitions.Add(harness.Status.Current.IsConnected);
        harness.Session.Enqueue(_ =>
        {
            harness.Status.SetConnected(true, ["stale"]);
            return Task.FromException(failure);
        });
        harness.Session.Enqueue(_ =>
        {
            harness.Status.SetConnected(true, ["fresh"]);
            return Task.CompletedTask;
        });

        var outcome = await harness.RunSessionAsync(CancellationToken.None);

        outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Completed>();
        harness.Session.CallCount.ShouldBe(2);
        connectedTransitions.ShouldBe([true, false, true]);
        harness.Status.Current.ConnectedChannels.ShouldBe(["fresh"]);
        var report = harness.Health.Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>();
        AssertReport(
            report,
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task TerminalFailure_RunningSession_ReportsUnhealthyWithoutRetry(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new TwitchAccessTokenUnavailableException(
            TwitchAccessTokenUnavailableReason.MissingRefreshToken,
            TwitchAccessTokenUnavailableException.MissingRefreshTokenMessage
        );
        harness.Session.Enqueue(_ =>
        {
            harness.Status.SetConnected(true, ["stale"]);
            return Task.FromException(failure);
        });

        var outcome = await harness.RunSessionAsync(CancellationToken.None);

        var unhealthy = outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(1);
        harness.Status.Current.IsAuthorized.ShouldBeFalse();
        harness.Status.Current.IsConnected.ShouldBeFalse();
        var report = harness.Health.Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>();
        unhealthy.Report.ShouldBeSameAs(report);
        AssertReport(
            report,
            runtime,
            TwitchRuntimeSessionFailureClassification.Terminal,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task UnexpectedFailure_RunningSession_ReportsUnhealthyWithoutRetry(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var failure = new ApplicationException("unexpected runtime defect");
        harness.Session.Enqueue(_ => Task.FromException(failure));

        var outcome = await harness.RunSessionAsync(CancellationToken.None);

        outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(1);
        var report = harness.Health.Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>();
        AssertReport(
            report,
            runtime,
            TwitchRuntimeSessionFailureClassification.Unexpected,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task TimeoutThenSuccess_RunningSession_RetriesThroughDirectTimeoutHook(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 2);
        var failure = new TimeoutRejectedException("session attempt timed out");
        harness.Session.Enqueue(_ => Task.FromException(failure));
        harness.Session.Enqueue(_ => Task.CompletedTask);

        var outcome = await harness.RunSessionAsync(CancellationToken.None);

        outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Completed>();
        harness.Session.CallCount.ShouldBe(2);
        var report = harness.Health.Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>();
        AssertReport(
            report,
            runtime,
            TwitchRuntimeSessionFailureClassification.Timeout,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task TransientFailures_ExhaustingAttempts_ReportsBoundedUnhealthyOutcome(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 3);
        var first = new IOException("first transport failure");
        var second = new IOException("second transport failure");
        var final = new IOException("final transport failure");
        harness.Session.Enqueue(_ => Task.FromException(first));
        harness.Session.Enqueue(_ => Task.FromException(second));
        harness.Session.Enqueue(_ => Task.FromException(final));

        var outcome = await harness.RunSessionAsync(CancellationToken.None);

        var unhealthy = outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(3);
        harness.Health.Reports.Count.ShouldBe(3);
        AssertReport(
            harness.Health.Reports[0]
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 1,
            first
        );
        AssertReport(
            harness.Health.Reports[1]
                .ShouldBeOfType<TwitchRuntimeSessionHealthReport.RetryScheduled>(),
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 2,
            second
        );
        var finalReport = harness.Health.Reports[2]
            .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>();
        unhealthy.Report.ShouldBeSameAs(finalReport);
        AssertReport(
            finalReport,
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 3,
            final
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task SingleAttemptPolicy_TransientFailure_DoesNotAddACompatibilityRetry(
        TwitchBotRuntime runtime
    )
    {
        var harness = CreateHarness(runtime, attemptLimit: 1);
        var failure = new IOException("only transport attempt failed");
        harness.Session.Enqueue(_ => Task.FromException(failure));

        var outcome = await harness.RunSessionAsync(CancellationToken.None);

        outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Unhealthy>();
        harness.Session.CallCount.ShouldBe(1);
        var report = harness.Health.Reports.ShouldHaveSingleItem()
            .ShouldBeOfType<TwitchRuntimeSessionHealthReport.Unhealthy>();
        AssertReport(
            report,
            runtime,
            TwitchRuntimeSessionFailureClassification.Transient,
            attempt: 1,
            failure
        );
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task CallerCancellation_DuringSession_StopsWithoutFailureReport(
        TwitchBotRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(runtime, attemptLimit: 3);
        harness.Session.Enqueue(attemptToken =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(attemptToken);
        });

        var outcome = await harness.RunSessionAsync(cancellation.Token);

        outcome.ShouldBeOfType<TwitchRuntimeSessionOutcome.Canceled>();
        harness.Session.CallCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
    }

    [Test]
    [Arguments(TwitchBotRuntime.Irc)]
    [Arguments(TwitchBotRuntime.EventSub)]
    public async Task IdleCompletion_RunningRuntime_WaitsOutsideRetryThenRechecks(
        TwitchBotRuntime runtime
    )
    {
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(runtime, attemptLimit: 3);
        harness.Session.Enqueue(_ => Task.CompletedTask);
        harness.Session.Enqueue(attemptToken =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(attemptToken);
        });

        await harness.RunRuntimeAsync(cancellation.Token);

        harness.Session.CallCount.ShouldBe(2);
        harness.IdleWait.CallCount.ShouldBe(1);
        harness.Health.Reports.ShouldBeEmpty();
    }

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

        TwitchIrcSessionFailureClassifier.Classify(cancellation, canceled.Token)
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Cancellation);
        TwitchEventSubSessionFailureClassifier.Classify(cancellation, CancellationToken.None)
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Unexpected);
        TwitchIrcSessionFailureClassifier.Classify(transientHttp, CancellationToken.None)
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Transient);
        TwitchEventSubSessionFailureClassifier.Classify(
                transientHttp,
                CancellationToken.None
            )
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Transient);
        TwitchIrcSessionFailureClassifier.Classify(terminalHttp, CancellationToken.None)
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Terminal);
        TwitchEventSubSessionFailureClassifier.Classify(terminalHttp, CancellationToken.None)
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Terminal);
    }

    [Test]
    public void BoundaryClassifiers_ClassifyingTransportAndProtocolFaults_UseBoundaryCases()
    {
        TwitchIrcSessionFailureClassifier.Classify(
                new SocketException((int)SocketError.ConnectionReset),
                CancellationToken.None
            )
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Transient);
        TwitchEventSubSessionFailureClassifier.Classify(
                new WebSocketException(WebSocketError.ConnectionClosedPrematurely),
                CancellationToken.None
            )
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Transient);
        TwitchIrcSessionFailureClassifier.Classify(
                new JsonException("invalid payload"),
                CancellationToken.None
            )
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Terminal);
        TwitchEventSubSessionFailureClassifier.Classify(
                new TimeoutException("session timeout"),
                CancellationToken.None
            )
            .ShouldBe(TwitchRuntimeSessionFailureClassification.Timeout);
    }

    [Test]
    public void StructuredHealthReport_Logging_ContainsSafeFieldsWithoutExceptionMessage()
    {
        const string secret = "oauth:do-not-log";
        var logger = new RecordingLogger<TwitchRuntimeSessionHealthLogger>();
        var health = new TwitchRuntimeSessionHealthLogger(logger);

        health.Report(
            new TwitchRuntimeSessionHealthReport.Unhealthy
            {
                Runtime = TwitchBotRuntime.Irc,
                Classification = TwitchRuntimeSessionFailureClassification.Unexpected,
                Attempt = 2,
                Exception = new ApplicationException(secret),
            }
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(secret);
        entry.Properties["Runtime"].ShouldBe(TwitchBotRuntime.Irc);
        entry.Properties["Classification"].ShouldBe(
            TwitchRuntimeSessionFailureClassification.Unexpected
        );
        entry.Properties["Attempt"].ShouldBe(2);
        entry.Properties["FailureType"].ShouldBe(typeof(ApplicationException).FullName);
    }

    private static void AssertReport(
        TwitchRuntimeSessionHealthReport report,
        TwitchBotRuntime runtime,
        TwitchRuntimeSessionFailureClassification classification,
        int attempt,
        Exception exception
    )
    {
        report.Runtime.ShouldBe(runtime);
        report.Classification.ShouldBe(classification);
        report.Attempt.ShouldBe(attempt);
        report.Exception.ShouldBeSameAs(exception);
    }

    private static RuntimeHarness CreateHarness(
        TwitchBotRuntime runtime,
        int attemptLimit
    )
    {
        var session = new ScriptedConnectionSession();
        var health = new RecordingHealthReporter();
        var status = new TwitchBotRuntimeStatusStore();
        var idleWait = new RecordingIdleWait();
        var builder = new ResiliencePipelineBuilder();
        switch (runtime)
        {
            case TwitchBotRuntime.Irc:
                TwitchRuntimeSessionResilience.ConfigureIrc(
                    builder,
                    new IrcSessionResiliencePolicy
                    {
                        AttemptLimit = attemptLimit,
                        Delay = TimeSpan.Zero,
                        MaximumDelay = TimeSpan.FromTicks(1),
                        DelayBackoffType = DelayBackoffType.Constant,
                        AttemptTimeout = TimeSpan.FromMinutes(1),
                    },
                    health
                );
                var irc = new TwitchIrcRuntime(
                    session,
                    new TwitchIrcSessionResiliencePipeline(builder.Build()),
                    health,
                    status,
                    idleWait
                );
                return new RuntimeHarness(
                    session,
                    health,
                    status,
                    idleWait,
                    irc.RunSessionAsync,
                    irc.RunAsync
                );
            case TwitchBotRuntime.EventSub:
                TwitchRuntimeSessionResilience.ConfigureEventSub(
                    builder,
                    new EventSubSessionResiliencePolicy
                    {
                        AttemptLimit = attemptLimit,
                        Delay = TimeSpan.Zero,
                        MaximumDelay = TimeSpan.FromTicks(1),
                        DelayBackoffType = DelayBackoffType.Constant,
                        AttemptTimeout = TimeSpan.FromMinutes(1),
                    },
                    health
                );
                var eventSub = new TwitchEventSubRuntime(
                    session,
                    new TwitchEventSubSessionResiliencePipeline(builder.Build()),
                    health,
                    status,
                    idleWait
                );
                return new RuntimeHarness(
                    session,
                    health,
                    status,
                    idleWait,
                    eventSub.RunSessionAsync,
                    eventSub.RunAsync
                );
            default:
                throw new UnreachableException($"Unknown Twitch runtime: {runtime}.");
        }
    }

    private sealed class RuntimeHarness(
        ScriptedConnectionSession session,
        RecordingHealthReporter health,
        TwitchBotRuntimeStatusStore status,
        RecordingIdleWait idleWait,
        Func<CancellationToken, Task<TwitchRuntimeSessionOutcome>> runSession,
        Func<CancellationToken, Task> runRuntime
    )
    {
        internal ScriptedConnectionSession Session { get; } = session;

        internal RecordingHealthReporter Health { get; } = health;

        internal TwitchBotRuntimeStatusStore Status { get; } = status;

        internal RecordingIdleWait IdleWait { get; } = idleWait;

        internal Task<TwitchRuntimeSessionOutcome> RunSessionAsync(
            CancellationToken cancellationToken
        ) => runSession(cancellationToken);

        internal Task RunRuntimeAsync(CancellationToken cancellationToken) =>
            runRuntime(cancellationToken);
    }

    private sealed class ScriptedConnectionSession
        : ITwitchIrcConnectionSession,
            ITwitchEventSubConnectionSession
    {
        private readonly Queue<Func<CancellationToken, Task>> operations = [];

        internal int CallCount { get; private set; }

        internal void Enqueue(Func<CancellationToken, Task> operation) =>
            operations.Enqueue(operation);

        public Task RunAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return operations.Dequeue()(cancellationToken);
        }
    }

    private sealed class RecordingHealthReporter : ITwitchRuntimeSessionHealthReporter
    {
        internal List<TwitchRuntimeSessionHealthReport> Reports { get; } = [];

        public void Report(TwitchRuntimeSessionHealthReport report) => Reports.Add(report);
    }

    private sealed class RecordingIdleWait : ITwitchRuntimeIdleWait
    {
        internal int CallCount { get; private set; }

        public ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => Scope.Instance;

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
            Entries.Add(
                new LogEntry(logLevel, formatter(state, exception), exception, properties)
            );
        }
    }

    private sealed class LogEntry(
        LogLevel level,
        string message,
        Exception? exception,
        IReadOnlyDictionary<string, object?> properties
    )
    {
        internal LogLevel Level { get; } = level;

        internal string Message { get; } = message;

        internal Exception? Exception { get; } = exception;

        internal IReadOnlyDictionary<string, object?> Properties { get; } = properties;
    }

    private sealed class Scope : IDisposable
    {
        internal static Scope Instance { get; } = new();

        public void Dispose() { }
    }
}
