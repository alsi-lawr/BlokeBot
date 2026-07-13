using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Eventing;

public enum ObserverFailureClassification
{
    Transient,
    Terminal,
    Unexpected,
}

public sealed record ObserverFailureSummary
{
    public required ObserverBoundary Boundary { get; init; }

    public required ObserverEventIdentity Event { get; init; }

    public required ObserverIdentity Observer { get; init; }

    public required ObserverCorrelationId CorrelationId { get; init; }

    public required int Attempt { get; init; }

    public required ObserverFailureClassification Classification { get; init; }

    public required string FailureType { get; init; }
}

public sealed record ObserverDeadLetter<TDeadLetter>
    where TDeadLetter : IObserverDeadLetterPayload
{
    public required ObserverFailureSummary Failure { get; init; }

    public required TDeadLetter Payload { get; init; }
}

public sealed record ObserverDispatch<TEvent, TDeadLetter>
    where TDeadLetter : IObserverDeadLetterPayload
{
    public required TEvent Event { get; init; }

    public required TDeadLetter DeadLetter { get; init; }

    public required ObserverEventIdentity EventIdentity { get; init; }
}

public enum ObserverFailureHandlingStage
{
    Reporter,
    DeadLetterSink,
}

public sealed record ObserverFailureHandlingSummary
{
    public required ObserverFailureSummary ObserverFailure { get; init; }

    public required ObserverFailureHandlingStage Stage { get; init; }

    public required string FailureType { get; init; }
}

public abstract record ObserverFanOutOutcome
{
    private protected ObserverFanOutOutcome() { }

    private protected abstract void Seal();

    public sealed record AllSucceeded : ObserverFanOutOutcome
    {
        public required int ObserverCount { get; init; }

        private protected override void Seal() { }
    }

    public sealed record CompletedWithFailures : ObserverFanOutOutcome
    {
        public required IReadOnlyList<ObserverFailureSummary> Failures { get; init; }

        private protected override void Seal() { }
    }
}

public sealed class ObserverFanOutEscalationException : Exception
{
    internal ObserverFanOutEscalationException(
        IReadOnlyList<ObserverFailureSummary> failures,
        IReadOnlyList<ObserverFailureHandlingSummary> handlingFailures,
        IReadOnlyList<Exception> causes
    )
        : base("Observer fan-out failure handling failed.")
    {
        Failures = Array.AsReadOnly(failures.ToArray());
        HandlingFailures = Array.AsReadOnly(handlingFailures.ToArray());
        Causes = Array.AsReadOnly(causes.ToArray());
    }

    public IReadOnlyList<ObserverFailureSummary> Failures { get; }

    public IReadOnlyList<ObserverFailureHandlingSummary> HandlingFailures { get; }

    internal IReadOnlyList<Exception> Causes { get; }
}

