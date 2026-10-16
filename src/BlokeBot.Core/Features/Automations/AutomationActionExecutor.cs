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

    internal abstract TResult Match<TResult>(
        Func<Succeeded, TResult> succeeded,
        Func<CountdownStarted, TResult> countdownStarted,
        Func<CountdownAlreadyRunning, TResult> countdownAlreadyRunning,
        Func<Failed, TResult> failed
    );

    internal sealed record Succeeded : AutomationActionOutcome
    {
        internal override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<CountdownStarted, TResult> countdownStarted,
            Func<CountdownAlreadyRunning, TResult> countdownAlreadyRunning,
            Func<Failed, TResult> failed
        ) => succeeded(this);
    }

    internal sealed record CountdownStarted : AutomationActionOutcome
    {
        internal override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<CountdownStarted, TResult> countdownStarted,
            Func<CountdownAlreadyRunning, TResult> countdownAlreadyRunning,
            Func<Failed, TResult> failed
        ) => countdownStarted(this);
    }

    internal sealed record CountdownAlreadyRunning : AutomationActionOutcome
    {
        internal override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<CountdownStarted, TResult> countdownStarted,
            Func<CountdownAlreadyRunning, TResult> countdownAlreadyRunning,
            Func<Failed, TResult> failed
        ) => countdownAlreadyRunning(this);
    }

    internal sealed record Failed(string Code) : AutomationActionOutcome
    {
        internal override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<CountdownStarted, TResult> countdownStarted,
            Func<CountdownAlreadyRunning, TResult> countdownAlreadyRunning,
            Func<Failed, TResult> failed
        ) => failed(this);
    }
}

