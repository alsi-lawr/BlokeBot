using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

/// <summary>
/// Maps Channel Points redemption EventSub payloads to the bounded automation context. Every
/// context carries only the documented fields below; access tokens and raw transport headers are
/// never included.
/// <list type="bullet">
/// <item>Safe <c>redemption_id</c>, <c>reward_id</c>, <c>reward_title</c>, <c>reward_cost</c>,
/// <c>status</c> (<c>unfulfilled</c>, <c>fulfilled</c>, <c>canceled</c>, or <c>unknown</c>), and
/// <c>redeemed_at</c>.</item>
/// <item>Sensitive <c>user_input</c>: viewer-authored untrusted text, bounded at 500 characters —
/// Twitch's own message maximum, not a BlokeBot capability limit.</item>
/// <item>The redeeming viewer as the sensitive actor.</item>
/// </list>
/// </summary>
internal static class RedemptionAutomationContext
{
    internal static AutomationContext Create(
        BotHost host,
        EventSubRewardRedemptionEvent redemption,
        DateTimeOffset receivedAtUtc
    ) =>
        TwitchEventAutomationContext.Create(
            host,
            AutomationDefinitionIds.RewardRedemptionSource,
            TwitchEventAutomationContext.Actor(
                redemption.UserId,
                redemption.UserLogin,
                redemption.UserName
            ),
            stream: null,
            redemption.RedeemedAt,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("redemption_id")] = TwitchEventAutomationContext.SafeText(
                    TwitchEventAutomationContext.Bound(redemption.RedemptionId)
                ),
                [new("reward_id")] = TwitchEventAutomationContext.SafeText(
                    TwitchEventAutomationContext.Bound(redemption.RewardId)
                ),
                [new("reward_title")] = TwitchEventAutomationContext.SafeText(
                    TwitchEventAutomationContext.Bound(redemption.RewardTitle)
                ),
                [new("reward_cost")] = TwitchEventAutomationContext.SafeNumber(
                    redemption.RewardCost
                ),
                [new("user_input")] = TwitchEventAutomationContext.SensitiveText(
                    TwitchEventAutomationContext.Bound(redemption.UserInput)
                ),
                [new("status")] = TwitchEventAutomationContext.SafeText(
                    StatusToken(redemption.Status)
                ),
                [new("redeemed_at")] = TwitchEventAutomationContext.SafeTimestamp(
                    redemption.RedeemedAt
                ),
            }
        );

    internal static string StatusToken(HelixRewardRedemptionStatus status) =>
        status switch
        {
            HelixRewardRedemptionStatus.Unfulfilled => "unfulfilled",
            HelixRewardRedemptionStatus.Fulfilled => "fulfilled",
            HelixRewardRedemptionStatus.Canceled => "canceled",
            _ => "unknown",
        };
}
