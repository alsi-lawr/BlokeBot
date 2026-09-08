namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationTraceCleanupWorker(AutomationTraceStore traces, TimeProvider clock)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        do
        {
            _ = await traces.DeleteExpiredAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
