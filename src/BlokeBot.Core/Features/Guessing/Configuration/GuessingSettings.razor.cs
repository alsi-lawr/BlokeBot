using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

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
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                AppEventKind.HostedChannelsChanged,
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private Task LoadAsync() => ObserveUiOperationAsync(nameof(LoadAsync), LoadCoreAsync);

    private async Task LoadCoreAsync()
    {
        _ = await LoadPageContextAsync();
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

    private void RemoveOption(GuessOptionEditor option) => _config?.Profile.Options.Remove(option);

    private Task CreateProfileAsync() =>
        ObserveUiOperationAsync(nameof(CreateProfileAsync), CreateProfileCoreAsync);

    private async Task CreateProfileCoreAsync() =>
        await GuessingConfigurationValidator
            .ValidateNewProfile(_newProfileName)
            .Match(
                CreateProfileAsync,
                errors =>
                {
                    _ = _toasts.Publish(
                        new ToastRequest<WarningToastStrategy>(ValidationMessage(errors))
                    );
                    return Task.CompletedTask;
                }
            );

    private async Task CreateProfileAsync(GuessingProfileCreateCommand command) =>
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var selectedId = _config?.Profile.Id;
                var result = await _configuration
                    .CreateProfile(HostId, command)
                    .ExecuteAsync(CancellationToken.None);
                await result.Match(
                    async created =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>(created.Message)
                        );
                        _newProfileName = string.Empty;
                        await LoadConfigurationAsync(
                            selectedId is { } id
                                ? new GuessingProfileSelection.Selected(id)
                                : new GuessingProfileSelection.Default()
                        );
                    },
                    failure =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(failure.Message)
                        );
                        return Task.CompletedTask;
                    }
                );
            }
        );

    private Task DeleteProfileAsync() =>
        ObserveUiOperationAsync(nameof(DeleteProfileAsync), DeleteProfileCoreAsync);

    private Task DeleteProfileCoreAsync() =>
        _config is null
            ? Task.CompletedTask
            : GuessingConfigurationValidator
                .ValidateDelete(_config)
                .Match(
                    DeleteProfileAsync,
                    errors =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(ValidationMessage(errors))
                        );
                        return Task.CompletedTask;
                    }
                );

    private async Task DeleteProfileAsync(GuessingProfileDeleteCommand command) =>
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await _configuration
                    .DeleteProfile(HostId, command)
                    .ExecuteAsync(CancellationToken.None);
                await result.Match(
                    async deleted =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>(deleted.Message)
                        );
                        await LoadConfigurationAsync(new GuessingProfileSelection.Default());
                    },
                    async failure =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(failure.Message)
                        );
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
        );

    private Task SaveAsync() => ObserveUiOperationAsync(nameof(SaveAsync), SaveCoreAsync);

    private async Task SaveCoreAsync() =>
        _ = await TrySaveAsync(reloadAfterConcurrentFailure: true);

    private async Task<bool> TrySaveAsync(bool reloadAfterConcurrentFailure) =>
        _config switch
        {
            null => false,
            { } config => await GuessingConfigurationValidator
                .Validate(config)
                .Match(
                    command => SaveConfigurationAsync(command, reloadAfterConcurrentFailure),
                    errors =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<ErrorToastStrategy>(ValidationMessage(errors))
                        );
                        return Task.FromResult(false);
                    }
                ),
        };

    private async Task<bool> SaveConfigurationAsync(
        GuessingConfigurationSaveCommand command,
        bool reloadAfterConcurrentFailure
    )
    {
        var saved = false;
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await _configuration
                    .SaveConfiguration(HostId, command)
                    .ExecuteAsync(CancellationToken.None);
                saved = await result.Match(
                    async completed =>
                    {
                        await LoadConfigurationAsync(
                            new GuessingProfileSelection.Selected(command.ProfileId)
                        );
                        _ = _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>("Guessing settings saved.")
                        );
                        return true;
                    },
                    async failure =>
                    {
                        _ = _toasts.Publish(new ToastRequest<ErrorToastStrategy>(failure.Message));
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
        );
        return saved;
    }

    private Task SelectProfileAsync(ChangeEventArgs args) =>
        ObserveUiOperationAsync(nameof(SelectProfileAsync), () => SelectProfileCoreAsync(args));

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

    private Task SaveAndSwitchAsync() =>
        ObserveUiOperationAsync(nameof(SaveAndSwitchAsync), SaveAndSwitchCoreAsync);

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

    private Task DiscardAndSwitchAsync() =>
        ObserveUiOperationAsync(nameof(DiscardAndSwitchAsync), DiscardAndSwitchCoreAsync);

    private async Task DiscardAndSwitchCoreAsync()
    {
        if (_pendingProfileId is not { } profileId)
        {
            return;
        }

        _pendingProfileId = null;
        await LoadConfigurationAsync(new GuessingProfileSelection.Selected(profileId));
    }

    private void KeepEditing() => _pendingProfileId = null;

    private bool HasUnsavedChanges() =>
        _config is not null && _loadedDraft is not null && !_loadedDraft.Matches(_config);

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
                _ = _toasts.Publish(new ToastRequest<WarningToastStrategy>(failure.Message));
                var fallback = await _configuration
                    .LoadConfiguration(HostId, new GuessingProfileSelection.Default())
                    .ExecuteAsync(CancellationToken.None);
                _ = fallback.Match(
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
                        _ = _toasts.Publish(
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
    ) => string.Join(" ", errors.Select(static error => error.Message));
}
