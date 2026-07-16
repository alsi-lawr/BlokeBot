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
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Eventing;
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

namespace BlokeBot.Core.Features.Points.Configuration;

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
    private IReadOnlyList<PointsConfigurationValidationError> _validationErrors = [];

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
        _validationErrors = [];
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
                command =>
                {
                    _validationErrors = [];
                    return SaveCommandAsync(command);
                },
                errors =>
                {
                    _validationErrors = errors.ToArray();
                    _toasts.Publish(
                        new ToastRequest<ErrorToastStrategy>(
                            string.Join(" ", errors.Select(error => error.Message))
                        )
                    );
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
                _validationErrors = [];
                _toasts.Publish(new ToastRequest<SuccessToastStrategy>("Points settings saved."));
            },
            failure =>
            {
                _toasts.Publish(new ToastRequest<ErrorToastStrategy>(failure.Message));
                return Task.CompletedTask;
            }
        );
    }
}
