namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubRuntime(
    BotSettings settings,
    IBotChannelProvider channels,
    EventSubChannelSessionFactory channelSessions,
    EventSubChannelReconciliationTrigger reconciliation,
    IEventSubSubscriptionTransport subscriptions,
    IRuntimeIdleWait idleWait
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
                await session.RepairMissingSubscriptionsAndDrainAsync(
                    await subscriptions.ListEnabledOwnedIdsAsync(
                        settings.Identity.ClientId,
                        stoppingToken
                    ),
                    BotChannelList.Normalize(await channels.GetChannelsAsync(stoppingToken)),
                    stoppingToken
                );
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
