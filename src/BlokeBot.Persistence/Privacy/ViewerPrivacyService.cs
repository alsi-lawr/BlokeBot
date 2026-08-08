using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

/// <summary>
/// Identifies the Twitch identity a privacy request concerns. At least one of the Twitch user id
/// and the login must be supplied; matching uses whichever parts are present.
/// </summary>
public sealed record PrivacySubject
{
    private PrivacySubject(string? twitchUserId, string? login)
    {
        TwitchUserId = twitchUserId;
        Login = login;
    }

    public string? TwitchUserId { get; }

    public string? Login { get; }

    internal string IdIdentityKey =>
        TwitchUserId is null ? ViewerPrivacyService.UnmatchableValue : $"id:{TwitchUserId}";

    internal string LoginIdentityKey =>
        Login is null ? ViewerPrivacyService.UnmatchableValue : $"login:{Login}";

    public static PrivacySubject Create(string? twitchUserId, string? login)
    {
        var normalizedId = string.IsNullOrWhiteSpace(twitchUserId) ? null : twitchUserId.Trim();
        var normalizedLogin = NormalizeLogin(login);
        return normalizedId is null && normalizedLogin is null
            ? throw new ArgumentException(
                "A privacy subject needs a Twitch user id, a login, or both."
            )
            : new PrivacySubject(normalizedId, normalizedLogin);
    }

    private static string? NormalizeLogin(string? login)
    {
        var trimmed = login?.Trim().TrimStart('@', '#');
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToLowerInvariant();
    }
}

public sealed record ViewerDataExport(IReadOnlyDictionary<string, IReadOnlyList<object>> Sections);

public sealed record ViewerErasureReport(IReadOnlyDictionary<string, int> ChangedRows)
{
    public int TotalChangedRows => ChangedRows.Values.Sum();
}

/// <summary>
/// Locates, exports, and erases the data attributable to one Twitch identity across every
/// persisted feature. Erasure follows the accepted policy: rows that exist only for the subject
/// are deleted; rows that must remain for non-personal aggregate, ledger, or audit integrity keep
/// their numbers but lose identity and free-text fields, replaced by a shared non-reversible
/// token. Reruns are no-ops because erased rows no longer match the subject.
/// </summary>
public static class ViewerPrivacyService
{
    public const string ErasedToken = "[erased]";

    // Comparisons against a missing identity part must match nothing, never NULL columns, so an
    // absent part becomes a value no Twitch id or login can contain.
    internal const string UnmatchableValue = "\u0001";

