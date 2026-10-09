namespace BlokeBot.Core.Features.Points.WatchTime;

internal sealed partial class WatchTimeRuntime
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        botRuntime.Changed += RuntimeChanged;
        _subscribed = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
                await ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Watch-time point observation failed; no missed opportunity will be replayed."
                );
                lock (_gate)
                {
                    foreach (var id in _epochs.Keys.ToArray())
                    {
                        var epoch = _epochs[id];
                        var ordinal = Math.Max(
                            epoch.NextOrdinal,
                            ((clock.GetUtcNow() - epoch.Anchor).Ticks / Interval.Ticks) + 1
                        );
                        _epochs[id] = epoch with { NextOrdinal = ordinal };
                        _statuses[id] = new(
                            WatchTimeStatusKind.Unavailable,
                            epoch.Settings.Amount,
                            Due(_epochs[id])
                        );
                        _ = _observations.Remove(id);
                    }
                }
                await NotifyStatusChangedAsync();
            }
            var delay = TimeSpan.FromMinutes(1);
            lock (_gate)
            {
                foreach (var epoch in _epochs.Values)
                {
                    var remaining = Due(epoch) - clock.GetUtcNow();
                    if (remaining < delay)
                    {
                        delay = remaining;
                    }
                }
            }
            if (delay <= TimeSpan.Zero)
            {
                continue;
            }
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var wake = _wake.WaitAsync(wait.Token);
            var timer = Task.Delay(delay, clock, wait.Token);
            _ = await Task.WhenAny(wake, timer);
            await wait.CancelAsync();
            try
            {
                await Task.WhenAll(wake, timer);
            }
            catch (OperationCanceledException) when (wait.IsCancellationRequested) { }
        }
    }
}
