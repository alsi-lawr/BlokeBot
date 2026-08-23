using System.Diagnostics;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.PluginWorker;

internal sealed partial class PluginWorkerSession
{
    private async ValueTask<bool> StartAsync(
        PluginWorkerInvocationIdentity identity,
        PluginLuaModuleId module,
        PluginHostOperationId operation,
        PluginValue input,
        bool live,
        CancellationToken cancellationToken
    )
    {
        if (_activeIdentity is not null)
        {
            return await RejectInvocationAsync(
                identity.InvocationId,
                PluginWorkerFailureCode.InvocationLimitExceeded,
                "The worker invocation limit is reached.",
                cancellationToken
            );
        }

        if (live && _handshake!.Mode == PluginWorkerMode.Staging)
        {
            return await RejectInvocationAsync(
                identity.InvocationId,
                PluginWorkerFailureCode.StagingLiveWorkRejected,
                "A staging worker cannot receive live work.",
                cancellationToken
            );
        }

        if (!IdentityMatchesPackage(identity))
        {
            return await RejectInvocationAsync(
                identity.InvocationId,
                PluginWorkerFailureCode.ProtocolViolation,
                "Invocation identity does not match the admitted package.",
                cancellationToken
            );
        }

        var admission = _cancellations.Begin(identity);
        if (admission == PluginInvocationCancellationAdmission.DeadlineExceeded)
        {
            return await RejectInvocationAsync(
                identity.InvocationId,
                PluginWorkerFailureCode.DeadlineExceeded,
                "The worker invocation deadline has expired.",
                cancellationToken
            );
        }

        if (admission != PluginInvocationCancellationAdmission.Admitted)
        {
            return await RejectInvocationAsync(
                identity.InvocationId,
                PluginWorkerFailureCode.ProtocolViolation,
                "Invocation identity does not match the admitted package.",
                cancellationToken
            );
        }

        _activeIdentity = identity;
        return await HandleStepAsync(
            _engine!.Start(identity, module, operation, input),
            cancellationToken
        );
    }

    private async ValueTask<bool> CompleteHostCallAsync(
        PluginWorkerMessage.HostCallCompleted completed,
        CancellationToken cancellationToken
    ) =>
        _activeIdentity is null
        || completed.InvocationId != _activeIdentity.InvocationId
        || completed.CancellationId != _activeIdentity.CancellationId
        || !_engine!.MatchesHostCallCompletion(completed.Completion)
            ? await RejectProtocolAsync(cancellationToken)
            : await HandleStepAsync(_engine.Resume(completed.Completion), cancellationToken);

    private async ValueTask<bool> CancelAsync(
        PluginWorkerMessage.Cancel cancel,
        CancellationToken cancellationToken
    )
    {
        var forwarded =
            _forwardedCancellations.TryRemove(
                cancel.Identity.InvocationId,
                out var forwardedIdentity
            )
            && forwardedIdentity == cancel.Identity;
        return _activeIdentity != cancel.Identity
            ? forwarded || await RejectProtocolAsync(cancellationToken)
            : await HandleStepAsync(_engine!.Cancel(cancel.Reason), cancellationToken);
    }

    private async ValueTask<bool> HandleStepAsync(
        PluginEngineStep step,
        CancellationToken cancellationToken
    )
    {
        switch (step)
        {
            case PluginEngineStep.HostCall hostCall:
                return await WriteAsync(
                    new PluginWorkerMessage.HostCallRequested(
                        _activeIdentity!.InvocationId,
                        _activeIdentity.CancellationId,
                        hostCall.Call
                    ),
                    cancellationToken
                );
            case PluginEngineStep.Completed completed:
                var completedId = CompleteActive();
                return await WriteAsync(
                    new PluginWorkerMessage.InvocationCompleted(
                        completedId,
                        completed.Outcome,
                        completed.Metrics
                    ),
                    cancellationToken
                );
            case PluginEngineStep.Cancelled cancelled:
                var cancelledId = CompleteActive();
                return await WriteAsync(
                    new PluginWorkerMessage.InvocationCancelled(
                        cancelledId,
                        cancelled.Reason,
                        cancelled.Metrics
                    ),
                    cancellationToken
                );
        }

        throw new UnreachableException("Unknown plugin engine step.");
    }

    private PluginWorkerInvocationId CompleteActive()
    {
        var invocationId = _activeIdentity!.InvocationId;
        _cancellations.Complete(invocationId);
        _activeIdentity = null;
        return invocationId;
    }

    private bool IdentityMatchesPackage(PluginWorkerInvocationIdentity identity) =>
        identity.Plugin == _handshake!.Package.Plugin
        && ContextPlugin(identity.Context) == identity.Plugin
        && ContextHostMatches(identity.Context, identity.Host);

    private static PluginInstallationIdentity ContextPlugin(PluginInvocationContext context) =>
        context is PluginInvocationContext.Installation installation ? installation.Plugin
        : context is PluginInvocationContext.Channel channel ? channel.Plugin
        : context is PluginInvocationContext.Automation automation ? automation.Plugin
        : context is PluginInvocationContext.Migration migration ? migration.Plugin
        : ((PluginInvocationContext.Page)context).Plugin;

    private static bool ContextHostMatches(PluginInvocationContext context, PluginHostId host) =>
        (context is not PluginInvocationContext.Channel channel || channel.Host == host)
        && (context is not PluginInvocationContext.Automation automation || automation.Host == host)
        && (context is not PluginInvocationContext.Page page || page.Host == host);

    private async ValueTask<bool> StopAsync(CancellationToken cancellationToken)
    {
        if (_activeIdentity is not null)
        {
            _ = _cancellations.Cancel(_activeIdentity, PluginCancellationReason.WorkerStopping);
            _ = _engine!.Cancel(PluginCancellationReason.WorkerStopping);
            _ = CompleteActive();
        }

        _ = await WriteAsync(new PluginWorkerMessage.Stopped(), cancellationToken);
        return false;
    }

    private ValueTask<bool> RejectInvocationAsync(
        PluginWorkerInvocationId invocationId,
        PluginWorkerFailureCode code,
        string message,
        CancellationToken cancellationToken
    ) =>
        WriteAsync(
            new PluginWorkerMessage.InvocationRejected(invocationId, new(code, message)),
            cancellationToken
        );

    private async ValueTask<bool> RejectProtocolAsync(CancellationToken cancellationToken)
    {
        _ = await WriteAsync(
            new PluginWorkerMessage.ProtocolRejected(
                new(PluginWorkerFailureCode.ProtocolViolation, "Unexpected worker message.")
            ),
            cancellationToken
        );
        return false;
    }
}
