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
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.Points.Replies;
using BlokeBot.Features.Replies;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.Points.Configuration;

public partial class PointsConfigurationPage
{
    private static readonly IReadOnlyList<ReplyDeliveryOption> _whisperReplyOptions =
    [
        new("Balance", PointsReplyKeys.Balance),
        new("Another viewer's balance", PointsReplyKeys.OtherBalance),
        new("Points given", PointsReplyKeys.Transfer),
        new("Moderator adds points", PointsReplyKeys.Add),
        new("Moderator removes points", PointsReplyKeys.Remove),
        new("Amount not understood", PointsReplyKeys.InvalidAmount),
        new("Not enough points", PointsReplyKeys.InsufficientBalance),
        new("Only moderators can use this", PointsReplyKeys.ModeratorOnly),
        new("Giveaway joined", PointsReplyKeys.GiveawayJoined),
        new("Already joined", PointsReplyKeys.GiveawayAlreadyJoined),
        new("Giveaway already running", PointsReplyKeys.GiveawayAlreadyActive),
        new("No giveaway running", PointsReplyKeys.GiveawayNotActive),
        new("Giveaway used too recently", PointsReplyKeys.GiveawayCooldown),
        new("Stream is offline", PointsReplyKeys.StreamOffline),
        new("Viewer cannot enter", PointsReplyKeys.NotEligible),
        new("Follower check unavailable", PointsReplyKeys.FollowerEligibilityUnavailable),
    ];

    private PointsConfiguration? _config;
    private bool _featureEnabled;

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            _events.SubscribeForComponentRefresh(
                AppEventKind.HostedChannelsChanged,
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private Task LoadAsync()
    {
        return ObserveUiOperationAsync(nameof(LoadAsync), LoadCoreAsync);
    }

    private async Task LoadCoreAsync()
    {
        await LoadPageContextAsync();
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Points,
                CancellationToken.None
            );
        _config = _featureEnabled
            ? await _configuration.LoadConfigurationAsync(HostId, CancellationToken.None)
            : null;
    }

    private Task SaveAsync()
    {
        return ObserveUiOperationAsync(nameof(SaveAsync), SaveCoreAsync);
    }

    private async Task SaveCoreAsync()
    {
        if (_config is null || HostId == 0)
        {
            return;
        }

        await PointsConfigurationValidator
            .Validate(_config)
            .Match(
                SaveCommandAsync,
                errors =>
                {
                    _toasts.Error(string.Join(" ", errors.Select(error => error.Message)));
                    return Task.CompletedTask;
                }
            );
    }

    private async Task SaveCommandAsync(PointsConfigurationSaveCommand command)
    {
        var result = await _configuration
            .SaveConfiguration(HostId, command)
            .ExecuteAsync(CancellationToken.None);
        await result.Match(
            async _ =>
            {
                _config = await _configuration.LoadConfigurationAsync(
                    HostId,
                    CancellationToken.None
                );
                _toasts.Success("Points settings saved.");
            },
            failure =>
            {
                _toasts.Error(failure.Message);
                return Task.CompletedTask;
            }
        );
    }
}
