using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Commands;
using BlokeBot.Components;
using BlokeBot.Components.Layout;
using BlokeBot.Eventing;
using BlokeBot.Features.AccessLists;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.Admin.SiteAccess;
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
using BlokeBot.Features.HostedChannels.Authorization;
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
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.Admin;

public partial class AdminPage
{
    private SiteAccessAdminState? state;
    private BotAccountAuthorizationStatus? botAccountStatus;
    private IReadOnlyList<HostedChannelAdminView> hosts = [];
    private IReadOnlyList<AccessListEntryProfile> siteBlacklistEntries = [];
    private IReadOnlyList<AccessListEntryProfile> siteWhitelistEntries = [];
    private bool isBotAccount;
    private int? pendingRuntimeHostId;
    private string newBlacklistLogin = string.Empty;
    private string newHostLogin = string.Empty;
    private string newWhitelistLogin = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            Events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.SiteAccessChanged],
                work => InvokeAsync(work),
                ReloadForEventAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task AddBlacklistAsync()
    {
        await SiteAccess.AddEntryAsync(
            AccessListEntryKind.Blacklist,
            newBlacklistLogin,
            CancellationToken.None
        );
        newBlacklistLogin = string.Empty;
        await LoadAsync();
    }

    private async Task AddWhitelistAsync()
    {
        await SiteAccess.AddEntryAsync(
            AccessListEntryKind.Whitelist,
            newWhitelistLogin,
            CancellationToken.None
        );
        newWhitelistLogin = string.Empty;
        await LoadAsync();
    }

    private async Task CreateHostAsync()
    {
        await ApplyHostOperationAsync(
            await HostManagement.CreateHostAsync(newHostLogin, CancellationToken.None),
            clearNewHostLogin: true
        );
        await LoadAsync();
    }

    private async Task RemoveHostAsync(int hostId)
    {
        await ApplyHostOperationAsync(
            await HostManagement.RemoveHostAsync(hostId, CancellationToken.None)
        );
        await LoadAsync();
    }

    private async Task StartBotAsync(int hostId)
    {
        await ApplyHostOperationAsync(
            await HostManagement.StartBotAsync(hostId, CancellationToken.None)
        );
        await LoadAsync();
    }

    private async Task StopBotAsync(int hostId)
    {
        await ApplyHostOperationAsync(
            await HostManagement.StopBotAsync(hostId, CancellationToken.None)
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isBotAccount = (await LoadPageContextAsync()).IsBotAccount;
        state = await SiteAccess.LoadAdminStateAsync(CancellationToken.None);
        siteWhitelistEntries = await AccessListProfiles.ResolveAsync(
            state.Whitelist,
            CancellationToken.None
        );
        siteBlacklistEntries = await AccessListProfiles.ResolveAsync(
            state.Blacklist,
            CancellationToken.None
        );
        hosts = await HostedChannels.LoadHostedChannelsAsync(CancellationToken.None);
        botAccountStatus = await BotAccountAuthorization.GetStatusAsync(CancellationToken.None);
    }

    private async Task ClearBotAccountAuthorizationAsync()
    {
        await BotAccountAuthorization.ClearAsync(CancellationToken.None);
        await LoadAsync();
    }

    private async Task RefreshBotAccountAuthorizationAsync()
    {
        await LoadAsync();
    }

    private async Task ReloadForEventAsync()
    {
        await LoadAsync();
        if (pendingRuntimeHostId is { } hostId)
        {
            ApplyHostOperation(HostManagement.RefreshPendingRuntime(hostId, hosts));
        }
    }

    private Task ApplyHostOperationAsync(
        AdminHostOperationResult result,
        bool clearNewHostLogin = false
    )
    {
        ApplyHostOperation(result);
        if (clearNewHostLogin && result.Succeeded)
            newHostLogin = string.Empty;
        return Task.CompletedTask;
    }

    private void ApplyHostOperation(AdminHostOperationResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
            Toasts.Publish(result.Succeeded ? ToastKind.Status : ToastKind.Error, result.Message);
        pendingRuntimeHostId = result.PendingRuntimeHostId;
    }

    private async Task RemoveBlacklistAsync(string login)
    {
        await SiteAccess.RemoveEntryAsync(
            AccessListEntryKind.Blacklist,
            login,
            CancellationToken.None
        );
        await LoadAsync();
    }

    private async Task RemoveWhitelistAsync(string login)
    {
        await SiteAccess.RemoveEntryAsync(
            AccessListEntryKind.Whitelist,
            login,
            CancellationToken.None
        );
        await LoadAsync();
    }

    private async Task ToggleWhitelistAsync(ChangeEventArgs args)
    {
        await SiteAccess.SetWhitelistEnabledAsync(args.Value is true, CancellationToken.None);
        await LoadAsync();
    }
}
