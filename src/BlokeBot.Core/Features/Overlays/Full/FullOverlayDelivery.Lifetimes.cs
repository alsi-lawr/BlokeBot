namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayDelivery : IHostedService, IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _maintenance;
    private int _disposeState;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        live.FullSourceChanged += SourceChanged;
        _maintenance = MaintainAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        live.FullSourceChanged -= SourceChanged;
        _stopping.Cancel();
        foreach (var lease in _renders.Values)
        {
            Close(lease);
        }
        _previews.Clear();
        if (_maintenance is not null)
        {
            await _maintenance.WaitAsync(cancellationToken);
        }
    }

    private void SourceChanged(int? hostId)
    {
        foreach (
            var lease in _renders.Values.Where(lease =>
                lease.Preview is not null && (hostId is null || lease.HostId == hostId)
            )
        )
        {
            lease.Wake();
        }
    }

    private async Task MaintainAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                ReclaimExpired();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }
        await StopAsync(CancellationToken.None);
        _stopping.Dispose();
    }
}
