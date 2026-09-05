namespace BlokeBot.Core.Features.ViewerPortal;

internal sealed class PortalRefreshCoordinator(
    TimeProvider clock,
    Func<IReadOnlySet<AppEventKind>, CancellationToken, Task> reload
) : IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<AppEventKind> _pending = [];
    private CancellationTokenSource _connection = new();
    private DateTimeOffset _nextRefresh = DateTimeOffset.MinValue;
    private bool _connected = true;
    private bool _disposed;
    private bool _running;
    internal CancellationToken ConnectionToken
    {
        get
        {
            lock (_gate)
            {
                return _connection.Token;
            }
        }
    }
    internal Task Completion { get; private set; } = Task.CompletedTask;

    internal void Notify(AppEventKind kind)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _ = _pending.Add(kind);
            Start();
        }
    }

    internal void SetConnected(bool connected)
    {
        lock (_gate)
        {
            if (_disposed || _connected == connected)
            {
                return;
            }
            _connected = connected;
            if (!connected)
            {
                _connection.Cancel();
                return;
            }
            _connection.Dispose();
            _connection = new();
            _ = _pending.Add(AppEventKind.HostedChannelsChanged);
            Start();
        }
    }

    private void Start()
    {
        if (!_connected || _running || _pending.Count == 0)
        {
            return;
        }
        _running = true;
        Completion = RunAsync(_connection.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await Task.Yield();
        try
        {
            while (true)
            {
                TimeSpan delay;
                lock (_gate)
                {
                    if (_disposed || !_connected || _pending.Count == 0)
                    {
                        return;
                    }
                    delay = _nextRefresh - clock.GetUtcNow();
                }
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, clock, ct);
                }
                ct.ThrowIfCancellationRequested();
                HashSet<AppEventKind> kinds;
                lock (_gate)
                {
                    ct.ThrowIfCancellationRequested();
                    kinds = [.. _pending];
                    _pending.Clear();
                    _nextRefresh = clock.GetUtcNow().AddSeconds(10);
                }
                await reload(kinds, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            lock (_gate)
            {
                _running = false;
                if (!_disposed)
                {
                    Start();
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _connection.Cancel();
            _connection.Dispose();
            _pending.Clear();
        }
    }
}
