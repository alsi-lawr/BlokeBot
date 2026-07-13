using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text.Json;
using Polly.Timeout;

namespace BlokeBot.Twitch.Runtime;

internal static class IrcSessionFailureClassifier
{
    internal static TwitchRuntimeSessionFailureClassification Classify(
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        return exception switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested =>
                TwitchRuntimeSessionFailureClassification.Cancellation,
            TimeoutRejectedException or TimeoutException =>
                TwitchRuntimeSessionFailureClassification.Timeout,
            HttpRequestException http => ClassifyHttp(http),
            SocketException => TwitchRuntimeSessionFailureClassification.Transient,
            InvalidDataException => TwitchRuntimeSessionFailureClassification.Terminal,
            IOException => TwitchRuntimeSessionFailureClassification.Transient,
            AccessTokenUnavailableException
            or AuthenticationException
            or InvalidOperationException
            or JsonException => TwitchRuntimeSessionFailureClassification.Terminal,
            _ => TwitchRuntimeSessionFailureClassification.Unexpected,
        };
    }

    private static TwitchRuntimeSessionFailureClassification ClassifyHttp(
        HttpRequestException exception
    )
    {
        return TwitchRuntimeSessionFailureClassifier.IsTransientHttpStatus(exception.StatusCode)
            ? TwitchRuntimeSessionFailureClassification.Transient
            : TwitchRuntimeSessionFailureClassification.Terminal;
    }
}

internal static class EventSubSessionFailureClassifier
{
    internal static TwitchRuntimeSessionFailureClassification Classify(
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        return exception switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested =>
                TwitchRuntimeSessionFailureClassification.Cancellation,
            TimeoutRejectedException or TimeoutException =>
                TwitchRuntimeSessionFailureClassification.Timeout,
            HttpRequestException http => ClassifyHttp(http),
            SocketException or WebSocketException =>
                TwitchRuntimeSessionFailureClassification.Transient,
            InvalidDataException => TwitchRuntimeSessionFailureClassification.Terminal,
            IOException => TwitchRuntimeSessionFailureClassification.Transient,
            AccessTokenUnavailableException
            or AuthenticationException
            or InvalidOperationException
            or JsonException => TwitchRuntimeSessionFailureClassification.Terminal,
            _ => TwitchRuntimeSessionFailureClassification.Unexpected,
        };
    }

    private static TwitchRuntimeSessionFailureClassification ClassifyHttp(
        HttpRequestException exception
    )
    {
        return TwitchRuntimeSessionFailureClassifier.IsTransientHttpStatus(exception.StatusCode)
            ? TwitchRuntimeSessionFailureClassification.Transient
            : TwitchRuntimeSessionFailureClassification.Terminal;
    }
}

internal static class TwitchRuntimeSessionFailureClassifier
{
    internal static bool IsTransientHttpStatus(HttpStatusCode? statusCode)
    {
        return statusCode is null
            || statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    internal static bool IsRetryable(TwitchRuntimeSessionFailureClassification classification)
    {
        return classification
            is TwitchRuntimeSessionFailureClassification.Transient
                or TwitchRuntimeSessionFailureClassification.Timeout;
    }
}
