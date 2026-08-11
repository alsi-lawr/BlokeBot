using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

/// <summary>
/// Identifies the Twitch identity a privacy request concerns. A Twitch user id is authoritative;
/// login-only attribution requires a unique, non-ambiguous viewer-passport claim.
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
        var scope = await ResolveIdentityScopeAsync(db, subject, hostId, ct);
        var userId = scope.UserId;
        var idKey = scope.IdIdentityKey;
        var passportIds = scope.PassportIds;
        var safeLoginClaims = SafeLoginClaims(db, passportIds);
        var safeGlobalLoginClaims = SafeGlobalLoginClaims(db, safeLoginClaims);

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
            db.Hosts.Where(x => x.TwitchUserId == userId)
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
                    (hostId == null || x.GuessRound!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.GuessRound!.HostId && claim.Login == x.Login
                    )
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
            db.PointBalances.Where(x =>
                (hostId == null || x.HostId == hostId)
                && safeLoginClaims.Any(claim => claim.HostId == x.HostId && claim.Login == x.Login)
            )
        );
        await AddAsync(
            "points.ledger",
            db.PointLedgerEntries.Where(x =>
                (
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId
                        && (
                            claim.Login == x.Login
                            || claim.Login == x.ActorLogin
                            || claim.Login == x.CounterpartyLogin
                        )
                    )
                    || (
                        x.BountyPledgeId != null
                        && db.BountyPledges.Any(pledge =>
                            pledge.HostId == x.HostId
                            && pledge.Id == x.BountyPledgeId
                            && (
                                pledge.ContributorTwitchUserId == userId
                                || safeLoginClaims.Any(claim =>
                                    claim.HostId == pledge.HostId
                                    && claim.Login == pledge.ContributorLogin
                                )
                            )
                        )
                    )
                    || (
                        x.BountyRewardId != null
                        && db.BountyContributorRewards.Any(reward =>
                            reward.HostId == x.HostId
                            && reward.Id == x.BountyRewardId
                            && (
                                reward.TwitchUserId == userId
                                || safeLoginClaims.Any(claim =>
                                    claim.HostId == reward.HostId && claim.Login == reward.Login
                                )
                            )
                        )
                    )
                    || (
                        x.CommunityCompletionId != null
                        && db.CommunityCompletions.Any(completion =>
                            completion.HostId == x.HostId
                            && completion.Id == x.CommunityCompletionId
                            && (
                                completion.ViewerTwitchUserId == userId
                                || safeLoginClaims.Any(claim =>
                                    claim.HostId == completion.HostId
                                    && claim.Login == completion.ViewerLogin
                                )
                            )
                        )
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "points.giveaway-entries",
            db.PointsGiveawayEntrants.Where(x =>
                    (hostId == null || x.Giveaway!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.Giveaway!.HostId && claim.Login == x.Login
                    )
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
                    (hostId == null || x.Giveaway!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.Giveaway!.HostId && claim.Login == x.Login
                    )
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
                    (
                        x.TwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.Login
                        )
                    ) && (hostId == null || x.HostId == hostId)
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
                    || x.TargetTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId
                        && (claim.Login == x.ActorLogin || claim.Login == x.TargetLogin)
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "alerts.acknowledgements",
            db.DurableAlerts.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.AcknowledgedByLogin
                    ) && (hostId == null || x.HostId == hostId)
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
            db.SiteAccessEntries.Where(x =>
                hostId == null && safeGlobalLoginClaims.Any(claim => claim.Login == x.Login)
            )
        );
        await AddAsync(
            "access.mod-entries",
            db.HostModAccessEntries.Where(x =>
                safeLoginClaims.Any(claim => claim.HostId == x.HostId && claim.Login == x.Login)
                && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "whispers.recipients",
            db.WhisperQuotaRecipients.Where(x =>
                    (
                        x.RecipientTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.WhisperQuotaBucket.HostId
                            && claim.Login == x.RecipientLogin
                        )
                    ) && (hostId == null || x.WhisperQuotaBucket.HostId == hostId)
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
                    || x.TargetTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId
                        && (claim.Login == x.SourceLogin || claim.Login == x.TargetLogin)
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "shoutouts.cooldowns",
            db.ShoutoutCooldowns.Where(x =>
                (
                    x.TargetTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.TargetLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "shoutouts.raid-outcomes",
            db.AutomaticRaidShoutoutOutcomes.Where(x =>
                (
                    x.SourceTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.SourceLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "channel-points.redemptions",
            db.TwitchRewardRedemptions.Where(x =>
                (
                    x.UserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.UserLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "clips.created",
            db.TwitchClips.Where(x =>
                    (
                        x.CreatorTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.CreatorLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
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
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.SubmitterLogin
                    ) && (hostId == null || x.HostId == hostId)
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
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.Submission!.HostId && claim.Login == x.VoterLogin
                    ) && (hostId == null || x.Submission!.HostId == hostId)
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
            "bounties.pledges",
            db.BountyPledges.Where(x =>
                (
                    x.ContributorTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ContributorLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "bounties.rewards",
            db.BountyContributorRewards.Where(x =>
                (
                    x.TwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.Login
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "bounties.moderation-audits",
            db.BountyModerationAudits.Where(x =>
                (
                    x.ActorTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "bingo.participants",
            db.BingoParticipants.Where(x =>
                (
                    x.TwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.Login
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "bingo.unique-cards",
            db.BingoCards.Where(x =>
                    x.Game!.Mode == BingoGameMode.UniquePerViewer
                    && x.Participants.Any(participant =>
                        participant.TwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == participant.Login
                        )
                    )
                    && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.GameId,
                    x.AssignmentName,
                    x.IssuedAtUtc,
                })
        );
        await AddAsync(
            "bingo.evidence",
            db.BingoEvidence.Where(x =>
                (
                    x.ParticipantTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ParticipantLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "bingo.win-recipients",
            db.BingoWinRecipients.Where(x =>
                (
                    x.TwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.Login
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "bingo.moderation-audits",
            db.BingoModerationAudit.Where(x =>
                (
                    x.ActorTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "bingo.template-revisions",
            db.BingoTemplateRevisions.Where(x =>
                (
                    x.CreatedByTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.CreatedByLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "community.progress",
            db.CommunityProgress.Where(x =>
                (
                    x.ViewerTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "community.completions",
            db.CommunityCompletions.Where(x =>
                (
                    x.ViewerTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "community.reward-unlocks",
            db.CommunityRewardUnlocks.Where(x =>
                (
                    x.ViewerTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "community.equipped-rewards",
            db.CommunityEquippedRewards.Where(x =>
                (
                    x.ViewerTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "community.standings",
            db.CommunitySeasonStandings.Where(x =>
                (
                    x.ViewerTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "community.moderation-audits",
            db.CommunityAudits.Where(x =>
                (
                    x.ActorTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "viewer-passports.profiles",
            db.ViewerPassports.Where(x => passportIds.Contains(x.Id))
        );
        await AddAsync(
            "viewer-passports.logins",
            db.ViewerPassportLogins.Where(x =>
                    passportIds.Contains(x.PassportId) && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.HostId,
                    x.Login,
                    x.FirstSeenAtUtc,
                    x.LastSeenAtUtc,
                })
        );
        await AddAsync(
            "viewer-passports.attendance-days",
            db.ViewerPassportAttendanceDays.Where(x =>
                    passportIds.Contains(x.PassportId) && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.HostId,
                    x.DateUtc,
                    x.FirstSeenAtUtc,
                })
        );
        await AddAsync(
            "play-queues.entries",
            db.PlayQueueEntries.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId
                            && (
                                claim.Login == x.NormalizedLogin
                                || x.IdentityKey == "login:" + claim.Login
                            )
                        )
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
                (
                    x.IdentityKey == idKey
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && x.IdentityKey == "login:" + claim.Login
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "play-queues.exclusions",
            db.PlayQueueExclusions.Where(x =>
                (
                    x.IdentityKey == idKey
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && x.IdentityKey == "login:" + claim.Login
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "moments.contributors",
            db.MomentContributors.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && (
                                claim.Login == x.NormalizedLogin
                                || x.IdentityKey == "login:" + claim.Login
                            )
                        )
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
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
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
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
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
                        || x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && (
                                claim.Login == x.NormalizedLogin
                                || x.IdentityKey == "login:" + claim.Login
                            )
                        )
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
                safeLoginClaims.Any(claim =>
                    claim.HostId == x.HostId && claim.Login == x.ActorLogin
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddAsync(
            "moments.merges",
            db.MomentMerges.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    ) && (hostId == null || x.HostId == hostId)
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
                    (
                        x.ActorUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ActorLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
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
        var scope = await ResolveIdentityScopeAsync(db, subject, hostId, ct);
        var userId = scope.UserId;
        var idKey = scope.IdIdentityKey;
        var passportIds = scope.PassportIds;
        var safeLoginClaims = SafeLoginClaims(db, passportIds);
        var safeGlobalLoginClaims = SafeGlobalLoginClaims(db, safeLoginClaims);
        var safeLoginClaimValues = await safeLoginClaims
            .Select(value => new SafeLoginClaim(value.HostId, value.Login))
            .Distinct()
            .ToArrayAsync(ct);
        var quotedIdentityClaims = IdentityTextClaims(
            subject.TwitchUserId,
            hostId,
            safeLoginClaimValues,
            static value => LikeContainsPattern($"\"{value}\"")
        );

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var bountyPledges = await db
            .BountyPledges.Where(x =>
                (
                    x.ContributorTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ContributorLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
            .Select(x => x.Id)
            .ToListAsync(ct);
        var bountyPledgeIds = bountyPledges;
        var bountyRewardIds = await db
            .BountyContributorRewards.Where(x =>
                (
                    x.TwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.Login
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
            .Select(x => x.Id)
            .ToListAsync(ct);
        var communityCompletionIds = await db
            .CommunityCompletions.Where(x =>
                (
                    x.ViewerTwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
            .Select(x => x.Id)
            .ToListAsync(ct);
        var bingoParticipants = await db
            .BingoParticipants.Where(x =>
                (
                    x.TwitchUserId == userId
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.Login
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
            .Where(x => x.CardId != null)
            .Select(x => x.CardId!.Value)
            .ToListAsync(ct);
        var uniqueBingoCards = await db
            .BingoCards.Where(x =>
                bingoParticipants.Contains(x.Id)
                && x.Game!.Mode == BingoGameMode.UniquePerViewer
                && (hostId == null || x.HostId == hostId)
            )
            .Select(x => x.Id)
            .ToListAsync(ct);
        var uniqueBingoCardIdsToErase = uniqueBingoCards.ToArray();
        var identityContentClaims = IdentityTextClaims(
            subject.TwitchUserId,
            hostId,
            safeLoginClaimValues,
            LikeContainsPattern
        );

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
                    (hostId == null || x.GuessRound!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.GuessRound!.HostId && claim.Login == x.Login
                    )
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "points.balances",
            await db
                .PointBalances.Where(x =>
                    (hostId == null || x.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.Login
                    )
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "points.ledger.subject-rows",
            await db
                .PointLedgerEntries.Where(x =>
                    safeLoginClaims.Any(claim => claim.HostId == x.HostId && claim.Login == x.Login)
                    && (hostId == null || x.HostId == hostId)
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
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorLogin, (string?)null)
                            .SetProperty(x => x.Note, string.Empty),
                    ct
                )
        );
        Record(
            "points.ledger.counterparty-references",
            await db
                .PointLedgerEntries.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.CounterpartyLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.CounterpartyLogin, (string?)null)
                            .SetProperty(x => x.Note, string.Empty),
                    ct
                )
        );
        var pointLedgerPrivateNotes = 0;
        foreach (var claim in identityContentClaims)
        {
            pointLedgerPrivateNotes += await db
                .PointLedgerEntries.Where(x =>
                    EF.Functions.Like(x.Note, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Note, string.Empty), ct);
        }
        Record("points.ledger.private-notes", pointLedgerPrivateNotes);
        if (bountyPledgeIds.Count > 0 || bountyRewardIds.Count > 0)
        {
            Record(
                "bounties.ledger",
                await db
                    .PointLedgerEntries.Where(x =>
                        (
                            bountyPledgeIds.Contains(x.BountyPledgeId ?? 0)
                            || bountyRewardIds.Contains(x.BountyRewardId ?? 0)
                        ) && (hostId == null || x.HostId == hostId)
                    )
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(x => x.Login, ErasedToken)
                                .SetProperty(x => x.ActorLogin, (string?)null)
                                .SetProperty(x => x.Note, string.Empty),
                        ct
                    )
            );
        }
        Record(
            "points.giveaway-entries",
            await db
                .PointsGiveawayEntrants.Where(x =>
                    (hostId == null || x.Giveaway!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.Giveaway!.HostId && claim.Login == x.Login
                    )
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "points.giveaway-wins",
            await db
                .PointsGiveawayWinners.Where(x =>
                    (hostId == null || x.Giveaway!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.Giveaway!.HostId && claim.Login == x.Login
                    )
                )
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Login, ErasedToken), ct)
        );
        Record(
            "commands.allowed-users",
            await db
                .CustomCommandAllowedUsers.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.Login
                        )
                    ) && (hostId == null || x.HostId == hostId)
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
                    (
                        x.ActorTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ActorLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
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
                    (
                        x.TargetTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.TargetLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
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
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.AcknowledgedByLogin
                    ) && (hostId == null || x.HostId == hostId)
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
                await db
                    .SiteAccessEntries.Where(x =>
                        safeGlobalLoginClaims.Any(claim => claim.Login == x.Login)
                    )
                    .ExecuteDeleteAsync(ct)
            );
        }

        Record(
            "access.mod-entries",
            await db
                .HostModAccessEntries.Where(x =>
                    safeLoginClaims.Any(claim => claim.HostId == x.HostId && claim.Login == x.Login)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "whispers.recipients",
            await db
                .WhisperQuotaRecipients.Where(x =>
                    (
                        x.RecipientTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.WhisperQuotaBucket.HostId
                            && claim.Login == x.RecipientLogin
                        )
                    ) && (hostId == null || x.WhisperQuotaBucket.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "shoutouts.history",
            await db
                .ShoutoutHistory.Where(x =>
                    (
                        x.SourceTwitchUserId == userId
                        || x.TargetTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId
                            && (claim.Login == x.SourceLogin || claim.Login == x.TargetLogin)
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "shoutouts.cooldowns",
            await db
                .ShoutoutCooldowns.Where(x =>
                    (
                        x.TargetTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.TargetLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "shoutouts.raid-outcomes",
            await db
                .AutomaticRaidShoutoutOutcomes.Where(x =>
                    (
                        x.SourceTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.SourceLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "channel-points.redemptions",
            await db
                .TwitchRewardRedemptions.Where(x =>
                    (
                        x.UserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.UserLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "clips.creator-references",
            await db
                .TwitchClips.Where(x =>
                    (
                        x.CreatorTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.CreatorLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
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
                safeLoginClaims.Any(claim =>
                    claim.HostId == x.Submission!.HostId && claim.Login == x.VoterLogin
                ) && (hostId == null || x.Submission!.HostId == hostId)
            )
            .Select(x => x.SubmissionId)
            .Distinct()
            .ToListAsync(ct);
        Record(
            "request-boards.votes",
            await db
                .RequestSubmissionVotes.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.Submission!.HostId && claim.Login == x.VoterLogin
                    ) && (hostId == null || x.Submission!.HostId == hostId)
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
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.SubmitterLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        var requestBoardEvents = 0;
        foreach (var claim in quotedIdentityClaims)
        {
            requestBoardEvents += await db
                .RequestBoardEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record("request-boards.events", requestBoardEvents);
        Record(
            "bounties.pledges",
            await db
                .BountyPledges.Where(x =>
                    (
                        x.ContributorTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ContributorLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ContributorTwitchUserId, ErasedToken)
                            .SetProperty(x => x.ContributorLogin, ErasedToken)
                            .SetProperty(
                                x => x.State,
                                x =>
                                    x.State == BountyPledgeState.Reserved
                                        ? BountyPledgeState.Consumed
                                        : x.State
                            ),
                    ct
                )
        );
        Record(
            "bounties.rewards",
            await db
                .BountyContributorRewards.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.Login
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.TwitchUserId, ErasedToken)
                            .SetProperty(x => x.Login, ErasedToken),
                    ct
                )
        );
        Record(
            "bounties.moderation-audits",
            await db
                .BountyModerationAudits.Where(x =>
                    (
                        x.ActorTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ActorLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorTwitchUserId, ErasedToken)
                            .SetProperty(x => x.ActorLogin, ErasedToken)
                            .SetProperty(x => x.Reason, string.Empty),
                    ct
                )
        );
        var bountyEvents = 0;
        foreach (var claim in quotedIdentityClaims)
        {
            bountyEvents += await db
                .BountyEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record("bounties.events", bountyEvents);
        Record(
            "bingo.unique-cards",
            await db
                .BingoCards.Where(x => uniqueBingoCardIdsToErase.Contains(x.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.AssignmentName, ErasedToken),
                    ct
                )
        );
        Record(
            "bingo.participants",
            await db
                .BingoParticipants.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.Login
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.TwitchUserId, x => "erased:" + x.Id)
                            .SetProperty(x => x.Login, ErasedToken)
                            .SetProperty(x => x.DisplayName, ErasedToken),
                    ct
                )
        );
        Record(
            "bingo.evidence",
            await db
                .BingoEvidence.Where(x =>
                    (
                        x.ParticipantTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ParticipantLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ParticipantTwitchUserId, (string?)null)
                            .SetProperty(x => x.ParticipantLogin, (string?)null)
                            .SetProperty(x => x.ParticipantDisplayName, (string?)null)
                            .SetProperty(x => x.Summary, "Bingo event recorded"),
                    ct
                )
        );
        Record(
            "bingo.win-recipients",
            await db
                .BingoWinRecipients.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.Login
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.TwitchUserId, x => "erased:" + x.Id)
                            .SetProperty(x => x.Login, ErasedToken)
                            .SetProperty(x => x.DisplayName, ErasedToken),
                    ct
                )
        );
        Record(
            "bingo.moderation-audits",
            await db
                .BingoModerationAudit.Where(x =>
                    (
                        x.ActorTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ActorLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorTwitchUserId, ErasedToken)
                            .SetProperty(x => x.ActorLogin, ErasedToken)
                            .SetProperty(x => x.PrivateNote, string.Empty),
                    ct
                )
        );
        Record(
            "bingo.template-revisions",
            await db
                .BingoTemplateRevisions.Where(x =>
                    (
                        x.CreatedByTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.CreatedByLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.CreatedByTwitchUserId, ErasedToken)
                            .SetProperty(x => x.CreatedByLogin, ErasedToken),
                    ct
                )
        );
        var bingoEvidenceText = 0;
        var bingoAuditText = 0;
        var bingoEvents = 0;
        var bingoOverlayItems = 0;
        foreach (var claim in identityContentClaims)
        {
            bingoEvidenceText += await db
                .BingoEvidence.Where(x =>
                    EF.Functions.Like(x.Summary, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Summary, "Bingo event recorded"),
                    ct
                );
            bingoAuditText += await db
                .BingoModerationAudit.Where(x =>
                    EF.Functions.Like(x.PrivateNote, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.PrivateNote, string.Empty),
                    ct
                );
            bingoEvents += await db
                .BingoEvents.Where(x =>
                    (
                        EF.Functions.Like(x.OperationKey, claim.Pattern, "\\")
                        || EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    ) && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
            bingoOverlayItems += await db
                .OverlayEventFeedItems.Where(x =>
                    (
                        EF.Functions.Like(x.SourceKey, claim.Pattern, "\\")
                        || EF.Functions.Like(x.Title, claim.Pattern, "\\")
                        || EF.Functions.Like(x.Body, claim.Pattern, "\\")
                    ) && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }
        Record("bingo.evidence-text", bingoEvidenceText);
        Record("bingo.moderation-audit-text", bingoAuditText);
        Record("bingo.events", bingoEvents);
        Record("bingo.overlay-items", bingoOverlayItems);
        if (communityCompletionIds.Count > 0)
        {
            Record(
                "community.points-ledger",
                await db
                    .PointLedgerEntries.Where(x =>
                        communityCompletionIds.Contains(x.CommunityCompletionId ?? 0)
                        && (hostId == null || x.HostId == hostId)
                    )
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(x => x.Login, ErasedToken)
                                .SetProperty(x => x.ActorLogin, (string?)null)
                                .SetProperty(x => x.Note, string.Empty),
                        ct
                    )
            );
        }
        Record(
            "community.equipped-rewards",
            await db
                .CommunityEquippedRewards.Where(x =>
                    (
                        x.ViewerTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "community.reward-unlocks",
            await db
                .CommunityRewardUnlocks.Where(x =>
                    (
                        x.ViewerTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "community.progress",
            await db
                .CommunityProgress.Where(x =>
                    (
                        x.ViewerTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "community.completions",
            await db
                .CommunityCompletions.Where(x =>
                    (
                        x.ViewerTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.SubjectKey, x => "erased:" + x.Id)
                            .SetProperty(x => x.ViewerTwitchUserId, ErasedToken)
                            .SetProperty(x => x.ViewerLogin, ErasedToken)
                            .SetProperty(x => x.ViewerDisplayName, ErasedToken),
                    ct
                )
        );
        Record(
            "community.standings",
            await db
                .CommunitySeasonStandings.Where(x =>
                    (
                        x.ViewerTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ViewerTwitchUserId, x => "erased:" + x.Id)
                            .SetProperty(x => x.ViewerLogin, ErasedToken)
                            .SetProperty(x => x.ViewerDisplayName, ErasedToken),
                    ct
                )
        );
        Record(
            "community.moderation-audits",
            await db
                .CommunityAudits.Where(x =>
                    (
                        x.ActorTwitchUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ActorLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorTwitchUserId, ErasedToken)
                            .SetProperty(x => x.ActorLogin, ErasedToken)
                            .SetProperty(x => x.PrivateNote, string.Empty),
                    ct
                )
        );
        var communityEvents = 0;
        foreach (var claim in quotedIdentityClaims)
        {
            communityEvents += await db
                .CommunityEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }
        Record("community.events", communityEvents);
        Record(
            "play-queues.entries",
            await db
                .PlayQueueEntries.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId
                            && (
                                claim.Login == x.NormalizedLogin
                                || x.IdentityKey == "login:" + claim.Login
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "play-queues.participation",
            await db
                .PlayQueueParticipation.Where(x =>
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "play-queues.exclusions",
            await db
                .PlayQueueExclusions.Where(x =>
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        var playQueueEvents = 0;
        foreach (var claim in quotedIdentityClaims)
        {
            playQueueEvents += await db
                .PlayQueueEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
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
                        || x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && (
                                claim.Login == x.NormalizedLogin
                                || x.IdentityKey == "login:" + claim.Login
                            )
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "moments.capture-requests",
            await db
                .MomentCaptureRequests.Where(x =>
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "moments.suggestions",
            await db
                .MomentSuggestions.Where(x =>
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "moments.votes",
            await db
                .MomentVotes.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && (
                                claim.Login == x.NormalizedLogin
                                || x.IdentityKey == "login:" + claim.Login
                            )
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            "moments.moderation-audits",
            await db
                .MomentModerationAudit.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    ) && (hostId == null || x.HostId == hostId)
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
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    ) && (hostId == null || x.HostId == hostId)
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
        foreach (var claim in quotedIdentityClaims)
        {
            momentEvents += await db
                .MomentEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record("moments.events", momentEvents);
        Record(
            "overlays.actor-events",
            await db
                .OverlayInstanceEvents.Where(x =>
                    (
                        x.ActorUserId == userId
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ActorLogin
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorUserId, ErasedToken)
                            .SetProperty(x => x.ActorLogin, ErasedToken),
                    ct
                )
        );
        var overlayEventFeedItems = 0;
        foreach (var claim in identityContentClaims)
        {
            overlayEventFeedItems += await db
                .OverlayEventFeedItems.Where(x =>
                    (
                        EF.Functions.Like(x.Title, claim.Pattern, "\\")
                        || EF.Functions.Like(x.Body, claim.Pattern, "\\")
                    ) && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }
        Record("overlays.event-feed", overlayEventFeedItems);

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
        foreach (var claim in quotedIdentityClaims)
        {
            automationRuns += await db
                .AutomationFlowRuns.Where(x =>
                    EF.Functions.Like(x.ContextJson, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record("automations.runs", automationRuns);

        await ViewerPassportAmbiguityTombstones.PersistForPassportsAsync(db, passportIds, ct);
        Record(
            "viewer-passports.logins",
            await db.ViewerPassportLogins.CountAsync(
                x => passportIds.Contains(x.PassportId) && (hostId == null || x.HostId == hostId),
                ct
            )
        );
        Record(
            "viewer-passports.attendance-days",
            await db.ViewerPassportAttendanceDays.CountAsync(
                x => passportIds.Contains(x.PassportId) && (hostId == null || x.HostId == hostId),
                ct
            )
        );
        Record(
            "viewer-passports.profiles",
            await db.ViewerPassports.Where(x => passportIds.Contains(x.Id)).ExecuteDeleteAsync(ct)
        );

        await transaction.CommitAsync(ct);
        return new ViewerErasureReport(changed);
    }

    private static Task<bool> IsAmbiguousLoginAsync(
        BlokeBotDbContext db,
        string? login,
        int? hostId,
        CancellationToken cancellationToken
    ) =>
        login is null
            ? Task.FromResult(false)
            : db
                .ViewerPassportAmbiguousLogins.AsNoTracking()
                .AnyAsync(
                    value => value.Login == login && (hostId == null || value.HostId == hostId),
                    cancellationToken
                );

    private static async Task<PrivacyIdentityScope> ResolveIdentityScopeAsync(
        BlokeBotDbContext db,
        PrivacySubject subject,
        int? hostId,
        CancellationToken cancellationToken
    )
    {
        var userId = subject.TwitchUserId ?? UnmatchableValue;
        var passports = db.ViewerPassports.Where(passport =>
            hostId == null || passport.HostId == hostId
        );
        var passportIds =
            subject.TwitchUserId is not null
                ? await passports
                    .Where(passport => passport.TwitchUserId == userId)
                    .Select(passport => passport.Id)
                    .ToArrayAsync(cancellationToken)
            : subject.Login is null
            || await IsAmbiguousLoginAsync(db, subject.Login, hostId, cancellationToken)
                ? []
            : await passports
                .Where(passport =>
                    passport.Login == subject.Login
                    || db.ViewerPassportLogins.Any(alias =>
                        alias.PassportId == passport.Id && alias.Login == subject.Login
                    )
                )
                .Select(passport => passport.Id)
                .ToArrayAsync(cancellationToken);
        return new(
            userId,
            subject.TwitchUserId is null ? UnmatchableValue : subject.IdIdentityKey,
            passportIds
        );
    }

    private static IQueryable<ViewerPassportLogin> SafeLoginClaims(
        BlokeBotDbContext db,
        IReadOnlyCollection<long> passportIds
    ) =>
        db.ViewerPassportLogins.Where(alias =>
            passportIds.Contains(alias.PassportId)
            && !db.ViewerPassportAmbiguousLogins.Any(ambiguous =>
                ambiguous.HostId == alias.HostId && ambiguous.Login == alias.Login
            )
        );

    private static IQueryable<ViewerPassportLogin> SafeGlobalLoginClaims(
        BlokeBotDbContext db,
        IQueryable<ViewerPassportLogin> safeLoginClaims
    ) =>
        safeLoginClaims.Where(alias =>
            !db.ViewerPassportAmbiguousLogins.Any(ambiguous => ambiguous.Login == alias.Login)
        );

    private static PrivacyTextClaim[] IdentityTextClaims(
        string? twitchUserId,
        int? hostId,
        IReadOnlyCollection<SafeLoginClaim> safeLoginClaims,
        Func<string, string> pattern
    ) =>
        safeLoginClaims
            .Select(value => new PrivacyTextClaim(value.HostId, pattern(value.Login)))
            .Concat(
                twitchUserId is null ? [] : [new PrivacyTextClaim(hostId, pattern(twitchUserId))]
            )
            .Distinct()
            .ToArray();

    private sealed record PrivacyIdentityScope(
        string UserId,
        string IdIdentityKey,
        long[] PassportIds
    );

    private sealed record SafeLoginClaim(int HostId, string Login);

    private sealed record PrivacyTextClaim(int? HostId, string Pattern);

    private static string LikeContainsPattern(string value) =>
        $"%{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
}
