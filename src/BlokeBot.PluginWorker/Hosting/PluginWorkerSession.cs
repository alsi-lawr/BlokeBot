using System.Collections.Concurrent;
using System.Threading.Channels;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.PluginWorker;

internal sealed partial class PluginWorkerSession(
    Stream transport,
    PluginWorkerLaunchArguments arguments,
    PluginRuntimeIdentifier runtimeIdentifier
) : IAsyncDisposable
{
    private readonly PluginWorkerProtocolCodec _codec = new();
    private readonly PluginInvocationCancellationRegistry _cancellations = new();
    private readonly ConcurrentDictionary<
        PluginWorkerInvocationId,
        PluginWorkerInvocationIdentity
    > _forwardedCancellations = new();
    private KeraLuaPluginEngine? _engine;
    private PluginWorkerMessage.HostHandshake? _handshake;
    private PluginWorkerInvocationIdentity? _activeIdentity;

    internal async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (
            !await WriteAsync(
                new PluginWorkerMessage.WorkerHello(
                    PluginWorkerCompatibilityDescriptor.Current,
                    KeraLuaPluginEngine.Descriptor,
                    runtimeIdentifier
                ),
                cancellationToken
            )
        )
        {
            return 2;
        }

        using var handshakeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        handshakeCancellation.CancelAfter(PluginWorkerLimits.HandshakeTimeoutMilliseconds);
        var handshakeFrame = await _codec.ReadAsync(transport, handshakeCancellation.Token);
        if (
            handshakeFrame
            is not PluginFrameReadOutcome.Message
            {
                Value: PluginWorkerMessage.HostHandshake handshake,
            }
        )
        {
            return 3;
        }

        var handshakeFailure = PluginWorkerHandshakeValidator.Validate(
            handshake,
            runtimeIdentifier,
            arguments
        );
        if (handshakeFailure is not null)
        {
            _ = await WriteAsync(
                new PluginWorkerMessage.HandshakeRejected(handshakeFailure),
                cancellationToken
            );
            return 4;
        }

        try
        {
            _engine = new(handshake.Package, arguments.PackageRoot, _cancellations);
        }
        catch (DllNotFoundException)
        {
            return await RejectEngineAsync(cancellationToken);
        }
        catch (EntryPointNotFoundException)
        {
            return await RejectEngineAsync(cancellationToken);
        }
        catch (BadImageFormatException)
        {
            return await RejectEngineAsync(cancellationToken);
        }

        _handshake = handshake;
        return await WriteAsync(
            new PluginWorkerMessage.HandshakeAccepted(handshake.Mode, handshake.Package),
            cancellationToken
        )
            ? await RunMessagesAsync(cancellationToken)
            : 5;
    }

    public ValueTask DisposeAsync()
    {
        _engine?.Dispose();
        _cancellations.Dispose();
        return transport.DisposeAsync();
    }

    private async Task<int> RunMessagesAsync(CancellationToken cancellationToken)
    {
        using var messageLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        var channel = Channel.CreateBounded<PluginFrameReadOutcome>(
            new BoundedChannelOptions(PluginWorkerLimits.MaximumQueuedMessages)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }
        );
        void ForwardCancellation(
            PluginWorkerInvocationIdentity identity,
            PluginCancellationReason reason
        )
        {
            _forwardedCancellations[identity.InvocationId] = identity;
            if (
                !channel.Writer.TryWrite(
                    new PluginFrameReadOutcome.Message(
                        new PluginWorkerMessage.Cancel(identity, reason)
                    )
                )
            )
            {
                _ = _forwardedCancellations.TryRemove(identity.InvocationId, out _);
            }
        }

        _cancellations.CancellationRequested += ForwardCancellation;
        var reader = ReadMessagesAsync(channel.Writer, messageLifetime.Token);
        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken))
            {
                switch (frame)
                {
                    case PluginFrameReadOutcome.Message message:
                        if (!await HandleAsync(message.Value, cancellationToken))
                        {
                            return 0;
                        }

                        break;
                    case PluginFrameReadOutcome.Rejected rejected:
                        _ = await WriteAsync(
                            new PluginWorkerMessage.ProtocolRejected(rejected.Failure),
                            cancellationToken
                        );
                        return 6;
                    case PluginFrameReadOutcome.EndOfStream:
                        return 0;
                }
            }
        }
        finally
        {
            _cancellations.CancellationRequested -= ForwardCancellation;
            messageLifetime.Cancel();
            await reader;
        }

        return 0;
    }

    private async Task ReadMessagesAsync(
        ChannelWriter<PluginFrameReadOutcome> writer,
        CancellationToken cancellationToken
    )
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await _codec.ReadAsync(transport, cancellationToken);
                if (
                    frame
                        is PluginFrameReadOutcome.Message
                        {
                            Value: PluginWorkerMessage.Cancel cancel,
                        }
                    && _cancellations.Cancel(cancel.Identity, cancel.Reason)
                )
                {
                    continue;
                }

                await writer.WriteAsync(frame, cancellationToken);
                if (frame is not PluginFrameReadOutcome.Message)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            _ = writer.TryComplete();
        }
    }

    private async ValueTask<bool> HandleAsync(
        PluginWorkerMessage message,
        CancellationToken cancellationToken
    ) =>
        message switch
        {
            PluginWorkerMessage.Prepare prepare => await StartAsync(
                prepare.Identity,
                prepare.Invocation.Module,
                prepare.Invocation.Operation,
                prepare.Invocation.Input,
                live: false,
                cancellationToken
            ),
            PluginWorkerMessage.Invoke invoke => await StartAsync(
                invoke.Identity,
                invoke.Invocation.Module,
                invoke.Invocation.Operation,
                invoke.Invocation.Input,
                live: true,
                cancellationToken
            ),
            PluginWorkerMessage.HostCallCompleted completed => await CompleteHostCallAsync(
                completed,
                cancellationToken
            ),
            PluginWorkerMessage.Cancel cancel => await CancelAsync(cancel, cancellationToken),
            PluginWorkerMessage.Stop => await StopAsync(cancellationToken),
            _ => await RejectProtocolAsync(cancellationToken),
        };

    private async Task<int> RejectEngineAsync(CancellationToken cancellationToken)
    {
        _ = await WriteAsync(
            new PluginWorkerMessage.HandshakeRejected(
                new(PluginWorkerHandshakeFailureCode.EngineMismatch, "KeraLua 1.4.9")
            ),
            cancellationToken
        );
        return 4;
    }

    private async ValueTask<bool> WriteAsync(
        PluginWorkerMessage message,
        CancellationToken cancellationToken
    ) =>
        await _codec.WriteAsync(transport, message, cancellationToken)
        is PluginFrameWriteOutcome.Written;
}
