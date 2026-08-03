using BlokeBot.Core.Features.HostedChannels.Runtime;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Admin.HostedChannels;

public partial class HostedChannelRow
{
    private const int _removalAnimationDelayMs = 150;
    private bool _pendingRemoval;
    private string _removalConfirmation = string.Empty;
    private bool _showRemovalConfirmation;

    [Parameter, EditorRequired]
    public HostedChannelAdminView Host { get; set; } =
        new(
            0,
            string.Empty,
            string.Empty,
            null,
            false,
            new HostedChannelRuntimeLifecycle.Stopped(null)
        );

    [Parameter]
    public bool CanEditHost { get; set; } = true;

    [Parameter, EditorRequired]
    public Func<int, Task> RemoveHost { get; set; } = static _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<int, Task> StartBot { get; set; } = static _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<int, Task> StopBot { get; set; } = static _ => Task.CompletedTask;

    private string _editHostHref =>
        $"/admin/select-host?hostId={Host.Id}&returnUrl={Uri.EscapeDataString("/admin")}";

    private string _removalConfirmationInputId => $"remove-host-{Host.Id}-confirmation";

    private bool _canConfirmRemoval =>
        Login.Normalize(_removalConfirmation) is { Length: > 0 } confirmation
        && string.Equals(confirmation, Login.Normalize(Host.Login), StringComparison.Ordinal);

    private string _rowClass =>
        _pendingRemoval
            ? "motion-list__item motion-list__item--removing flex flex-col gap-3 p-5 sm:flex-row sm:items-center sm:justify-between"
            : "motion-list__item flex flex-col gap-3 p-5 sm:flex-row sm:items-center sm:justify-between";

    private string _botStartedBadgeClass
    {
        get
        {
            var color = Host.Lifecycle.Match(
                static _ => "bg-slate-100 text-slate-600 ring-slate-200",
                static _ => "bg-orange-50 text-orange-700 ring-orange-200",
                static _ => "bg-emerald-50 text-emerald-700 ring-emerald-200",
                static _ => "bg-purple-50 text-purple-700 ring-purple-200"
            );

            return $"status-pill ring-1 {color}";
        }
    }

    private string _botStartedDotClass =>
        Host.Lifecycle.Match(
            static _ => "status-pill__dot bg-slate-400",
            static _ => "status-pill__dot bg-orange-500",
            static _ => "status-pill__dot bg-emerald-500",
            static _ => "status-pill__dot bg-purple-500"
        );

    private string _botStartedText =>
        Host.Lifecycle.Match(
            static _ => "bot offline",
            static _ => "bot starting",
            static _ => "bot running",
            static _ => "bot stopping"
        );

    private bool _isStopping => Host.Lifecycle is HostedChannelRuntimeLifecycle.Stopping;

    private bool _canStop =>
        Host.Lifecycle
            is HostedChannelRuntimeLifecycle.Starting
                or HostedChannelRuntimeLifecycle.Started;

    private static string Initials(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? "?" : trimmed[..1].ToUpperInvariant();
    }

    private static string StatusBadgeClass(bool active) =>
        active
            ? "status-pill bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200"
            : "status-pill bg-amber-50 text-amber-700 ring-1 ring-amber-200";

    private static string StatusDotClass(bool active) =>
        active ? "status-pill__dot bg-emerald-500" : "status-pill__dot bg-amber-500";

    private async Task RemoveHostAsync()
    {
        if (_pendingRemoval)
        {
            return;
        }

        _pendingRemoval = true;
        StateHasChanged();
        try
        {
            await Task.Delay(_removalAnimationDelayMs);
            await RemoveHost(Host.Id);
        }
        finally
        {
            _pendingRemoval = false;
        }
    }

    private void OpenRemovalConfirmation()
    {
        if (_pendingRemoval)
        {
            return;
        }

        _removalConfirmation = string.Empty;
        _showRemovalConfirmation = true;
    }

    private void UpdateRemovalConfirmation(ChangeEventArgs args) =>
        _removalConfirmation = args.Value?.ToString() ?? string.Empty;

    private void CancelRemoval()
    {
        _removalConfirmation = string.Empty;
        _showRemovalConfirmation = false;
    }

    private async Task ConfirmRemovalAsync()
    {
        if (!_canConfirmRemoval)
        {
            return;
        }

        _showRemovalConfirmation = false;
        await RemoveHostAsync();
    }
}
