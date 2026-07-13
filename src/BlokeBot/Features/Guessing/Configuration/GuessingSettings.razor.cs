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

namespace BlokeBot.Features.Guessing.Configuration;

public partial class GuessingSettings
{
    private static readonly IReadOnlyList<ReplyDeliveryOption> _whisperReplyOptions =
    [
        new("Round already running", GuessingReplyKeys.RoundAlreadyOpen),
        new("No round running", GuessingReplyKeys.NoOpenRound),
        new("Guessing already stopped", GuessingReplyKeys.GuessingAlreadyStopped),
        new("Guessing closed", GuessingReplyKeys.GuessingClosed),
        new("Invalid guess", GuessingReplyKeys.InvalidGuess),
        new("How to guess", GuessingReplyKeys.GuessUsage),
        new("Available guesses", GuessingReplyKeys.AvailableGuesses),
        new("How to choose a winner", GuessingReplyKeys.WinUsage),
        new("Only moderators can use this", GuessingReplyKeys.ModeratorOnly),
    ];

    private GuessingConfiguration? _config;
    private bool _featureEnabled;
    private string _newProfileName = string.Empty;

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

    private async Task LoadAsync()
    {
        await LoadPageContextAsync();
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Guessing,
                CancellationToken.None
            );
        _config = _featureEnabled
            ? await _configuration.LoadConfigurationAsync(HostId, null, CancellationToken.None)
            : null;
    }

    private void AddOption()
    {
        if (_config is null)
        {
            return;
        }

        _config.Profile.Options.Add(
            new GuessOptionEditor
            {
                ReplyTarget = ReplyDeliveryTargets.FromCommandTarget(
                    _config.Profile.WhisperAnswerReplies
                        ? CommandResponseTarget.Whisper
                        : CommandResponseTarget.Chat
                ),
            }
        );
    }

    private void RemoveOption(GuessOptionEditor option)
    {
        _config?.Profile.Options.Remove(option);
    }

    private async Task CreateProfileAsync()
    {
        var result = await _configuration.CreateProfileAsync(
            HostId,
            _newProfileName,
            CancellationToken.None
        );
        PublishResult(result);
        _newProfileName = string.Empty;
        _config = await _configuration.LoadConfigurationAsync(HostId, null, CancellationToken.None);
    }

    private async Task DeleteProfileAsync()
    {
        if (_config is null)
        {
            return;
        }

        var result = await _configuration.DeleteProfileAsync(
            HostId,
            _config.Profile.Id,
            CancellationToken.None
        );
        PublishResult(result);
        _config = await _configuration.LoadConfigurationAsync(HostId, null, CancellationToken.None);
    }

    private async Task SaveAsync()
    {
        if (_config is null)
        {
            return;
        }

        try
        {
            await _configuration.SaveConfigurationAsync(HostId, _config, CancellationToken.None);
            var selectedId = _config.Profile.Id;
            _config = await _configuration.LoadConfigurationAsync(
                HostId,
                selectedId,
                CancellationToken.None
            );
            _toasts.Success("Guessing settings saved.");
        }
        catch (InvalidOperationException ex)
        {
            _toasts.Error(ex.Message);
        }
    }

    private async Task SelectProfileAsync(ChangeEventArgs args)
    {
        if (!int.TryParse(args.Value?.ToString(), out var profileId))
        {
            return;
        }

        _config = await _configuration.LoadConfigurationAsync(
            HostId,
            profileId,
            CancellationToken.None
        );
    }

    private void PublishResult(GuessingOperationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
        {
            return;
        }

        _toasts.Publish(result.Succeeded ? ToastKind.Success : ToastKind.Warning, result.Message);
    }
}
