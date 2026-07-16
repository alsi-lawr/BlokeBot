namespace BlokeBot.Core.Features.Toasts;

public sealed record ToastNotification(
    Guid Id,
    ToastKind Kind,
    ToastTone Tone,
    string Message,
    string Title,
    DateTimeOffset CreatedAtUtc,
    TimeSpan? AutoDismissAfter
)
{
    public bool RequiresManualDismiss => AutoDismissAfter is null;
}
