namespace BlokeBot.Features.Toasts;

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

    public ToastNotification Error(string message, string? title = null, ToastTone? tone = null)
    {
        return Publish(ToastKind.Error, message, title, tone);
    }

    public ToastNotification Status(string message, string? title = null, ToastTone? tone = null)
    {
        return Publish(ToastKind.Status, message, title, tone);
    }

    public ToastNotification Success(string message, string? title = null, ToastTone? tone = null)
    {
        return Publish(ToastKind.Success, message, title, tone);
    }

    public ToastNotification Warning(string message, string? title = null, ToastTone? tone = null)
    {
        return Publish(ToastKind.Warning, message, title, tone);
    }

    public ToastNotification Publish(
        ToastKind kind,
        string message,
        string? title = null,
        ToastTone? tone = null
    )
    {
        var trimmed = message.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Toast message is required.", nameof(message));
        }

        var toast = new ToastNotification(
            Guid.NewGuid(),
            kind,
            tone ?? DefaultTone(kind),
            trimmed,
            string.IsNullOrWhiteSpace(title) ? DefaultTitle(kind) : title.Trim(),
            DateTimeOffset.UtcNow,
            DefaultAutoDismiss(kind)
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

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }

    private static ToastTone DefaultTone(ToastKind kind)
    {
        return kind switch
        {
            ToastKind.Success => ToastTone.Positive,
            ToastKind.Warning => ToastTone.Caution,
            ToastKind.Error => ToastTone.Critical,
            _ => ToastTone.Neutral,
        };
    }

    private static TimeSpan? DefaultAutoDismiss(ToastKind kind)
    {
        return kind switch
        {
            ToastKind.Status => TimeSpan.FromSeconds(4),
            ToastKind.Success => TimeSpan.FromSeconds(4),
            ToastKind.Warning => null,
            ToastKind.Error => null,
            _ => null,
        };
    }

    private static string DefaultTitle(ToastKind kind)
    {
        return kind switch
        {
            ToastKind.Status => "Status",
            ToastKind.Success => "Done",
            ToastKind.Warning => "Needs attention",
            ToastKind.Error => "Something went wrong",
            _ => "Notification",
        };
    }
}