public sealed class ObserverFanOut<TBoundary, TEvent, TDeadLetter>
    where TDeadLetter : IObserverDeadLetterPayload
{
    private readonly ObserverFailurePolicy<TBoundary, TDeadLetter> _policy;
    private readonly IObserverFailureDiagnosticReporter _reporter;
    private readonly IObserverCorrelationIdProvider _correlations;

    internal ObserverFanOut(
        ObserverFailurePolicy<TBoundary, TDeadLetter> policy,
        IObserverFailureDiagnosticReporter reporter,
        IObserverCorrelationIdProvider correlations
    )
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(reporter);
        ArgumentNullException.ThrowIfNull(correlations);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.Boundary.Value);
        if (policy is ObserverFailurePolicy<TBoundary, TDeadLetter>.DeadLetter deadLetter)
        {
            ArgumentNullException.ThrowIfNull(deadLetter.Sink);
        }

        _policy = policy;
        _reporter = reporter;
        _correlations = correlations;
    }

    public async ValueTask<ObserverFanOutOutcome> DispatchAsync<TObserver>(
        IReadOnlyList<TObserver> observers,
        Func<ObserverCorrelationId, ObserverDispatch<TEvent, TDeadLetter>> createDispatch,
        Func<TObserver, ObserverIdentity> identify,
        Func<TObserver, TEvent, CancellationToken, ValueTask> invoke,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(createDispatch);
        ArgumentNullException.ThrowIfNull(identify);
        ArgumentNullException.ThrowIfNull(invoke);
        cancellationToken.ThrowIfCancellationRequested();

        var correlationId = _correlations.Next();
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId.Value);
        var dispatch = createDispatch(correlationId);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatch.EventIdentity.Value);
        var failures = new List<ObserverFailureSummary>();
        var exactObserverFailures = new List<Exception>();
        var failureHandlingFailures = new List<ObserverFailureHandlingDetails>();

        foreach (var observer in observers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observerIdentity = identify(observer);
            ArgumentException.ThrowIfNullOrWhiteSpace(observerIdentity.Value);
            await InvokeObserverAsync(
                observer,
                observerIdentity,
                dispatch,
                invoke,
                correlationId,
                failures,
                exactObserverFailures,
                failureHandlingFailures,
                cancellationToken
            );
        }

        if (failureHandlingFailures.Count > 0)
        {
            throw new ObserverFanOutEscalationException(
                failures,
                failureHandlingFailures.Select(failure => failure.Summary).ToArray(),
                exactObserverFailures
                    .Concat(failureHandlingFailures.Select(failure => failure.Exception))
                    .ToArray()
            );
        }

        return failures.Count == 0
            ? new ObserverFanOutOutcome.AllSucceeded { ObserverCount = observers.Count }
            : new ObserverFanOutOutcome.CompletedWithFailures
            {
                Failures = Array.AsReadOnly(failures.ToArray()),
            };
    }

    private async ValueTask InvokeObserverAsync<TObserver>(
        TObserver observer,
        ObserverIdentity observerIdentity,
        ObserverDispatch<TEvent, TDeadLetter> dispatch,
        Func<TObserver, TEvent, CancellationToken, ValueTask> invoke,
        ObserverCorrelationId correlationId,
        List<ObserverFailureSummary> failures,
        List<Exception> exactObserverFailures,
        List<ObserverFailureHandlingDetails> failureHandlingFailures,
        CancellationToken cancellationToken
    )
    {
        switch (_policy)
        {
            case ObserverFailurePolicy<TBoundary, TDeadLetter>.ContinueAndReport:
                AppendEscalation(
                    await InvokeAttemptAsync(
                        observer,
                        observerIdentity,
                        dispatch,
                        invoke,
                        correlationId,
                        attempt: 1,
                        failures,
                        exactObserverFailures,
                        cancellationToken
                    ),
                    failureHandlingFailures
                );
                return;
            case ObserverFailurePolicy<TBoundary, TDeadLetter>.BoundedRetry retry:
                for (var attempt = 1; attempt <= retry.AttemptLimit; attempt++)
                {
                    var result = await InvokeAttemptAsync(
                        observer,
                        observerIdentity,
                        dispatch,
                        invoke,
                        correlationId,
                        attempt,
                        failures,
                        exactObserverFailures,
                        cancellationToken
                    );
                    AppendEscalation(result, failureHandlingFailures);
                    if (
                        result
                        is ObserverAttemptOutcome.Succeeded
                            or ObserverAttemptOutcome.Failed
                        {
                            Details.Summary.Classification: not ObserverFailureClassification.Transient,
                        }
                    )
                    {
                        return;
                    }
                }

                return;
            case ObserverFailurePolicy<TBoundary, TDeadLetter>.DeadLetter deadLetter:
                var deadLetterResult = await InvokeAttemptAsync(
                    observer,
                    observerIdentity,
                    dispatch,
                    invoke,
                    correlationId,
                    attempt: 1,
                    failures,
                    exactObserverFailures,
                    cancellationToken
                );
                if (deadLetterResult is ObserverAttemptOutcome.Failed failed)
                {
                    var handlingFailures = failed.HandlingFailures.ToList();
                    try
                    {
                        await deadLetter.Sink.StoreAsync(
                            new ObserverDeadLetter<TDeadLetter>
                            {
                                Failure = failed.Details.Summary,
                                Payload = dispatch.DeadLetter,
                            },
                            cancellationToken
                        );
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        handlingFailures.Add(
                            ObserverFailureHandlingDetails.Create(
                                failed.Details.Summary,
                                ObserverFailureHandlingStage.DeadLetterSink,
                                exception
                            )
                        );
                    }

                    AppendEscalation(handlingFailures, failureHandlingFailures);
                }

                return;
            default:
                throw new UnreachableException(
                    "Unknown observer failure policy."
                );
        }
    }

    private async ValueTask<ObserverAttemptOutcome> InvokeAttemptAsync<TObserver>(
        TObserver observer,
        ObserverIdentity observerIdentity,
        ObserverDispatch<TEvent, TDeadLetter> dispatch,
        Func<TObserver, TEvent, CancellationToken, ValueTask> invoke,
        ObserverCorrelationId correlationId,
        int attempt,
        List<ObserverFailureSummary> failures,
        List<Exception> exactObserverFailures,
        CancellationToken cancellationToken
    )
    {
        ObserverFailureDetails details;
        try
        {
            await invoke(observer, dispatch.Event, cancellationToken);
            return new ObserverAttemptOutcome.Succeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            details = ObserverFailureClassifier.Classify(
                _policy.Boundary,
                dispatch.EventIdentity,
                observerIdentity,
                correlationId,
                attempt,
                exception
            );
            failures.Add(details.Summary);
            exactObserverFailures.Add(details.Exception);
        }

        var handlingFailures = new List<ObserverFailureHandlingDetails>();
        try
        {
            await _reporter.ReportAsync(
                new ObserverFailureDiagnosticReport
                {
                    Summary = details.Summary,
                    Exception = details.Exception,
                },
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            handlingFailures.Add(
                ObserverFailureHandlingDetails.Create(
                    details.Summary,
                    ObserverFailureHandlingStage.Reporter,
                    exception
                )
            );
        }

        return new ObserverAttemptOutcome.Failed
        {
            Details = details,
            HandlingFailures = Array.AsReadOnly(handlingFailures.ToArray()),
        };
    }

    private static void AppendEscalation(
        ObserverAttemptOutcome outcome,
        List<ObserverFailureHandlingDetails> escalationFailures
    )
    {
        if (outcome is not ObserverAttemptOutcome.Failed failed)
        {
            return;
        }

        AppendEscalation(failed.HandlingFailures, escalationFailures);
    }

    private static void AppendEscalation(
        IReadOnlyList<ObserverFailureHandlingDetails> handlingFailures,
        List<ObserverFailureHandlingDetails> escalationFailures
    )
    {
        if (handlingFailures.Count == 0)
        {
            return;
        }

        escalationFailures.AddRange(handlingFailures);
    }
}

