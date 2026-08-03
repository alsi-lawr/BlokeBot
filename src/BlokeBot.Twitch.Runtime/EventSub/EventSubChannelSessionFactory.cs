namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubChannelSessionFactory(
    IEventSubChannelOperations operations,
    EventSubChannelRecoveryPipeline recovery,
    EventSubSubscriptionReconciliationStore pendingDeletions,
    EventSubChannelStatusStore channelStatus,
    BotRuntimeStatusStore runtimeStatus,
    IEventSubChannelDiagnosticReporter diagnostics,
    TimeProvider timeProvider
)
{
    internal bool HasPendingReconciliation => pendingDeletions.HasPendingReconciliation;

    internal EventSubChannelSession Create() =>
        new(
            operations,
            recovery,
            pendingDeletions,
            channelStatus.CreateScope(),
            runtimeStatus,
            diagnostics,
            timeProvider
        );
}
