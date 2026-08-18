using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubRuntime(
    BotSettings settings,
    IBotChannelProvider channels,
    EventSubChannelSessionFactory channelSessions,
    EventSubChannelReconciliationTrigger reconciliation,
    IEventSubSubscriptionTransport subscriptions,
    IRuntimeIdleWait idleWait,
    ILogger<EventSubRuntime> log
)
{
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        await subscriptions.ResetAsync(settings.Identity.ClientId, stoppingToken);
        var session = channelSessions.Create();
        using var registration = reconciliation.Register(session);
        try
        {
            session.Start(
                BotChannelList.Normalize(await channels.GetChannelsAsync(stoppingToken)),
                stoppingToken
            );
            while (!stoppingToken.IsCancellationRequested)
            {
                await idleWait.WaitAsync(stoppingToken);
                await ReconcileAsync(session, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private async Task ReconcileAsync(
        EventSubChannelSession session,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await session.RepairMissingSubscriptionsAndDrainAsync(
                token => subscriptions.ListEnabledOwnedIdsAsync(settings.Identity.ClientId, token),
                channels.GetChannelsAsync,
                stoppingToken
            );
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            var details = EventSubChannelFailureClassifier.Classify(
                exception,
                EventSubChannelPhase.Reconciliation,
                stoppingToken
            );
            log.LogError(
                exception,
                "EventSub reconciliation failed at {Phase}; classified {Classification} ({FailureType}). The runtime stays up and retries on the next idle cycle.",
                details.Phase,
                details.Classification,
                details.FailureType
            );
        }
    }
}