internal sealed record ObserverFailureDiagnosticReport
{
    internal required ObserverFailureSummary Summary { get; init; }

    internal required Exception Exception { get; init; }
}

internal interface IObserverFailureDiagnosticReporter
{
    ValueTask ReportAsync(
        ObserverFailureDiagnosticReport report,
        CancellationToken cancellationToken
    );
}

internal sealed class ObserverFailureDiagnosticLogger(
    ILogger<ObserverFailureDiagnosticLogger> log
) : IObserverFailureDiagnosticReporter
{
    public ValueTask ReportAsync(
        ObserverFailureDiagnosticReport report,
        CancellationToken cancellationToken
    )
    {
        log.LogWarning(
            "Observer {Observer} failed for {Event} at {Boundary} attempt {Attempt}; classified {Classification} ({FailureType}), correlation {CorrelationId}.",
            report.Summary.Observer,
            report.Summary.Event,
            report.Summary.Boundary,
            report.Summary.Attempt,
            report.Summary.Classification,
            report.Summary.FailureType,
            report.Summary.CorrelationId
        );
        return ValueTask.CompletedTask;
    }
}

internal interface IObserverCorrelationIdProvider
{
    ObserverCorrelationId Next();
}

internal sealed class ObserverCorrelationIdProvider : IObserverCorrelationIdProvider
{
    public ObserverCorrelationId Next()
    {
        return ObserverCorrelationId.Named(Guid.NewGuid().ToString("N"));
    }
}

internal readonly record struct ObserverFailureDetails(
    ObserverFailureSummary Summary,
    Exception Exception
);

internal readonly record struct ObserverFailureHandlingDetails(
    ObserverFailureHandlingSummary Summary,
    Exception Exception
)
{
    internal static ObserverFailureHandlingDetails Create(
        ObserverFailureSummary observerFailure,
        ObserverFailureHandlingStage stage,
        Exception exception
    )
    {
        return new(
            new ObserverFailureHandlingSummary
            {
                ObserverFailure = observerFailure,
                Stage = stage,
                FailureType = exception.GetType().FullName ?? exception.GetType().Name,
            },
            exception
        );
    }
}

internal static class ObserverFailureClassifier
{
    internal static ObserverFailureDetails Classify(
        ObserverBoundary boundary,
        ObserverEventIdentity eventIdentity,
        ObserverIdentity observer,
        ObserverCorrelationId correlationId,
        int attempt,
        Exception exception
    )
    {
        return new(
            new ObserverFailureSummary
            {
                Boundary = boundary,
                Event = eventIdentity,
                Observer = observer,
                CorrelationId = correlationId,
                Attempt = attempt,
                Classification = Classify(exception),
                FailureType = exception.GetType().FullName ?? exception.GetType().Name,
            },
            exception
        );
    }

    private static ObserverFailureClassification Classify(Exception exception)
    {
        return exception switch
        {
            TimeoutException or IOException or SocketException =>
                ObserverFailureClassification.Transient,
            HttpRequestException http when IsTransientHttpStatus(http.StatusCode) =>
                ObserverFailureClassification.Transient,
            HttpRequestException
            or AuthenticationException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or InvalidDataException
            or JsonException => ObserverFailureClassification.Terminal,
            _ => ObserverFailureClassification.Unexpected,
        };
    }

    private static bool IsTransientHttpStatus(HttpStatusCode? statusCode)
    {
        return statusCode is null
        || statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;
    }
}

internal abstract record ObserverAttemptOutcome
{
    private protected ObserverAttemptOutcome() { }

    private protected abstract void Seal();

    internal sealed record Succeeded : ObserverAttemptOutcome
    {
        private protected override void Seal() { }
    }

    internal sealed record Failed : ObserverAttemptOutcome
    {
        internal required ObserverFailureDetails Details { get; init; }

        internal required IReadOnlyList<ObserverFailureHandlingDetails> HandlingFailures
        {
            get;
            init;
        }

        private protected override void Seal() { }
    }
}
