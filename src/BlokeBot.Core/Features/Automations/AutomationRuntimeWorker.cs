namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationRuntimeWorker(AutomationRuntimeService runtime, TimeProvider clock)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await runtime.InitializeAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await runtime.ResumeDueAsync(stoppingToken);
        }
    }
}
