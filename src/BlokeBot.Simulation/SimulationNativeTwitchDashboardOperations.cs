using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;

namespace BlokeBot.Simulation;

internal sealed class SimulationNativeTwitchDashboardOperations
    : IShoutoutDashboardOperations,
        IPollDashboardOperations,
        IClipMarkerDashboardOperations,
        IChannelPointsDashboardOperations,
        IPredictionDashboardOperations
{
    private readonly Lock _gate = new();
    private readonly Dictionary<int, HostState> _hosts = [];

    public Task<ShoutoutDashboardState> LoadAsync(
        int hostId,
        string? targetLogin,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            ShoutoutTargetCooldownReadiness targetCooldown = string.IsNullOrWhiteSpace(targetLogin)
                ? new ShoutoutTargetCooldownReadiness.Unknown()
                : new ShoutoutTargetCooldownReadiness.EligibleAt(SimulationMode.Now.UtcDateTime);
            return Task.FromResult(
                new ShoutoutDashboardState(
                    SimulationMode.Now.UtcDateTime,
                    targetCooldown,
                    host.Shoutouts.ToArray()
                )
            );
        }
    }

    public Task<ShoutoutOperationOutcome> SendAsync(
        int hostId,
        string targetLogin,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = targetLogin.Trim().TrimStart('@').ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return Task.FromResult<ShoutoutOperationOutcome>(
                new ShoutoutOperationOutcome.TargetNotFound(targetLogin)
            );
        }

        lock (_gate)
        {
            ForHost(hostId)
                .Shoutouts.Insert(
                    0,
                    new ShoutoutHistoryView(
                        ShoutoutDirection.Sent,
                        SimulationMode.Login,
                        normalized,
                        42,
                        SimulationMode.Now.UtcDateTime,
                        SimulationMode.Now.AddMinutes(2).UtcDateTime,
                        SimulationMode.Now.AddHours(1).UtcDateTime
                    )
                );
        }
        return Task.FromResult<ShoutoutOperationOutcome>(
            new ShoutoutOperationOutcome.Sent(normalized)
        );
    }

    Task<PollDashboardState> IPollDashboardOperations.LoadAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            return Task.FromResult(
                new PollDashboardState(
                    new PollAuthorizationReadiness.Ready(),
                    host.ActivePoll,
                    host.PollTemplates.ToArray(),
                    host.PollResults.ToArray()
                )
            );
        }
    }

    public Task<PollOperationOutcome> SaveTemplateAsync(
        int hostId,
        PollTemplateDraft draft,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = draft.Validate();
        if (validation is PollTemplateValidationOutcome.Invalid invalid)
        {
            return Task.FromResult<PollOperationOutcome>(
                new PollOperationOutcome.InvalidTemplate(invalid.Message)
            );
        }

        var valid = ((PollTemplateValidationOutcome.Valid)validation).Draft;
        lock (_gate)
        {
            var host = ForHost(hostId);
            var template = new PollTemplateView(
                host.NextPollTemplateId++,
                valid.Title,
                valid.Choices,
                valid.DurationSeconds,
                valid.ChannelPointsVotingEnabled,
                valid.ChannelPointsPerVote
            );
            host.PollTemplates.Add(template);
            return Task.FromResult<PollOperationOutcome>(
                new PollOperationOutcome.TemplateSaved(template)
            );
        }
    }

    Task<PollOperationOutcome> IPollDashboardOperations.StartAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            if (host.ActivePoll is not null)
            {
                return Task.FromResult<PollOperationOutcome>(
                    new PollOperationOutcome.ActivePollExists()
                );
            }
            var template = host.PollTemplates.SingleOrDefault(value => value.Id == templateId);
            if (template is null)
            {
                return Task.FromResult<PollOperationOutcome>(
                    new PollOperationOutcome.TemplateNotFound()
                );
            }

            host.ActivePoll = new PollView(
                $"simulation-poll-{host.NextPollId++}",
                template.Title,
                template
                    .Choices.Select(
                        (choice, index) => new PollChoiceView($"choice-{index + 1}", choice, 0, 0)
                    )
                    .ToArray(),
                "Active",
                false,
                SimulationMode.Now.UtcDateTime,
                SimulationMode.Now.AddSeconds(template.DurationSeconds).UtcDateTime,
                null
            );
            return Task.FromResult<PollOperationOutcome>(
                new PollOperationOutcome.Started(host.ActivePoll)
            );
        }
    }

    public Task<PollOperationOutcome> EndAsync(
        int hostId,
        bool confirmedExternal,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            if (host.ActivePoll is null)
            {
                return Task.FromResult<PollOperationOutcome>(
                    new PollOperationOutcome.ProviderRejected("There is no active poll to end.")
                );
            }
            if (host.ActivePoll.IsExternallyStarted && !confirmedExternal)
            {
                return Task.FromResult<PollOperationOutcome>(
                    new PollOperationOutcome.ConfirmationRequired()
                );
            }

            var ended = host.ActivePoll with
            {
                Status = "Terminated",
                EndedAtUtc = SimulationMode.Now.UtcDateTime,
            };
            host.ActivePoll = null;
            host.PollResults.Insert(0, ended);
            return Task.FromResult<PollOperationOutcome>(new PollOperationOutcome.Ended(ended));
        }
    }

    Task<ClipMarkerDashboardState> IClipMarkerDashboardOperations.LoadAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            return Task.FromResult(
                new ClipMarkerDashboardState(
                    new ClipMarkerAuthorizationReadiness.Ready(),
                    host.PendingClips.ToArray(),
                    host.ClipResults.ToArray(),
                    host.Markers.ToArray()
                )
            );
        }
    }

    public Task<ClipMarkerOperationOutcome> CreateClipAsync(
        int hostId,
        bool hasDelay,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            var attempt = new ClipAttemptReference(host.NextClipAttempt++);
            var pending = new ClipView(
                attempt,
                "Pending",
                $"simulation-clip-{attempt.Value}",
                $"https://clips.twitch.tv/simulation-{attempt.Value}/edit",
                null,
                SimulationMode.Login,
                null,
                hasDelay ? "Including stream delay" : null,
                SimulationMode.Now.UtcDateTime,
                null
            );
            host.PendingClips.Insert(0, pending);
            return Task.FromResult<ClipMarkerOperationOutcome>(
                new ClipMarkerOperationOutcome.ClipPending(pending)
            );
        }
    }

    public Task<ClipMarkerOperationOutcome> CreateMarkerAsync(
        int hostId,
        string description,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(description))
        {
            return Task.FromResult<ClipMarkerOperationOutcome>(
                new ClipMarkerOperationOutcome.InvalidRequest(
                    "Add a short description before creating the marker."
                )
            );
        }

        lock (_gate)
        {
            var host = ForHost(hostId);
            var attempt = new StreamMarkerAttemptReference(host.NextMarkerAttempt++);
            var marker = new StreamMarkerView(
                attempt,
                "Succeeded",
                $"simulation-marker-{attempt.Value}",
                description.Trim(),
                615,
                $"https://dashboard.twitch.tv/u/{SimulationMode.Login}/content/video-producer",
                "simulation-video",
                null,
                SimulationMode.Now.UtcDateTime
            );
            host.Markers.Insert(0, marker);
            return Task.FromResult<ClipMarkerOperationOutcome>(
                new ClipMarkerOperationOutcome.MarkerCreated(marker)
            );
        }
    }

    public Task<ClipMarkerOperationOutcome> RetryClipAsync(
        int hostId,
        ClipAttemptReference attempt,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            var pending = host.PendingClips.SingleOrDefault(value => value.Attempt == attempt);
            if (pending is null)
            {
                var retained = host.ClipResults.SingleOrDefault(value => value.Attempt == attempt);
                return Task.FromResult<ClipMarkerOperationOutcome>(
                    retained is null
                        ? new ClipMarkerOperationOutcome.InvalidRequest(
                            "That clip attempt is no longer available."
                        )
                        : new ClipMarkerOperationOutcome.ClipAvailable(retained)
                );
            }

            _ = host.PendingClips.Remove(pending);
            var available = pending with
            {
                Status = "Succeeded",
                FinalUrl = $"https://clips.twitch.tv/simulation-{attempt.Value}",
                FailureReason = null,
                ResolvedAtUtc = SimulationMode.Now.UtcDateTime,
            };
            host.ClipResults.Insert(0, available);
            return Task.FromResult<ClipMarkerOperationOutcome>(
                new ClipMarkerOperationOutcome.ClipAvailable(available)
            );
        }
    }

    public Task<ClipMarkerOperationOutcome> RetryMarkerAsync(
        int hostId,
        StreamMarkerAttemptReference attempt,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var marker = ForHost(hostId).Markers.SingleOrDefault(value => value.Attempt == attempt);
            return Task.FromResult<ClipMarkerOperationOutcome>(
                marker is null
                    ? new ClipMarkerOperationOutcome.InvalidRequest(
                        "That marker attempt is no longer available."
                    )
                    : new ClipMarkerOperationOutcome.MarkerCreated(marker)
            );
        }
    }

    Task<ChannelPointsDashboardState> IChannelPointsDashboardOperations.LoadAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            return Task.FromResult(
                new ChannelPointsDashboardState(
                    new ChannelPointsAuthorizationReadiness.Ready(),
                    host.Rewards.ToArray(),
                    host.ActiveRedemptions.ToArray(),
                    host.RedemptionHistory.ToArray()
                )
            );
        }
    }

    public Task<ChannelPointsOperationOutcome> CreateRewardAsync(
        int hostId,
        ChannelPointsRewardDraft draft,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (draft.Validate() is { } error)
        {
            return Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.InvalidRequest(error)
            );
        }

        lock (_gate)
        {
            var host = ForHost(hostId);
            var reward = Reward($"simulation-reward-{host.NextRewardId++}", draft, true, false);
            host.Rewards.Add(reward);
            return Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.RewardCreated(reward)
            );
        }
    }

    public Task<ChannelPointsOperationOutcome> UpdateRewardAsync(
        int hostId,
        string rewardId,
        ChannelPointsRewardDraft draft,
        bool isEnabled,
        bool paused,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (draft.Validate() is { } error)
        {
            return Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.InvalidRequest(error)
            );
        }

        lock (_gate)
        {
            var host = ForHost(hostId);
            var index = host.Rewards.FindIndex(value => value.ProviderRewardId == rewardId);
            if (index < 0 || !host.Rewards[index].IsManageable)
            {
                return Task.FromResult<ChannelPointsOperationOutcome>(
                    new ChannelPointsOperationOutcome.ExternalReadOnly()
                );
            }
            host.Rewards[index] = Reward(rewardId, draft, isEnabled, paused);
            return Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.RewardUpdated()
            );
        }
    }

    public Task<ChannelPointsOperationOutcome> DeleteRewardAsync(
        int hostId,
        string rewardId,
        bool confirmed,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!confirmed)
        {
            return Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.ConfirmationRequired(
                    "Confirm the deletion before continuing."
                )
            );
        }

        lock (_gate)
        {
            var host = ForHost(hostId);
            var reward = host.Rewards.SingleOrDefault(value => value.ProviderRewardId == rewardId);
            if (reward is null || !reward.IsManageable)
            {
                return Task.FromResult<ChannelPointsOperationOutcome>(
                    new ChannelPointsOperationOutcome.ExternalReadOnly()
                );
            }
            _ = host.Rewards.Remove(reward);
            return Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.RewardDeleted()
            );
        }
    }

    public Task<ChannelPointsOperationOutcome> UpdateRedemptionAsync(
        int hostId,
        string redemptionId,
        bool fulfill,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            var redemption = host.ActiveRedemptions.SingleOrDefault(value =>
                value.ProviderRedemptionId == redemptionId
            );
            if (redemption is null)
            {
                return Task.FromResult<ChannelPointsOperationOutcome>(
                    new ChannelPointsOperationOutcome.RedemptionNotActionable()
                );
            }
            _ = host.ActiveRedemptions.Remove(redemption);
            host.RedemptionHistory.Insert(
                0,
                redemption with
                {
                    Status = fulfill ? "Fulfilled" : "Canceled",
                    UpdatedAtUtc = SimulationMode.Now.UtcDateTime,
                }
            );
            return Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.RedemptionUpdated()
            );
        }
    }

    Task<PredictionDashboardState> IPredictionDashboardOperations.LoadAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            return Task.FromResult(
                new PredictionDashboardState(
                    new PredictionAuthorizationReadiness.Ready(),
                    host.ActivePrediction,
                    host.PredictionTemplates.ToArray(),
                    host.PredictionResults.ToArray()
                )
            );
        }
    }

    public Task<PredictionOperationOutcome> SaveTemplateAsync(
        int hostId,
        PredictionTemplateDraft draft,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = draft.Validate();
        if (validation is PredictionTemplateValidationOutcome.Invalid invalid)
        {
            return Task.FromResult<PredictionOperationOutcome>(
                new PredictionOperationOutcome.InvalidTemplate(invalid.Message)
            );
        }

        var valid = ((PredictionTemplateValidationOutcome.Valid)validation).Draft;
        lock (_gate)
        {
            var host = ForHost(hostId);
            var template = new PredictionTemplateView(
                host.NextPredictionTemplateId++,
                valid.Title,
                valid.Outcomes,
                valid.PredictionWindowSeconds
            );
            host.PredictionTemplates.Add(template);
            return Task.FromResult<PredictionOperationOutcome>(
                new PredictionOperationOutcome.TemplateSaved(template)
            );
        }
    }

    public Task<PredictionOperationOutcome> DeleteTemplateAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            var removed = host.PredictionTemplates.RemoveAll(value => value.Id == templateId);
            return Task.FromResult<PredictionOperationOutcome>(
                removed == 0
                    ? new PredictionOperationOutcome.TemplateNotFound()
                    : new PredictionOperationOutcome.TemplateDeleted()
            );
        }
    }

    Task<PredictionOperationOutcome> IPredictionDashboardOperations.StartAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var host = ForHost(hostId);
            if (host.ActivePrediction is not null)
            {
                return Task.FromResult<PredictionOperationOutcome>(
                    new PredictionOperationOutcome.ActivePredictionExists()
                );
            }
            var template = host.PredictionTemplates.SingleOrDefault(value =>
                value.Id == templateId
            );
            if (template is null)
            {
                return Task.FromResult<PredictionOperationOutcome>(
                    new PredictionOperationOutcome.TemplateNotFound()
                );
            }
            host.ActivePrediction = new PredictionView(
                $"simulation-prediction-{host.NextPredictionId++}",
                template.Title,
                template
                    .Outcomes.Select(
                        (outcome, index) =>
                            new PredictionOutcomeView(
                                $"outcome-{index + 1}",
                                outcome,
                                index == 0 ? "Blue" : "Pink",
                                0,
                                0,
                                []
                            )
                    )
                    .ToArray(),
                "Active",
                false,
                SimulationMode.Now.UtcDateTime,
                SimulationMode.Now.AddSeconds(template.PredictionWindowSeconds).UtcDateTime,
                null
            );
            return Task.FromResult<PredictionOperationOutcome>(
                new PredictionOperationOutcome.Started(host.ActivePrediction)
            );
        }
    }

    public Task<PredictionOperationOutcome> LockAsync(
        int hostId,
        bool confirmed,
        CancellationToken cancellationToken
    ) => ChangePredictionAsync(hostId, confirmed, "Locked", null, cancellationToken);

    public Task<PredictionOperationOutcome> CancelAsync(
        int hostId,
        bool confirmed,
        CancellationToken cancellationToken
    ) => ChangePredictionAsync(hostId, confirmed, "Canceled", null, cancellationToken);

    public Task<PredictionOperationOutcome> ResolveAsync(
        int hostId,
        string winningOutcomeId,
        bool confirmed,
        CancellationToken cancellationToken
    ) => ChangePredictionAsync(hostId, confirmed, "Resolved", winningOutcomeId, cancellationToken);

    private Task<PredictionOperationOutcome> ChangePredictionAsync(
        int hostId,
        bool confirmed,
        string status,
        string? winningOutcomeId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!confirmed)
        {
            return Task.FromResult<PredictionOperationOutcome>(
                new PredictionOperationOutcome.ConfirmationRequired()
            );
        }

        lock (_gate)
        {
            var host = ForHost(hostId);
            if (host.ActivePrediction is null)
            {
                return Task.FromResult<PredictionOperationOutcome>(
                    new PredictionOperationOutcome.ProviderRejected(
                        "There is no active Prediction to update."
                    )
                );
            }
            if (
                winningOutcomeId is not null
                && host.ActivePrediction.Outcomes.All(value => value.Id != winningOutcomeId)
            )
            {
                return Task.FromResult<PredictionOperationOutcome>(
                    new PredictionOperationOutcome.InvalidOutcome()
                );
            }

            var updated = host.ActivePrediction with
            {
                Status = status,
                EndedAtUtc = status is "Canceled" or "Resolved"
                    ? SimulationMode.Now.UtcDateTime
                    : null,
            };
            if (status is "Canceled" or "Resolved")
            {
                host.ActivePrediction = null;
                host.PredictionResults.Insert(0, updated);
            }
            else
            {
                host.ActivePrediction = updated;
            }
            return Task.FromResult<PredictionOperationOutcome>(
                new PredictionOperationOutcome.Updated(updated)
            );
        }
    }

    private HostState ForHost(int hostId)
    {
        if (!_hosts.TryGetValue(hostId, out var state))
        {
            state = HostState.Create();
            _hosts.Add(hostId, state);
        }
        return state;
    }

    private static ChannelPointsRewardView Reward(
        string rewardId,
        ChannelPointsRewardDraft draft,
        bool isEnabled,
        bool isPaused
    ) =>
        new(
            rewardId,
            draft.Title.Trim(),
            draft.Prompt?.Trim(),
            draft.Cost,
            true,
            isEnabled,
            isPaused,
            draft.IsUserInputRequired,
            draft.IsMaxPerStreamEnabled,
            draft.MaxPerStream,
            draft.IsMaxPerUserPerStreamEnabled,
            draft.MaxPerUserPerStream,
            draft.IsGlobalCooldownEnabled,
            draft.GlobalCooldownSeconds,
            draft.ShouldRedemptionsSkipRequestQueue,
            draft.BackgroundColor
        );

    private sealed class HostState
    {
        public List<ShoutoutHistoryView> Shoutouts { get; } = [];
        public List<PollTemplateView> PollTemplates { get; } = [];
        public PollView? ActivePoll { get; set; }
        public List<PollView> PollResults { get; } = [];
        public List<ClipView> PendingClips { get; } = [];
        public List<ClipView> ClipResults { get; } = [];
        public List<StreamMarkerView> Markers { get; } = [];
        public List<ChannelPointsRewardView> Rewards { get; } = [];
        public List<ChannelPointsRedemptionView> ActiveRedemptions { get; } = [];
        public List<ChannelPointsRedemptionView> RedemptionHistory { get; } = [];
        public List<PredictionTemplateView> PredictionTemplates { get; } = [];
        public PredictionView? ActivePrediction { get; set; }
        public List<PredictionView> PredictionResults { get; } = [];
        public int NextPollTemplateId { get; set; } = 2;
        public int NextPollId { get; set; } = 1;
        public int NextClipAttempt { get; set; } = 1;
        public int NextMarkerAttempt { get; set; } = 1;
        public int NextRewardId { get; set; } = 2;
        public int NextPredictionTemplateId { get; set; } = 2;
        public int NextPredictionId { get; set; } = 1;

        public static HostState Create()
        {
            var state = new HostState();
            state.PollTemplates.Add(
                new PollTemplateView(
                    1,
                    "What should we play next?",
                    ["Puzzle game", "Adventure game"],
                    60,
                    false,
                    null
                )
            );
            state.Rewards.Add(
                new ChannelPointsRewardView(
                    "simulation-reward-1",
                    "Choose the next emote",
                    "Tell us which emote to use.",
                    500,
                    true,
                    true,
                    false,
                    true,
                    false,
                    null,
                    false,
                    null,
                    false,
                    null,
                    false,
                    "#9147FF"
                )
            );
            state.ActiveRedemptions.Add(
                new ChannelPointsRedemptionView(
                    "simulation-redemption-1",
                    "simulation-reward-1",
                    "Choose the next emote",
                    "chatregular",
                    "PartyParrot",
                    "Unfulfilled",
                    SimulationMode.Now.AddMinutes(-5).UtcDateTime,
                    SimulationMode.Now.AddMinutes(-5).UtcDateTime,
                    true
                )
            );
            state.ActiveRedemptions.Add(
                new ChannelPointsRedemptionView(
                    "simulation-redemption-2",
                    "simulation-reward-1",
                    "Choose the next emote",
                    "nightowl",
                    "PrideLion",
                    "Unfulfilled",
                    SimulationMode.Now.AddMinutes(-2).UtcDateTime,
                    SimulationMode.Now.AddMinutes(-2).UtcDateTime,
                    true
                )
            );
            state.ActiveRedemptions.Add(
                new ChannelPointsRedemptionView(
                    "simulation-redemption-3",
                    "simulation-reward-1",
                    "Choose the next emote",
                    "firsttimer",
                    "bleedPurple",
                    "Unfulfilled",
                    SimulationMode.Now.AddMinutes(-1).UtcDateTime,
                    SimulationMode.Now.AddMinutes(-1).UtcDateTime,
                    true
                )
            );
            state.PredictionTemplates.Add(
                new PredictionTemplateView(
                    1,
                    "Will we finish this level?",
                    ["Yes", "Not this time"],
                    60
                )
            );
            return state;
        }
    }
}
