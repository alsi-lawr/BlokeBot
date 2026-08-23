using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginWorkerClient
{
    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = _connection.ReadAsync(cancellationToken).AsTask();
            var completed = await Task.WhenAny(read, _connection.Terminal);
            if (completed == _connection.Terminal)
            {
                CompletePendingFailure(await _connection.Terminal);
                return;
            }

            var frame = await read;
            if (frame is not PluginFrameReadOutcome.Message message)
            {
                var failure = frame is PluginFrameReadOutcome.Rejected rejected
                    ? rejected.Failure
                    : new(PluginWorkerFailureCode.WorkerExited, "Plugin worker transport closed.");
                CompletePendingFailure(failure);
                return;
            }

            await HandleMessageAsync(message.Value, cancellationToken);
        }
    }

    private ValueTask HandleMessageAsync(
        PluginWorkerMessage message,
        CancellationToken cancellationToken
    )
    {
        var pending = Pending();
        switch (message)
        {
            case PluginWorkerMessage.InvocationCompleted completed
                when pending?.Identity.InvocationId == completed.InvocationId:
                CompleteInvocation(pending, completed.Outcome, completed.Metrics);
                break;
            case PluginWorkerMessage.InvocationCancelled cancelled
                when pending?.Identity.InvocationId == cancelled.InvocationId:
                CompleteInvocation(
                    pending,
                    new PluginWorkerInvocationOutcome.Cancelled(
                        cancelled.Reason,
                        WorkerTerminated: false
                    ),
                    cancelled.Metrics
                );
                break;
            case PluginWorkerMessage.InvocationRejected rejected
                when pending?.Identity.InvocationId == rejected.InvocationId:
                CompleteInvocation(
                    pending,
                    new PluginWorkerInvocationOutcome.Failed(rejected.Failure),
                    PluginWorkerInvocationMetrics.Empty
                );
                break;
            case PluginWorkerMessage.HostCallRequested hostCall
                when pending?.Identity.InvocationId == hostCall.InvocationId:
                if (
                    !pending.TryBeginHostCall(
                        hostCall.CancellationId,
                        hostCall.Call,
                        out var hostToken
                    )
                )
                {
                    RejectProtocolMessage();
                    break;
                }

                _ = DispatchHostCallAsync(pending, hostCall, hostToken, cancellationToken);
                break;
            case PluginWorkerMessage.Diagnostics diagnostics
                when pending?.Identity.InvocationId == diagnostics.InvocationId:
                if (!pending.AddDiagnostics(diagnostics.Items, out var failure))
                {
                    _connection.Terminate(failure);
                    CompletePendingFailure(failure);
                }

                break;
            case PluginWorkerMessage.ProtocolRejected rejected:
                _connection.Terminate(rejected.Failure);
                CompletePendingProtocolFailure(rejected.Failure);
                break;
            default:
                RejectProtocolMessage();
                break;
        }

        return ValueTask.CompletedTask;
    }

    private async Task DispatchHostCallAsync(
        PluginWorkerPendingInvocation pending,
        PluginWorkerMessage.HostCallRequested requested,
        CancellationToken hostToken,
        CancellationToken cancellationToken
    )
    {
        PluginHostCallOutcome outcome;
        try
        {
            outcome = await _hostCalls.DispatchAsync(requested.Call, hostToken);
        }
        catch (OperationCanceledException) when (hostToken.IsCancellationRequested)
        {
            _ = pending.CompleteHostCall();
            return;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Plugin host call dispatcher failed with {ExceptionType}.",
                exception.GetType().Name
            );
            outcome = new PluginHostCallOutcome.Failed(
                new(PluginHostFailureCode.Unavailable, "Host call failed.")
            );
        }

        if (!pending.CompleteHostCall())
        {
            return;
        }

        _ = await _connection.WriteAsync(
            new PluginWorkerMessage.HostCallCompleted(
                requested.InvocationId,
                requested.CancellationId,
                new(requested.Call.CallId, requested.Call.CoroutineId, outcome)
            ),
            cancellationToken
        );
    }

    private void CompleteInvocation(
        PluginWorkerPendingInvocation pending,
        PluginWorkerInvocationOutcome outcome,
        PluginWorkerInvocationMetrics metrics
    )
    {
        var admittedOutcome = pending.TryGetCancellationReason(out var cancellationReason)
            ? new PluginWorkerInvocationOutcome.Cancelled(
                cancellationReason,
                WorkerTerminated: false
            )
            : outcome;
        _ = pending.Completion.TrySetResult(new(admittedOutcome, metrics, pending.Diagnostics()));
    }

    private PluginWorkerPendingInvocation? Pending()
    {
        lock (_sync)
        {
            return _pending;
        }
    }

    private void CompletePendingFailure(PluginWorkerFailure failure)
    {
        var pending = Pending();
        if (pending is null)
        {
            return;
        }

        if (pending.TryGetCancellationReason(out var cancellationReason))
        {
            _ = pending.Completion.TrySetResult(
                new(
                    new PluginWorkerInvocationOutcome.Cancelled(
                        cancellationReason,
                        WorkerTerminated: true
                    ),
                    PluginWorkerInvocationMetrics.Empty,
                    pending.Diagnostics()
                )
            );
            return;
        }

        _ = pending.Completion.TrySetResult(
            new(
                new PluginWorkerInvocationOutcome.Failed(failure),
                PluginWorkerInvocationMetrics.Empty,
                pending.Diagnostics()
            )
        );
    }

    private void RejectProtocolMessage()
    {
        var failure = new PluginWorkerFailure(
            PluginWorkerFailureCode.ProtocolViolation,
            "Plugin worker sent an unexpected or mismatched message."
        );
        _connection.Terminate(failure);
        CompletePendingProtocolFailure(failure);
    }

    private void CompletePendingProtocolFailure(PluginWorkerFailure failure)
    {
        var pending = Pending();
        if (pending is null)
        {
            return;
        }

        _ = pending.Completion.TrySetResult(
            new(
                new PluginWorkerInvocationOutcome.Failed(failure),
                PluginWorkerInvocationMetrics.Empty,
                pending.Diagnostics()
            )
        );
    }
}
