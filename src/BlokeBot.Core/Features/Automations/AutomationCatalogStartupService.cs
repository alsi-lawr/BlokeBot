namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationCatalogStartupService(AutomationDefinitionCatalog catalog)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = catalog.Descriptors;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
