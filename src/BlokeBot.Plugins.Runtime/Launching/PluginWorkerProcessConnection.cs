using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

internal abstract record PluginWorkerConnectionStartOutcome
{
    private PluginWorkerConnectionStartOutcome() { }

    internal sealed record Connected(PluginWorkerProcessConnection Connection)
        : PluginWorkerConnectionStartOutcome;

    internal sealed record Rejected(PluginWorkerHandshakeFailure Failure)
        : PluginWorkerConnectionStartOutcome;

    internal sealed record Failed(PluginWorkerFailure Failure) : PluginWorkerConnectionStartOutcome;
}

internal sealed partial class PluginWorkerProcessConnection : IAsyncDisposable
{
    private readonly Process _process;
    private readonly NamedPipeServerStream _transport;
    private readonly PluginWorkerProtocolCodec _codec = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<PluginWorkerFailure> _terminal = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int _processOutputBytes;

    private PluginWorkerProcessConnection(Process process, NamedPipeServerStream transport)
    {
        _process = process;
        _transport = transport;
        _ = MonitorOutputAsync(process.StandardOutput, _lifetime.Token);
        _ = MonitorOutputAsync(process.StandardError, _lifetime.Token);
        _ = MonitorExitAsync(_lifetime.Token);
    }

    internal Task<PluginWorkerFailure> Terminal => _terminal.Task;

    internal static async ValueTask<PluginWorkerConnectionStartOutcome> StartAsync(
        PluginWorkerExecutable executable,
        PreparedPluginWorkerPackage package,
        PluginWorkerMode mode,
        string stateRoot,
        CancellationToken cancellationToken
    )
    {
        var pipeName = $"blokebot-plugin-{Guid.NewGuid():N}";
        var transport = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
        );
        Process? process = null;
        try
        {
            _ = Directory.CreateDirectory(stateRoot);
            process = StartProcess(executable, pipeName, package.PackageRoot, stateRoot);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            handshake.CancelAfter(PluginWorkerLimits.HandshakeTimeoutMilliseconds);
            await transport.WaitForConnectionAsync(handshake.Token);
            var connection = new PluginWorkerProcessConnection(process, transport);
            var outcome = await connection.HandshakeAsync(
                package.Descriptor,
                mode,
                handshake.Token
            );
            if (outcome is PluginWorkerConnectionStartOutcome.Connected)
            {
                return outcome;
            }

            await connection.DisposeAsync();
            return outcome;
        }
        catch (Win32Exception)
        {
            process?.Dispose();
            await transport.DisposeAsync();
            return Failed(PluginWorkerFailureCode.WorkerExited, "Plugin worker could not start.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }

            process?.Dispose();
            await transport.DisposeAsync();
            return Failed(
                PluginWorkerFailureCode.WorkerExited,
                "Plugin worker handshake timed out."
            );
        }
        catch (OperationCanceledException)
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }

            process?.Dispose();
            await transport.DisposeAsync();
            throw;
        }
    }

    internal async ValueTask<PluginFrameReadOutcome> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _codec.ReadAsync(_transport, cancellationToken);
        }
        catch (IOException)
        {
            return TransportReadFailed();
        }
    }

    internal async ValueTask<PluginFrameWriteOutcome> WriteAsync(
        PluginWorkerMessage message,
        CancellationToken cancellationToken
    )
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                return await _codec.WriteAsync(_transport, message, cancellationToken);
            }
            catch (IOException)
            {
                return TransportWriteFailed();
            }
        }
        finally
        {
            _ = _writeGate.Release();
        }
    }

    internal void Terminate(PluginWorkerFailure failure)
    {
        _ = _terminal.TrySetResult(failure);
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (!_process.HasExited)
        {
            try
            {
                _ = await WriteAsync(new PluginWorkerMessage.Stop(), CancellationToken.None);
                using var stop = new CancellationTokenSource(
                    PluginWorkerLimits.CancellationGraceMilliseconds
                );
                await _process.WaitForExitAsync(stop.Token);
            }
            catch (OperationCanceledException)
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (IOException)
            {
                _process.Kill(entireProcessTree: true);
            }
        }

        await _transport.DisposeAsync();
        _process.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    private static Process StartProcess(
        PluginWorkerExecutable executable,
        string pipeName,
        string packageRoot,
        string stateRoot
    )
    {
        var start = new ProcessStartInfo
        {
            FileName = executable.IsManagedAssembly
                ? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"
                : executable.Path,
            WorkingDirectory = stateRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (executable.IsManagedAssembly)
        {
            start.ArgumentList.Add(executable.Path);
        }

        start.ArgumentList.Add("--pipe");
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--package");
        start.ArgumentList.Add(packageRoot);
        start.ArgumentList.Add("--state");
        start.ArgumentList.Add(stateRoot);
        return Process.Start(start)
            ?? throw new Win32Exception("Plugin worker process did not start.");
    }

    private static PluginWorkerConnectionStartOutcome.Failed Failed(
        PluginWorkerFailureCode code,
        string message
    ) => new(new(code, message));

    private static PluginFrameReadOutcome.Rejected TransportReadFailed() =>
        new(
            new(
                PluginWorkerFailureCode.WorkerExited,
                "Plugin worker transport disconnected while reading."
            )
        );

    private static PluginFrameWriteOutcome.Rejected TransportWriteFailed() =>
        new(
            new(
                PluginWorkerFailureCode.WorkerExited,
                "Plugin worker transport disconnected while writing."
            )
        );
}
