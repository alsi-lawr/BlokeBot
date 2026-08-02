using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;

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
    private long _generalFocusRequest;
    private long _giveawaysFocusRequest;
    private string _validationFocusId = "gamblingCooldown";

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

    private Task LoadAsync() => ObserveUiOperationAsync(nameof(LoadAsync), LoadCoreAsync);

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

    private Task SaveAsync() => ObserveUiOperationAsync(nameof(SaveAsync), SaveCoreAsync);

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
                    switch (_validationErrors[0])
                    {
                        case PointsConfigurationValidationError.NegativeGamblingCooldown:
                            _validationFocusId = "gamblingCooldown";
                            _generalFocusRequest++;
                            break;
                        case PointsConfigurationValidationError.GiveawayDurationBelowMinimum:
                            _validationFocusId = "duration";
                            _giveawaysFocusRequest++;
                            break;
                        case PointsConfigurationValidationError.GiveawayWinnerCountBelowMinimum:
                            _validationFocusId = "winnerCount";
                            _giveawaysFocusRequest++;
                            break;
                        case PointsConfigurationValidationError.GiveawayCooldownBelowMinimum:
                            _validationFocusId = "cooldown";
                            _giveawaysFocusRequest++;
                            break;
                    }
                    _toasts.Publish(
                        new ToastRequest<ErrorToastStrategy>(
                            string.Join(" ", errors.Select(error => error.Message))
                        )
                    );
                    return Task.CompletedTask;
                }
            );
    }

    private async Task SaveCommandAsync(PointsConfigurationSaveCommand command) =>
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
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
                        _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>("Points settings saved.")
                        );
                    },
                    failure =>
                    {
                        _toasts.Publish(new ToastRequest<ErrorToastStrategy>(failure.Message));
                        return Task.CompletedTask;
                    }
                );
            }
        );
}
