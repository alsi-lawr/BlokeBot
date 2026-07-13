using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Polly.Timeout;

namespace BlokeBot.Twitch.Runtime;

internal static class PublicChatDeliveryClassifier
{
    internal static PublicChatPreparationOutcome ClassifyPreparationFailure(
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        PropagateCallerCancellation(exception, cancellationToken);
        var diagnostic = PreparationDiagnostic(exception);
        return IsSafePreSendTransient(exception)
            ? new PublicChatPreparationOutcome.SafePreSendTransient { Diagnostic = diagnostic }
            : new PublicChatPreparationOutcome.Unexpected
            {
                Diagnostic = diagnostic,
                Cause = exception,
            };
    }

    internal static PublicChatTransportSendResult ClassifySendResult(ChatMessageSendResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSent)
        {
            return new PublicChatTransportSendResult.Sent();
        }

        var code = result.DropReason?.Code;
        return new PublicChatTransportSendResult.Rejected
        {
            Reason = string.IsNullOrWhiteSpace(code)
                ? new PublicChatRejectionReason.Unspecified()
                : new PublicChatRejectionReason.ProviderCode(
                    new PublicChatProviderRejectionCode(code)
                ),
        };
    }

    internal static PublicChatDeliveryOutcome ClassifyPostBoundaryFailure(
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        PropagateCallerCancellation(exception, cancellationToken);
        return new PublicChatDeliveryOutcome.Ambiguous { Diagnostic = SendDiagnostic(exception) };
    }

    internal static PublicChatFailureDiagnostic.Send PostBoundaryInterruption(
        OperationCanceledException exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        return SendDiagnostic(exception);
    }

    internal static PublicChatDeliveryOutcome MapPreparationFailure(
        PublicChatPreparationOutcome outcome
    )
    {
        return outcome.Match<PublicChatDeliveryOutcome>(
            _ =>
                throw new InvalidOperationException(
                    "A ready public chat preparation is not a delivery failure."
                ),
            transient => new PublicChatDeliveryOutcome.SafePreSendTransient
            {
                Diagnostic = transient.Diagnostic,
            },
            unexpected => new PublicChatDeliveryOutcome.Unexpected
            {
                Diagnostic = unexpected.Diagnostic,
                Cause = unexpected.Cause,
            }
        );
    }

    internal static PublicChatDeliveryOutcome MapSendResult(PublicChatTransportSendResult result)
    {
        return result.Match<PublicChatDeliveryOutcome>(
            _ => new PublicChatDeliveryOutcome.Sent(),
            rejected => new PublicChatDeliveryOutcome.Rejection { Reason = rejected.Reason }
        );
    }

    private static bool IsSafePreSendTransient(Exception exception)
    {
        return exception switch
        {
            TimeoutRejectedException or TimeoutException or OperationCanceledException => true,
            HttpRequestException http => IsTransientHttpStatus(http.StatusCode),
            SocketException or IOException => true,
            _ => false,
        };
    }

    private static bool IsTransientHttpStatus(HttpStatusCode? statusCode)
    {
        return statusCode is null
            || statusCode is HttpStatusCode.RequestTimeout
            || (int)statusCode == 429
            || (int)statusCode >= 500;
    }

    private static PublicChatFailureDiagnostic.Preparation PreparationDiagnostic(
        Exception exception
    )
    {
        return new()
        {
            FailureType = PublicChatFailureType.From(exception),
            HttpStatus = exception is HttpRequestException { StatusCode: { } status }
                ? new PublicChatHttpStatus.Known(PublicChatHttpStatusCode.From(status))
                : new PublicChatHttpStatus.Unavailable(),
        };
    }

    private static PublicChatFailureDiagnostic.Send SendDiagnostic(Exception exception)
    {
        return new()
        {
            FailureType = PublicChatFailureType.From(exception),
            HttpStatus = exception is HttpRequestException { StatusCode: { } status }
                ? new PublicChatHttpStatus.Known(PublicChatHttpStatusCode.From(status))
                : new PublicChatHttpStatus.Unavailable(),
        };
    }

    private static void PropagateCallerCancellation(
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        if (
            exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested
        )
        {
            return;
        }

        ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
