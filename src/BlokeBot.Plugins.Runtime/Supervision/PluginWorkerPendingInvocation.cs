using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

internal sealed class PluginWorkerPendingInvocation(PluginWorkerInvocationIdentity identity)
    : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _hostCallCancellation = new();
    private readonly List<PluginWorkerDiagnostic> _diagnostics = [];
    private PluginCancellationReason? _cancellationReason;
    private int _diagnosticBytes;
    private bool _hostCallActive;

    internal PluginWorkerInvocationIdentity Identity { get; } = identity;

    internal TaskCompletionSource<PluginWorkerInvocationResult> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool TryBeginCancellation(PluginCancellationReason reason)
    {
        lock (_sync)
        {
            if (Completion.Task.IsCompleted || _cancellationReason.HasValue)
            {
                return false;
            }

            _cancellationReason = reason;
            _hostCallCancellation.Cancel();
            return true;
        }
    }

    internal bool TryGetCancellationReason(out PluginCancellationReason reason)
    {
        lock (_sync)
        {
            if (_cancellationReason.HasValue)
            {
                reason = _cancellationReason.Value;
                return true;
            }
        }

        reason = default;
        return false;
    }

    internal bool TryBeginHostCall(
        PluginWorkerCancellationId cancellationId,
        PluginHostCall call,
        out CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            var admitted =
                !Completion.Task.IsCompleted
                && !_cancellationReason.HasValue
                && !_hostCallActive
                && cancellationId == Identity.CancellationId
                && call.CoroutineId == Identity.CoroutineId
                && call.Context == Identity.Context;
            _hostCallActive = admitted;
            cancellationToken = _hostCallCancellation.Token;
            return admitted;
        }
    }

    internal bool CompleteHostCall()
    {
        lock (_sync)
        {
            _hostCallActive = false;
            return !Completion.Task.IsCompleted && !_cancellationReason.HasValue;
        }
    }

    internal bool AddDiagnostics(
        IReadOnlyList<PluginWorkerDiagnostic> diagnostics,
        out PluginWorkerFailure failure
    )
    {
        lock (_sync)
        {
            foreach (var diagnostic in diagnostics)
            {
                var bytes = System.Text.Encoding.UTF8.GetByteCount(diagnostic.Message);
                if (
                    bytes > PluginWorkerLimits.MaximumDiagnosticLineBytes
                    || _diagnostics.Count >= PluginWorkerLimits.MaximumDiagnosticsPerInvocation
                    || _diagnosticBytes + bytes > PluginWorkerLimits.MaximumDiagnosticBytes
                )
                {
                    failure = new(
                        PluginWorkerFailureCode.DiagnosticLimitExceeded,
                        "Plugin worker diagnostics exceeded their bound."
                    );
                    return false;
                }

                _diagnostics.Add(diagnostic);
                _diagnosticBytes += bytes;
            }
        }

        failure = null!;
        return true;
    }

    internal IReadOnlyList<PluginWorkerDiagnostic> Diagnostics()
    {
        lock (_sync)
        {
            return _diagnostics.ToArray();
        }
    }

    public void Dispose() => _hostCallCancellation.Dispose();
}
