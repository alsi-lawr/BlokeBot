using System.Text;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

internal sealed partial class PluginWorkerProcessConnection
{
    private async Task MonitorOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    return;
                }

                var bytes = Encoding.UTF8.GetByteCount(buffer.AsSpan(0, read));
                if (
                    Interlocked.Add(ref _processOutputBytes, bytes)
                    > PluginWorkerLimits.MaximumProcessOutputBytes
                )
                {
                    Terminate(
                        new(
                            PluginWorkerFailureCode.OutputLimitExceeded,
                            "Plugin worker process output exceeded its bound."
                        )
                    );
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task MonitorExitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _process.WaitForExitAsync(cancellationToken);
            _ = _terminal.TrySetResult(
                new(PluginWorkerFailureCode.WorkerExited, "Plugin worker exited.")
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
