using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.RaidCollaboration;

public sealed class RaidCollaborationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IRaidCollaborationProvider provider,
    IRaidWelcomeSender welcomeSender,
    IShoutoutDashboardOperations shoutouts,
    AutomaticRaidShoutoutRunner automaticShoutouts,
    IEnumerable<IRaidCollaborationDomainEventObserver> domainEventObservers,
    EventBus<AppEventKind> events,
    TimeProvider clock
) : IIncomingRaidEventObserver
{
    private const int _historyLimit = 100;
    private const int _approvedChannelLimit = 50;
    private const int _automaticShoutoutOutcomeLimit = 20;
    private readonly IRaidCollaborationDomainEventObserver[] _domainEventObservers =
    [
        .. domainEventObservers,
    ];

    public async Task<RaidCollaborationLoadOutcome> LoadAsync(
        int hostId,
        string? shoutoutTargetLogin,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == hostId, cancellationToken);
        if (host is null)
        {
            return new RaidCollaborationLoadOutcome.HostNotFound();
        }
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.RaidCollaboration))
        {
            return new RaidCollaborationLoadOutcome.FeatureDisabled();
        }

        var configuration = await LoadConfigurationAsync(db, hostId, cancellationToken);
        var history = await db
            .RaidCollaborationHistory.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderByDescending(value => value.OccurredAtUtc)
            .ThenByDescending(value => value.Id)
            .Take(_historyLimit)
            .Select(value => new RaidRelationshipHistory(
                value.Direction,
                value.OtherTwitchUserId,
                value.OtherLogin,
                value.OtherDisplayName,
                value.ViewerCount,
                value.Category,
                value.ProviderStreamId,
                value.OccurredAtUtc,
                value.WelcomeOutcome,
                value.ShoutoutOutcome
            ))
            .ToArrayAsync(cancellationToken);
        var shortlist = await BuildShortlistAsync(
            hostId,
            configuration,
            history,
            cancellationToken
        );
        if (shortlist is null)
        {
            return await FeatureAcceptsCurrentWorkAsync(hostId, null, cancellationToken)
                ? new RaidCollaborationLoadOutcome.ProviderUnavailable()
                : new RaidCollaborationLoadOutcome.FeatureDisabled();
        }
        if (!await FeatureAcceptsCurrentWorkAsync(hostId, null, cancellationToken))
        {
            return new RaidCollaborationLoadOutcome.FeatureDisabled();
        }

        var shoutoutContext = await shoutouts.LoadAsync(
            hostId,
            shoutoutTargetLogin,
            cancellationToken
        );
        var automaticOutcomes = await LoadAutomaticShoutoutOutcomesAsync(
            db,
            hostId,
            cancellationToken
        );
        var latestArrival = history
            .Where(value => value.Direction == RaidDirection.Incoming)
            .Select(value => new RaidArrivalSummary(
                value.Login,
                value.DisplayName,
                value.ViewerCount,
                value.Category,
                value.OccurredAt,
                value.WelcomeOutcome,
                value.ShoutoutOutcome
            ))
            .FirstOrDefault();
        return new RaidCollaborationLoadOutcome.Loaded(
            new(
                configuration,
                shortlist.Value.Eligible,
                shortlist.Value.Excluded,
                history,
                latestArrival,
                shoutoutContext,
                automaticOutcomes,
                await provider.HasRaidManagementAuthorizationAsync(hostId, cancellationToken),
                shortlist.Value.FollowedLiveSource
            )
        );
    }

    private static async Task<
        IReadOnlyList<AutomaticRaidShoutoutOutcomeView>
    > LoadAutomaticShoutoutOutcomesAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) =>
        await db
            .AutomaticRaidShoutoutOutcomes.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderByDescending(value => value.MessageTimestampUtc)
            .ThenByDescending(value => value.Id)
            .Take(_automaticShoutoutOutcomeLimit)
            .Select(value => new AutomaticRaidShoutoutOutcomeView(
                value.Id,
                value.ProviderMessageId,
                value.SourceLogin,
                value.SourceDisplayName,
                value.ViewerCount,
                value.Status,
                value.ResultCode,
                new DateTimeOffset(value.MessageTimestampUtc, TimeSpan.Zero),
                value.CompletedAtUtc == null
                    ? null
                    : new DateTimeOffset(value.CompletedAtUtc.Value, TimeSpan.Zero)
            ))
            .ToArrayAsync(cancellationToken);

    public async Task<RaidCollaborationSaveOutcome> SaveAsync(
        int hostId,
        RaidCollaborationConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        var errors = Validate(configuration);
        var shoutoutErrors = configuration.AutomaticShoutout.Validate();
        if (errors.Count > 0 || shoutoutErrors.Count > 0)
        {
            return new RaidCollaborationSaveOutcome.Invalid(errors, shoutoutErrors);
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(
            value => value.Id == hostId,
            cancellationToken
        );
        if (host is null)
        {
            return new RaidCollaborationSaveOutcome.HostNotFound();
        }
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.RaidCollaboration))
        {
            return new RaidCollaborationSaveOutcome.FeatureDisabled();
        }

        var currentSettings = await db
            .RaidCollaborationSettings.AsNoTracking()
            .SingleOrDefaultAsync(value => value.HostId == hostId, cancellationToken);
        if (
            configuration.IncludeFollowedLiveChannels
            && currentSettings?.IncludeFollowedLiveChannels != true
            && !await provider.HasFollowedLiveAuthorizationAsync(hostId, cancellationToken)
        )
        {
            return new RaidCollaborationSaveOutcome.FollowedLiveAuthorizationRequired(
                await LoadConfigurationAsync(db, hostId, cancellationToken)
            );
        }

        var settings = await db.RaidCollaborationSettings.SingleOrDefaultAsync(
            value => value.HostId == hostId,
            cancellationToken
        );
        if (settings is null)
        {
            settings = new RaidCollaborationSettings { HostId = hostId };
            _ = db.RaidCollaborationSettings.Add(settings);
        }
        settings.WelcomeEnabled = configuration.WelcomeEnabled;
        settings.WelcomeMessage = configuration.WelcomeMessage.Trim();
        settings.DeduplicationWindowMinutes = configuration.DeduplicationWindowMinutes;
        settings.Language = configuration.Language.Trim().ToLowerInvariant();
        settings.EligibleCategories = FormatCategories(configuration.EligibleCategories);
        settings.RelationshipCooldownHours = configuration.RelationshipCooldownHours;
        settings.IncludeFollowedLiveChannels = configuration.IncludeFollowedLiveChannels;
        settings.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;

        var shoutoutSettings = await db.AutomaticRaidShoutoutSettings.SingleOrDefaultAsync(
            value => value.HostId == hostId,
            cancellationToken
        );
        if (shoutoutSettings is null)
        {
            shoutoutSettings = new AutomaticRaidShoutoutSettings { HostId = hostId };
            _ = db.AutomaticRaidShoutoutSettings.Add(shoutoutSettings);
        }
        var shoutout = configuration.AutomaticShoutout;
        shoutoutSettings.Enabled = shoutout.Enabled;
        shoutoutSettings.OnlyApprovedChannels = shoutout.OnlyApprovedChannels;
        shoutoutSettings.MinimumViewerCount = shoutout.MinimumViewerCount;
        shoutoutSettings.Mechanism = shoutout.Mechanism;
        shoutoutSettings.ChatPresentation = shoutout.ChatPresentation;
        shoutoutSettings.MessageTemplate = shoutout.MessageTemplate;
        shoutoutSettings.PinDurationSeconds = shoutout.PinDurationSeconds;
        shoutoutSettings.AnnouncementColor = shoutout.AnnouncementColor;
        shoutoutSettings.UpdatedAtUtc = settings.UpdatedAtUtc;

        var existing = await db
            .ApprovedRaidChannels.Where(value => value.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var twitchUserIds = existing
            .Where(static channel => !string.IsNullOrWhiteSpace(channel.TwitchUserId))
            .ToDictionary(static channel => channel.Login, static channel => channel.TwitchUserId);
        db.ApprovedRaidChannels.RemoveRange(existing);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var channel in configuration.ApprovedChannels)
        {
            _ = db.ApprovedRaidChannels.Add(
                new ApprovedRaidChannel
                {
                    HostId = hostId,
                    TwitchUserId =
                        channel.TwitchUserId
                        ?? twitchUserIds.GetValueOrDefault(Login.Normalize(channel.Login)),
                    Login = Login.Normalize(channel.Login),
                    DisplayName = DisplayName(channel),
                    ApprovedClipId = NormalizeOptional(channel.ApprovedClipId),
                    ApprovedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        _ = await events.PublishAsync(AppEventKind.RaidCollaborationChanged, cancellationToken);
        return new RaidCollaborationSaveOutcome.Saved(
            await LoadConfigurationAsync(db, hostId, cancellationToken)
        );
    }

    public async Task<ShoutoutOperationOutcome> SendShortlistShoutoutAsync(
        int hostId,
        string targetTwitchUserId,
        string targetLogin,
        CancellationToken cancellationToken
    )
    {
        var candidate = await RevalidateCandidateAsync(
            hostId,
            targetTwitchUserId,
            cancellationToken
        );
        return candidate is CandidateRevalidation.Eligible eligible
            ? await shoutouts.SendAsync(hostId, eligible.Snapshot.Login, cancellationToken)
            : new ShoutoutOperationOutcome.NotReady(
                candidate is CandidateRevalidation.AuthorizationRequired
                    ? "Reconnect Twitch with followed-channel permission. No shoutout was sent."
                    : $"@{targetLogin} is no longer an eligible shortlist channel. No shoutout was sent."
            );
    }

    public Task<ShoutoutOperationOutcome> SendShoutoutAsync(
        int hostId,
        string targetLogin,
        CancellationToken cancellationToken
    ) => shoutouts.SendAsync(hostId, targetLogin, cancellationToken);

    public async Task<ApproveRaidChannelOutcome> ApproveChannelAsync(
        int hostId,
        string twitchUserId,
        string login,
        string displayName,
        CancellationToken cancellationToken
    )
    {
        var normalized = Login.Normalize(login);
        if (
            string.IsNullOrWhiteSpace(normalized)
            || !await FeatureAcceptsCurrentWorkAsync(hostId, null, cancellationToken)
        )
        {
            return new ApproveRaidChannelOutcome.FeatureDisabled();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var approved = await db
            .ApprovedRaidChannels.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => value.Login)
            .ToArrayAsync(cancellationToken);
        if (approved.Contains(normalized, StringComparer.Ordinal))
        {
            return new ApproveRaidChannelOutcome.AlreadyApproved();
        }
        if (approved.Length >= _approvedChannelLimit)
        {
            return new ApproveRaidChannelOutcome.LimitReached(_approvedChannelLimit);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var channel = new ApprovedRaidChannel
        {
            HostId = hostId,
            TwitchUserId = string.IsNullOrWhiteSpace(twitchUserId) ? null : twitchUserId,
            Login = normalized,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalized : displayName.Trim(),
            ApprovedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _ = db.ApprovedRaidChannels.Add(channel);
        _ = await db.SaveChangesAsync(cancellationToken);
        _ = await events.PublishAsync(AppEventKind.RaidCollaborationChanged, cancellationToken);
        return new ApproveRaidChannelOutcome.Approved(
            new(channel.Login, channel.DisplayName, channel.ApprovedClipId)
            {
                TwitchUserId = channel.TwitchUserId,
            }
        );
    }

    public async Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
        int hostId,
        string targetTwitchUserId,
        string targetLogin,
        CancellationToken cancellationToken
    )
    {
        var candidate = await RevalidateCandidateAsync(
            hostId,
            targetTwitchUserId,
            cancellationToken
        );
        return candidate switch
        {
            CandidateRevalidation.FeatureDisabled =>
                new ConfirmedRaidStartOutcome.FeatureDisabled(),
            CandidateRevalidation.AuthorizationRequired =>
                new ConfirmedRaidStartOutcome.AuthorizationRequired(),
            CandidateRevalidation.Ineligible ineligible =>
                new ConfirmedRaidStartOutcome.TargetIneligible(ineligible.Reasons),
            CandidateRevalidation.Eligible eligible => await provider.StartConfirmedRaidAsync(
                hostId,
                eligible.Snapshot.TwitchUserId,
                eligible.Snapshot.Login,
                cancellationToken
            ),
            _ => new ConfirmedRaidStartOutcome.TargetNotApproved(),
        };
    }

    public async Task IncomingRaidReceivedAsync(
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellationToken
    )
    {
        if (!ValidEvent(incomingRaid))
        {
            return;
        }
        var direction =
            incomingRaid.SubscriptionDirection is EventSubRaidSubscriptionDirection.Outgoing
                ? RaidDirection.Outgoing
                : RaidDirection.Incoming;
        var ownerId =
            direction == RaidDirection.Incoming
                ? incomingRaid.ToBroadcasterUserId
                : incomingRaid.FromBroadcasterUserId;
        await using var lookup = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await lookup
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.TwitchUserId == ownerId, cancellationToken);
        if (
            host is null
            || !host.EnabledFeatures.Contains(HostFeatureFlags.RaidCollaboration)
            || (
                host.RaidCollaborationAcceptEventsAfterUtc is { } acceptAfter
                && incomingRaid.MessageTimestamp.UtcDateTime < acceptAfter
            )
        )
        {
            return;
        }
        await RecordRaidAsync(host, direction, incomingRaid, cancellationToken);
    }

    private async Task RecordRaidAsync(
        BotHost host,
        RaidDirection direction,
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellationToken
    )
    {
        var otherId =
            direction == RaidDirection.Incoming
                ? incomingRaid.FromBroadcasterUserId
                : incomingRaid.ToBroadcasterUserId;
        var otherLogin = Login.Normalize(
            direction == RaidDirection.Incoming
                ? incomingRaid.FromBroadcasterUserLogin
                : incomingRaid.ToBroadcasterUserLogin
        );
        var otherDisplayName =
            direction == RaidDirection.Incoming
                ? incomingRaid.FromBroadcasterUserName
                : incomingRaid.ToBroadcasterUserName;
        var directionToken = PersistedEnumTokens<RaidDirection>.Format(direction);
        var welcomeToken = PersistedEnumTokens<RaidWelcomeOutcome>.Format(
            RaidWelcomeOutcome.NotConfigured
        );
        var shoutoutToken = PersistedEnumTokens<RaidShoutoutOutcome>.Format(
            RaidShoutoutOutcome.NotConfigured
        );
        var feature = (long)HostFeatureFlags.RaidCollaboration;
        await using var claim = await dbFactory.CreateDbContextAsync(cancellationToken);
        var changed = await claim.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT OR IGNORE INTO raid_collaboration_history
                (HostId, ProviderMessageId, Direction, OtherTwitchUserId, OtherLogin,
                 OtherDisplayName, ViewerCount, OccurredAtUtc, WelcomeOutcome,
                 ShoutoutOutcome, RecordedAtUtc)
            SELECT
                 {host.Id}, {incomingRaid.MessageId}, {directionToken}, {otherId}, {otherLogin},
                 {otherDisplayName}, {incomingRaid.ViewerCount}, {incomingRaid.MessageTimestamp.UtcDateTime},
                 {welcomeToken}, {shoutoutToken}, {clock.GetUtcNow().UtcDateTime}
            FROM hosts
            WHERE Id = {host.Id}
              AND (EnabledFeatures & {feature}) = {feature}
              AND (RaidCollaborationAcceptEventsAfterUtc IS NULL OR RaidCollaborationAcceptEventsAfterUtc <= {incomingRaid.MessageTimestamp.UtcDateTime});
            """,
            cancellationToken
        );
        if (changed != 1)
        {
            return;
        }

        var snapshot = await provider.LoadLiveChannelAsync(
            host.Id,
            otherLogin,
            null,
            cancellationToken
        );
        var available = snapshot as RaidChannelSnapshotOutcome.Available;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.RaidCollaborationHistory.SingleAsync(
            value => value.HostId == host.Id && value.ProviderMessageId == incomingRaid.MessageId,
            cancellationToken
        );
        row.Category = available?.Snapshot.Category;
        row.ProviderStreamId = available?.Snapshot.StreamId;
        if (direction == RaidDirection.Incoming)
        {
            await RunWelcomeSequenceAsync(db, host, row, incomingRaid, cancellationToken);
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        await db.Entry(row).ReloadAsync(cancellationToken);
        await PublishDomainEventAsync(
            row,
            direction == RaidDirection.Incoming
                ? RaidCollaborationDomainEventKind.IncomingRaidRecorded
                : RaidCollaborationDomainEventKind.OutgoingRaidRecorded,
            cancellationToken
        );
        if (row.WelcomeOutcome == RaidWelcomeOutcome.Delivered)
        {
            await PublishDomainEventAsync(
                row,
                RaidCollaborationDomainEventKind.WelcomeDelivered,
                cancellationToken
            );
        }
        if (row.ShoutoutOutcome == RaidShoutoutOutcome.Sent)
        {
            await PublishDomainEventAsync(
                row,
                RaidCollaborationDomainEventKind.NativeShoutoutSent,
                cancellationToken
            );
        }
        _ = await db
            .RaidCollaborationHistory.Where(value => value.HostId == host.Id)
            .OrderByDescending(value => value.OccurredAtUtc)
            .ThenByDescending(value => value.Id)
            .Skip(_historyLimit)
            .ExecuteDeleteAsync(cancellationToken);
        _ = await events.PublishAsync(AppEventKind.RaidCollaborationChanged, cancellationToken);
    }

    private async Task RunWelcomeSequenceAsync(
        BlokeBotDbContext db,
        BotHost host,
        RaidCollaborationHistoryEntry row,
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellationToken
    )
    {
        var configuration = await LoadConfigurationAsync(db, host.Id, cancellationToken);
        var shoutoutRequested =
            configuration.AutomaticShoutout.Enabled
            && (
                !configuration.AutomaticShoutout.OnlyApprovedChannels
                || configuration.ApprovedChannels.Any(channel =>
                    string.Equals(channel.Login, row.OtherLogin, StringComparison.Ordinal)
                )
            );
        if (!configuration.WelcomeEnabled && !shoutoutRequested)
        {
            return;
        }
        var dedupeAfter = incomingRaid.MessageTimestamp.UtcDateTime.AddMinutes(
            -configuration.DeduplicationWindowMinutes
        );
        var dedupeBefore = incomingRaid.MessageTimestamp.UtcDateTime.AddMinutes(
            configuration.DeduplicationWindowMinutes
        );
        var acceptAfter = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == host.Id)
            .Select(value => value.RaidCollaborationAcceptEventsAfterUtc)
            .SingleAsync(cancellationToken);
        var duplicate = await db
            .RaidCollaborationHistory.AsNoTracking()
            .AnyAsync(
                value =>
                    value.HostId == host.Id
                    && value.Id < row.Id
                    && value.Direction == RaidDirection.Incoming
                    && value.OtherTwitchUserId == row.OtherTwitchUserId
                    && value.OccurredAtUtc >= dedupeAfter
                    && value.OccurredAtUtc <= dedupeBefore
                    && (acceptAfter == null || value.OccurredAtUtc >= acceptAfter),
                cancellationToken
            );
        if (duplicate)
        {
            row.WelcomeOutcome = configuration.WelcomeEnabled
                ? RaidWelcomeOutcome.Deduplicated
                : RaidWelcomeOutcome.NotConfigured;
            row.ShoutoutOutcome = shoutoutRequested
                ? RaidShoutoutOutcome.Deduplicated
                : RaidShoutoutOutcome.NotConfigured;
            return;
        }

        if (configuration.WelcomeEnabled)
        {
            if (
                !await FeatureAcceptsCurrentWorkAsync(host.Id, row.OccurredAtUtc, cancellationToken)
            )
            {
                row.WelcomeOutcome = RaidWelcomeOutcome.Suppressed;
            }
            else
            {
                var delivered = await welcomeSender.SendAsync(
                    host.Id,
                    host.Login,
                    row.ProviderMessageId,
                    RenderWelcome(configuration.WelcomeMessage, row),
                    cancellationToken
                );
                row.WelcomeOutcome = delivered
                    ? RaidWelcomeOutcome.Delivered
                    : RaidWelcomeOutcome.Rejected;
            }
        }
        if (!shoutoutRequested)
        {
            return;
        }
        if (!await FeatureAcceptsCurrentWorkAsync(host.Id, row.OccurredAtUtc, cancellationToken))
        {
            row.ShoutoutOutcome = RaidShoutoutOutcome.Suppressed;
            return;
        }
        _ = await automaticShoutouts.RunAsync(
            host,
            configuration.AutomaticShoutout,
            incomingRaid,
            cancellationToken
        );
    }

    private async Task<(
        IReadOnlyList<RaidShortlistEntry> Eligible,
        IReadOnlyList<RaidShortlistExclusion> Excluded,
        FollowedLiveSourceState FollowedLiveSource
    )?> BuildShortlistAsync(
        int hostId,
        RaidCollaborationConfiguration configuration,
        IReadOnlyList<RaidRelationshipHistory> history,
        CancellationToken cancellationToken
    )
    {
        var candidates = new Dictionary<string, CandidateSource>(StringComparer.Ordinal);
        var excluded = new List<RaidShortlistExclusion>();
        FollowedLiveChannelsOutcome? followed = null;
        var followedState = FollowedLiveSourceState.Disabled;
        if (configuration.IncludeFollowedLiveChannels)
        {
            followed = await provider.LoadFollowedLiveChannelsAsync(hostId, cancellationToken);
            followedState = followed switch
            {
                FollowedLiveChannelsOutcome.Available => FollowedLiveSourceState.Ready,
                FollowedLiveChannelsOutcome.AuthorizationRequired =>
                    FollowedLiveSourceState.AuthorizationRequired,
                _ => FollowedLiveSourceState.Unavailable,
            };
        }

        foreach (var approved in configuration.ApprovedChannels)
        {
            if (!await FeatureAcceptsCurrentWorkAsync(hostId, null, cancellationToken))
            {
                return null;
            }
            var outcome = string.IsNullOrWhiteSpace(approved.TwitchUserId)
                ? await provider.LoadLiveChannelAsync(
                    hostId,
                    approved.Login,
                    approved.ApprovedClipId,
                    cancellationToken
                )
                : await provider.LoadLiveChannelByIdAsync(
                    hostId,
                    approved.TwitchUserId,
                    approved.ApprovedClipId,
                    cancellationToken
                );
            if (outcome is RaidChannelSnapshotOutcome.Unavailable)
            {
                return followedState == FollowedLiveSourceState.AuthorizationRequired
                    ? ([], [], followedState)
                    : null;
            }
            if (outcome is not RaidChannelSnapshotOutcome.Available available)
            {
                excluded.Add(new(approved.Login, [ExclusionReason(outcome)]));
                continue;
            }
            await PersistApprovalIdentityAsync(
                hostId,
                approved.Login,
                available.Snapshot.TwitchUserId,
                cancellationToken
            );
            candidates[available.Snapshot.TwitchUserId] = new(
                available.Snapshot,
                RaidCandidateProvenance.Approved
            );
        }

        if (followed is FollowedLiveChannelsOutcome.Available followedAvailable)
        {
            foreach (var snapshot in followedAvailable.Channels)
            {
                _ = candidates.TryAdd(
                    snapshot.TwitchUserId,
                    new(snapshot with { ApprovedClip = null }, RaidCandidateProvenance.Followed)
                );
            }
        }

        var eligible = new List<RaidShortlistEntry>();
        foreach (var candidate in candidates.Values)
        {
            var recent = history
                .Where(value =>
                    value.Direction == RaidDirection.Outgoing
                    && string.Equals(
                        value.TwitchUserId,
                        candidate.Snapshot.TwitchUserId,
                        StringComparison.Ordinal
                    )
                )
                .Select(value => (DateTime?)value.OccurredAt.UtcDateTime)
                .FirstOrDefault();
            var reasons = FilterReasons(configuration, candidate.Snapshot, recent);
            if (reasons.Count > 0)
            {
                excluded.Add(new(candidate.Snapshot.Login, reasons));
                continue;
            }
            eligible.Add(ToShortlistEntry(configuration, candidate, recent));
        }
        return (eligible.OrderBy(value => value.ViewerCount).ToArray(), excluded, followedState);
    }

    private static RaidShortlistEntry ToShortlistEntry(
        RaidCollaborationConfiguration configuration,
        CandidateSource candidate,
        DateTime? recent
    ) =>
        new(
            candidate.Snapshot.TwitchUserId,
            candidate.Snapshot.Login,
            candidate.Snapshot.DisplayName,
            candidate.Snapshot.StreamId,
            candidate.Snapshot.Category,
            candidate.Snapshot.Language,
            candidate.Snapshot.Title,
            candidate.Snapshot.ViewerCount,
            EligibilityReasons(configuration, recent, candidate.Provenance),
            candidate.Snapshot.ApprovedClip,
            candidate.Provenance
        );

    private IReadOnlyList<string> FilterReasons(
        RaidCollaborationConfiguration configuration,
        RaidChannelSnapshot snapshot,
        DateTime? lastOutgoingRaidUtc
    )
    {
        var reasons = new List<string>();
        if (
            !string.IsNullOrWhiteSpace(configuration.Language)
            && !string.Equals(
                configuration.Language,
                snapshot.Language,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            reasons.Add($"Language is {snapshot.Language}, not {configuration.Language}.");
        }
        if (
            configuration.EligibleCategories.Count > 0
            && !configuration.EligibleCategories.Contains(
                snapshot.Category,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            reasons.Add($"Category “{snapshot.Category}” is not selected.");
        }
        if (
            lastOutgoingRaidUtc is { } lastRaid
            && lastRaid.AddHours(configuration.RelationshipCooldownHours)
                > clock.GetUtcNow().UtcDateTime
        )
        {
            reasons.Add(
                $"Last outgoing raid was {lastRaid.ToLocalTime():MMM d}; relationship gap is still active."
            );
        }
        return reasons;
    }

    private static IReadOnlyList<string> EligibilityReasons(
        RaidCollaborationConfiguration configuration,
        DateTime? lastOutgoingRaidUtc,
        RaidCandidateProvenance provenance
    ) =>
        [
            provenance == RaidCandidateProvenance.Approved
                ? "Approved by this channel"
                : "Followed by this channel",
            "Live now",
            string.IsNullOrWhiteSpace(configuration.Language)
                ? "Any language allowed"
                : $"Language: {configuration.Language}",
            configuration.EligibleCategories.Count == 0
                ? "Any category allowed"
                : "Category selected",
            lastOutgoingRaidUtc is null ? "No recent outgoing raid" : "Relationship gap passed",
        ];

    private async Task<RaidCollaborationConfiguration> LoadConfigurationAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var settings = await db
            .RaidCollaborationSettings.AsNoTracking()
            .SingleOrDefaultAsync(value => value.HostId == hostId, cancellationToken);
        var shoutoutSettings = await db
            .AutomaticRaidShoutoutSettings.AsNoTracking()
            .SingleOrDefaultAsync(value => value.HostId == hostId, cancellationToken);
        var shoutout = shoutoutSettings is null
            ? AutomaticRaidShoutoutConfiguration.Defaults
            : AutomaticRaidShoutoutConfiguration.From(shoutoutSettings);
        var channels = await db
            .ApprovedRaidChannels.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Login)
            .Select(value => new ApprovedRaidChannelDraft(
                value.Login,
                value.DisplayName,
                value.ApprovedClipId
            )
            {
                TwitchUserId = value.TwitchUserId,
            })
            .ToArrayAsync(cancellationToken);
        return settings is null
            ? RaidCollaborationConfiguration.Defaults with
            {
                AutomaticShoutout = shoutout,
                ApprovedChannels = channels,
            }
            : new(
                settings.WelcomeEnabled,
                settings.WelcomeMessage,
                shoutout,
                settings.DeduplicationWindowMinutes,
                settings.Language,
                ParseCategories(settings.EligibleCategories),
                settings.RelationshipCooldownHours,
                channels,
                settings.IncludeFollowedLiveChannels
            );
    }

    private async Task PersistApprovalIdentityAsync(
        int hostId,
        string approvedLogin,
        string twitchUserId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var approved = await db.ApprovedRaidChannels.SingleOrDefaultAsync(
            channel => channel.HostId == hostId && channel.Login == approvedLogin,
            cancellationToken
        );
        if (approved is null || approved.TwitchUserId == twitchUserId)
        {
            return;
        }
        approved.TwitchUserId = twitchUserId;
        approved.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<CandidateRevalidation> RevalidateCandidateAsync(
        int hostId,
        string targetTwitchUserId,
        CancellationToken cancellationToken
    )
    {
        if (!await FeatureAcceptsCurrentWorkAsync(hostId, null, cancellationToken))
        {
            return new CandidateRevalidation.FeatureDisabled();
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await LoadConfigurationAsync(db, hostId, cancellationToken);
        RaidChannelSnapshot? snapshot = null;
        foreach (var approved in configuration.ApprovedChannels)
        {
            var outcome = string.IsNullOrWhiteSpace(approved.TwitchUserId)
                ? await provider.LoadLiveChannelAsync(
                    hostId,
                    approved.Login,
                    approved.ApprovedClipId,
                    cancellationToken
                )
                : await provider.LoadLiveChannelByIdAsync(
                    hostId,
                    approved.TwitchUserId,
                    approved.ApprovedClipId,
                    cancellationToken
                );
            if (
                outcome is RaidChannelSnapshotOutcome.Available available
                && string.Equals(
                    available.Snapshot.TwitchUserId,
                    targetTwitchUserId,
                    StringComparison.Ordinal
                )
            )
            {
                snapshot = available.Snapshot;
                break;
            }
        }

        if (snapshot is null && configuration.IncludeFollowedLiveChannels)
        {
            var followed = await provider.LoadFollowedLiveChannelsAsync(hostId, cancellationToken);
            if (followed is FollowedLiveChannelsOutcome.AuthorizationRequired)
            {
                return new CandidateRevalidation.AuthorizationRequired();
            }
            if (followed is FollowedLiveChannelsOutcome.Available available)
            {
                snapshot = available.Channels.FirstOrDefault(channel =>
                    string.Equals(
                        channel.TwitchUserId,
                        targetTwitchUserId,
                        StringComparison.Ordinal
                    )
                );
            }
        }
        if (snapshot is null)
        {
            return new CandidateRevalidation.NotInSource();
        }

        var recent = await db
            .RaidCollaborationHistory.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.Direction == RaidDirection.Outgoing
                && value.OtherTwitchUserId == targetTwitchUserId
            )
            .OrderByDescending(value => value.OccurredAtUtc)
            .Select(value => (DateTime?)value.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var reasons = FilterReasons(configuration, snapshot, recent);
        return reasons.Count > 0 ? new CandidateRevalidation.Ineligible(reasons)
            : !await FeatureAcceptsCurrentWorkAsync(hostId, null, cancellationToken)
                ? new CandidateRevalidation.FeatureDisabled()
            : new CandidateRevalidation.Eligible(snapshot);
    }

    private async Task<bool> FeatureAcceptsCurrentWorkAsync(
        int hostId,
        DateTime? occurredAtUtc,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .AnyAsync(
                host =>
                    host.Id == hostId
                    && (host.EnabledFeatures & HostFeatureFlags.RaidCollaboration)
                        == HostFeatureFlags.RaidCollaboration
                    && (
                        occurredAtUtc == null
                        || host.RaidCollaborationAcceptEventsAfterUtc == null
                        || occurredAtUtc >= host.RaidCollaborationAcceptEventsAfterUtc
                    ),
                cancellationToken
            );
    }

    private async Task PublishDomainEventAsync(
        RaidCollaborationHistoryEntry row,
        RaidCollaborationDomainEventKind kind,
        CancellationToken cancellationToken
    )
    {
        if (!await FeatureAcceptsCurrentWorkAsync(row.HostId, row.OccurredAtUtc, cancellationToken))
        {
            return;
        }
        var domainEvent = new RaidCollaborationDomainEvent(
            row.HostId,
            kind,
            row.ProviderMessageId,
            row.Direction,
            row.OtherTwitchUserId,
            row.OtherLogin,
            row.OtherDisplayName,
            row.ViewerCount,
            row.Category,
            row.ProviderStreamId,
            row.OccurredAtUtc
        );
        foreach (var observer in _domainEventObservers)
        {
            await observer.CollaborationEventAsync(domainEvent, cancellationToken);
        }
    }

    internal static IReadOnlyList<string> Validate(RaidCollaborationConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.WelcomeMessage.Trim().Length is 0 or > 500)
        {
            errors.Add("Welcome message must be between 1 and 500 characters.");
        }
        if (configuration.DeduplicationWindowMinutes is < 1 or > 1440)
        {
            errors.Add("Deduplication window must be between 1 and 1,440 minutes.");
        }
        if (configuration.Language.Trim().Length > 16)
        {
            errors.Add("Language code must be 16 characters or fewer.");
        }
        if (configuration.RelationshipCooldownHours is < 0 or > 8760)
        {
            errors.Add("Relationship gap must be between 0 and 8,760 hours.");
        }
        if (configuration.EligibleCategories.Count > 20)
        {
            errors.Add("Choose no more than 20 eligible categories.");
        }
        if (configuration.ApprovedChannels.Count > _approvedChannelLimit)
        {
            errors.Add($"Approve no more than {_approvedChannelLimit} channels.");
        }
        var normalized = configuration
            .ApprovedChannels.Select(value => Login.Normalize(value.Login))
            .ToArray();
        if (normalized.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Every approved channel needs a Twitch login.");
        }
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            errors.Add("Each approved Twitch channel can appear only once.");
        }
        if (
            configuration.ApprovedChannels.Any(value =>
                value.DisplayName.Trim().Length > 128 || value.ApprovedClipId?.Trim().Length > 128
            )
        )
        {
            errors.Add("Approved channel names and clip IDs are too long.");
        }
        return errors;
    }

    private static string RenderWelcome(string template, RaidCollaborationHistoryEntry row) =>
        template
            .Replace("{display_name}", row.OtherDisplayName, StringComparison.Ordinal)
            .Replace("{twitch_handle}", $"@{row.OtherLogin}", StringComparison.Ordinal)
            .Replace(
                "{viewer_count}",
                row.ViewerCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal
            );

    private static string ExclusionReason(RaidChannelSnapshotOutcome outcome) =>
        outcome switch
        {
            RaidChannelSnapshotOutcome.Offline => "Channel is offline.",
            RaidChannelSnapshotOutcome.NotFound => "Twitch channel was not found.",
            _ => "Live Twitch context is unavailable.",
        };

    private static bool ValidEvent(EventSubIncomingRaidEvent value) =>
        !string.IsNullOrWhiteSpace(value.MessageId)
        && value.MessageId.Length <= 128
        && value.MessageTimestamp != default
        && value.ViewerCount >= 0
        && !string.IsNullOrWhiteSpace(value.FromBroadcasterUserId)
        && !string.IsNullOrWhiteSpace(value.ToBroadcasterUserId)
        && !string.IsNullOrWhiteSpace(Login.Normalize(value.FromBroadcasterUserLogin))
        && !string.IsNullOrWhiteSpace(Login.Normalize(value.ToBroadcasterUserLogin));

    private static string DisplayName(ApprovedRaidChannelDraft value) =>
        string.IsNullOrWhiteSpace(value.DisplayName)
            ? Login.Normalize(value.Login)
            : value.DisplayName.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatCategories(IReadOnlyList<string> categories) =>
        string.Join(
            '\n',
            categories
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        );

    private static IReadOnlyList<string> ParseCategories(string value) =>
        value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record CandidateSource(
        RaidChannelSnapshot Snapshot,
        RaidCandidateProvenance Provenance
    );

    private abstract record CandidateRevalidation
    {
        private CandidateRevalidation() { }

        public sealed record Eligible(RaidChannelSnapshot Snapshot) : CandidateRevalidation;

        public sealed record Ineligible(IReadOnlyList<string> Reasons) : CandidateRevalidation;

        public sealed record NotInSource : CandidateRevalidation;

        public sealed record AuthorizationRequired : CandidateRevalidation;

        public sealed record FeatureDisabled : CandidateRevalidation;
    }
}
