using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Features.Toasts;

public partial class ToastHost
{
    private const int _removalAnimationDelayMs = 180;
    private readonly Dictionary<Guid, CancellationTokenSource> _autoDismissTokens = [];
    private readonly HashSet<Guid> _dismissingToastIds = [];
    private CancellationTokenSource _disposeToken = new();
    private IReadOnlyList<ToastNotification> _visibleToasts = [];

    protected override void OnInitialized()
    {
        _toasts.Changed += OnToastsChanged;
        RefreshToasts();
    }

    public void Dispose()
    {
        _toasts.Changed -= OnToastsChanged;
        _disposeToken.Cancel();
        _disposeToken.Dispose();
        foreach (var token in _autoDismissTokens.Values)
        {
            token.Cancel();
            token.Dispose();
        }
        _autoDismissTokens.Clear();
    }

    private async Task BeginDismissAsync(Guid toastId)
    {
        if (!_dismissingToastIds.Add(toastId))
        {
            return;
        }

        if (_autoDismissTokens.Remove(toastId, out var autoDismissToken))
        {
            autoDismissToken.Cancel();
            autoDismissToken.Dispose();
        }

        await InvokeAsync(StateHasChanged);
        await Task.Delay(_removalAnimationDelayMs);
        _toasts.Dismiss(toastId);
        _dismissingToastIds.Remove(toastId);
    }

    private Task BeginDismissOnKeyAsync(KeyboardEventArgs args, Guid toastId) =>
        args.Key is "Enter" or " " ? BeginDismissAsync(toastId) : Task.CompletedTask;

    private async Task AutoDismissAsync(Guid toastId, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            await InvokeAsync(() => BeginDismissAsync(toastId));
        }
        catch (OperationCanceledException) { }
    }

    private void OnToastsChanged() => _ = InvokeAsync(RefreshToasts);

    private void RefreshToasts()
    {
        _visibleToasts = _toasts.Current;
        ScheduleAutoDismiss();
        StateHasChanged();
    }

    private void ScheduleAutoDismiss()
    {
        var currentIds = _visibleToasts.Select(toast => toast.Id).ToHashSet();
        foreach (
            var staleId in _autoDismissTokens.Keys.Where(id => !currentIds.Contains(id)).ToArray()
        )
        {
            _autoDismissTokens[staleId].Cancel();
            _autoDismissTokens[staleId].Dispose();
            _autoDismissTokens.Remove(staleId);
        }

        foreach (var toast in _visibleToasts)
        {
            if (
                toast.AutoDismissAfter is not { } delay
                || _autoDismissTokens.ContainsKey(toast.Id)
                || _dismissingToastIds.Contains(toast.Id)
            )
            {
                continue;
            }

            var token = CancellationTokenSource.CreateLinkedTokenSource(_disposeToken.Token);
            _autoDismissTokens[toast.Id] = token;
            _ = AutoDismissAsync(toast.Id, delay, token.Token);
        }
    }

    private string ToastClass(ToastNotification toast)
    {
        var classes = $"toast-card toast-card--{ToneCssClass(toast.Tone)}";
        return _dismissingToastIds.Contains(toast.Id) ? $"{classes} toast-card--removing" : classes;
    }

    private static string DismissLabel(ToastNotification toast) =>
        $"Dismiss notification: {toast.Title}. {toast.Message}";

    private static string ToneCssClass(ToastTone tone) =>
        tone switch
        {
            ToastTone.Positive => "positive",
            ToastTone.Caution => "caution",
            ToastTone.Critical => "critical",
            _ => "neutral",
        };

    private static string ToastRole(ToastNotification toast) =>
        toast.Kind is ToastKind.Error or ToastKind.Warning ? "alert" : "status";
}