    public static async Task<ViewerDataExport> ExportAsync(
        BlokeBotDbContext db,
        PrivacySubject subject,
        int? hostId,
        CancellationToken ct
    )
    {
        var sections = new Dictionary<string, IReadOnlyList<object>>(StringComparer.Ordinal);
        var userId = subject.TwitchUserId ?? UnmatchableValue;
        var login = subject.Login ?? UnmatchableValue;
        var idKey = subject.IdIdentityKey;
        var loginKey = subject.LoginIdentityKey;

        async Task AddAsync<T>(string section, IQueryable<T> query)
            where T : class
        {
            var rows = await query.AsNoTracking().ToListAsync(ct);
            if (rows.Count > 0)
            {
                sections[section] = rows;
            }
        }

        await AddAsync(
            "hosts.channels",
            db.Hosts.Where(x => x.TwitchUserId == userId || x.Login == login)
                .Select(x => new
                {
                    x.Id,
                    x.TwitchUserId,
                    x.Login,
                    x.DisplayName,
                    x.CreatedAtUtc,
                    Note = "Hosted channel record; erased by removing the channel, not by viewer erasure.",
                })
        );
        await AddAsync(
            "guessing.votes",
            db.Votes.Where(x =>
                    x.Login == login && (hostId == null || x.GuessRound!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.GuessRound!.HostId,
                    x.GuessRoundId,
                    x.Login,
                    x.GuessName,
                    x.GuessedAtUtc,
                })
        );
        await AddAsync(
            "points.balances",
            db.PointBalances.Where(x => x.Login == login && (hostId == null || x.HostId == hostId))
        );
        await AddAsync(
            "points.ledger",
            db.PointLedgerEntries.Where(x =>
                (x.Login == login || x.ActorLogin == login || x.CounterpartyLogin == login)
                && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "points.giveaway-entries",
            db.PointsGiveawayEntrants.Where(x =>
                    x.Login == login && (hostId == null || x.Giveaway!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Giveaway!.HostId,
                    x.GiveawayId,
                    x.Login,
                    x.JoinedAtUtc,
                })
        );
        await AddAsync(
            "points.giveaway-wins",
            db.PointsGiveawayWinners.Where(x =>
                    x.Login == login && (hostId == null || x.Giveaway!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Giveaway!.HostId,
                    x.GiveawayId,
                    x.Login,
                    x.Payout,
                })
        );
        await AddAsync(
            "commands.allowed-users",
            db.CustomCommandAllowedUsers.Where(x =>
                    (x.TwitchUserId == userId || x.Login == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchUserId,
                    x.Login,
                    x.DisplayName,
                })
        );
        await AddAsync(
            "commands.usage-claims",
            db.CustomCommandInvocationClaims.Where(x =>
                    x.TwitchUserId == userId && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchUserId,
                    x.ClaimedAtUtc,
                })
        );
        await AddAsync(
            "commands.reset-audits",
            db.CustomCommandInvocationResetAudits.Where(x =>
                (
                    x.ActorTwitchUserId == userId
                    || x.ActorLogin == login
                    || x.TargetTwitchUserId == userId
                    || x.TargetLogin == login
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "alerts.acknowledgements",
            db.DurableAlerts.Where(x =>
                    x.AcknowledgedByLogin == login && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.Title,
                    x.AcknowledgedAtUtc,
                    x.AcknowledgedByLogin,
                })
        );
        await AddAsync(
            "access.site-entries",
            db.SiteAccessEntries.Where(x => x.Login == login && hostId == null)
        );
        await AddAsync(
            "access.mod-entries",
            db.HostModAccessEntries.Where(x =>
                x.Login == login && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "whispers.recipients",
            db.WhisperQuotaRecipients.Where(x =>
                    (x.RecipientTwitchUserId == userId || x.RecipientLogin == login)
                    && (hostId == null || x.WhisperQuotaBucket.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.WhisperQuotaBucket.HostId,
                    x.RecipientTwitchUserId,
                    x.RecipientLogin,
                    x.FirstSentAtUtc,
                })
        );
        await AddAsync(
            "shoutouts.history",
            db.ShoutoutHistory.Where(x =>
                (
                    x.SourceTwitchUserId == userId
                    || x.SourceLogin == login
                    || x.TargetTwitchUserId == userId
                    || x.TargetLogin == login
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "shoutouts.cooldowns",
            db.ShoutoutCooldowns.Where(x =>
                (x.TargetTwitchUserId == userId || x.TargetLogin == login)
                && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "shoutouts.raid-outcomes",
            db.AutomaticRaidShoutoutOutcomes.Where(x =>
                (x.SourceTwitchUserId == userId || x.SourceLogin == login)
                && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "channel-points.redemptions",
            db.TwitchRewardRedemptions.Where(x =>
                (x.UserId == userId || x.UserLogin == login)
                && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "clips.created",
            db.TwitchClips.Where(x =>
                    (x.CreatorTwitchUserId == userId || x.CreatorLogin == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.CreatorTwitchUserId,
                    x.CreatorLogin,
                    x.FinalUrl,
                    x.RequestedAtUtc,
                })
        );
        await AddAsync(
            "request-boards.submissions",
            db.RequestSubmissions.Where(x =>
                    x.SubmitterLogin == login && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.BoardId,
                    x.SubmitterLogin,
                    x.Title,
                    x.NormalizedUrl,
                    x.Status,
                    x.PublicNote,
                    x.CreatedAtUtc,
                    Values = x.Values.Select(value => value.Value).ToList(),
                })
        );
        await AddAsync(
            "request-boards.votes",
            db.RequestSubmissionVotes.Where(x =>
                    x.VoterLogin == login && (hostId == null || x.Submission!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Submission!.HostId,
                    x.SubmissionId,
                    x.VoterLogin,
                    x.CreatedAtUtc,
                })
        );
        await AddAsync(
            "play-queues.entries",
            db.PlayQueueEntries.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || x.NormalizedLogin == login
                        || x.IdentityKey == idKey
                        || x.IdentityKey == loginKey
                    ) && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.QueueId,
                    x.TwitchUserId,
                    x.NormalizedLogin,
                    x.DisplayName,
                    x.Status,
                    x.JoinedAtUtc,
                    Values = x.Values.Select(value => value.Value).ToList(),
                })
        );
        await AddAsync(
            "play-queues.participation",
            db.PlayQueueParticipation.Where(x =>
                (x.IdentityKey == idKey || x.IdentityKey == loginKey)
                && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "play-queues.exclusions",
            db.PlayQueueExclusions.Where(x =>
                (x.IdentityKey == idKey || x.IdentityKey == loginKey)
                && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "moments.contributors",
            db.MomentContributors.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || x.NormalizedLogin == login
                        || x.IdentityKey == idKey
                        || x.IdentityKey == loginKey
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Candidate!.HostId,
                    x.CandidateId,
                    x.TwitchUserId,
                    x.NormalizedLogin,
                    x.DisplayName,
                    x.CaptureCount,
                })
        );
        await AddAsync(
            "moments.capture-requests",
            db.MomentCaptureRequests.Where(x =>
                    (x.IdentityKey == idKey || x.IdentityKey == loginKey)
                    && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Candidate!.HostId,
                    x.CandidateId,
                    x.IdentityKey,
                    x.CapturedAtUtc,
                })
        );
        await AddAsync(
            "moments.suggestions",
            db.MomentSuggestions.Where(x =>
                    (x.IdentityKey == idKey || x.IdentityKey == loginKey)
                    && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Candidate!.HostId,
                    x.CandidateId,
                    x.IdentityKey,
                    x.SuggestedTitle,
                    x.SuggestedCategory,
                    x.CreatedAtUtc,
                })
        );
        await AddAsync(
            "moments.votes",
            db.MomentVotes.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || x.NormalizedLogin == login
                        || x.IdentityKey == idKey
                        || x.IdentityKey == loginKey
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Candidate!.HostId,
                    x.CandidateId,
                    x.TwitchUserId,
                    x.NormalizedLogin,
                    x.CreatedAtUtc,
                })
        );
        await AddAsync(
            "moments.moderation-audits",
            db.MomentModerationAudit.Where(x =>
                x.ActorLogin == login && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "moments.merges",
            db.MomentMerges.Where(x =>
                    x.ActorLogin == login && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.SourceCandidateId,
                    x.TargetCandidateId,
                    x.ActorLogin,
                    x.PrivateText,
                    x.MergedAtUtc,
                })
        );
        await AddAsync(
            "overlays.actor-events",
            db.OverlayInstanceEvents.Where(x =>
                    (x.ActorUserId == userId || x.ActorLogin == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.Kind,
                    x.ActorUserId,
                    x.ActorLogin,
                    x.OccurredAtUtc,
                })
        );
        await AddAsync(
            "public-chat.pins",
            db.PublicChatPinOperations.Where(x =>
                    x.PinnerTwitchUserId == userId && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.Kind,
                    x.PinnerTwitchUserId,
                    x.CreatedAtUtc,
                })
        );

        return new ViewerDataExport(sections);
    }

    public static async Task<ViewerErasureReport> EraseAsync(
        BlokeBotDbContext db,
        PrivacySubject subject,
        int? hostId,
        CancellationToken ct
    )
    {
        var changed = new Dictionary<string, int>(StringComparer.Ordinal);
        var userId = subject.TwitchUserId ?? UnmatchableValue;
        var login = subject.Login ?? UnmatchableValue;
        var idKey = subject.IdIdentityKey;
        var loginKey = subject.LoginIdentityKey;
        var quotedNeedles = new List<string>();
        if (subject.Login is not null)
        {
            quotedNeedles.Add($"%\"{subject.Login}\"%");
        }

        if (subject.TwitchUserId is not null)
        {
            quotedNeedles.Add($"%\"{subject.TwitchUserId}\"%");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        void Record(string section, int rows)
        {
            if (rows > 0)
            {
                changed[section] = rows;
            }
        }

        Record(
            "guessing.votes",
            await db
                .Votes.Where(x =>
                    x.Login == login && (hostId == null || x.GuessRound!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "points.balances",
            await db
                .PointBalances.Where(x =>
                    x.Login == login && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "points.ledger.subject-rows",
            await db
                .PointLedgerEntries.Where(x =>
                    x.Login == login && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.Login, ErasedToken)
                            .SetProperty(x => x.Note, string.Empty),
                    ct
                )
        );
        Record(
            "points.ledger.actor-references",
            await db
                .PointLedgerEntries.Where(x =>
                    x.ActorLogin == login && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.ActorLogin, (string?)null),
                    ct
                )
        );
        Record(
            "points.ledger.counterparty-references",
            await db
                .PointLedgerEntries.Where(x =>
                    x.CounterpartyLogin == login && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.CounterpartyLogin, (string?)null),
                    ct
                )
        );
        Record(
            "points.giveaway-entries",
            await db
                .PointsGiveawayEntrants.Where(x =>
                    x.Login == login && (hostId == null || x.Giveaway!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "points.giveaway-wins",
            await db
                .PointsGiveawayWinners.Where(x =>
                    x.Login == login && (hostId == null || x.Giveaway!.HostId == hostId)
                )
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Login, ErasedToken), ct)
        );
        Record(
            "commands.allowed-users",
            await db
                .CustomCommandAllowedUsers.Where(x =>
                    (x.TwitchUserId == userId || x.Login == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "commands.usage-claims",
            await db
                .CustomCommandInvocationClaims.Where(x =>
                    x.TwitchUserId == userId && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "commands.reset-audits.actor",
            await db
                .CustomCommandInvocationResetAudits.Where(x =>
                    (x.ActorTwitchUserId == userId || x.ActorLogin == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorTwitchUserId, ErasedToken)
                            .SetProperty(x => x.ActorLogin, ErasedToken),
                    ct
                )
        );
        Record(
            "commands.reset-audits.target",
            await db
                .CustomCommandInvocationResetAudits.Where(x =>
                    (x.TargetTwitchUserId == userId || x.TargetLogin == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.TargetTwitchUserId, (string?)null)
                            .SetProperty(x => x.TargetLogin, (string?)null),
                    ct
                )
        );
        Record(
            "alerts.acknowledgements",
            await db
                .DurableAlerts.Where(x =>
                    x.AcknowledgedByLogin == login && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.AcknowledgedByLogin, (string?)null),
                    ct
                )
        );
        if (hostId is null)
        {
            Record(
                "access.site-entries",
                await db.SiteAccessEntries.Where(x => x.Login == login).ExecuteDeleteAsync(ct)
            );
        }

        Record(
            "access.mod-entries",
            await db
                .HostModAccessEntries.Where(x =>
                    x.Login == login && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "whispers.recipients",
            await db
                .WhisperQuotaRecipients.Where(x =>
                    (x.RecipientTwitchUserId == userId || x.RecipientLogin == login)
                    && (hostId == null || x.WhisperQuotaBucket.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "shoutouts.history",
            await db
                .ShoutoutHistory.Where(x =>
                    (
                        x.SourceTwitchUserId == userId
                        || x.SourceLogin == login
                        || x.TargetTwitchUserId == userId
                        || x.TargetLogin == login
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "shoutouts.cooldowns",
            await db
                .ShoutoutCooldowns.Where(x =>
                    (x.TargetTwitchUserId == userId || x.TargetLogin == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "shoutouts.raid-outcomes",
            await db
                .AutomaticRaidShoutoutOutcomes.Where(x =>
                    (x.SourceTwitchUserId == userId || x.SourceLogin == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "channel-points.redemptions",
            await db
                .TwitchRewardRedemptions.Where(x =>
                    (x.UserId == userId || x.UserLogin == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "clips.creator-references",
            await db
                .TwitchClips.Where(x =>
                    (x.CreatorTwitchUserId == userId || x.CreatorLogin == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.CreatorTwitchUserId, (string?)null)
                            .SetProperty(x => x.CreatorLogin, (string?)null),
                    ct
                )
        );

        var votedSubmissionIds = await db
            .RequestSubmissionVotes.Where(x =>
                x.VoterLogin == login && (hostId == null || x.Submission!.HostId == hostId)
            )
            .Select(x => x.SubmissionId)
            .Distinct()
            .ToListAsync(ct);
        Record(
            "request-boards.votes",
            await db
                .RequestSubmissionVotes.Where(x =>
                    x.VoterLogin == login && (hostId == null || x.Submission!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        if (votedSubmissionIds.Count > 0)
        {
            _ = await db
                .RequestSubmissions.Where(x => votedSubmissionIds.Contains(x.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.VoteCount, x => x.Votes.Count),
                    ct
                );
        }

        Record(
            "request-boards.submissions",
            await db
                .RequestSubmissions.Where(x =>
                    x.SubmitterLogin == login && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        var requestBoardEvents = 0;
        foreach (var needle in quotedNeedles)
        {
            requestBoardEvents += await db
                .RequestBoardEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, needle)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record("request-boards.events", requestBoardEvents);
        Record(
            "play-queues.entries",
            await db
                .PlayQueueEntries.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || x.NormalizedLogin == login
                        || x.IdentityKey == idKey
                        || x.IdentityKey == loginKey
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "play-queues.participation",
            await db
                .PlayQueueParticipation.Where(x =>
                    (x.IdentityKey == idKey || x.IdentityKey == loginKey)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "play-queues.exclusions",
            await db
                .PlayQueueExclusions.Where(x =>
                    (x.IdentityKey == idKey || x.IdentityKey == loginKey)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        var playQueueEvents = 0;
        foreach (var needle in quotedNeedles)
        {
            playQueueEvents += await db
                .PlayQueueEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, needle)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record("play-queues.events", playQueueEvents);
        Record(
            "moments.contributors",
            await db
                .MomentContributors.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || x.NormalizedLogin == login
                        || x.IdentityKey == idKey
                        || x.IdentityKey == loginKey
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "moments.capture-requests",
            await db
                .MomentCaptureRequests.Where(x =>
                    (x.IdentityKey == idKey || x.IdentityKey == loginKey)
                    && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "moments.suggestions",
            await db
                .MomentSuggestions.Where(x =>
                    (x.IdentityKey == idKey || x.IdentityKey == loginKey)
                    && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "moments.votes",
            await db
                .MomentVotes.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || x.NormalizedLogin == login
                        || x.IdentityKey == idKey
                        || x.IdentityKey == loginKey
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "moments.moderation-audits",
            await db
                .MomentModerationAudit.Where(x =>
                    x.ActorLogin == login && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorLogin, ErasedToken)
                            .SetProperty(x => x.PrivateText, string.Empty),
                    ct
                )
        );
        Record(
            "moments.merges",
            await db
                .MomentMerges.Where(x =>
                    x.ActorLogin == login && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorLogin, ErasedToken)
                            .SetProperty(x => x.PrivateText, string.Empty),
                    ct
                )
        );
        var momentEvents = 0;
        foreach (var needle in quotedNeedles)
        {
            momentEvents += await db
                .MomentEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, needle)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record("moments.events", momentEvents);
        Record(
            "overlays.actor-events",
            await db
                .OverlayInstanceEvents.Where(x =>
                    (x.ActorUserId == userId || x.ActorLogin == login)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorUserId, ErasedToken)
                            .SetProperty(x => x.ActorLogin, ErasedToken),
                    ct
                )
        );
        if (subject.Login is not null)
        {
            Record(
                "overlays.event-feed",
                await db
                    .OverlayEventFeedItems.Where(x =>
                        (
                            EF.Functions.Like(x.Title, $"%{login}%")
                            || EF.Functions.Like(x.Body, $"%{login}%")
                        ) && (hostId == null || x.HostId == hostId)
                    )
                    .ExecuteDeleteAsync(ct)
            );
        }

        Record(
            "public-chat.pin-operations",
            await db
                .PublicChatPinOperations.Where(x =>
                    x.PinnerTwitchUserId == userId && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.PinnerTwitchUserId, (string?)null),
                    ct
                )
        );
        Record(
            "public-chat.active-pins",
            await db
                .ActivePublicChatPins.Where(x =>
                    x.PinnerTwitchUserId == userId && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.PinnerTwitchUserId, ErasedToken),
                    ct
                )
        );
        var automationRuns = 0;
        foreach (var needle in quotedNeedles)
        {
            automationRuns += await db
                .AutomationFlowRuns.Where(x =>
                    EF.Functions.Like(x.ContextJson, needle)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record("automations.runs", automationRuns);

        await transaction.CommitAsync(ct);
        return new ViewerErasureReport(changed);
    }
}
