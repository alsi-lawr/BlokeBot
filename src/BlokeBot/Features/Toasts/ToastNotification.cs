namespace BlokeBot.Features.Toasts;

public sealed record ToastNotification(
    Guid Id,
    ToastKind Kind,
    string Message,
    string Title,
    DateTimeOffset CreatedAtUtc,
    TimeSpan? AutoDismissAfter
)
{
    public bool RequiresManualDismiss => AutoDismissAfter is null;
}
