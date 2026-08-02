using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text.Json;
using Polly.Timeout;

namespace BlokeBot.Twitch.Runtime;

internal static class IrcSessionFailureClassifier
{
    internal static RuntimeSessionFailureClassification Classify(
        Exception exception,
        CancellationToken cancellationToken
    ) =>
        exception switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested =>
                RuntimeSessionFailureClassification.Cancellation,
            TimeoutRejectedException or TimeoutException =>
                RuntimeSessionFailureClassification.Timeout,
            HttpRequestException http => ClassifyHttp(http),
            SocketException => RuntimeSessionFailureClassification.Transient,
            InvalidDataException => RuntimeSessionFailureClassification.Terminal,
            IOException => RuntimeSessionFailureClassification.Transient,
            AuthenticationException or InvalidOperationException or JsonException =>
                RuntimeSessionFailureClassification.Terminal,
            _ => RuntimeSessionFailureClassification.Unexpected,
        };

    private static RuntimeSessionFailureClassification ClassifyHttp(
        HttpRequestException exception
    ) =>
        RuntimeSessionFailureClassifier.IsTransientHttpStatus(exception.StatusCode)
            ? RuntimeSessionFailureClassification.Transient
            : RuntimeSessionFailureClassification.Terminal;
}

internal static class EventSubSessionFailureClassifier
{
    internal static RuntimeSessionFailureClassification Classify(
        Exception exception,
        CancellationToken cancellationToken
    ) =>
        exception switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested =>
                RuntimeSessionFailureClassification.Cancellation,
            TimeoutRejectedException or TimeoutException =>
                RuntimeSessionFailureClassification.Timeout,
            HttpRequestException http => ClassifyHttp(http),
            SocketException or WebSocketException => RuntimeSessionFailureClassification.Transient,
            InvalidDataException => RuntimeSessionFailureClassification.Terminal,
            IOException => RuntimeSessionFailureClassification.Transient,
            AuthenticationException or InvalidOperationException or JsonException =>
                RuntimeSessionFailureClassification.Terminal,
            _ => RuntimeSessionFailureClassification.Unexpected,
        };

    private static RuntimeSessionFailureClassification ClassifyHttp(
        HttpRequestException exception
    ) =>
        RuntimeSessionFailureClassifier.IsTransientHttpStatus(exception.StatusCode)
            ? RuntimeSessionFailureClassification.Transient
            : RuntimeSessionFailureClassification.Terminal;
}

internal static class RuntimeSessionFailureClassifier
{
    internal static bool IsTransientHttpStatus(HttpStatusCode? statusCode) =>
        statusCode is null
        || statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    internal static bool IsRetryable(RuntimeSessionFailureClassification classification) =>
        classification
            is RuntimeSessionFailureClassification.Transient
                or RuntimeSessionFailureClassification.Timeout;
}
