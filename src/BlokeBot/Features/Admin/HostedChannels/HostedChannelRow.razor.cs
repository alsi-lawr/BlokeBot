using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Components;
using BlokeBot.Components.Layout;
using BlokeBot.Eventing;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.History;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostConfig.Page;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.Admin.HostedChannels;

public partial class HostedChannelRow
{
    private const int _removalAnimationDelayMs = 150;
    private bool _pendingRemoval;

    [Parameter, EditorRequired]
    public HostedChannelAdminView Host { get; set; } =
        new(0, string.Empty, string.Empty, null, false, BotChannelRuntimeState.Stopped);

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

    private string _rowClass =>
        _pendingRemoval
            ? "motion-list__item motion-list__item--removing flex flex-col gap-3 p-5 sm:flex-row sm:items-center sm:justify-between"
            : "motion-list__item flex flex-col gap-3 p-5 sm:flex-row sm:items-center sm:justify-between";

    private string _botStartedBadgeClass
    {
        get
        {
            var color = Host.RuntimeState switch
            {
                BotChannelRuntimeState.Starting => "bg-orange-50 text-orange-700 ring-orange-200",
                BotChannelRuntimeState.Started => "bg-emerald-50 text-emerald-700 ring-emerald-200",
                BotChannelRuntimeState.Stopping => "bg-purple-50 text-purple-700 ring-purple-200",
                _ => "bg-slate-100 text-slate-600 ring-slate-200",
            };

            return $"inline-flex h-6 items-center gap-1.5 rounded-full px-2.5 text-xs font-bold ring-1 {color}";
        }
    }

    private string _botStartedDotClass =>
        Host.RuntimeState switch
        {
            BotChannelRuntimeState.Starting => "h-1.5 w-1.5 rounded-full bg-orange-500",
            BotChannelRuntimeState.Started => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            BotChannelRuntimeState.Stopping => "h-1.5 w-1.5 rounded-full bg-purple-500",
            _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
        };

    private string _botStartedText => Host.RuntimeState switch
    {
        BotChannelRuntimeState.Starting => "bot starting",
        BotChannelRuntimeState.Started => "bot running",
        BotChannelRuntimeState.Stopping => "bot stopping",
        _ => "bot offline",
    };

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
}
