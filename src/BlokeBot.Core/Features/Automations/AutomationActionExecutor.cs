using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
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
    IChannelPointsDashboardOperations? channelPoints = null,
    IShoutoutDashboardOperations? shoutouts = null,
    IClipMarkerDashboardOperations? clipsMarkers = null,
    IPollAutomationOperations? polls = null,
    IPredictionAutomationOperations? predictions = null
)
{
    internal async Task<AutomationActionOutcome> ExecuteAsync(
        AutomationHostId hostId,
        AutomationConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
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
                    inputs,
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
                SendShoutoutActionConfiguration => await SendShoutoutAsync(
                    hostId,
                    context,
                    cancellationToken
                ),
                StartPollActionConfiguration startPoll => await StartPollAsync(
                    hostId,
                    startPoll,
                    fields,
                    context,
                    cancellationToken
                ),
                EndPollActionConfiguration => await EndPollAsync(hostId, cancellationToken),
                CreateClipActionConfiguration createClip => await CreateClipAsync(
                    hostId,
                    createClip,
                    cancellationToken
                ),
                CreateMarkerActionConfiguration createMarker => await CreateMarkerAsync(
                    hostId,
                    createMarker,
                    fields,
                    context,
                    cancellationToken
                ),
                StartPredictionActionConfiguration startPrediction => await StartPredictionAsync(
                    hostId,
                    startPrediction,
                    fields,
                    context,
                    cancellationToken
                ),
                LockPredictionActionConfiguration => await EndPredictionAsync(
                    hostId,
                    winningOutcomeId: null,
                    cancel: false,
                    cancellationToken
                ),
                CancelPredictionActionConfiguration => await EndPredictionAsync(
                    hostId,
                    winningOutcomeId: null,
                    cancel: true,
                    cancellationToken
                ),
                ResolvePredictionActionConfiguration resolvePrediction =>
                    await ResolvePredictionAsync(
                        hostId,
                        resolvePrediction,
                        fields,
                        context,
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
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.None, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        AutomationPublicTextAdmission admission;
        if (inputs.TryGetValue(new("message"), out var input))
        {
            admission = AutomationPublicSinkAdmission.AdmitText(input);
        }
        else
        {
            var message = fields.TryGetValue(new("message"), out var expression)
                ? expressions.Evaluate(expression, context)
                : expressions.Interpolate(configuration.Message, context);
            if (message is not AutomationExpressionResult.Value value)
            {
                return new AutomationActionOutcome.Failed("action-expression-invalid");
            }

            admission = AutomationPublicSinkAdmission.AdmitText(value);
        }

        if (admission is not AutomationPublicTextAdmission.Admitted admitted)
        {
            return new AutomationActionOutcome.Failed("sensitive-output-blocked");
        }

        var outcome = await chat.SendAsync(
            context.Channel.Login,
            admitted.Text,
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

    private async Task<AutomationActionOutcome> SendShoutoutAsync(
        AutomationHostId hostId,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.RaidCollaboration, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        if (shoutouts is null)
        {
            return new AutomationActionOutcome.Failed("action-unsupported");
        }

        if (context.Actor is not { } actor || string.IsNullOrWhiteSpace(actor.Login))
        {
            return new AutomationActionOutcome.Failed("shoutout-target-missing");
        }

        var outcome = await shoutouts.SendAsync(hostId.Value, actor.Login, cancellationToken);
        return outcome switch
        {
            ShoutoutOperationOutcome.Sent => new AutomationActionOutcome.Succeeded(),
            ShoutoutOperationOutcome.TargetNotFound => new AutomationActionOutcome.Failed(
                "shoutout-target-not-found"
            ),
            ShoutoutOperationOutcome.SelfTarget => new AutomationActionOutcome.Failed(
                "shoutout-self-target"
            ),
            ShoutoutOperationOutcome.TargetOffline => new AutomationActionOutcome.Failed(
                "shoutout-target-offline"
            ),
            ShoutoutOperationOutcome.CooldownActive or ShoutoutOperationOutcome.CooldownUnknown =>
                new AutomationActionOutcome.Failed("shoutout-cooldown-active"),
            ShoutoutOperationOutcome.NotReady => new AutomationActionOutcome.Failed(
                "shoutout-not-ready"
            ),
            _ => new AutomationActionOutcome.Failed("shoutout-rejected"),
        };
    }

    private async Task<AutomationActionOutcome> StartPollAsync(
        AutomationHostId hostId,
        StartPollActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.Polls, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        if (polls is null)
        {
            return new AutomationActionOutcome.Failed("action-unsupported");
        }

        if (
            ResolveText(fields, "title", configuration.Title, context, out var title) is
            { } titleFailure
        )
        {
            return titleFailure;
        }

        if (
            ResolveLines(fields, "choices", configuration.Choices, context, out var choices) is
            { } choicesFailure
        )
        {
            return choicesFailure;
        }

        var outcome = await polls.StartAsync(
            hostId.Value,
            new PollTemplateDraft(
                title,
                choices,
                configuration.DurationSeconds,
                configuration.ChannelPointsPerVote is not null,
                configuration.ChannelPointsPerVote
            ),
            cancellationToken
        );
        return outcome switch
        {
            PollOperationOutcome.Started => new AutomationActionOutcome.Succeeded(),
            PollOperationOutcome.ActivePollExists => new AutomationActionOutcome.Failed(
                "poll-already-active"
            ),
            PollOperationOutcome.InvalidTemplate => new AutomationActionOutcome.Failed(
                "poll-invalid"
            ),
            PollOperationOutcome.NotReady => new AutomationActionOutcome.Failed(
                "broadcaster-authorization-missing"
            ),
            _ => new AutomationActionOutcome.Failed("poll-rejected"),
        };
    }

    private async Task<AutomationActionOutcome> EndPollAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.Polls, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        if (polls is null)
        {
            return new AutomationActionOutcome.Failed("action-unsupported");
        }

        var outcome = await polls.EndAsync(
            hostId.Value,
            confirmedExternal: false,
            cancellationToken
        );
        return outcome switch
        {
            PollOperationOutcome.Ended => new AutomationActionOutcome.Succeeded(),
            PollOperationOutcome.ConfirmationRequired => new AutomationActionOutcome.Failed(
                "poll-externally-started"
            ),
            PollOperationOutcome.NotReady => new AutomationActionOutcome.Failed(
                "broadcaster-authorization-missing"
            ),
            _ => new AutomationActionOutcome.Failed("poll-rejected"),
        };
    }

    private async Task<AutomationActionOutcome> CreateClipAsync(
        AutomationHostId hostId,
        CreateClipActionConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.ClipsAndMarkers, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        if (clipsMarkers is null)
        {
            return new AutomationActionOutcome.Failed("action-unsupported");
        }

        var outcome = await clipsMarkers.CreateClipAsync(
            hostId.Value,
            configuration.HasDelay,
            cancellationToken
        );
        return outcome switch
        {
            ClipMarkerOperationOutcome.ClipPending or ClipMarkerOperationOutcome.ClipAvailable =>
                new AutomationActionOutcome.Succeeded(),
            ClipMarkerOperationOutcome.ClipAmbiguous => new AutomationActionOutcome.Failed(
                "clip-ambiguous"
            ),
            ClipMarkerOperationOutcome.Offline => new AutomationActionOutcome.Failed(
                "channel-offline"
            ),
            ClipMarkerOperationOutcome.VodsDisabled => new AutomationActionOutcome.Failed(
                "vods-disabled"
            ),
            ClipMarkerOperationOutcome.RerunOrPremiere => new AutomationActionOutcome.Failed(
                "rerun-or-premiere"
            ),
            ClipMarkerOperationOutcome.InvalidRequest => new AutomationActionOutcome.Failed(
                "clip-invalid"
            ),
            ClipMarkerOperationOutcome.NotReady => new AutomationActionOutcome.Failed(
                "broadcaster-authorization-missing"
            ),
            _ => new AutomationActionOutcome.Failed("clip-rejected"),
        };
    }

    private async Task<AutomationActionOutcome> CreateMarkerAsync(
        AutomationHostId hostId,
        CreateMarkerActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.ClipsAndMarkers, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        if (clipsMarkers is null)
        {
            return new AutomationActionOutcome.Failed("action-unsupported");
        }

        if (
            ResolveText(fields, "description", configuration.Description, context, out var text) is
            { } failure
        )
        {
            return failure;
        }

        var outcome = await clipsMarkers.CreateMarkerAsync(hostId.Value, text, cancellationToken);
        return outcome switch
        {
            ClipMarkerOperationOutcome.MarkerCreated => new AutomationActionOutcome.Succeeded(),
            ClipMarkerOperationOutcome.MarkerAmbiguous => new AutomationActionOutcome.Failed(
                "marker-ambiguous"
            ),
            ClipMarkerOperationOutcome.Offline => new AutomationActionOutcome.Failed(
                "channel-offline"
            ),
            ClipMarkerOperationOutcome.VodsDisabled => new AutomationActionOutcome.Failed(
                "vods-disabled"
            ),
            ClipMarkerOperationOutcome.RerunOrPremiere => new AutomationActionOutcome.Failed(
                "rerun-or-premiere"
            ),
            ClipMarkerOperationOutcome.InvalidRequest => new AutomationActionOutcome.Failed(
                "marker-invalid"
            ),
            ClipMarkerOperationOutcome.NotReady => new AutomationActionOutcome.Failed(
                "broadcaster-authorization-missing"
            ),
            _ => new AutomationActionOutcome.Failed("marker-rejected"),
        };
    }

    private async Task<AutomationActionOutcome> StartPredictionAsync(
        AutomationHostId hostId,
        StartPredictionActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.Predictions, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        if (predictions is null)
        {
            return new AutomationActionOutcome.Failed("action-unsupported");
        }

        if (
            ResolveText(fields, "title", configuration.Title, context, out var title) is
            { } titleFailure
        )
        {
            return titleFailure;
        }

        if (
            ResolveLines(fields, "outcomes", configuration.Outcomes, context, out var outcomes) is
            { } outcomesFailure
        )
        {
            return outcomesFailure;
        }

        var outcome = await predictions.StartAsync(
            hostId.Value,
            new PredictionTemplateDraft(title, outcomes, configuration.WindowSeconds),
            cancellationToken
        );
        return outcome switch
        {
            PredictionOperationOutcome.Started => new AutomationActionOutcome.Succeeded(),
            PredictionOperationOutcome.ActivePredictionExists => new AutomationActionOutcome.Failed(
                "prediction-already-active"
            ),
            PredictionOperationOutcome.InvalidTemplate => new AutomationActionOutcome.Failed(
                "prediction-invalid"
            ),
            _ => MapPredictionOutcome(outcome),
        };
    }

    private async Task<AutomationActionOutcome> EndPredictionAsync(
        AutomationHostId hostId,
        string? winningOutcomeId,
        bool cancel,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.Predictions, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        if (predictions is null)
        {
            return new AutomationActionOutcome.Failed("action-unsupported");
        }

        var outcome =
            winningOutcomeId is { } outcomeId
                ? await predictions.ResolveAsync(
                    hostId.Value,
                    outcomeId,
                    confirmed: true,
                    cancellationToken
                )
            : cancel
                ? await predictions.CancelAsync(hostId.Value, confirmed: true, cancellationToken)
            : await predictions.LockAsync(hostId.Value, confirmed: true, cancellationToken);
        return outcome switch
        {
            PredictionOperationOutcome.Updated => new AutomationActionOutcome.Succeeded(),
            PredictionOperationOutcome.InvalidOutcome => new AutomationActionOutcome.Failed(
                "prediction-outcome-invalid"
            ),
            _ => MapPredictionOutcome(outcome),
        };
    }

    private async Task<AutomationActionOutcome> ResolvePredictionAsync(
        AutomationHostId hostId,
        ResolvePredictionActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    ) =>
        !await IsEnabledAsync(hostId, HostFeatureFlags.Predictions, cancellationToken)
            ? new AutomationActionOutcome.Failed("feature-disabled")
        : predictions is null ? new AutomationActionOutcome.Failed("action-unsupported")
        : ResolveText(
            fields,
            "winning-outcome-id",
            configuration.WinningOutcomeId,
            context,
            out var winningOutcomeId
        )
            is { } failure
            ? failure
        : string.IsNullOrWhiteSpace(winningOutcomeId)
            ? new AutomationActionOutcome.Failed("prediction-outcome-invalid")
        : await EndPredictionAsync(hostId, winningOutcomeId, cancel: false, cancellationToken);

    private static AutomationActionOutcome MapPredictionOutcome(
        PredictionOperationOutcome outcome
    ) =>
        outcome switch
        {
            PredictionOperationOutcome.Ineligible => new AutomationActionOutcome.Failed(
                "channel-ineligible"
            ),
            PredictionOperationOutcome.NotReady => new AutomationActionOutcome.Failed(
                "broadcaster-authorization-missing"
            ),
            PredictionOperationOutcome.Unavailable => new AutomationActionOutcome.Failed(
                "twitch-unavailable"
            ),
            _ => new AutomationActionOutcome.Failed("prediction-rejected"),
        };

    private AutomationActionOutcome? ResolveText(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        string fieldId,
        string template,
        AutomationContext context,
        out string text
    )
    {
        text = string.Empty;
        var result = fields.TryGetValue(new(fieldId), out var expression)
            ? expressions.Evaluate(expression, context)
            : expressions.Interpolate(template, context);
        if (result is not AutomationExpressionResult.Value value)
        {
            return new AutomationActionOutcome.Failed("action-expression-invalid");
        }

        if (
            AutomationPublicSinkAdmission.AdmitText(value)
            is not AutomationPublicTextAdmission.Admitted admitted
        )
        {
            return new AutomationActionOutcome.Failed("sensitive-output-blocked");
        }

        text = admitted.Text;
        return null;
    }

    private AutomationActionOutcome? ResolveLines(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        string fieldId,
        string template,
        AutomationContext context,
        out string[] lines
    )
    {
        lines = [];
        if (fields.TryGetValue(new(fieldId), out _))
        {
            if (ResolveText(fields, fieldId, template, context, out var resolved) is { } failure)
            {
                return failure;
            }

            lines = [.. NativeOperationAutomations.SplitEntries(resolved)];
            return null;
        }

        var entries = new List<string>();
        foreach (var line in NativeOperationAutomations.SplitEntries(template))
        {
            var result = expressions.Interpolate(line, context);
            if (result is not AutomationExpressionResult.Value value)
            {
                return new AutomationActionOutcome.Failed("action-expression-invalid");
            }

            if (
                AutomationPublicSinkAdmission.AdmitText(value)
                is not AutomationPublicTextAdmission.Admitted admitted
            )
            {
                return new AutomationActionOutcome.Failed("sensitive-output-blocked");
            }

            entries.Add(admitted.Text.Trim());
        }

        lines = [.. entries.Where(static entry => entry.Length > 0)];
        return null;
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
