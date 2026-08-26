using BlokeBot.Eventing;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

internal sealed class PluginBlokeBotEventBridge(
    EventBus<AppEventKind> events,
    IPluginDispatchSnapshotProvider dispatch,
    IPluginDispatchInvoker invoker,
    TimeProvider timeProvider,
    IPluginAutomationSourceAdmission? automationSources = null
) : BackgroundService
{
    private static readonly IReadOnlyDictionary<AppEventKind, PluginBlokeBotEventKind> _sources =
        new Dictionary<AppEventKind, PluginBlokeBotEventKind>
        {
            [AppEventKind.HostedChannelsChanged] = PluginBlokeBotEventKind.HostedChannelsChanged,
            [AppEventKind.GuessingChanged] = PluginBlokeBotEventKind.GuessingChanged,
            [AppEventKind.PointsChanged] = PluginBlokeBotEventKind.PointsChanged,
            [AppEventKind.OverlaysChanged] = PluginBlokeBotEventKind.OverlaysChanged,
            [AppEventKind.TwitchOperationsChanged] =
                PluginBlokeBotEventKind.TwitchOperationsChanged,
        };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var subscriptions = events.Subscribe(
            _sources.Keys,
            ObserverIdentity.For(typeof(PluginBlokeBotEventBridge)),
            DispatchAsync
        );
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async ValueTask DispatchAsync(
        EventNotification<AppEventKind> notification,
        CancellationToken cancellationToken
    )
    {
        var kind = _sources[notification.Key];
        var occurredAt = timeProvider.GetUtcNow();
        var eventId = notification.CorrelationId.Value;
        var endpoints = dispatch
            .Current.Events.Where(endpoint =>
                endpoint.Descriptor.Source is PluginEventSource.BlokeBot source
                && source.Kind == kind
            )
            .ToArray();
        foreach (var endpoint in endpoints)
        {
            var source = kind.ToString().ToLowerInvariant();
            var context = new PluginInvocationContext.Channel(
                endpoint.Declaration.Installation,
                endpoint.State.Key.HostId,
                Event: new(endpoint.Descriptor.Id, source, eventId, occurredAt)
            );
            var outcome = await invoker.InvokeEventAsync(
                endpoint,
                context,
                new PluginValue.Map([
                    new("event_id", new PluginValue.String(eventId)),
                    new("source", new PluginValue.String(source)),
                ]),
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
