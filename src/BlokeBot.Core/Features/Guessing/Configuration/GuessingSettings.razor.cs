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

namespace BlokeBot.Core.Features.Guessing.Configuration;

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
    private GuessingConfigurationDraftSnapshot? _loadedDraft;
    private string _newProfileName = string.Empty;
    private int? _pendingProfileId;

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
            _loadedDraft = null;
            _pendingProfileId = null;
            return;
        }

        _pendingProfileId = null;
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
        _ = await TrySaveAsync(reloadAfterConcurrentFailure: true);
    }

    private async Task<bool> TrySaveAsync(bool reloadAfterConcurrentFailure)
    {
        if (_config is null)
        {
            return false;
        }

        return await GuessingConfigurationValidator
            .Validate(_config)
            .Match(
                command => SaveConfigurationAsync(command, reloadAfterConcurrentFailure),
                errors =>
                {
                    _toasts.Publish(
                        new ToastRequest<ErrorToastStrategy>(ValidationMessage(errors))
                    );
                    return Task.FromResult(false);
                }
            );
    }

    private async Task<bool> SaveConfigurationAsync(
        GuessingConfigurationSaveCommand command,
        bool reloadAfterConcurrentFailure
    )
    {
        var result = await _configuration
            .SaveConfiguration(HostId, command)
            .ExecuteAsync(CancellationToken.None);
        return await result.Match(
            async _ =>
            {
                await LoadConfigurationAsync(
                    new GuessingProfileSelection.Selected(command.ProfileId)
                );
                _toasts.Publish(new ToastRequest<SuccessToastStrategy>("Guessing settings saved."));
                return true;
            },
            async failure =>
            {
                _toasts.Publish(new ToastRequest<ErrorToastStrategy>(failure.Message));
                if (
                    reloadAfterConcurrentFailure
                    && failure
                        is GuessingConfigurationSaveFailure.ProfileNotFound
                            or GuessingConfigurationSaveFailure.ConcurrentEdit
                )
                {
                    await LoadConfigurationAsync(
                        new GuessingProfileSelection.Selected(command.ProfileId)
                    );
                }

                return false;
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

        if (_config?.Profile.Id == profileId)
        {
            return;
        }

        if (HasUnsavedChanges())
        {
            _pendingProfileId = profileId;
            return;
        }

        await LoadConfigurationAsync(new GuessingProfileSelection.Selected(profileId));
    }

    private Task SaveAndSwitchAsync()
    {
        return ObserveUiOperationAsync(nameof(SaveAndSwitchAsync), SaveAndSwitchCoreAsync);
    }

    private async Task SaveAndSwitchCoreAsync()
    {
        if (_pendingProfileId is not { } profileId)
        {
            return;
        }

        if (!await TrySaveAsync(reloadAfterConcurrentFailure: false))
        {
            return;
        }

        _pendingProfileId = null;
        await LoadConfigurationAsync(new GuessingProfileSelection.Selected(profileId));
    }

    private Task DiscardAndSwitchAsync()
    {
        return ObserveUiOperationAsync(nameof(DiscardAndSwitchAsync), DiscardAndSwitchCoreAsync);
    }

    private async Task DiscardAndSwitchCoreAsync()
    {
        if (_pendingProfileId is not { } profileId)
        {
            return;
        }

        _pendingProfileId = null;
        await LoadConfigurationAsync(new GuessingProfileSelection.Selected(profileId));
    }

    private void KeepEditing()
    {
        _pendingProfileId = null;
    }

    private bool HasUnsavedChanges()
    {
        return _config is not null && _loadedDraft is not null && !_loadedDraft.Matches(_config);
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
                _loadedDraft = GuessingConfigurationDraftSnapshot.Capture(draft);
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
                        _loadedDraft = GuessingConfigurationDraftSnapshot.Capture(draft);
                        return true;
                    },
                    fallbackFailure =>
                    {
                        _config = null;
                        _loadedDraft = null;
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
