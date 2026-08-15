using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

/// <summary>
/// Applies the redemption source's completion policy when a redemption-started flow reaches a
/// terminal outcome: fulfil the redemption when the flow completes under fulfil-on-success, or
/// cancel it when the flow fails under cancel-on-failure. Updates run only while both the
/// Automations and Rewards &amp; redemptions switches are on and only for BlokeBot-manageable
/// rewards; the manageability check runs before any Twitch call. Invalidated runs are never
/// reported here, so disabled features cause zero redemption mutations, and every policy outcome
/// is logged explicitly so a failed update is never mistaken for a refund.
/// </summary>
internal sealed class RedemptionCompletionPolicyObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostFeatureService features,
    IChannelPointsDashboardOperations? channelPoints = null,
    ILogger<RedemptionCompletionPolicyObserver>? log = null
) : IAutomationRunCompletionObserver
{
    private readonly ILogger _log =
        log
        ?? Microsoft
            .Extensions
            .Logging
            .Abstractions
            .NullLogger<RedemptionCompletionPolicyObserver>
            .Instance;

    public async Task RunFinishedAsync(
        AutomationRunId runId,
        AutomationResumeStatus status,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await ApplyPolicyAsync(runId, status, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.LogError(
                "Redemption completion policy for run {RunId} failed with {FailureType}.",
                runId.Value,
                exception.GetType().Name
            );
        }
    }

    private async Task ApplyPolicyAsync(
        AutomationRunId runId,
        AutomationResumeStatus status,
        CancellationToken cancellationToken
    )
    {
        if (
            channelPoints is null
            || status is not (AutomationResumeStatus.Completed or AutomationResumeStatus.Failed)
        )
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db
            .AutomationFlowRuns.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == runId.Value, cancellationToken);
        if (
            run is null
            || run.SourceDefinitionId != AutomationDefinitionIds.RewardRedemptionSource.Value
        )
        {
            return;
        }

        if (ReadPolicy(run.DefinitionJson, run.SourceNodeId) is not { } policy)
        {
            return;
        }

        bool? update = policy switch
        {
            RedemptionCompletionPolicy.FulfilOnSuccess
                when status == AutomationResumeStatus.Completed => true,
            RedemptionCompletionPolicy.CancelOnFailure
                when status == AutomationResumeStatus.Failed => false,
            _ => null,
        };
        if (update is not { } fulfill)
        {
            return;
        }

        if (
            AutomationRuntimeSerialization.RestoreContext(run.ContextSchemaVersion, run.ContextJson)
                is not AutomationContextRestoreOutcome.Available available
            || !TryReadTextVariable(available.Context, "redemption_id", out var redemptionId)
            || !TryReadTextVariable(available.Context, "reward_id", out var rewardId)
        )
        {
            return;
        }

        // Zero mutation while either switch is off; suppressed policies are never replayed.
        var enabledFeatures = await features.Load(run.HostId).RunAsync(cancellationToken);
        var enabled = enabledFeatures.Match(
            static flags =>
                flags.Contains(
                    HostFeatureFlags.Automations | HostFeatureFlags.RewardsAndRedemptions
                ),
            static () => false
        );
        if (!enabled)
        {
            return;
        }

        // Reward manageability is validated before any Twitch API call; externally created
        // rewards never receive automatic status updates.
        var manageable = await db
            .TwitchCustomRewards.AsNoTracking()
            .AnyAsync(
                reward =>
                    reward.HostId == run.HostId
                    && reward.ProviderRewardId == rewardId
                    && reward.IsManageable,
                cancellationToken
            );
        if (!manageable)
        {
            _log.LogWarning(
                "Redemption completion policy for run {RunId} skipped: the reward is not manageable by BlokeBot.",
                runId.Value
            );
            return;
        }

        var outcome = await channelPoints.UpdateRedemptionAsync(
            run.HostId,
            redemptionId,
            fulfill,
            cancellationToken
        );
        if (outcome is ChannelPointsOperationOutcome.RedemptionUpdated)
        {
            _log.LogInformation(
                "Redemption completion policy {Policy} updated redemption {RedemptionId} for run {RunId}.",
                fulfill ? "fulfil-on-success" : "cancel-on-failure",
                redemptionId,
                runId.Value
            );
        }
        else
        {
            _log.LogWarning(
                "Redemption completion policy {Policy} did not update redemption {RedemptionId} for run {RunId}: {Outcome}. The redemption status is unchanged on Twitch.",
                fulfill ? "fulfil-on-success" : "cancel-on-failure",
                redemptionId,
                runId.Value,
                outcome.GetType().Name
            );
        }
    }

    private static RedemptionCompletionPolicy? ReadPolicy(string definitionJson, Guid sourceNodeId)
    {
        var flow = AutomationRuntimeSerialization.DeserializeDefinition(definitionJson);
        var sources = flow
            .Nodes.Where(node =>
                node.DefinitionId == AutomationDefinitionIds.RewardRedemptionSource.Value
            )
            .ToArray();
        var source =
            sourceNodeId == Guid.Empty
                ? sources.Length == 1
                    ? sources[0]
                    : null
                : sources.FirstOrDefault(node => node.Id == sourceNodeId);
        if (source is null)
        {
            return null;
        }

        using var configuration = JsonDocument.Parse(source.ConfigurationJson);
        return
            configuration.RootElement.ValueKind == JsonValueKind.Object
            && configuration.RootElement.TryGetProperty("completion-policy", out var policy)
            && policy.ValueKind == JsonValueKind.String
            ? TwitchEventAutomationSources.ParseCompletionPolicy(policy.GetString() ?? string.Empty)
            : null;
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
}
