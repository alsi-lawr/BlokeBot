using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

internal sealed partial class PluginWorkerProcessConnection
{
    private async ValueTask<PluginWorkerConnectionStartOutcome> HandshakeAsync(
        PluginWorkerPackageDescriptor package,
        PluginWorkerMode mode,
        CancellationToken cancellationToken
    )
    {
        var helloFrame = await ReadAsync(cancellationToken);
        if (
            helloFrame
            is not PluginFrameReadOutcome.Message { Value: PluginWorkerMessage.WorkerHello hello }
        )
        {
            return HandshakeReadFailed(helloFrame, "Worker hello is unavailable.");
        }

        var failure = ValidateHello(hello, package.RuntimeIdentifier);
        if (failure is not null)
        {
            return new PluginWorkerConnectionStartOutcome.Rejected(failure);
        }

        var write = await WriteAsync(
            new PluginWorkerMessage.HostHandshake(
                PluginWorkerCompatibilityDescriptor.Current,
                PluginWorkerEngineContract.Selected,
                mode,
                package
            ),
            cancellationToken
        );
        if (write is PluginFrameWriteOutcome.Rejected writeRejected)
        {
            return new PluginWorkerConnectionStartOutcome.Failed(writeRejected.Failure);
        }

        var admissionFrame = await ReadAsync(cancellationToken);
        return admissionFrame switch
        {
            PluginFrameReadOutcome.Message { Value: PluginWorkerMessage.HandshakeAccepted accepted }
                when accepted.Mode == mode
                    && PluginWorkerPackageCompatibility.Matches(accepted.Package, package) =>
                new PluginWorkerConnectionStartOutcome.Connected(this),
            PluginFrameReadOutcome.Message
            {
                Value: PluginWorkerMessage.HandshakeRejected rejected,
            } => new PluginWorkerConnectionStartOutcome.Rejected(rejected.Failure),
            _ => HandshakeReadFailed(admissionFrame, "Worker rejected admission."),
        };
    }

    private static PluginWorkerConnectionStartOutcome.Failed HandshakeReadFailed(
        PluginFrameReadOutcome frame,
        string protocolMessage
    ) =>
        frame switch
        {
            PluginFrameReadOutcome.Rejected rejected => new(rejected.Failure),
            PluginFrameReadOutcome.EndOfStream => Failed(
                PluginWorkerFailureCode.WorkerExited,
                "Plugin worker transport closed during handshake."
            ),
            _ => Failed(PluginWorkerFailureCode.ProtocolViolation, protocolMessage),
        };

    private static PluginWorkerHandshakeFailure? ValidateHello(
        PluginWorkerMessage.WorkerHello hello,
        PluginRuntimeIdentifier expectedTarget
    )
    {
        var compatibilityFailure = PluginWorkerCompatibility.Compare(hello.Compatibility);
        return compatibilityFailure is not null ? compatibilityFailure
            : hello.Engine != PluginWorkerEngineContract.Selected
            || PluginCompatibilityEvaluator.AdmitEngine(hello.Engine)
                is PluginEngineAdmissionOutcome.Rejected
                ? new(PluginWorkerHandshakeFailureCode.EngineMismatch, hello.Engine.Engine.Value)
            : hello.RuntimeIdentifier != expectedTarget
                ? new(
                    PluginWorkerHandshakeFailureCode.TargetMismatch,
                    hello.RuntimeIdentifier.ToString()
                )
            : null;
    }
}
