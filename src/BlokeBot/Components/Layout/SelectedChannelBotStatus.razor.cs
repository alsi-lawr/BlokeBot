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
using BlokeBot.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Components.Layout;

public partial class SelectedChannelBotStatus
{
    private int? loadedStatusHostId;
    private IDisposable? hostedChannelSubscription;
    private HostedChannelRuntimeSummary? selectedHostStatus;

    [Parameter, EditorRequired]
    public AuthenticatedSession Session { get; set; } = AuthenticatedSession.Anonymous;

    private BotHostSelection? Selection => Session.HostSelection;

    private bool SelectedHostBotAuthorized =>
        selectedHostStatus?.IsChannelBotAuthorized == true
        && selectedHostStatus.ChannelBotAuthorizationScopesCurrent;

    private string SelectedBotStatusShellClass
    {
        get
        {
            var color = selectedHostStatus switch
            {
                { IsChannelBotAuthorized: false } => "border-amber-200 bg-amber-50 text-amber-700",
                { ChannelBotAuthorizationScopesCurrent: false } =>
                    "border-amber-200 bg-amber-50 text-amber-700",
                { RuntimeState: BotChannelRuntimeState.Starting } =>
                    "border-orange-200 bg-orange-50 text-orange-700",
                { RuntimeState: BotChannelRuntimeState.Stopping } =>
                    "border-purple-200 bg-purple-50 text-purple-700",
                { RuntimeState: BotChannelRuntimeState.Started } =>
                    "border-emerald-200 bg-emerald-50 text-emerald-700",
                { IsChannelBotAuthorized: true } => "app-blue-status",
                _ => "border-slate-200 bg-slate-100 text-slate-600",
            };

            return $"hidden h-8 items-center gap-1.5 rounded-full border px-2.5 text-xs font-bold shadow-sm sm:flex {color}";
        }
    }

    private string SelectedBotStatusDotClass
    {
        get
        {
            var color = selectedHostStatus switch
            {
                { IsChannelBotAuthorized: false } => "bg-amber-500",
                { ChannelBotAuthorizationScopesCurrent: false } => "bg-amber-500",
                { RuntimeState: BotChannelRuntimeState.Starting } => "bg-orange-500",
                { RuntimeState: BotChannelRuntimeState.Stopping } => "bg-purple-500",
                { RuntimeState: BotChannelRuntimeState.Started } => "bg-emerald-500",
                { IsChannelBotAuthorized: true } => "app-blue-dot",
                _ => "bg-slate-400",
            };

            return $"h-1.5 w-1.5 rounded-full {color}";
        }
    }

    private string SelectedBotStatusText =>
        selectedHostStatus switch
        {
            { RuntimeState: BotChannelRuntimeState.Starting } => "bot starting",
            { RuntimeState: BotChannelRuntimeState.Stopping } => "bot stopping",
            { RuntimeState: BotChannelRuntimeState.Started } => "bot started",
            { IsChannelBotAuthorized: true, ChannelBotAuthorizationScopesCurrent: false } =>
                "reconnect bot",
            { IsChannelBotAuthorized: true } => "bot connected",
            _ => "bot not connected",
        };

    protected override void OnInitialized()
    {
        hostedChannelSubscription = Events.SubscribeForComponentRefresh(
            AppEventKind.HostedChannelsChanged,
            work => InvokeAsync(work),
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
        hostedChannelSubscription?.Dispose();
    }

    private bool CanAuthorizeSelectedHost()
    {
        return Session.CanAuthorizeSelectedHost;
    }

    private async Task LoadSelectedHostStatusAsync()
    {
        if (Selection is null)
        {
            loadedStatusHostId = null;
            selectedHostStatus = null;
            return;
        }

        if (loadedStatusHostId == Selection.Current.Id)
            return;

        loadedStatusHostId = Selection.Current.Id;
        selectedHostStatus = await HostedChannels.LoadHostRuntimeSummaryAsync(
            Selection.Current.Id,
            CancellationToken.None
        );
    }

    private async Task ReloadForEventAsync()
    {
        loadedStatusHostId = null;
        await LoadSelectedHostStatusAsync();
    }
}
