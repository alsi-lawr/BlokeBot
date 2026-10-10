namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardRefreshQueue(Func<Task> read, Action<Exception> failed)
    : IAsyncDisposable
{
    private readonly object _gate = new();
    private Task _work = Task.CompletedTask;
    private bool _reading;
    private bool _pending;
    private bool _visible = true;
    private bool _disposed;

    internal void Request()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _pending = true;
            if (_reading || !_visible)
            {
                return;
            }
            _reading = true;
            _work = Task.Run(DrainAsync);
        }
    }

    internal void SetVisible(bool visible)
    {
        lock (_gate)
        {
            _visible = visible;
        }
        if (visible)
        {
            Request();
        }
    }

    internal Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return _work;
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            lock (_gate)
            {
                if (_disposed || !_visible || !_pending)
                {
                    _reading = false;
                    return;
                }
                _pending = false;
            }
            try
            {
                await read();
            }
            catch (Exception exception)
            {
                failed(exception);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending = false;
        }
        return ValueTask.CompletedTask;
    }
}
