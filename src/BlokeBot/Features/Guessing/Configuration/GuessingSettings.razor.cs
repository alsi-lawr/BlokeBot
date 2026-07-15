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
                HostFeatureFlags.Guessing,
                CancellationToken.None
            );
        if (!_featureEnabled)
        {
            _config = null;
            return;
        }

        await LoadConfigurationAsync(new GuessingProfileSelection.Default());
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
                ReplyTarget = _config.Profile.WhisperAnswerReplies
                    ? ReplyDeliveryTarget.Whisper
                    : ReplyDeliveryTarget.Chat,
            }
        );
    }

    private void RemoveOption(GuessOptionEditor option)
    {
        _config?.Profile.Options.Remove(option);
    }

    private Task CreateProfileAsync()
    {
        return ObserveUiOperationAsync(nameof(CreateProfileAsync), CreateProfileCoreAsync);
    }

    private async Task CreateProfileCoreAsync()
    {
        await GuessingConfigurationValidator
            .ValidateNewProfile(_newProfileName)
            .Match(
                CreateProfileAsync,
                errors =>
                {
                    _toasts.Publish(
                        new ToastRequest<WarningToastStrategy>(ValidationMessage(errors))
                    );
                    return Task.CompletedTask;
                }
            );
    }

    private async Task CreateProfileAsync(GuessingProfileCreateCommand command)
    {
        var selectedId = _config?.Profile.Id;
        var result = await _configuration
            .CreateProfile(HostId, command)
            .ExecuteAsync(CancellationToken.None);
        await result.Match(
            async created =>
            {
                _toasts.Publish(new ToastRequest<SuccessToastStrategy>(created.Message));
                _newProfileName = string.Empty;
                await LoadConfigurationAsync(
                    selectedId is { } id
                        ? new GuessingProfileSelection.Selected(id)
                        : new GuessingProfileSelection.Default()
                );
            },
            failure =>
            {
                _toasts.Publish(new ToastRequest<WarningToastStrategy>(failure.Message));
                return Task.CompletedTask;
            }
        );
    }

    private Task DeleteProfileAsync()
    {
        return ObserveUiOperationAsync(nameof(DeleteProfileAsync), DeleteProfileCoreAsync);
    }

    private Task DeleteProfileCoreAsync()
    {
        if (_config is null)
        {
            return Task.CompletedTask;
        }

        return GuessingConfigurationValidator
            .ValidateDelete(_config)
            .Match(
                DeleteProfileAsync,
                errors =>
                {
                    _toasts.Publish(
                        new ToastRequest<WarningToastStrategy>(ValidationMessage(errors))
                    );
                    return Task.CompletedTask;
                }
            );
    }

    private async Task DeleteProfileAsync(GuessingProfileDeleteCommand command)
    {
        var result = await _configuration
            .DeleteProfile(HostId, command)
            .ExecuteAsync(CancellationToken.None);
        await result.Match(
            async deleted =>
            {
                _toasts.Publish(new ToastRequest<SuccessToastStrategy>(deleted.Message));
                await LoadConfigurationAsync(new GuessingProfileSelection.Default());
            },
            async failure =>
            {
                _toasts.Publish(new ToastRequest<WarningToastStrategy>(failure.Message));
                if (
                    failure
                    is GuessingProfileDeleteFailure.ProfileNotFound
                        or GuessingProfileDeleteFailure.ConcurrentEdit
                )
                {
                    await LoadConfigurationAsync(new GuessingProfileSelection.Default());
                }
            }
        );
    }

    private Task SaveAsync()
    {
        return ObserveUiOperationAsync(nameof(SaveAsync), SaveCoreAsync);
    }

    private async Task SaveCoreAsync()
    {
        if (_config is null)
        {
            return;
        }

        await GuessingConfigurationValidator
            .Validate(_config)
            .Match(
                SaveConfigurationAsync,
                errors =>
                {
                    _toasts.Publish(
                        new ToastRequest<ErrorToastStrategy>(ValidationMessage(errors))
                    );
                    return Task.CompletedTask;
                }
            );
    }

    private async Task SaveConfigurationAsync(GuessingConfigurationSaveCommand command)
    {
        var result = await _configuration
            .SaveConfiguration(HostId, command)
            .ExecuteAsync(CancellationToken.None);
        await result.Match(
            async _ =>
            {
                await LoadConfigurationAsync(
                    new GuessingProfileSelection.Selected(command.ProfileId)
                );
                _toasts.Publish(new ToastRequest<SuccessToastStrategy>("Guessing settings saved."));
            },
            async failure =>
            {
                _toasts.Publish(new ToastRequest<ErrorToastStrategy>(failure.Message));
                if (
                    failure
                    is GuessingConfigurationSaveFailure.ProfileNotFound
                        or GuessingConfigurationSaveFailure.ConcurrentEdit
                )
                {
                    await LoadConfigurationAsync(
                        new GuessingProfileSelection.Selected(command.ProfileId)
                    );
                }
            }
        );
    }

    private Task SelectProfileAsync(ChangeEventArgs args)
    {
        return ObserveUiOperationAsync(
            nameof(SelectProfileAsync),
            () => SelectProfileCoreAsync(args)
        );
    }

    private async Task SelectProfileCoreAsync(ChangeEventArgs args)
    {
        if (!int.TryParse(args.Value?.ToString(), out var profileId))
        {
            return;
        }

        await LoadConfigurationAsync(new GuessingProfileSelection.Selected(profileId));
    }

    private async Task LoadConfigurationAsync(GuessingProfileSelection selection)
    {
        var result = await _configuration
            .LoadConfiguration(HostId, selection)
            .ExecuteAsync(CancellationToken.None);
        await result.Match(
            draft =>
            {
                _config = draft;
                return Task.CompletedTask;
            },
            async failure =>
            {
                _toasts.Publish(new ToastRequest<WarningToastStrategy>(failure.Message));
                var fallback = await _configuration
                    .LoadConfiguration(HostId, new GuessingProfileSelection.Default())
                    .ExecuteAsync(CancellationToken.None);
                fallback.Match(
                    draft =>
                    {
                        _config = draft;
                        return true;
                    },
                    fallbackFailure =>
                    {
                        _config = null;
                        _toasts.Publish(
                            new ToastRequest<ErrorToastStrategy>(fallbackFailure.Message)
                        );
                        return false;
                    }
                );
            }
        );
    }

    private static string ValidationMessage(
        IReadOnlyList<GuessingConfigurationValidationError> errors
    )
    {
        return string.Join(" ", errors.Select(error => error.Message));
    }
}
