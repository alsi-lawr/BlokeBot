using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.Automations;

internal abstract record AutomationUiChange
{
    private AutomationUiChange() { }

    internal sealed record Run(AutomationHostId Host, AutomationRunId Id) : AutomationUiChange;

    internal sealed record Countdown(AutomationHostId Host, string Name) : AutomationUiChange;

    internal sealed record CountdownHost(AutomationHostId Host) : AutomationUiChange;
}

internal sealed class AutomationUiChanges(
    EventBus<AutomationUiChange> events,
    ILogger<AutomationUiChanges> log
) : IAutomationUiChangePublisher
{
    internal IDisposable Subscribe(
        AutomationUiChange identity,
        ObserverIdentity observer,
        Func<EventNotification<AutomationUiChange>, CancellationToken, ValueTask> handler
    ) => events.Subscribe(identity, observer, handler);

    public Task RunChangedAsync(AutomationHostId host, AutomationRunId run) =>
        PublishAsync(new AutomationUiChange.Run(host, run));

    public Task CountdownChangedAsync(AutomationHostId host, string name) =>
        PublishAsync(new AutomationUiChange.Countdown(host, name));

    public Task CountdownsChangedAsync(AutomationHostId host) =>
        PublishAsync(new AutomationUiChange.CountdownHost(host));

    private async Task PublishAsync(AutomationUiChange identity)
    {
        try
        {
            _ = await events.PublishAsync(identity, CancellationToken.None);
        }
        catch (ObserverFanOutEscalationException exception)
        {
            log.LogError(
                exception,
                "Automation UI observer diagnostics failed after committed change {Identity}.",
                identity
            );
        }
    }
}
