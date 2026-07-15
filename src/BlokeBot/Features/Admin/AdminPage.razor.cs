using System.Diagnostics;
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
using BlokeBot.Functional;
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
    private SiteAccessAdminState? _state;
    private BotAccountAuthorizationStatus? _botAccountStatus;
    private IReadOnlyList<HostedChannelAdminView> _hosts = [];
    private IReadOnlyList<AccessListEntryProfile> _siteBlacklistEntries = [];
    private IReadOnlyList<AccessListEntryProfile> _siteWhitelistEntries = [];
    private bool _isBotAccount;
    private int? _pendingRuntimeHostId;
    private string _newBlacklistLogin = string.Empty;
    private string _newHostLogin = string.Empty;
    private string _newWhitelistLogin = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.SiteAccessChanged],
                InvokeAsync,
                ReloadForEventAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task AddBlacklistAsync()
    {
        await _siteAccess.AddEntryAsync(
            AccessListEntryKind.Blacklist,
            _newBlacklistLogin,
            CancellationToken.None
        );
        _newBlacklistLogin = string.Empty;
        await LoadAsync();
    }

    private async Task AddWhitelistAsync()
    {
        await _siteAccess.AddEntryAsync(
            AccessListEntryKind.Whitelist,
            _newWhitelistLogin,
            CancellationToken.None
        );
        _newWhitelistLogin = string.Empty;
        await LoadAsync();
    }

    private async Task CreateHostAsync()
    {
        await ApplyHostOperationAsync(
            await _hostManagement.CreateHost(_newHostLogin).ExecuteAsync(CancellationToken.None),
            clearNewHostLogin: true
        );
        await LoadAsync();
    }

    private async Task RemoveHostAsync(int hostId)
    {
        await ApplyHostOperationAsync(
            await _hostManagement.RemoveHost(hostId).ExecuteAsync(CancellationToken.None)
        );
        await LoadAsync();
    }

    private async Task StartBotAsync(int hostId)
    {
        await ApplyHostOperationAsync(
            await _hostManagement.StartBot(hostId).ExecuteAsync(CancellationToken.None)
        );
        await LoadAsync();
    }

    private async Task StopBotAsync(int hostId)
    {
        await ApplyHostOperationAsync(
            await _hostManagement.StopBot(hostId).ExecuteAsync(CancellationToken.None)
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _isBotAccount = (await LoadPageContextAsync()).IsBotAccount;
        _state = await _siteAccess.LoadAdminStateAsync(CancellationToken.None);
        _siteWhitelistEntries = await _accessListProfiles.ResolveAsync(
            _state.Whitelist,
            CancellationToken.None
        );
        _siteBlacklistEntries = await _accessListProfiles.ResolveAsync(
            _state.Blacklist,
            CancellationToken.None
        );
        _hosts = await _hostedChannels.LoadHostedChannelsAsync(CancellationToken.None);
        _botAccountStatus = await _botAccountAuthorization.GetStatusAsync(CancellationToken.None);
    }

    private async Task ClearBotAccountAuthorizationAsync()
    {
        await _botAccountAuthorization.ClearAsync(CancellationToken.None);
        await LoadAsync();
    }

    private async Task RefreshBotAccountAuthorizationAsync()
    {
        await LoadAsync();
    }

    private async Task ReloadForEventAsync()
    {
        await LoadAsync();
        if (_pendingRuntimeHostId is { } hostId)
        {
            ApplyHostOperation(_hostManagement.RefreshPendingRuntime(hostId, _hosts));
        }
    }

    private Task ApplyHostOperationAsync(
        Result<AdminHostOperationOutcome, AdminHostOperationError> result,
        bool clearNewHostLogin = false
    )
    {
        var succeeded = result.Match(
            outcome =>
            {
                ApplyHostOperation(outcome);
                return outcome is not AdminHostOperationOutcome.Rejected;
            },
            error =>
            {
                ApplyHostOperation(error);
                return false;
            }
        );
        if (clearNewHostLogin && succeeded)
        {
            _newHostLogin = string.Empty;
        }

        return Task.CompletedTask;
    }

    private void ApplyHostOperation(AdminHostOperationOutcome outcome)
    {
        switch (outcome)
        {
            case AdminHostOperationOutcome.Completed completed:
                _toasts.Publish(new ToastRequest<StatusToastStrategy>(completed.Message));
                _pendingRuntimeHostId = null;
                break;
            case AdminHostOperationOutcome.PendingRuntime pending:
                _toasts.Publish(new ToastRequest<StatusToastStrategy>(pending.Message));
                _pendingRuntimeHostId = pending.HostId;
                break;
            case AdminHostOperationOutcome.Rejected rejected:
                _toasts.Publish(new ToastRequest<ErrorToastStrategy>(rejected.Message));
                _pendingRuntimeHostId = null;
                break;
            default:
                throw new UnreachableException();
        }
    }

    private void ApplyHostOperation(AdminHostOperationError error)
    {
        var message = error switch
        {
            AdminHostOperationError.LookupUnavailable =>
                "Twitch could not look up that user. Try again.",
            AdminHostOperationError.BotTokenUnavailable =>
                "Connect the bot account before adding channels.",
            _ => throw new UnreachableException(),
        };
        _toasts.Publish(new ToastRequest<ErrorToastStrategy>(message));
        _pendingRuntimeHostId = null;
    }

    private async Task RemoveBlacklistAsync(string login)
    {
        await _siteAccess.RemoveEntryAsync(
            AccessListEntryKind.Blacklist,
            login,
            CancellationToken.None
        );
        await LoadAsync();
    }

    private async Task RemoveWhitelistAsync(string login)
    {
        await _siteAccess.RemoveEntryAsync(
            AccessListEntryKind.Whitelist,
            login,
            CancellationToken.None
        );
        await LoadAsync();
    }

    private async Task ToggleWhitelistAsync(ChangeEventArgs args)
    {
        await _siteAccess.SetWhitelistEnabledAsync(args.Value is true, CancellationToken.None);
        await LoadAsync();
    }
}
