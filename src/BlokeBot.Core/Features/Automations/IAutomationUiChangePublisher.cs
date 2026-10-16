namespace BlokeBot.Core.Features.Automations;

public interface IAutomationUiChangePublisher
{
    Task SourceReadinessChangedAsync(AutomationHostId host);

    Task RunLimitRejectedAsync(
        AutomationHostId host,
        AutomationFlowId flow,
        AutomationAdmissionRejectionReason reason
    );

    Task RunChangedAsync(AutomationHostId host, AutomationRunId run);
    Task CountdownChangedAsync(AutomationHostId host, string name);
    Task CountdownsChangedAsync(AutomationHostId host);
}
