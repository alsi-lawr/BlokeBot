namespace BlokeBot.Core.Features.Toasts;

public sealed class ToastService
{
    private const int _maxVisibleToasts = 5;
    private readonly object _gate = new();
    private readonly List<ToastNotification> _toasts = [];

    public event Action? Changed;

    public IReadOnlyList<ToastNotification> Current
    {
        get
        {
            lock (_gate)
            {
                return [.. _toasts];
            }
        }
    }

    public ToastNotification Publish<TStrategy>(ToastRequest<TStrategy> request)
        where TStrategy : IToastStrategy
    {
        ArgumentNullException.ThrowIfNull(request);
        var dismissal = TStrategy.Dismissal;

        var toast = new ToastNotification(
            Guid.NewGuid(),
            TStrategy.Kind,
            TStrategy.Tone,
            request.Message,
            request.Title,
            DateTimeOffset.UtcNow,
            dismissal.AutoDismissAfter
        );

        lock (_gate)
        {
            _toasts.Add(toast);
            if (_toasts.Count > _maxVisibleToasts)
            {
                _toasts.RemoveRange(0, _toasts.Count - _maxVisibleToasts);
            }
        }

        NotifyChanged();
        return toast;
    }

    public bool Dismiss(Guid toastId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _toasts.RemoveAll(toast => toast.Id == toastId) > 0;
        }

        if (removed)
        {
            NotifyChanged();
        }

        return removed;
    }

    private void NotifyChanged() => Changed?.Invoke();
}
