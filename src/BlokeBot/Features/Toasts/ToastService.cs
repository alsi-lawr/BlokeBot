namespace BlokeBot.Features.Toasts;

public sealed class ToastService
{
    private const int MaxVisibleToasts = 5;
    private readonly object gate = new();
    private readonly List<ToastNotification> toasts = [];

    public event Action? Changed;

    public IReadOnlyList<ToastNotification> Current
    {
        get
        {
            lock (gate)
            {
                return [.. toasts];
            }
        }
    }

    public ToastNotification Error(string message, string? title = null) =>
        Publish(ToastKind.Error, message, title);

    public ToastNotification Status(string message, string? title = null) =>
        Publish(ToastKind.Status, message, title);

    public ToastNotification Success(string message, string? title = null) =>
        Publish(ToastKind.Success, message, title);

    public ToastNotification Warning(string message, string? title = null) =>
        Publish(ToastKind.Warning, message, title);

    public ToastNotification Publish(ToastKind kind, string message, string? title = null)
    {
        var trimmed = message.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Toast message is required.", nameof(message));

        var toast = new ToastNotification(
            Guid.NewGuid(),
            kind,
            trimmed,
            string.IsNullOrWhiteSpace(title) ? DefaultTitle(kind) : title.Trim(),
            DateTimeOffset.UtcNow,
            DefaultAutoDismiss(kind)
        );

        lock (gate)
        {
            toasts.Add(toast);
            if (toasts.Count > MaxVisibleToasts)
                toasts.RemoveRange(0, toasts.Count - MaxVisibleToasts);
        }

        NotifyChanged();
        return toast;
    }

    public bool Dismiss(Guid toastId)
    {
        bool removed;
        lock (gate)
        {
            removed = toasts.RemoveAll(toast => toast.Id == toastId) > 0;
        }

        if (removed)
            NotifyChanged();

        return removed;
    }

    private void NotifyChanged() => Changed?.Invoke();

    private static TimeSpan? DefaultAutoDismiss(ToastKind kind) =>
        kind switch
        {
            ToastKind.Status => TimeSpan.FromSeconds(4),
            ToastKind.Success => TimeSpan.FromSeconds(4),
            ToastKind.Warning => null,
            ToastKind.Error => null,
            _ => null,
        };

    private static string DefaultTitle(ToastKind kind) =>
        kind switch
        {
            ToastKind.Status => "Status",
            ToastKind.Success => "Done",
            ToastKind.Warning => "Needs attention",
            ToastKind.Error => "Something went wrong",
            _ => "Notification",
        };
}