public sealed class AutomationActionExecutor(
    HostFeatureService features,
    IPublicChatMessageSender chat,
    IOverlayCueAdmissionService overlayCues,
    AutomationExpressionService expressions,
    AutomationCountdownService countdowns,
    IDbContextFactory<BlokeBotDbContext>? dbFactory = null,
    IChannelPointsDashboardOperations? channelPoints = null,
    IShoutoutDashboardOperations? shoutouts = null,
    IClipMarkerDashboardOperations? clipsMarkers = null,
    IPollAutomationOperations? polls = null,
    IPredictionAutomationOperations? predictions = null
)
{
    private readonly AutomationNativeActionPreparation _preparation = new(expressions);

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
                CountdownActionConfiguration countdown => await AutomationNativeActionPreparation
                    .Countdown(countdown, inputs, context.Actor, countdowns.UtcNow)
                    .Match(
                        async prepared =>
                            (
                                await countdowns.ApplyPreparedAsync(
                                    hostId,
                                    prepared.Payload,
                                    cancellationToken
                                )
                            ).Match<AutomationActionOutcome>(
                                static _ => new AutomationActionOutcome.CountdownStarted(),
                                static _ => new AutomationActionOutcome.CountdownAlreadyRunning(),
                                static _ => new AutomationActionOutcome.Succeeded(),
                                static _ => new AutomationActionOutcome.Failed(
                                    "countdown-unavailable"
                                )
                            ),
                        failed =>
                            Task.FromResult<AutomationActionOutcome>(
                                new AutomationActionOutcome.Failed(failed.Code)
                            )
                    ),
                SendChatActionConfiguration => await SendChatAsync(
                    hostId,
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
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        AutomationContext context,
        CancellationToken cancellationToken
    ) =>
        !await IsEnabledAsync(hostId, HostFeatureFlags.None, cancellationToken)
            ? new AutomationActionOutcome.Failed("feature-disabled")
            : await AutomationNativeActionPreparation
                .Chat(inputs, context)
                .Match<Task<AutomationActionOutcome>>(
                    async prepared =>
                    {
                        var outcome = await chat.SendAsync(
                            prepared.Payload.Channel,
                            prepared.Payload.Message,
                            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                            cancellationToken
                        );
                        return outcome.Match<AutomationActionOutcome>(
                            static _ => new AutomationActionOutcome.Succeeded(),
                            static _ => new AutomationActionOutcome.Failed("chat-rejected")
                        );
                    },
                    failed =>
                        Task.FromResult<AutomationActionOutcome>(
                            new AutomationActionOutcome.Failed(failed.Code)
                        )
                );

    private async Task<AutomationActionOutcome> PlayCueAsync(
        AutomationHostId hostId,
        PlayOverlayCueActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    ) =>
        !await IsEnabledAsync(hostId, HostFeatureFlags.Overlays, cancellationToken)
            ? new AutomationActionOutcome.Failed("feature-disabled")
            : await _preparation
                .Cue(configuration, fields, context)
                .Match<Task<AutomationActionOutcome>>(
                    async prepared =>
                    {
                        var references = await overlayCues.ResolveReferencesAsync(
                            new(hostId.Value, prepared.Payload.TargetId, prepared.Payload.CueId),
                            cancellationToken
                        );
                        if (
                            references is not OverlayCueReferenceOutcome.Available
                            || !await IsEnabledAsync(
                                hostId,
                                HostFeatureFlags.Overlays,
                                cancellationToken
                            )
                        )
                        {
                            return new AutomationActionOutcome.Failed(
                                "overlay-reference-unavailable"
                            );
                        }

                        var admissionCatalog = await overlayCues.QueryCatalogAsync(
                            hostId.Value,
                            cancellationToken
                        );
                        var cue = admissionCatalog.Cues.SingleOrDefault(value =>
                            value.Id == prepared.Payload.CueId
                        );
                        if (cue is null)
                        {
                            return new AutomationActionOutcome.Failed(
                                "overlay-reference-unavailable"
                            );
                        }

                        var admission = await overlayCues.AdmitAsync(
                            new(
                                hostId.Value,
                                prepared.Payload.TargetId,
                                prepared.Payload.CueId,
                                cue.DefaultQueuePolicy,
                                OverlayCueAdmissionOrigin.Automation,
                                OverlayCueSafeContext.Empty
                            ),
                            cancellationToken
                        );
                        return
                            admission
                                is OverlayCueAdmissionOutcome.Running
                                    or OverlayCueAdmissionOutcome.Queued
                            ? new AutomationActionOutcome.Succeeded()
                            : new AutomationActionOutcome.Failed("overlay-rejected");
                    },
                    failed =>
                        Task.FromResult<AutomationActionOutcome>(
                            new AutomationActionOutcome.Failed(failed.Code)
                        )
                );

    private async Task<AutomationActionOutcome> UpdateRedemptionAsync(
        AutomationHostId hostId,
        AutomationContext context,
        bool fulfill,
        CancellationToken cancellationToken
    ) =>
        !await IsEnabledAsync(hostId, HostFeatureFlags.RewardsAndRedemptions, cancellationToken)
            ? new AutomationActionOutcome.Failed("feature-disabled")
            : await AutomationNativeActionPreparation
                .Redemption(context)
                .Match<Task<AutomationActionOutcome>>(
                    async prepared =>
                    {
                        if (dbFactory is null || channelPoints is null)
                        {
                            return new AutomationActionOutcome.Failed("action-unsupported");
                        }

                        // Reward manageability is validated before any Twitch API call or state mutation;
                        // externally created rewards are rejected here without partial execution.
                        await using var db = await dbFactory.CreateDbContextAsync(
                            cancellationToken
                        );
                        var manageable = await db
                            .TwitchCustomRewards.AsNoTracking()
                            .AnyAsync(
                                reward =>
                                    reward.HostId == hostId.Value
                                    && reward.ProviderRewardId == prepared.Payload.RewardId
                                    && reward.IsManageable,
                                cancellationToken
                            );
                        if (!manageable)
                        {
                            return new AutomationActionOutcome.Failed("reward-not-manageable");
                        }

                        var outcome = await channelPoints.UpdateRedemptionAsync(
                            hostId.Value,
                            prepared.Payload.RedemptionId,
                            fulfill,
                            cancellationToken
                        );
                        return outcome switch
                        {
                            ChannelPointsOperationOutcome.RedemptionUpdated =>
                                new AutomationActionOutcome.Succeeded(),
                            ChannelPointsOperationOutcome.ExternalReadOnly =>
                                new AutomationActionOutcome.Failed("reward-not-manageable"),
                            ChannelPointsOperationOutcome.RedemptionNotActionable =>
                                new AutomationActionOutcome.Failed("redemption-not-actionable"),
                            ChannelPointsOperationOutcome.NotReady =>
                                new AutomationActionOutcome.Failed(
                                    "broadcaster-authorization-missing"
                                ),
                            ChannelPointsOperationOutcome.Ineligible =>
                                new AutomationActionOutcome.Failed("channel-ineligible"),
                            _ => new AutomationActionOutcome.Failed("redemption-rejected"),
                        };
                    },
                    failed =>
                        Task.FromResult<AutomationActionOutcome>(
                            new AutomationActionOutcome.Failed(failed.Code)
                        )
                );

    private async Task<AutomationActionOutcome> SendShoutoutAsync(
        AutomationHostId hostId,
        AutomationContext context,
        CancellationToken cancellationToken
    ) =>
        !await IsEnabledAsync(hostId, HostFeatureFlags.RaidCollaboration, cancellationToken)
            ? new AutomationActionOutcome.Failed("feature-disabled")
        : shoutouts is null ? new AutomationActionOutcome.Failed("action-unsupported")
        : await AutomationNativeActionPreparation
            .Shoutout(context)
            .Match<Task<AutomationActionOutcome>>(
                async prepared =>
                {
                    var outcome = await shoutouts.SendAsync(
                        hostId.Value,
                        prepared.Payload,
                        cancellationToken
                    );
                    return outcome switch
                    {
                        ShoutoutOperationOutcome.Sent => new AutomationActionOutcome.Succeeded(),
                        ShoutoutOperationOutcome.TargetNotFound =>
                            new AutomationActionOutcome.Failed("shoutout-target-not-found"),
                        ShoutoutOperationOutcome.SelfTarget => new AutomationActionOutcome.Failed(
                            "shoutout-self-target"
                        ),
                        ShoutoutOperationOutcome.TargetOffline =>
                            new AutomationActionOutcome.Failed("shoutout-target-offline"),
                        ShoutoutOperationOutcome.CooldownActive
                        or ShoutoutOperationOutcome.CooldownUnknown =>
                            new AutomationActionOutcome.Failed("shoutout-cooldown-active"),
                        ShoutoutOperationOutcome.NotReady => new AutomationActionOutcome.Failed(
                            "shoutout-not-ready"
                        ),
                        _ => new AutomationActionOutcome.Failed("shoutout-rejected"),
                    };
                },
                failed =>
                    Task.FromResult<AutomationActionOutcome>(
                        new AutomationActionOutcome.Failed(failed.Code)
                    )
            );

    private async Task<AutomationActionOutcome> StartPollAsync(
        AutomationHostId hostId,
        StartPollActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    ) =>
        !await IsEnabledAsync(hostId, HostFeatureFlags.Polls, cancellationToken)
            ? new AutomationActionOutcome.Failed("feature-disabled")
        : polls is null ? new AutomationActionOutcome.Failed("action-unsupported")
        : await _preparation
            .Poll(configuration, fields, context)
            .Match<Task<AutomationActionOutcome>>(
                async prepared =>
                {
                    var outcome = await polls.StartAsync(
                        hostId.Value,
                        prepared.Payload,
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
                },
                failed =>
                    Task.FromResult<AutomationActionOutcome>(
                        new AutomationActionOutcome.Failed(failed.Code)
                    )
            );

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
    ) =>
        !await IsEnabledAsync(hostId, HostFeatureFlags.ClipsAndMarkers, cancellationToken)
            ? new AutomationActionOutcome.Failed("feature-disabled")
        : clipsMarkers is null ? new AutomationActionOutcome.Failed("action-unsupported")
        : await _preparation
            .Marker(configuration, fields, context)
            .Match<Task<AutomationActionOutcome>>(
                async prepared =>
                {
                    var outcome = await clipsMarkers.CreateMarkerAsync(
                        hostId.Value,
                        prepared.Payload,
                        cancellationToken
                    );
                    return outcome switch
                    {
                        ClipMarkerOperationOutcome.MarkerCreated =>
                            new AutomationActionOutcome.Succeeded(),
                        ClipMarkerOperationOutcome.MarkerAmbiguous =>
                            new AutomationActionOutcome.Failed("marker-ambiguous"),
                        ClipMarkerOperationOutcome.Offline => new AutomationActionOutcome.Failed(
                            "channel-offline"
                        ),
                        ClipMarkerOperationOutcome.VodsDisabled =>
                            new AutomationActionOutcome.Failed("vods-disabled"),
                        ClipMarkerOperationOutcome.RerunOrPremiere =>
                            new AutomationActionOutcome.Failed("rerun-or-premiere"),
                        ClipMarkerOperationOutcome.InvalidRequest =>
                            new AutomationActionOutcome.Failed("marker-invalid"),
                        ClipMarkerOperationOutcome.NotReady => new AutomationActionOutcome.Failed(
                            "broadcaster-authorization-missing"
                        ),
                        _ => new AutomationActionOutcome.Failed("marker-rejected"),
                    };
                },
                failed =>
                    Task.FromResult<AutomationActionOutcome>(
                        new AutomationActionOutcome.Failed(failed.Code)
                    )
            );

    private async Task<AutomationActionOutcome> StartPredictionAsync(
        AutomationHostId hostId,
        StartPredictionActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    ) =>
        !await IsEnabledAsync(hostId, HostFeatureFlags.Predictions, cancellationToken)
            ? new AutomationActionOutcome.Failed("feature-disabled")
        : predictions is null ? new AutomationActionOutcome.Failed("action-unsupported")
        : await _preparation
            .Prediction(configuration, fields, context)
            .Match<Task<AutomationActionOutcome>>(
                async prepared =>
                {
                    var outcome = await predictions.StartAsync(
                        hostId.Value,
                        prepared.Payload,
                        cancellationToken
                    );
                    return outcome switch
                    {
                        PredictionOperationOutcome.Started =>
                            new AutomationActionOutcome.Succeeded(),
                        PredictionOperationOutcome.ActivePredictionExists =>
                            new AutomationActionOutcome.Failed("prediction-already-active"),
                        PredictionOperationOutcome.InvalidTemplate =>
                            new AutomationActionOutcome.Failed("prediction-invalid"),
                        _ => MapPredictionOutcome(outcome),
                    };
                },
                failed =>
                    Task.FromResult<AutomationActionOutcome>(
                        new AutomationActionOutcome.Failed(failed.Code)
                    )
            );

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
        : await _preparation
            .PredictionOutcome(configuration, fields, context)
            .Match(
                prepared =>
                    EndPredictionAsync(hostId, prepared.Payload, cancel: false, cancellationToken),
                failed =>
                    Task.FromResult<AutomationActionOutcome>(
                        new AutomationActionOutcome.Failed(failed.Code)
                    )
            );

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

    private async Task<bool> IsEnabledAsync(
        AutomationHostId hostId,
        HostFeatureFlags secondary,
        CancellationToken cancellationToken
    )
    {
        var enabled = await features.Load(hostId.Value).RunAsync(cancellationToken);
        return enabled.Match(
            flags => secondary == HostFeatureFlags.None || flags.Contains(secondary),
            static () => false
        );
    }
}
