using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginRawEventSubBridge(
    IPluginHostContextResolver hosts,
    IPluginDispatchSnapshotProvider dispatch,
    IPluginDispatchInvoker invoker,
    IPluginAutomationSourceAdmission? automationSources = null
) : IEventSubRawObserver
{
    public async Task RawEventReceivedAsync(
        EventSubRawNotification notification,
        CancellationToken cancellationToken
    )
    {
        if (!PluginRawEventSubEnvelopeMapper.TryMap(notification, out var input))
        {
            return;
        }
        var host = await hosts.FindAsync(notification.BroadcasterUserLogin, cancellationToken);
        if (host is null)
        {
            return;
        }

        var endpoints = dispatch
            .Current.Events.Where(endpoint =>
                endpoint.State.Key.HostId == host.Id
                && endpoint.Descriptor.Source is PluginEventSource.TwitchRaw source
                && source.EventSubType == notification.SubscriptionType
                && source.Version == notification.SubscriptionVersion
            )
            .ToArray();
        foreach (var endpoint in endpoints)
        {
            var context = new PluginInvocationContext.Channel(
                endpoint.Declaration.Installation,
                host.Id,
                Event: new(
                    endpoint.Descriptor.Id,
                    notification.SubscriptionType,
                    notification.MessageId,
                    notification.MessageTimestamp
                )
            );
            var outcome = await invoker.InvokeEventAsync(
                endpoint,
                context,
                input,
                cancellationToken
            );
            if (
                outcome is PluginDispatchInvocationOutcome.Returned returned
                && !returned.AutomationSources.IsDefaultOrEmpty
                && automationSources is not null
            )
            {
                await automationSources.AdmitAsync(
                    endpoint,
                    context,
                    returned.AutomationSources,
                    cancellationToken
                );
            }
        }
    }
}
