using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot.Core;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

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
    public Func<int, Task> RemoveHost { get; set; } = _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<int, Task> StartBot { get; set; } = _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<int, Task> StopBot { get; set; } = _ => Task.CompletedTask;

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

            return $"inline-flex h-6 items-center gap-1.5 rounded-full px-2.5 text-xs font-bold ring-1 {color}";
        }
    }

    private string _botStartedDotClass =>
        Host.Lifecycle.Match(
            static _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
            static _ => "h-1.5 w-1.5 rounded-full bg-orange-500",
            static _ => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            static _ => "h-1.5 w-1.5 rounded-full bg-purple-500"
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

    private static string StatusBadgeClass(bool active)
    {
        return active
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200"
            : "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200";
    }

    private static string StatusDotClass(bool active)
    {
        return active
            ? "h-1.5 w-1.5 rounded-full bg-emerald-500"
            : "h-1.5 w-1.5 rounded-full bg-amber-500";
    }

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

    private void UpdateRemovalConfirmation(ChangeEventArgs args)
    {
        _removalConfirmation = args.Value?.ToString() ?? string.Empty;
    }

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
