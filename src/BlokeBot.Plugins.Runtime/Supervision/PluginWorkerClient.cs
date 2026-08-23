using System.Diagnostics;
using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginWorkerClient : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly PluginWorkerProcessConnection _connection;
    private readonly IPluginHostCallDispatcher _hostCalls;
    private readonly ILogger<PluginWorkerClient> _logger;
    private readonly SemaphoreSlim _invocationGate = new(
        PluginWorkerLimits.MaximumConcurrentInvocations
    );
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _reader;
    private PluginWorkerPendingInvocation? _pending;

    private PluginWorkerClient(
        PluginWorkerProcessConnection connection,
        IPluginHostCallDispatcher hostCalls,
        ILogger<PluginWorkerClient> logger
    )
    {
        _connection = connection;
        _hostCalls = hostCalls;
        _logger = logger;
        _reader = ReadMessagesAsync(_lifetime.Token);
    }

    public static async ValueTask<PluginWorkerStartOutcome> StartAsync(
        PluginWorkerStartOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var executable = options.Executable ?? ResolveExecutable();
        if (executable is null)
        {
            return new PluginWorkerStartOutcome.Failed(
                new(PluginWorkerFailureCode.WorkerExited, "Plugin worker executable was not found.")
            );
        }

        var connection = await PluginWorkerProcessConnection.StartAsync(
            executable,
            options.Package,
            options.Mode,
            Path.GetFullPath(options.StateRoot),
            cancellationToken
        );
        return connection switch
        {
            PluginWorkerConnectionStartOutcome.Connected connected =>
                new PluginWorkerStartOutcome.Started(
                    new(connected.Connection, options.HostCalls, options.Logger)
                ),
            PluginWorkerConnectionStartOutcome.Rejected rejected =>
                new PluginWorkerStartOutcome.Rejected(rejected.Failure),
            PluginWorkerConnectionStartOutcome.Failed failed => new PluginWorkerStartOutcome.Failed(
                failed.Failure
            ),
            _ => throw new UnreachableException("Unknown plugin worker start outcome."),
        };
    }

    public ValueTask<PluginWorkerInvocationResult> PrepareAsync(
        PluginWorkerInvocationIdentity identity,
        PluginPreparationInvocation invocation,
        CancellationToken cancellationToken
    ) =>
        InvokeCoreAsync(
            identity,
            new PluginWorkerMessage.Prepare(identity, invocation),
            cancellationToken
        );

    public ValueTask<PluginWorkerInvocationResult> InvokeAsync(
        PluginWorkerInvocationIdentity identity,
        PluginLiveInvocation invocation,
        CancellationToken cancellationToken
    ) =>
        InvokeCoreAsync(
            identity,
            new PluginWorkerMessage.Invoke(identity, invocation),
            cancellationToken
        );

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _connection.DisposeAsync();
        try
        {
            await _reader;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }

        lock (_sync)
        {
            _pending?.Dispose();
            _pending = null;
        }

        _invocationGate.Dispose();
        _lifetime.Dispose();
    }

    private async ValueTask<PluginWorkerInvocationResult> InvokeCoreAsync(
        PluginWorkerInvocationIdentity identity,
        PluginWorkerMessage request,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(PluginCancellationReason.CallerRequested);
        }

        if (identity.Deadline.ToDateTimeOffset() <= DateTimeOffset.UtcNow)
        {
            return Cancelled(PluginCancellationReason.DeadlineExceeded);
        }

        if (!_invocationGate.Wait(0))
        {
            return Failed(
                PluginWorkerFailureCode.InvocationLimitExceeded,
                "The worker invocation limit is reached."
            );
        }

        var pending = new PluginWorkerPendingInvocation(identity);
        lock (_sync)
        {
            _pending = pending;
        }

        try
        {
            var write = await _connection.WriteAsync(request, _lifetime.Token);
            return write is PluginFrameWriteOutcome.Rejected rejected
                ? new(
                    new PluginWorkerInvocationOutcome.Failed(rejected.Failure),
                    PluginWorkerInvocationMetrics.Empty,
                    []
                )
                : await AwaitInvocationAsync(pending, cancellationToken);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pending, pending))
                {
                    _pending = null;
                }
            }

            pending.Dispose();
            _ = _invocationGate.Release();
        }
    }

    private async ValueTask<PluginWorkerInvocationResult> AwaitInvocationAsync(
        PluginWorkerPendingInvocation pending,
        CancellationToken cancellationToken
    )
    {
        var remaining = pending.Identity.Deadline.ToDateTimeOffset() - DateTimeOffset.UtcNow;
        var duration = TimeSpan.FromMilliseconds(
            PluginWorkerLimits.MaximumInvocationDurationMilliseconds
        );
        var timeout =
            remaining <= TimeSpan.Zero ? TimeSpan.Zero
            : remaining < duration ? remaining
            : duration;
        using var deadline = new CancellationTokenSource(timeout);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadline.Token,
            _lifetime.Token
        );
        var cancellation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var registration = combined.Token.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellation
        );
        if (
            await Task.WhenAny(pending.Completion.Task, cancellation.Task)
            == pending.Completion.Task
        )
        {
            return await pending.Completion.Task;
        }

        var reason =
            cancellationToken.IsCancellationRequested ? PluginCancellationReason.CallerRequested
            : _lifetime.IsCancellationRequested ? PluginCancellationReason.WorkerStopping
            : PluginCancellationReason.DeadlineExceeded;
        if (!pending.TryBeginCancellation(reason))
        {
            return await pending.Completion.Task;
        }

        _ = await _connection.WriteAsync(
            new PluginWorkerMessage.Cancel(pending.Identity, reason),
            _lifetime.Token
        );
        var grace = Task.Delay(PluginWorkerLimits.CancellationGraceMilliseconds);
        if (await Task.WhenAny(pending.Completion.Task, grace) == pending.Completion.Task)
        {
            return await pending.Completion.Task;
        }

        _connection.Terminate(
            new(
                PluginWorkerFailureCode.WorkerTerminated,
                "Plugin worker did not stop cancelled work within the grace period."
            )
        );
        return new(
            new PluginWorkerInvocationOutcome.Cancelled(reason, WorkerTerminated: true),
            PluginWorkerInvocationMetrics.Empty,
            pending.Diagnostics()
        );
    }

    private static PluginWorkerExecutable? ResolveExecutable() =>
        PluginWorkerDiscovery.Discover() is PluginWorkerDiscoveryOutcome.Found found
            ? found.Executable
            : null;

    private static PluginWorkerInvocationResult Failed(
        PluginWorkerFailureCode code,
        string message
    ) =>
        new(
            new PluginWorkerInvocationOutcome.Failed(new(code, message)),
            PluginWorkerInvocationMetrics.Empty,
            []
        );

    private static PluginWorkerInvocationResult Cancelled(PluginCancellationReason reason) =>
        new(
            new PluginWorkerInvocationOutcome.Cancelled(reason, WorkerTerminated: false),
            PluginWorkerInvocationMetrics.Empty,
            []
        );
}
