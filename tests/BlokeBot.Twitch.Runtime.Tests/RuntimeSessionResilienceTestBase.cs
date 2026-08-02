using Microsoft.Extensions.Logging;
using Polly;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public abstract class RuntimeSessionResilienceTestBase
{
    private protected static Task<RuntimeSessionEstablishment> IdleAsync() =>
        Task.FromResult<RuntimeSessionEstablishment>(new RuntimeSessionEstablishment.Idle());

    private protected static bool IsConnected(BotRuntimeStatus status) =>
        status.Match(static _ => false, static _ => false, static _ => true);

    private protected static Task<RuntimeSessionEstablishment> EstablishedAsync(
        ScriptedEstablishedSession session
    ) =>
        Task.FromResult<RuntimeSessionEstablishment>(
            new RuntimeSessionEstablishment.Established { Session = session }
        );

    private protected static Task<RuntimeSessionEstablishment> FailedEstablishmentAsync(
        Exception exception
    ) => Task.FromException<RuntimeSessionEstablishment>(exception);

    private protected static Task<RuntimeReconnectRequest> FailedListeningAsync(
        Exception exception
    ) => Task.FromException<RuntimeReconnectRequest>(exception);

    private protected static void AssertReport(
        RuntimeSessionHealthReport report,
        ChatRuntime runtime,
        RuntimeSessionFailureClassification classification,
        int attempt,
        Exception exception
    )
    {
        report.Runtime.ShouldBe(runtime);
        report.Classification.ShouldBe(classification);
        report.Attempt.ShouldBe(attempt);
        report.Exception.ShouldBeSameAs(exception);
    }

    private protected static RuntimeHarness CreateEventSubProtocolHarness(int attemptLimit)
    {
        var session = new ScriptedConnectionSession();
        var health = new RecordingHealthReporter();
        var status = new BotRuntimeStatusStore();
        var idleWait = new RecordingIdleWait();
        var builder = new ResiliencePipelineBuilder();
        RuntimeSessionResilience.ConfigureEventSub(
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
        var eventSub = new EventSubRuntime(
            session,
            new EventSubSessionResiliencePipeline(builder.Build()),
            health,
            status,
            idleWait
        );
        return new RuntimeHarness(
            session,
            health,
            status,
            idleWait,
            eventSub.EstablishSessionAsync,
            eventSub.RunAsync
        );
    }

    private protected static RuntimeHarness CreateRunnerHarness(int attemptLimit)
    {
        var session = new ScriptedConnectionSession();
        var health = new RecordingHealthReporter();
        var status = new BotRuntimeStatusStore();
        var idleWait = new RecordingIdleWait();
        var builder = new ResiliencePipelineBuilder();
        RuntimeSessionResilience.ConfigureIrc(
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
        var pipeline = new IrcSessionResiliencePipeline(builder.Build());

        Task<RuntimeSessionOutcome> EstablishAsync(
            RuntimeConnectionTarget target,
            CancellationToken cancellationToken
        ) =>
            RuntimeSessionRunner.EstablishOnceAsync(
                ChatRuntime.Irc,
                token => session.EstablishAsync(target, token),
                pipeline.ExecuteAsync,
                IrcSessionFailureClassifier.Classify,
                health,
                status,
                cancellationToken
            );

        Task RunAsync(CancellationToken cancellationToken) =>
            RuntimeSessionRunner.RunUntilStoppedAsync(
                ChatRuntime.Irc,
                new RuntimeConnectionTarget.Initial(),
                EstablishAsync,
                IrcSessionFailureClassifier.Classify,
                health,
                status,
                idleWait,
                cancellationToken
            );

        return new(session, health, status, idleWait, EstablishAsync, RunAsync);
    }

    private protected sealed class RuntimeHarness(
        ScriptedConnectionSession session,
        RecordingHealthReporter health,
        BotRuntimeStatusStore status,
        RecordingIdleWait idleWait,
        Func<
            RuntimeConnectionTarget,
            CancellationToken,
            Task<RuntimeSessionOutcome>
        > establishSession,
        Func<CancellationToken, Task> runRuntime
    )
    {
        internal ScriptedConnectionSession Session { get; } = session;

        internal RecordingHealthReporter Health { get; } = health;

        internal BotRuntimeStatusStore Status { get; } = status;

        internal RecordingIdleWait IdleWait { get; } = idleWait;

        internal Task<RuntimeSessionOutcome> EstablishSessionAsync(
            RuntimeConnectionTarget target,
            CancellationToken cancellationToken
        ) => establishSession(target, cancellationToken);

        internal Task RunRuntimeAsync(CancellationToken cancellationToken) =>
            runRuntime(cancellationToken);
    }

    private protected sealed class ScriptedConnectionSession
        : IIrcConnectionSession,
            IEventSubConnectionSession
    {
        private readonly Queue<
            Func<RuntimeConnectionTarget, CancellationToken, Task<RuntimeSessionEstablishment>>
        > _operations = [];

        internal int CallCount { get; private set; }

        internal List<RuntimeConnectionTarget> Targets { get; } = [];

        internal void Enqueue(
            Func<
                RuntimeConnectionTarget,
                CancellationToken,
                Task<RuntimeSessionEstablishment>
            > operation
        ) => _operations.Enqueue(operation);

        public Task<RuntimeSessionEstablishment> EstablishAsync(
            RuntimeConnectionTarget target,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            Targets.Add(target);
            return _operations.Dequeue()(target, cancellationToken);
        }
    }

    private protected sealed class ScriptedEstablishedSession : IRuntimeEstablishedSession
    {
        private readonly Queue<Func<CancellationToken, Task<RuntimeReconnectRequest>>> _listeners =
        [];

        internal int ListenCount { get; private set; }

        internal int DisposeCount { get; private set; }

        internal Exception? DisposeException { get; init; }

        internal void Enqueue(Func<CancellationToken, Task<RuntimeReconnectRequest>> listener) =>
            _listeners.Enqueue(listener);

        public Task<RuntimeReconnectRequest> ListenAsync(CancellationToken cancellationToken)
        {
            ListenCount++;
            return _listeners.Dequeue()(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeException is { } exception
                ? new ValueTask(Task.FromException(exception))
                : ValueTask.CompletedTask;
        }
    }

    private protected sealed class RecordingHealthReporter : IRuntimeSessionHealthReporter
    {
        internal List<RuntimeSessionHealthReport> Reports { get; } = [];

        public void Report(RuntimeSessionHealthReport report) => Reports.Add(report);
    }

    private protected sealed class RecordingIdleWait : IRuntimeIdleWait
    {
        internal int CallCount { get; private set; }

        public ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private protected sealed class RecordingLogger<T> : ILogger<T>
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
                : [];
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, properties));
        }
    }

    private protected sealed class LogEntry(
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

    private protected sealed class Scope : IDisposable
    {
        internal static Scope Instance { get; } = new();

        public void Dispose() { }
    }
}
