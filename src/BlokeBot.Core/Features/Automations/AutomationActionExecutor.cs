using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal abstract record AutomationActionOutcome
{
    private AutomationActionOutcome() { }

    internal sealed record Succeeded : AutomationActionOutcome;

    internal sealed record Failed(string Code) : AutomationActionOutcome;
}

public sealed class AutomationActionExecutor(
    HostFeatureService features,
    IPublicChatMessageSender chat,
    IOverlayCueAdmissionService overlayCues,
    AutomationExpressionService expressions,
    IDbContextFactory<BlokeBotDbContext>? dbFactory = null,
    IChannelPointsDashboardOperations? channelPoints = null
)
{
    internal async Task<AutomationActionOutcome> ExecuteAsync(
        AutomationHostId hostId,
        AutomationConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return configuration switch
            {
                SendChatActionConfiguration sendChat => await SendChatAsync(
                    hostId,
                    sendChat,
                    fields,
                    context,
                    cancellationToken
                ),
                PlayOverlayCueActionConfiguration cue => await PlayCueAsync(
                    hostId,
                    cue,
                    fields,
                    context,
                    cancellationToken
                ),
                FulfilRedemptionActionConfiguration => await UpdateRedemptionAsync(
                    hostId,
                    context,
                    fulfill: true,
                    cancellationToken
                ),
                CancelRedemptionActionConfiguration => await UpdateRedemptionAsync(
                    hostId,
                    context,
                    fulfill: false,
                    cancellationToken
                ),
                _ => new AutomationActionOutcome.Failed("action-unsupported"),
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new AutomationActionOutcome.Failed("action-failed");
        }
    }

    private async Task<AutomationActionOutcome> SendChatAsync(
        AutomationHostId hostId,
        SendChatActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.None, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        var message = fields.TryGetValue(new("message"), out var expression)
            ? expressions.Evaluate(expression, context)
            : expressions.Interpolate(configuration.Message, context);
        if (
            message is not AutomationExpressionResult.Value value
            || value.Result is not string text
        )
        {
            return new AutomationActionOutcome.Failed("action-expression-invalid");
        }

        if (value.UsesSensitiveValues)
        {
            return new AutomationActionOutcome.Failed("sensitive-output-blocked");
        }

        var outcome = await chat.SendAsync(
            context.Channel.Login,
            text,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
        return outcome.Match<AutomationActionOutcome>(
            static _ => new AutomationActionOutcome.Succeeded(),
            static _ => new AutomationActionOutcome.Failed("chat-rejected")
        );
    }

    private async Task<AutomationActionOutcome> PlayCueAsync(
        AutomationHostId hostId,
        PlayOverlayCueActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.Overlays, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        var targetId = configuration.TargetId.Value;
        var cueId = configuration.CueId.Value;
        if (
            !TryEvaluateGuid(fields, "target-id", targetId, context, out targetId)
            || !TryEvaluateGuid(fields, "cue-id", cueId, context, out cueId)
        )
        {
            return new AutomationActionOutcome.Failed("action-expression-invalid");
        }

        var references = await overlayCues.ResolveReferencesAsync(
            new(hostId.Value, targetId, cueId),
            cancellationToken
        );
        if (
            references is not OverlayCueReferenceOutcome.Available
            || !await IsEnabledAsync(hostId, HostFeatureFlags.Overlays, cancellationToken)
        )
        {
            return new AutomationActionOutcome.Failed("overlay-reference-unavailable");
        }

        var admissionCatalog = await overlayCues.QueryCatalogAsync(hostId.Value, cancellationToken);
        var cue = admissionCatalog.Cues.SingleOrDefault(value => value.Id == cueId);
        if (cue is null)
        {
            return new AutomationActionOutcome.Failed("overlay-reference-unavailable");
        }

        var admission = await overlayCues.AdmitAsync(
            new(
                hostId.Value,
                targetId,
                cueId,
                cue.DefaultQueuePolicy,
                OverlayCueAdmissionOrigin.Automation,
                OverlayCueSafeContext.Empty
            ),
            cancellationToken
        );
        return admission is OverlayCueAdmissionOutcome.Running or OverlayCueAdmissionOutcome.Queued
            ? new AutomationActionOutcome.Succeeded()
            : new AutomationActionOutcome.Failed("overlay-rejected");
    }

    private async Task<AutomationActionOutcome> UpdateRedemptionAsync(
        AutomationHostId hostId,
        AutomationContext context,
        bool fulfill,
        CancellationToken cancellationToken
    )
    {
        // Both the Automations and Rewards & redemptions switches must be on before any effect.
        if (
            !await IsEnabledAsync(hostId, HostFeatureFlags.RewardsAndRedemptions, cancellationToken)
        )
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        if (
            context.Event.SourceDefinitionId != AutomationDefinitionIds.RewardRedemptionSource
            || !TryReadTextVariable(context, "redemption_id", out var redemptionId)
            || !TryReadTextVariable(context, "reward_id", out var rewardId)
        )
        {
            return new AutomationActionOutcome.Failed("redemption-context-missing");
        }

        if (dbFactory is null || channelPoints is null)
        {
            return new AutomationActionOutcome.Failed("action-unsupported");
        }

        // Reward manageability is validated before any Twitch API call or state mutation;
        // externally created rewards are rejected here without partial execution.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var manageable = await db
            .TwitchCustomRewards.AsNoTracking()
            .AnyAsync(
                reward =>
                    reward.HostId == hostId.Value
                    && reward.ProviderRewardId == rewardId
                    && reward.IsManageable,
                cancellationToken
            );
        if (!manageable)
        {
            return new AutomationActionOutcome.Failed("reward-not-manageable");
        }

        var outcome = await channelPoints.UpdateRedemptionAsync(
            hostId.Value,
            redemptionId,
            fulfill,
            cancellationToken
        );
        return outcome switch
        {
            ChannelPointsOperationOutcome.RedemptionUpdated =>
                new AutomationActionOutcome.Succeeded(),
            ChannelPointsOperationOutcome.ExternalReadOnly => new AutomationActionOutcome.Failed(
                "reward-not-manageable"
            ),
            ChannelPointsOperationOutcome.RedemptionNotActionable =>
                new AutomationActionOutcome.Failed("redemption-not-actionable"),
            ChannelPointsOperationOutcome.NotReady => new AutomationActionOutcome.Failed(
                "broadcaster-authorization-missing"
            ),
            ChannelPointsOperationOutcome.Ineligible => new AutomationActionOutcome.Failed(
                "channel-ineligible"
            ),
            _ => new AutomationActionOutcome.Failed("redemption-rejected"),
        };
    }

    private static bool TryReadTextVariable(
        AutomationContext context,
        string name,
        out string value
    )
    {
        value = string.Empty;
        if (
            context.Variables.ForExecution().TryGetValue(new(name), out var variable)
            && variable.Value is AutomationValue.Text { Value: var text }
            && !string.IsNullOrWhiteSpace(text)
        )
        {
            value = text;
            return true;
        }

        return false;
    }

    private bool TryEvaluateGuid(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        string fieldId,
        Guid configured,
        AutomationContext context,
        out Guid value
    )
    {
        value = configured;
        if (!fields.TryGetValue(new(fieldId), out var expression))
        {
            return true;
        }

        var evaluated = expressions.Evaluate(expression, context);
        return evaluated is AutomationExpressionResult.Value { Result: { } result }
            && Guid.TryParse(
                Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture),
                out value
            );
    }

    private async Task<bool> IsEnabledAsync(
        AutomationHostId hostId,
        HostFeatureFlags secondary,
        CancellationToken cancellationToken
    )
    {
        var enabled = await features.Load(hostId.Value).RunAsync(cancellationToken);
        return enabled.Match(
            flags =>
                flags.Contains(HostFeatureFlags.Automations)
                && (secondary == HostFeatureFlags.None || flags.Contains(secondary)),
            static () => false
        );
    }
}
