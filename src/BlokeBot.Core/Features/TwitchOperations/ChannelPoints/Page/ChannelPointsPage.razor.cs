using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.ChannelPoints.Page;

public partial class ChannelPointsPage
{
    private ChannelPointsDashboardState? _state;
    private string? _editingRewardId;
    private string _rewardTitle = string.Empty;
    private string _rewardPrompt = string.Empty;
    private string _rewardCost = "100";
    private bool _rewardUserInput;
    private bool _rewardQueueSkip;
    private bool _rewardMaxPerStreamEnabled;
    private string _rewardMaxPerStream = string.Empty;
    private bool _rewardMaxPerUserPerStreamEnabled;
    private string _rewardMaxPerUserPerStream = string.Empty;
    private bool _rewardCooldownEnabled;
    private string _rewardCooldownSeconds = string.Empty;
    private string _rewardBackgroundColor = string.Empty;
    private bool _rewardEnabled = true;
    private bool _rewardPaused;
    private bool _nativeTwitchEnabled;
    private bool _loading = true;
    private bool _loadFailed;

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.TwitchOperationsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            _ = await LoadPageContextAsync();
            _nativeTwitchEnabled =
                HostId != 0
                && await _nativeTwitch.IsEnabledAsync(
                    HostId,
                    HostFeatureFlags.RewardsAndRedemptions,
                    CancellationToken.None
                );
            _state = _nativeTwitchEnabled
                ? await _channelPoints.LoadAsync(HostId, CancellationToken.None)
                : null;
        }
        catch (Exception exception)
        {
            _state = null;
            _nativeTwitchEnabled = false;
            _loadFailed = true;
            ReportUiFault(nameof(LoadAsync), exception);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SaveRewardAsync()
    {
        if (!int.TryParse(_rewardCost, out var cost))
        {
            Warn("Reward cost must be a whole number.");
            return;
        }
        var draft = Draft(cost);
        await MutateAsync(async hostId =>
        {
            var outcome = _editingRewardId is { } rewardId
                ? await _channelPoints.UpdateRewardAsync(
                    hostId,
                    rewardId,
                    draft,
                    _rewardEnabled,
                    _rewardPaused,
                    CancellationToken.None
                )
                : await _channelPoints.CreateRewardAsync(hostId, draft, CancellationToken.None);
            Publish(outcome);
            if (
                outcome
                is ChannelPointsOperationOutcome.RewardCreated
                    or ChannelPointsOperationOutcome.RewardUpdated
            )
            {
                ClearRewardEditor();
            }
        });
    }

    private void EditReward(ChannelPointsRewardView reward)
    {
        _editingRewardId = reward.ProviderRewardId;
        _rewardTitle = reward.Title;
        _rewardPrompt = reward.Prompt ?? string.Empty;
        _rewardCost = reward.Cost.ToString(CultureInfo.InvariantCulture);
        _rewardUserInput = reward.IsUserInputRequired;
        _rewardQueueSkip = reward.ShouldRedemptionsSkipRequestQueue;
        _rewardMaxPerStreamEnabled = reward.IsMaxPerStreamEnabled;
        _rewardMaxPerStream =
            reward.MaxPerStream?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _rewardMaxPerUserPerStreamEnabled = reward.IsMaxPerUserPerStreamEnabled;
        _rewardMaxPerUserPerStream =
            reward.MaxPerUserPerStream?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _rewardCooldownEnabled = reward.IsGlobalCooldownEnabled;
        _rewardCooldownSeconds =
            reward.GlobalCooldownSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _rewardBackgroundColor = reward.BackgroundColor ?? string.Empty;
        _rewardEnabled = reward.IsEnabled;
        _rewardPaused = reward.IsPaused;
    }

    private void ClearRewardEditor()
    {
        _editingRewardId = null;
        _rewardTitle = string.Empty;
        _rewardPrompt = string.Empty;
        _rewardCost = "100";
        _rewardUserInput = false;
        _rewardQueueSkip = false;
        _rewardMaxPerStreamEnabled = false;
        _rewardMaxPerStream = string.Empty;
        _rewardMaxPerUserPerStreamEnabled = false;
        _rewardMaxPerUserPerStream = string.Empty;
        _rewardCooldownEnabled = false;
        _rewardCooldownSeconds = string.Empty;
        _rewardBackgroundColor = string.Empty;
        _rewardEnabled = true;
        _rewardPaused = false;
    }

    private Task UpdateRewardAvailabilityAsync(
        ChannelPointsRewardView reward,
        bool isEnabled,
        bool isPaused
    ) =>
        MutateAsync(async hostId =>
            Publish(
                await _channelPoints.UpdateRewardAsync(
                    hostId,
                    reward.ProviderRewardId,
                    Draft(reward),
                    isEnabled,
                    isPaused,
                    CancellationToken.None
                )
            )
        );

    private async Task DeleteRewardAsync(string rewardId)
    {
        var confirmed = await _js.InvokeAsync<bool>(
            "confirm",
            [
                "Deleting this reward makes Twitch fulfil all outstanding unfulfilled redemptions. Cancel redemptions first if viewers should receive a refund.",
            ]
        );
        await MutateAsync(async hostId =>
            Publish(
                await _channelPoints.DeleteRewardAsync(
                    hostId,
                    rewardId,
                    confirmed,
                    CancellationToken.None
                )
            )
        );
    }

    private Task UpdateRedemptionAsync(string redemptionId, bool fulfill) =>
        MutateAsync(async hostId =>
            Publish(
                await _channelPoints.UpdateRedemptionAsync(
                    hostId,
                    redemptionId,
                    fulfill,
                    CancellationToken.None
                )
            )
        );

    private async Task MutateAsync(Func<int, Task> operation)
    {
        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                await operation(hostId);
                await LoadAsync();
            }
        );
    }

    private ChannelPointsRewardDraft Draft(int cost) =>
        new(
            _rewardTitle,
            _rewardPrompt,
            cost,
            _rewardUserInput,
            _rewardMaxPerStreamEnabled,
            ParseNullableInt(_rewardMaxPerStream),
            _rewardMaxPerUserPerStreamEnabled,
            ParseNullableInt(_rewardMaxPerUserPerStream),
            _rewardCooldownEnabled,
            ParseNullableInt(_rewardCooldownSeconds),
            _rewardQueueSkip,
            string.IsNullOrWhiteSpace(_rewardBackgroundColor) ? null : _rewardBackgroundColor
        );

    private static ChannelPointsRewardDraft Draft(ChannelPointsRewardView reward) =>
        new(
            reward.Title,
            reward.Prompt,
            reward.Cost,
            reward.IsUserInputRequired,
            reward.IsMaxPerStreamEnabled,
            reward.MaxPerStream,
            reward.IsMaxPerUserPerStreamEnabled,
            reward.MaxPerUserPerStream,
            reward.IsGlobalCooldownEnabled,
            reward.GlobalCooldownSeconds,
            reward.ShouldRedemptionsSkipRequestQueue,
            reward.BackgroundColor
        );

    private static int? ParseNullableInt(string value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private void Publish(ChannelPointsOperationOutcome outcome)
    {
        var (message, success) = outcome switch
        {
            ChannelPointsOperationOutcome.RewardCreated => ("Reward created.", true),
            ChannelPointsOperationOutcome.RewardUpdated => ("Reward updated.", true),
            ChannelPointsOperationOutcome.RewardDeleted => ("Reward deleted.", true),
            ChannelPointsOperationOutcome.RedemptionUpdated => ("Redemption updated.", true),
            ChannelPointsOperationOutcome.ConfirmationRequired confirmation => (
                confirmation.Message,
                false
            ),
            ChannelPointsOperationOutcome.NotReady => (
                "Reconnect this channel to Twitch, then try again.",
                false
            ),
            ChannelPointsOperationOutcome.Ineligible => (
                "Channel Points rewards are available after this channel becomes a Twitch Affiliate or Partner.",
                false
            ),
            ChannelPointsOperationOutcome.ExternalReadOnly => (
                "This Twitch reward is managed outside BlokeBot and is read-only.",
                false
            ),
            ChannelPointsOperationOutcome.RedemptionNotActionable => (
                "Only unfulfilled redemptions can be updated.",
                false
            ),
            ChannelPointsOperationOutcome.InvalidRequest invalid => (invalid.Message, false),
            ChannelPointsOperationOutcome.ProviderRejected => (
                "Twitch could not complete that reward action. Reload the page before trying again.",
                false
            ),
            _ => throw new UnreachableException(),
        };
        if (success)
        {
            _ = _toasts.Publish(new ToastRequest<SuccessToastStrategy>(message));
        }
        else
        {
            Warn(message);
        }
    }

    private void Warn(string message) =>
        _toasts.Publish(new ToastRequest<WarningToastStrategy>(message));
}
