using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.ChannelPoints.Page;

public partial class ChannelPointsPage
{
    private readonly StudioOpenSet<ChannelPointsStage> _openStages = new();
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

    private enum ChannelPointsStage
    {
        Editor,
        Rewards,
        History,
    }

    protected override HostFeatureFlags Feature => HostFeatureFlags.RewardsAndRedemptions;

    protected override async Task<ChannelPointsDashboardState?> LoadStateAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var state = await _channelPoints.LoadAsync(hostId, cancellationToken);
        if (state is not null)
        {
            _openStages.SeedOnce(ChannelPointsStage.Rewards, state.Rewards.Count > 0);
        }

        return state;
    }

    private string _editorSummary =>
        _editingRewardId is null
            ? string.IsNullOrWhiteSpace(_rewardTitle)
                ? "Cost, limits, cooldown and a static tile preview"
                : $"New reward: “{_rewardTitle}” · {_rewardCost} Channel Points"
            : $"Editing “{_rewardTitle}” · {_rewardCost} Channel Points";

    private string _rewardsSummary =>
        State is not { Rewards.Count: > 0 } state
            ? "No rewards found"
            : $"{state.Rewards.Count} rewards · {state.Rewards.Count(static reward => reward.IsEnabled)} enabled";

    private string _historySummary =>
        State is not { History.Count: > 0 } state
            ? "No redemption history yet"
            : $"Latest: {state.History[0].RewardTitle} · {state.History[0].Status}";

    private string CooldownProse() =>
        int.TryParse(_rewardCooldownSeconds, out var seconds) && seconds > 0
            ? $"That is {DurationProse.Format(seconds)}."
            : "1 to 604,800 seconds.";

    private string PreviewTileColor() =>
        HexColour().IsMatch(_rewardBackgroundColor) ? _rewardBackgroundColor : "#9147FF";

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();

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
        _openStages.Open(ChannelPointsStage.Editor);
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
        Publish(message, success);
    }
}
