namespace BlokeBot.Core.Features.Automations;

public interface IAutomationUiChangePublisher
{
    Task RunChangedAsync(AutomationHostId host, AutomationRunId run);
    Task CountdownChangedAsync(AutomationHostId host, string name);
    Task CountdownsChangedAsync(AutomationHostId host);
}
