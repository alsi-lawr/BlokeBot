using System.Diagnostics;
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
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Core.Components.Layout;

public partial class SelectedChannelBotStatus
{
    private int? _loadedStatusHostId;
    private IDisposable? _hostedChannelSubscription;
    private HostedChannelRuntimeSummary? _selectedHostStatus;

    [Parameter, EditorRequired]
    public AuthenticatedSession Session { get; set; } = AuthenticatedSession.Anonymous;

    private BotHostSelection? _selection =>
        Session.State.Match<BotHostSelection?>(
            _ => null,
            selected => selected.Selection,
            _ => null
        );

    private bool _selectedHostBotAuthorized =>
        _selectedHostStatus?.IsChannelBotAuthorized == true
        && _selectedHostStatus.ChannelBotAuthorizationScopesCurrent;

    private string _selectedBotStatusShellClass
    {
        get
        {
            var color = _selectedHostStatus switch
            {
                { IsChannelBotAuthorized: false } => "border-amber-200 bg-amber-50 text-amber-700",
                { ChannelBotAuthorizationScopesCurrent: false } =>
                    "border-amber-200 bg-amber-50 text-amber-700",
                { } status => status.Lifecycle.Match(
                    static _ => "app-blue-status",
                    static _ => "border-orange-200 bg-orange-50 text-orange-700",
                    static _ => "border-emerald-200 bg-emerald-50 text-emerald-700",
                    static _ => "border-purple-200 bg-purple-50 text-purple-700"
                ),
                null => "border-slate-200 bg-slate-100 text-slate-600",
            };

            return $"selected-channel-bot-status flex h-8 w-full min-w-0 items-center justify-center gap-1.5 rounded-full border px-2.5 text-xs font-bold shadow-sm sm:w-auto sm:justify-start {color}";
        }
    }

    private string _selectedBotStatusDotClass
    {
        get
        {
            var color = _selectedHostStatus switch
            {
                { IsChannelBotAuthorized: false } => "bg-amber-500",
                { ChannelBotAuthorizationScopesCurrent: false } => "bg-amber-500",
                { } status => status.Lifecycle.Match(
                    static _ => "app-blue-dot",
                    static _ => "bg-orange-500",
                    static _ => "bg-emerald-500",
                    static _ => "bg-purple-500"
                ),
                null => "bg-slate-400",
            };

            return $"h-1.5 w-1.5 rounded-full {color}";
        }
    }

    private string _selectedBotStatusText =>
        _selectedHostStatus is { } status
            ? status.Lifecycle.Match(
                _ =>
                    status.IsChannelBotAuthorized
                        ? (
                            status.ChannelBotAuthorizationScopesCurrent ? "chat connected"
                            : CanAuthorizeSelectedHost() ? "reconnect bot"
                            : "Channel owner needs to reconnect the bot"
                        )
                        : (
                            CanAuthorizeSelectedHost()
                                ? "connect bot"
                                : "Channel owner needs to reconnect the bot"
                        ),
                static _ => "bot starting",
                static _ => "bot running",
                static _ => "bot stopping"
            )
            : "Channel owner needs to reconnect the bot";

    protected override void OnInitialized()
    {
        _hostedChannelSubscription = _events.SubscribeForComponentRefresh(
            AppEventKind.HostedChannelsChanged,
            InvokeAsync,
            ReloadForEventAsync,
            StateHasChanged
        );
    }

    protected override async Task OnParametersSetAsync()
    {
        await LoadSelectedHostStatusAsync();
    }

    public void Dispose()
    {
        _hostedChannelSubscription?.Dispose();
    }

    private bool CanAuthorizeSelectedHost()
    {
        return Session.CanAuthorizeSelectedHost;
    }

    private async Task LoadSelectedHostStatusAsync()
    {
        if (_selection is null)
        {
            _loadedStatusHostId = null;
            _selectedHostStatus = null;
            return;
        }

        if (_loadedStatusHostId == _selection.Current.Id)
        {
            return;
        }

        _loadedStatusHostId = _selection.Current.Id;
        var result = await _hostedChannels
            .LoadHostRuntimeSummary(_selection.Current.Id)
            .ExecuteAsync(CancellationToken.None);
        _selectedHostStatus = result.Match(
            option => option.Match<HostedChannelRuntimeSummary?>(value => value, () => null),
            _ => throw new UnreachableException()
        );
    }

    private async Task ReloadForEventAsync()
    {
        _loadedStatusHostId = null;
        await LoadSelectedHostStatusAsync();
    }
}
