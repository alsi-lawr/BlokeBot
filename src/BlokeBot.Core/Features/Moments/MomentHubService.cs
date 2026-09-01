using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Moments;

public sealed class MomentHubService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IMomentProviderOperations provider,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider
)
{
    private const int _eventSchemaVersion = 1;
    private const int _maximumEventPayloadLength = 1024;
    private const int _persistenceRetryCount = 20;

    public async Task<MomentResult<MomentHubSettingsView>> ConfigureAsync(
        int hostId,
        ConfigureMomentHubCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<MomentHubSettingsView>(new MomentRejection.FeatureDisabled());
        }

        if (
            command.MergeWindowSeconds
            is < MomentLimits.MinimumMergeWindowSeconds
                or > MomentLimits.MaximumMergeWindowSeconds
        )
        {
            return Rejected<MomentHubSettingsView>(
                new MomentRejection.Invalid("The merge window must be from 15 to 300 seconds.")
            );
        }

        PointAmount reward;
        try
        {
            reward = PointAmount.ParseAbsolute(command.RewardAmount);
        }
        catch (Exception error) when (error is FormatException or ArgumentOutOfRangeException)
        {
            return Rejected<MomentHubSettingsView>(
                new MomentRejection.Invalid("The point reward must be a non-negative whole number.")
            );
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Hosts.AnyAsync(host => host.Id == hostId, ct))
        {
            return Rejected<MomentHubSettingsView>(new MomentRejection.NotFound());
        }
        var settings = await db.MomentHubSettings.SingleOrDefaultAsync(
            value => value.HostId == hostId,
            ct
        );
        if (settings is null)
        {
            settings = new MomentHubSettings { HostId = hostId };
            _ = db.MomentHubSettings.Add(settings);
        }
        settings.MergeWindowSeconds = command.MergeWindowSeconds;
        settings.MarkerFallbackEnabled = command.MarkerFallbackEnabled;
        settings.RewardPolicy = command.RewardPolicy;
        settings.RewardAmount = reward.ToString();
        settings.UpdatedAtUtc = Now();
        _ = await db.SaveChangesAsync(ct);
        await NotifyAsync(ct);
        return Succeeded(SettingsView(settings));
    }

    public async Task<MomentResult<MomentView>> CaptureAsync(
        int hostId,
        CaptureMomentCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<MomentView>(new MomentRejection.FeatureDisabled());
        }

        var stream = command.StreamIdentity.Trim();
        var login = MomentInput.NormalizeLogin(command.Requester.Login);
        if (stream.Length is < 1 or > 128)
        {
            return Rejected<MomentView>(
                new MomentRejection.Invalid("A live Twitch stream identity is required.")
            );
        }
        if (!MomentInput.IsValidLogin(login))
        {
            return Rejected<MomentView>(
                new MomentRejection.Invalid("A valid Twitch login is required.")
            );
        }
        if (
            command.SuggestedTitle.Trim().Length > MomentLimits.MaximumTitleLength
            || command.SuggestedCategory.Trim().Length > MomentLimits.MaximumCategoryLength
        )
        {
            return Rejected<MomentView>(
                new MomentRejection.Invalid("Moment title or category is too long.")
            );
        }

        var decisionResult = await RetryPersistenceAsync(
            () => DecideCaptureAsync(hostId, stream, login, command, ct),
            ct
        );
        if (decisionResult is MomentResult<CaptureDecision>.Rejected rejected)
        {
            return Rejected<MomentView>(rejected.Reason);
        }
        var decision = ((MomentResult<CaptureDecision>.Succeeded)decisionResult).Value;
        if (decision.State == MomentCandidateState.ProviderPending)
        {
            var outcome = await provider.CaptureAsync(
                hostId,
                decision.PublicId,
                decision.MarkerFallbackEnabled,
                Description(decision.PublicId),
                ct
            );
            await RetryPersistenceAsync(
                () => ApplyProviderOutcomeAsync(hostId, decision.PublicId, outcome, ct),
                ct
            );
        }

        await using var read = await dbFactory.CreateDbContextAsync(ct);
        var candidate =
            await LoadPublicCandidateAsync(read, hostId, decision.PublicId, ct)
            ?? throw new InvalidOperationException("The committed moment candidate was not found.");
        await NotifyAsync(ct);
        return new MomentResult<MomentView>.Succeeded(
            ToPublic(candidate, decision.HostLogin),
            !decision.Created
        );
    }

    private async Task<MomentResult<CaptureDecision>> DecideCaptureAsync(
        int hostId,
        string stream,
        string login,
        CaptureMomentCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await MainDatabaseWriteTransaction.StartImmediateAsync(
            db,
            ct
        );
        var host = await db.Hosts.SingleOrDefaultAsync(value => value.Id == hostId, ct);
        if (host is null)
        {
            return Rejected<CaptureDecision>(new MomentRejection.NotFound());
        }
        var settings = await LoadSettingsAsync(db, hostId, ct);
        var now = Now();
        var mergeBoundary = now.AddSeconds(-settings.MergeWindowSeconds);
        var candidate = await db
            .MomentCandidates.Include(value => value.Contributors)
            .Include(value => value.Suggestions)
            .Where(value =>
                value.HostId == hostId
                && value.StreamIdentity == stream
                && value.LastCapturedAtUtc >= mergeBoundary
                && value.State != MomentCandidateState.Approved
                && value.State != MomentCandidateState.Rejected
                && value.State != MomentCandidateState.Merged
            )
            .OrderByDescending(value => value.LastCapturedAtUtc)
            .ThenByDescending(value => value.Id)
            .FirstOrDefaultAsync(ct);
        var created = candidate is null;
        if (candidate is null)
        {
            candidate = new MomentCandidate
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                StreamIdentity = stream,
                State = MomentCandidateState.ProviderPending,
                CapturedAtUtc = now,
                LastCapturedAtUtc = now,
            };
            _ = db.MomentCandidates.Add(candidate);
        }
        else
        {
            candidate.LastCapturedAtUtc = now;
        }

        var existingContributor = candidate.Contributors.SingleOrDefault(value =>
            value.IdentityKey == MomentInput.IdentityKey(command.Requester)
            || value.NormalizedLogin == login
        );
        if (
            existingContributor is null
            && candidate.Contributors.Count >= MomentLimits.MaximumContributorCount
        )
        {
            return Rejected<CaptureDecision>(
                new MomentRejection.Conflict("This moment has reached its contributor limit.")
            );
        }
        candidate.CaptureRequests.Add(
            new MomentCaptureRequest
            {
                IdentityKey = MomentInput.IdentityKey(command.Requester),
                CapturedAtUtc = now,
            }
        );
        AddOrUpdateContributor(candidate, command.Requester, login, now);
        AddSuggestion(candidate, command, now);
        _ = await db.SaveChangesAsync(ct);
        AddEvent(
            db,
            candidate,
            MomentEventKind.Captured,
            new { candidate.PublicId, candidate.StreamIdentity },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Succeeded(
            new CaptureDecision(
                candidate.PublicId,
                host.Login,
                candidate.State,
                settings.MarkerFallbackEnabled,
                created
            )
        );
    }

    public async Task<MomentResult<ModeratorMomentView>> ApproveAsync(
        int hostId,
        ModerateMomentCommand command,
        CancellationToken ct
    ) =>
        await ModerateAsync(
            hostId,
            command,
            async (db, candidate, now) =>
            {
                if (
                    candidate.State
                    is not MomentCandidateState.ClipReady
                        and not MomentCandidateState.MarkerReady
                        and not MomentCandidateState.Approved
                )
                {
                    return new ModerationDecision(
                        new MomentRejection.Conflict(
                            "Only a resolved clip or marker can be approved."
                        )
                    );
                }
                var wasApproved = candidate.State == MomentCandidateState.Approved;
                if (wasApproved)
                {
                    return new ModerationDecision(null, true);
                }
                candidate.PublicTitle = CleanTitle(command.PublicTitle, candidate);
                candidate.PublicCategory = command.PublicCategory.Trim();
                candidate.State = MomentCandidateState.Approved;
                candidate.ApprovedAtUtc ??= now;
                AddAudit(db, candidate, "Approved", command.ActorLogin, command.PrivateText, now);
                AddEvent(
                    db,
                    candidate,
                    MomentEventKind.Approved,
                    new
                    {
                        candidate.PublicId,
                        candidate.StreamIdentity,
                        candidate.PublicTitle,
                        candidate.PublicCategory,
                    },
                    now,
                    ApprovalEventKey(candidate.PublicId)
                );
                await ApplyRewardsAsync(db, candidate, command.ActorLogin, now, ct);
                return new ModerationDecision();
            },
            ct
        );

    public Task<MomentResult<ModeratorMomentView>> EditAsync(
        int hostId,
        ModerateMomentCommand command,
        CancellationToken ct
    ) =>
        ModerateAsync(
            hostId,
            command,
            (db, candidate, now) =>
            {
                if (candidate.State is MomentCandidateState.Rejected or MomentCandidateState.Merged)
                {
                    return Task.FromResult(
                        new ModerationDecision(
                            new MomentRejection.Conflict(
                                "Rejected or merged moments cannot be edited."
                            )
                        )
                    );
                }
                candidate.PublicTitle = CleanTitle(command.PublicTitle, candidate);
                candidate.PublicCategory = command.PublicCategory.Trim();
                AddAudit(db, candidate, "Edited", command.ActorLogin, command.PrivateText, now);
                return Task.FromResult(new ModerationDecision());
            },
            ct
        );

    public Task<MomentResult<ModeratorMomentView>> RejectAsync(
        int hostId,
        ModerateMomentCommand command,
        CancellationToken ct
    ) =>
        ModerateAsync(
            hostId,
            command,
            (db, candidate, now) =>
            {
                if (candidate.State == MomentCandidateState.Approved)
                {
                    return Task.FromResult(
                        new ModerationDecision(
                            new MomentRejection.Conflict("Approved moments cannot be rejected.")
                        )
                    );
                }
                candidate.State = MomentCandidateState.Rejected;
                candidate.RejectedAtUtc = now;
                candidate.PrivateRejectionReason = command.PrivateText.Trim();
                AddAudit(db, candidate, "Rejected", command.ActorLogin, command.PrivateText, now);
                return Task.FromResult(new ModerationDecision());
            },
            ct
        );

    public async Task<MomentResult<ModeratorMomentView>> MergeAsync(
        int hostId,
        Guid sourcePublicId,
        Guid targetPublicId,
        string actorLogin,
        string privateText,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<ModeratorMomentView>(new MomentRejection.FeatureDisabled());
        }

        if (sourcePublicId == targetPublicId)
        {
            return Rejected<ModeratorMomentView>(
                new MomentRejection.Invalid("Choose two different moments.")
            );
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await MainDatabaseWriteTransaction.StartImmediateAsync(
            db,
            ct
        );
        var candidates = await db
            .MomentCandidates.Include(value => value.Contributors)
            .Include(value => value.Suggestions)
            .Include(value => value.Votes)
            .Where(value =>
                value.HostId == hostId
                && (value.PublicId == sourcePublicId || value.PublicId == targetPublicId)
            )
            .ToArrayAsync(ct);
        var source = candidates.SingleOrDefault(value => value.PublicId == sourcePublicId);
        var target = candidates.SingleOrDefault(value => value.PublicId == targetPublicId);
        if (source is null || target is null)
        {
            return Rejected<ModeratorMomentView>(new MomentRejection.NotFound());
        }
        if (source.StreamIdentity != target.StreamIdentity)
        {
            return Rejected<ModeratorMomentView>(
                new MomentRejection.Conflict("Moments from different Twitch streams cannot merge.")
            );
        }
        if (
            source.State is MomentCandidateState.Approved or MomentCandidateState.Merged
            || target.State is MomentCandidateState.Rejected or MomentCandidateState.Merged
        )
        {
            return Rejected<ModeratorMomentView>(
                new MomentRejection.Conflict(
                    "The selected moments cannot be merged in their current states."
                )
            );
        }
        var mergedContributorCount = source
            .Contributors.Select(value => value.NormalizedLogin)
            .Concat(target.Contributors.Select(value => value.NormalizedLogin))
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (mergedContributorCount > MomentLimits.MaximumContributorCount)
        {
            return Rejected<ModeratorMomentView>(
                new MomentRejection.Conflict(
                    "Merging these moments would exceed the contributor limit."
                )
            );
        }
        if (
            source.Suggestions.Count + target.Suggestions.Count
            > MomentLimits.MaximumSuggestionCount
        )
        {
            return Rejected<ModeratorMomentView>(
                new MomentRejection.Conflict(
                    "Merging these moments would exceed the suggestion limit."
                )
            );
        }

        foreach (var contributor in source.Contributors)
        {
            var existing = target.Contributors.SingleOrDefault(value =>
                value.IdentityKey == contributor.IdentityKey
                || value.NormalizedLogin == contributor.NormalizedLogin
            );
            if (existing is null)
            {
                target.Contributors.Add(
                    new MomentContributor
                    {
                        IdentityKey = contributor.IdentityKey,
                        TwitchUserId = contributor.TwitchUserId,
                        NormalizedLogin = contributor.NormalizedLogin,
                        DisplayName = contributor.DisplayName,
                        CaptureCount = contributor.CaptureCount,
                        FirstCapturedAtUtc = contributor.FirstCapturedAtUtc,
                        LastCapturedAtUtc = contributor.LastCapturedAtUtc,
                    }
                );
            }
            else
            {
                _ = ReconcileIdentity(existing, contributor.TwitchUserId, contributor.IdentityKey);
                existing.CaptureCount += contributor.CaptureCount;
                existing.FirstCapturedAtUtc = Earlier(
                    existing.FirstCapturedAtUtc,
                    contributor.FirstCapturedAtUtc
                );
                existing.LastCapturedAtUtc = Later(
                    existing.LastCapturedAtUtc,
                    contributor.LastCapturedAtUtc
                );
            }
        }
        foreach (var suggestion in source.Suggestions)
        {
            target.Suggestions.Add(
                new MomentSuggestion
                {
                    IdentityKey = suggestion.IdentityKey,
                    SuggestedTitle = suggestion.SuggestedTitle,
                    SuggestedCategory = suggestion.SuggestedCategory,
                    CreatedAtUtc = suggestion.CreatedAtUtc,
                }
            );
        }
        foreach (var vote in source.Votes)
        {
            var existing = target.Votes.SingleOrDefault(value =>
                value.IdentityKey == vote.IdentityKey
                || value.NormalizedLogin == vote.NormalizedLogin
            );
            if (existing is null)
            {
                target.Votes.Add(
                    new MomentVote
                    {
                        IdentityKey = vote.IdentityKey,
                        TwitchUserId = vote.TwitchUserId,
                        NormalizedLogin = vote.NormalizedLogin,
                        CreatedAtUtc = vote.CreatedAtUtc,
                    }
                );
            }
            else
            {
                _ = ReconcileIdentity(existing, vote.TwitchUserId, vote.IdentityKey);
            }
        }
        var mergedAt = Now();
        source.State = MomentCandidateState.Merged;
        source.MergedIntoCandidateId = target.Id;
        _ = db.MomentMerges.Add(
            new MomentMerge
            {
                HostId = hostId,
                SourceCandidateId = source.Id,
                TargetCandidateId = target.Id,
                ActorLogin = MomentInput.NormalizeLogin(actorLogin),
                PrivateText = privateText.Trim(),
                MergedAtUtc = mergedAt,
            }
        );
        AddAudit(db, source, "Merged", actorLogin, privateText, mergedAt);
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await NotifyAsync(ct);
        return Succeeded(await ToModeratorAsync(db, target, ct));
    }

    public async Task<MomentResult<MomentView>> VoteAsync(
        int hostId,
        Guid publicId,
        MomentViewerIdentity viewer,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<MomentView>(new MomentRejection.FeatureDisabled());
        }

        var login = MomentInput.NormalizeLogin(viewer.Login);
        if (!MomentInput.IsValidLogin(login))
        {
            return Rejected<MomentView>(
                new MomentRejection.Invalid("A valid Twitch login is required.")
            );
        }
        var vote = await RetryPersistenceAsync(
            () => VoteCoreAsync(hostId, publicId, viewer, login, ct),
            ct
        );
        if (vote.Changed)
        {
            await NotifyAsync(ct);
        }
        return vote.Result;
    }

    private async Task<VoteDecision> VoteCoreAsync(
        int hostId,
        Guid publicId,
        MomentViewerIdentity viewer,
        string login,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await MainDatabaseWriteTransaction.StartImmediateAsync(
            db,
            ct
        );
        var candidate = await LoadPublicCandidateAsync(db, hostId, publicId, ct);
        if (candidate is null)
        {
            return new VoteDecision(Rejected<MomentView>(new MomentRejection.NotFound()));
        }
        if (candidate.State != MomentCandidateState.Approved)
        {
            return new VoteDecision(
                Rejected<MomentView>(
                    new MomentRejection.Conflict("Only approved moments can receive votes.")
                )
            );
        }
        var identity = MomentInput.IdentityKey(viewer);
        var existing = candidate.Votes.SingleOrDefault(value =>
            value.IdentityKey == identity || value.NormalizedLogin == login
        );
        var changed = false;
        if (existing is not null && !string.IsNullOrWhiteSpace(viewer.TwitchUserId))
        {
            changed = ReconcileIdentity(existing, viewer.TwitchUserId, identity);
        }
        if (existing is not null)
        {
            if (changed)
            {
                _ = await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            var hostLogin = await HostLoginAsync(db, hostId, ct);
            return new VoteDecision(
                new MomentResult<MomentView>.Succeeded(ToPublic(candidate, hostLogin), true),
                changed
            );
        }
        candidate.Votes.Add(
            new MomentVote
            {
                IdentityKey = identity,
                TwitchUserId = CleanOptional(viewer.TwitchUserId),
                NormalizedLogin = login,
                CreatedAtUtc = Now(),
            }
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new VoteDecision(
            Succeeded(ToPublic(candidate, await HostLoginAsync(db, hostId, ct))),
            true
        );
    }

    public async Task<MomentModeratorPage?> GetModeratorPageAsync(int hostId, CancellationToken ct)
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await LoadSettingsAsync(db, hostId, ct);
        var candidates = await db
            .MomentCandidates.Include(value => value.Contributors)
            .Include(value => value.Suggestions)
            .Include(value => value.Votes)
            .Include(value => value.TwitchClip)
            .Include(value => value.TwitchStreamMarker)
            .Where(value => value.HostId == hostId)
            .OrderByDescending(value => value.CapturedAtUtc)
            .ThenByDescending(value => value.Id)
            .Take(100)
            .ToArrayAsync(ct);
        var views = new List<ModeratorMomentView>(candidates.Length);
        foreach (var candidate in candidates)
        {
            views.Add(await ToModeratorAsync(db, candidate, ct));
        }
        return new MomentModeratorPage(SettingsView(settings), views);
    }

    public async Task<MomentRecapPage?> GetWeeklyRecapAsync(
        string channel,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(channel, ct))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await ResolveHostAsync(db, channel, ct);
        if (host is null)
        {
            return null;
        }
        var weekStart = MomentInput.WeekStart(nowUtc);
        var weekEnd = weekStart.AddDays(7);
        var moments = await LoadApprovedAsync(
            db,
            host.Id,
            value => value.ApprovedAtUtc >= weekStart && value.ApprovedAtUtc < weekEnd,
            ct
        );
        var finalization = await db
            .MomentWeeklyFinalizations.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.HostId == host.Id && value.WeekStartsAtUtc == weekStart,
                ct
            );
        return new MomentRecapPage(
            host.Login,
            weekStart,
            null,
            moments.Select(value => ToPublic(value, host.Login)).ToArray(),
            finalization is null
                ? null
                : moments.Single(value => value.Id == finalization.WinningCandidateId).PublicId
        );
    }

    public async Task<MomentRecapPage?> GetStreamRecapAsync(
        string channel,
        string streamIdentity,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(channel, ct))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await ResolveHostAsync(db, channel, ct);
        if (host is null)
        {
            return null;
        }
        var moments = await LoadApprovedAsync(
            db,
            host.Id,
            value => value.StreamIdentity == streamIdentity,
            ct
        );
        return new MomentRecapPage(
            host.Login,
            null,
            streamIdentity,
            moments.Select(value => ToPublic(value, host.Login)).ToArray(),
            null
        );
    }

    public async Task<MomentResult<MomentView>> FinalizeWeekAsync(
        int hostId,
        DateTime weekStartsAtUtc,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<MomentView>(new MomentRejection.FeatureDisabled());
        }

        var weekStart = MomentInput.WeekStart(weekStartsAtUtc);
        if (weekStart.AddDays(7) > Now())
        {
            return Rejected<MomentView>(
                new MomentRejection.Conflict("Only a completed ISO-UTC week can be finalized.")
            );
        }
        var finalization = await RetryPersistenceAsync(
            () => FinalizeWeekCoreAsync(hostId, weekStart, ct),
            ct
        );
        if (finalization.Changed)
        {
            await NotifyAsync(ct);
        }
        return finalization.Result;
    }

    private async Task<FinalizationDecision> FinalizeWeekCoreAsync(
        int hostId,
        DateTime weekStart,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await MainDatabaseWriteTransaction.StartImmediateAsync(
            db,
            ct
        );
        var existing = await db.MomentWeeklyFinalizations.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.WeekStartsAtUtc == weekStart,
            ct
        );
        if (existing is not null)
        {
            var existingWinner = await LoadPublicCandidateAsync(
                db,
                hostId,
                (
                    await db.MomentCandidates.SingleAsync(
                        value => value.Id == existing.WinningCandidateId,
                        ct
                    )
                ).PublicId,
                ct
            );
            return new FinalizationDecision(
                new MomentResult<MomentView>.Succeeded(
                    ToPublic(existingWinner!, await HostLoginAsync(db, hostId, ct)),
                    true
                )
            );
        }
        var weekEnd = weekStart.AddDays(7);
        var candidates = await LoadApprovedAsync(
            db,
            hostId,
            value => value.ApprovedAtUtc >= weekStart && value.ApprovedAtUtc < weekEnd,
            ct
        );
        var winner = candidates.FirstOrDefault();
        if (winner is null)
        {
            return new FinalizationDecision(
                Rejected<MomentView>(
                    new MomentRejection.Conflict("This week has no approved moments.")
                )
            );
        }
        var now = Now();
        _ = db.MomentWeeklyFinalizations.Add(
            new MomentWeeklyFinalization
            {
                HostId = hostId,
                WeekStartsAtUtc = weekStart,
                WinningCandidateId = winner.Id,
                FinalizedAtUtc = now,
            }
        );
        AddEvent(
            db,
            winner,
            MomentEventKind.Winner,
            new
            {
                winner.PublicId,
                winner.StreamIdentity,
                WeekStartsAtUtc = weekStart,
            },
            now,
            WinnerEventKey(weekStart)
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new FinalizationDecision(
            Succeeded(ToPublic(winner, await HostLoginAsync(db, hostId, ct))),
            true
        );
    }

    public async Task<IReadOnlyList<MomentEventView>> GetEventsAsync(
        int hostId,
        long afterId,
        int count,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return [];
        }

        var take = Math.Clamp(count, 1, MomentLimits.MaximumEventReadCount);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .MomentEvents.AsNoTracking()
            .Where(value => value.HostId == hostId && value.Id > afterId)
            .OrderBy(value => value.Id)
            .Take(take)
            .Join(
                db.MomentCandidates,
                domainEvent => domainEvent.CandidateId,
                candidate => candidate.Id,
                (domainEvent, candidate) =>
                    new MomentEventView(
                        domainEvent.Id,
                        domainEvent.HostId,
                        candidate.PublicId,
                        domainEvent.SchemaVersion,
                        domainEvent.Kind,
                        domainEvent.StreamIdentity,
                        domainEvent.PublicPayload,
                        domainEvent.OccurredAtUtc
                    )
            )
            .ToArrayAsync(ct);
    }

    private Task<bool> FeatureIsEnabledAsync(int hostId, CancellationToken ct) =>
        HostFeatureAvailability.IsEnabledAsync(dbFactory, hostId, HostFeatureFlags.Moments, ct);

    private Task<bool> FeatureIsEnabledAsync(string channel, CancellationToken ct) =>
        HostFeatureAvailability.IsEnabledAsync(dbFactory, channel, HostFeatureFlags.Moments, ct);

    private async Task<MomentResult<ModeratorMomentView>> ModerateAsync(
        int hostId,
        ModerateMomentCommand command,
        Func<BlokeBotDbContext, MomentCandidate, DateTime, Task<ModerationDecision>> mutation,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<ModeratorMomentView>(new MomentRejection.FeatureDisabled());
        }

        if (
            command.PublicTitle.Trim().Length > MomentLimits.MaximumTitleLength
            || command.PublicCategory.Trim().Length > MomentLimits.MaximumCategoryLength
            || command.PrivateText.Trim().Length > 1000
        )
        {
            return Rejected<ModeratorMomentView>(
                new MomentRejection.Invalid("Moment metadata is too long.")
            );
        }
        var result = await RetryPersistenceAsync(
            () => ModerateCoreAsync(hostId, command, mutation, ct),
            ct
        );
        if (result.Changed)
        {
            await NotifyAsync(ct);
        }
        return result.Result;
    }

    private async Task<ModerationResult> ModerateCoreAsync(
        int hostId,
        ModerateMomentCommand command,
        Func<BlokeBotDbContext, MomentCandidate, DateTime, Task<ModerationDecision>> mutation,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await MainDatabaseWriteTransaction.StartImmediateAsync(
            db,
            ct
        );
        var candidate = await db
            .MomentCandidates.Include(value => value.Contributors)
            .Include(value => value.Suggestions)
            .Include(value => value.Votes)
            .Include(value => value.TwitchClip)
            .Include(value => value.TwitchStreamMarker)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.PublicId == command.PublicId,
                ct
            );
        if (candidate is null)
        {
            return new ModerationResult(
                Rejected<ModeratorMomentView>(new MomentRejection.NotFound())
            );
        }
        var decision = await mutation(db, candidate, Now());
        if (decision.Rejection is not null)
        {
            return new ModerationResult(Rejected<ModeratorMomentView>(decision.Rejection));
        }
        if (decision.WasIdempotent)
        {
            return new ModerationResult(
                new MomentResult<ModeratorMomentView>.Succeeded(
                    await ToModeratorAsync(db, candidate, ct),
                    true
                )
            );
        }
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ModerationResult(Succeeded(await ToModeratorAsync(db, candidate, ct)), true);
    }

    private static void AddOrUpdateContributor(
        MomentCandidate candidate,
        MomentViewerIdentity requester,
        string login,
        DateTime now
    )
    {
        var identity = MomentInput.IdentityKey(requester);
        var contributor = candidate.Contributors.SingleOrDefault(value =>
            value.IdentityKey == identity || value.NormalizedLogin == login
        );
        if (contributor is null)
        {
            candidate.Contributors.Add(
                new MomentContributor
                {
                    IdentityKey = identity,
                    TwitchUserId = CleanOptional(requester.TwitchUserId),
                    NormalizedLogin = login,
                    DisplayName = string.IsNullOrWhiteSpace(requester.DisplayName)
                        ? requester.Login.Trim()
                        : requester.DisplayName.Trim(),
                    CaptureCount = 1,
                    FirstCapturedAtUtc = now,
                    LastCapturedAtUtc = now,
                }
            );
            return;
        }
        if (!string.IsNullOrWhiteSpace(requester.TwitchUserId))
        {
            _ = ReconcileIdentity(contributor, requester.TwitchUserId, identity);
        }
        contributor.CaptureCount++;
        contributor.LastCapturedAtUtc = now;
        contributor.DisplayName = string.IsNullOrWhiteSpace(requester.DisplayName)
            ? requester.Login.Trim()
            : requester.DisplayName.Trim();
    }

    private static void AddSuggestion(
        MomentCandidate candidate,
        CaptureMomentCommand command,
        DateTime now
    )
    {
        var title = command.SuggestedTitle.Trim();
        var category = command.SuggestedCategory.Trim();
        if (
            (title.Length == 0 && category.Length == 0)
            || candidate.Suggestions.Count >= MomentLimits.MaximumSuggestionCount
        )
        {
            return;
        }
        candidate.Suggestions.Add(
            new MomentSuggestion
            {
                IdentityKey = MomentInput.IdentityKey(command.Requester),
                SuggestedTitle = title,
                SuggestedCategory = category,
                CreatedAtUtc = now,
            }
        );
    }

    private async Task ApplyProviderOutcomeAsync(
        int hostId,
        Guid publicId,
        MomentProviderOutcome outcome,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await MainDatabaseWriteTransaction.StartImmediateAsync(
            db,
            ct
        );
        var candidate = await db.MomentCandidates.SingleAsync(
            value => value.HostId == hostId && value.PublicId == publicId,
            ct
        );
        if (
            candidate.State
            is MomentCandidateState.Approved
                or MomentCandidateState.Rejected
                or MomentCandidateState.Merged
                or MomentCandidateState.ClipReady
                or MomentCandidateState.MarkerReady
        )
        {
            return;
        }
        switch (outcome)
        {
            case MomentProviderOutcome.Pending pending:
                candidate.State = MomentCandidateState.ProviderPending;
                candidate.TwitchClipId = pending.ClipId;
                break;
            case MomentProviderOutcome.ClipReady clip:
                candidate.State = MomentCandidateState.ClipReady;
                candidate.TwitchClipId = clip.ClipId;
                candidate.ProviderFailureReason = string.Empty;
                break;
            case MomentProviderOutcome.MarkerReady marker:
                candidate.State = MomentCandidateState.MarkerReady;
                candidate.TwitchStreamMarkerId = marker.MarkerId;
                candidate.ProviderFailureReason = string.Empty;
                break;
            case MomentProviderOutcome.Ambiguous ambiguous:
                candidate.State = MomentCandidateState.ProviderPending;
                candidate.TwitchClipId = ambiguous.ClipId;
                candidate.TwitchStreamMarkerId = ambiguous.MarkerId;
                candidate.ProviderFailureReason = ambiguous.Reason;
                break;
            case MomentProviderOutcome.Failed failed:
                candidate.State = MomentCandidateState.Failed;
                candidate.TwitchClipId = failed.ClipId;
                candidate.TwitchStreamMarkerId = failed.MarkerId;
                candidate.ProviderFailureReason = failed.Reason;
                break;
        }
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task ApplyRewardsAsync(
        BlokeBotDbContext db,
        MomentCandidate candidate,
        string actorLogin,
        DateTime now,
        CancellationToken ct
    )
    {
        var settings = await LoadSettingsAsync(db, candidate.HostId, ct);
        var amount = PointAmount.ParseAbsolute(settings.RewardAmount);
        if (settings.RewardPolicy == MomentRewardPolicy.None || amount.IsZero)
        {
            return;
        }
        var contributors = candidate
            .Contributors.OrderBy(value => value.FirstCapturedAtUtc)
            .ThenBy(value => value.Id)
            .Take(
                settings.RewardPolicy == MomentRewardPolicy.FirstRequester
                    ? 1
                    : MomentLimits.MaximumContributorCount
            )
            .ToArray();
        foreach (var contributor in contributors)
        {
            var operationKey = $"moment:{candidate.PublicId:N}:approval:{contributor.IdentityKey}";
            if (
                await db.PointLedgerEntries.AnyAsync(
                    value => value.HostId == candidate.HostId && value.OperationKey == operationKey,
                    ct
                )
            )
            {
                continue;
            }
            var balance = await db.PointBalances.SingleOrDefaultAsync(
                value =>
                    value.HostId == candidate.HostId && value.Login == contributor.NormalizedLogin,
                ct
            );
            if (balance is null)
            {
                balance = new PointBalance
                {
                    HostId = candidate.HostId,
                    Login = contributor.NormalizedLogin,
                    Amount = "0",
                };
                _ = db.PointBalances.Add(balance);
            }
            var current = PointAmount.ParseAbsolute(balance.Amount);
            if (
                !await PointCreditCapacity.CanCreditAsync(
                    db,
                    candidate.HostId,
                    contributor.NormalizedLogin,
                    current,
                    amount.Value,
                    ct
                )
            )
            {
                throw new InvalidOperationException("Moment reward would exceed the point limit.");
            }
            var next = current.Add(amount);
            balance.Amount = next.ToString();
            balance.UpdatedAtUtc = now;
            _ = db.PointLedgerEntries.Add(
                new PointLedgerEntry
                {
                    HostId = candidate.HostId,
                    CreatedAtUtc = now,
                    Kind = PointLedgerKind.MomentReward,
                    Login = contributor.NormalizedLogin,
                    Delta = amount.ToString(),
                    BalanceAfter = next.ToString(),
                    ActorLogin = MomentInput.NormalizeLogin(actorLogin),
                    Note = $"Approved moment {candidate.PublicId:N}",
                    OperationKey = operationKey,
                }
            );
        }
    }

    private static void AddEvent(
        BlokeBotDbContext db,
        MomentCandidate candidate,
        MomentEventKind kind,
        object payload,
        DateTime now,
        string? operationKey = null
    )
    {
        var serialized = JsonSerializer.Serialize(payload);
        if (serialized.Length > _maximumEventPayloadLength)
        {
            throw new InvalidOperationException("Moment event payload exceeds its durable bound.");
        }
        _ = db.MomentEvents.Add(
            new MomentDomainEvent
            {
                HostId = candidate.HostId,
                CandidateId = candidate.Id,
                OperationKey = operationKey,
                SchemaVersion = _eventSchemaVersion,
                Kind = kind,
                StreamIdentity = candidate.StreamIdentity,
                PublicPayload = serialized,
                OccurredAtUtc = now,
            }
        );
    }

    private static void AddAudit(
        BlokeBotDbContext db,
        MomentCandidate candidate,
        string action,
        string actorLogin,
        string privateText,
        DateTime now
    ) =>
        db.MomentModerationAudit.Add(
            new MomentModerationAudit
            {
                HostId = candidate.HostId,
                CandidateId = candidate.Id,
                Action = action,
                ActorLogin = MomentInput.NormalizeLogin(actorLogin),
                PrivateText = privateText.Trim(),
                OccurredAtUtc = now,
            }
        );

    private static async Task<MomentHubSettings> LoadSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db
            .MomentHubSettings.AsNoTracking()
            .SingleOrDefaultAsync(value => value.HostId == hostId, ct)
        ?? new MomentHubSettings
        {
            HostId = hostId,
            MergeWindowSeconds = MomentLimits.DefaultMergeWindowSeconds,
            MarkerFallbackEnabled = true,
            RewardPolicy = MomentRewardPolicy.None,
            RewardAmount = "0",
        };

    private static MomentHubSettingsView SettingsView(MomentHubSettings value) =>
        new(
            value.HostId,
            value.MergeWindowSeconds,
            value.MarkerFallbackEnabled,
            value.RewardPolicy,
            value.RewardAmount
        );

    private static async Task<MomentCandidate?> LoadPublicCandidateAsync(
        BlokeBotDbContext db,
        int hostId,
        Guid publicId,
        CancellationToken ct
    ) =>
        await db
            .MomentCandidates.Include(value => value.Contributors)
            .Include(value => value.Votes)
            .Include(value => value.TwitchClip)
            .Include(value => value.TwitchStreamMarker)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.PublicId == publicId,
                ct
            );

    private static async Task<IReadOnlyList<MomentCandidate>> LoadApprovedAsync(
        BlokeBotDbContext db,
        int hostId,
        System.Linq.Expressions.Expression<Func<MomentCandidate, bool>> predicate,
        CancellationToken ct
    ) =>
        await db
            .MomentCandidates.AsNoTracking()
            .Include(value => value.Contributors)
            .Include(value => value.Votes)
            .Include(value => value.TwitchClip)
            .Include(value => value.TwitchStreamMarker)
            .Where(value => value.HostId == hostId && value.State == MomentCandidateState.Approved)
            .Where(predicate)
            .OrderByDescending(value => value.Votes.Count)
            .ThenBy(value => value.ApprovedAtUtc)
            .ThenBy(value => value.PublicId)
            .ToArrayAsync(ct);

    private static MomentView ToPublic(MomentCandidate candidate, string hostLogin) =>
        new(
            candidate.PublicId,
            candidate.HostId,
            hostLogin,
            candidate.StreamIdentity,
            candidate.State,
            candidate.PublicTitle,
            candidate.PublicCategory,
            candidate.TwitchClip?.FinalUrl ?? candidate.TwitchStreamMarker?.MarkerUrl,
            candidate.Votes.Count,
            candidate.CapturedAtUtc,
            candidate.ApprovedAtUtc,
            candidate
                .Contributors.OrderBy(static value => value.FirstCapturedAtUtc)
                .ThenBy(static value => value.Id)
                .Take(MomentLimits.MaximumContributorCount)
                .Select(static value => new MomentContributorView(
                    value.DisplayName,
                    value.NormalizedLogin,
                    value.CaptureCount,
                    value.FirstCapturedAtUtc
                ))
                .ToArray()
        );

    private static async Task<ModeratorMomentView> ToModeratorAsync(
        BlokeBotDbContext db,
        MomentCandidate candidate,
        CancellationToken ct
    )
    {
        var audits = await db
            .MomentModerationAudit.AsNoTracking()
            .Where(value => value.HostId == candidate.HostId && value.CandidateId == candidate.Id)
            .OrderByDescending(value => value.Id)
            .Take(100)
            .Select(value => $"{value.Action} by @{value.ActorLogin}: {value.PrivateText}")
            .ToArrayAsync(ct);
        return new ModeratorMomentView(
            ToPublic(candidate, await HostLoginAsync(db, candidate.HostId, ct)),
            candidate.ProviderFailureReason,
            candidate.PrivateRejectionReason,
            candidate
                .Suggestions.OrderBy(value => value.CreatedAtUtc)
                .ThenBy(value => value.Id)
                .Take(MomentLimits.MaximumSuggestionCount)
                .Select(value => new MomentSuggestionView(
                    value.SuggestedTitle,
                    value.SuggestedCategory
                ))
                .ToArray(),
            audits
        );
    }

    private static async Task<BotHost?> ResolveHostAsync(
        BlokeBotDbContext db,
        string channel,
        CancellationToken ct
    )
    {
        var login = MomentInput.NormalizeLogin(channel);
        return await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Login == login, ct);
    }

    private static Task<string> HostLoginAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) => db.Hosts.Where(value => value.Id == hostId).Select(value => value.Login).SingleAsync(ct);

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private Task NotifyAsync(CancellationToken ct) =>
        events.PublishAsync(AppEventKind.MomentsChanged, ct).AsTask();

    private static string Description(Guid publicId) =>
        $"Community moment {publicId:N}"[..Math.Min(49, 17 + 32)];

    private static string ApprovalEventKey(Guid publicId) => $"moment:{publicId:N}:approval";

    private static string WinnerEventKey(DateTime weekStart) =>
        $"moment:week:{weekStart:yyyyMMdd}:winner";

    private static string CleanTitle(string requested, MomentCandidate candidate)
    {
        var title = requested.Trim();
        return (
            title,
            candidate
                .Suggestions.OrderBy(value => value.CreatedAtUtc)
                .FirstOrDefault()
                ?.SuggestedTitle
        ) switch
        {
            ({ Length: > 0 } requestedTitle, _) => requestedTitle,
            (_, { Length: > 0 } suggestion) => suggestion,
            _ => $"Moment {candidate.CapturedAtUtc:yyyy-MM-dd HH:mm:ss} UTC",
        };
    }

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime Earlier(DateTime first, DateTime second) =>
        first <= second ? first : second;

    private static DateTime Later(DateTime first, DateTime second) =>
        first >= second ? first : second;

    private static bool ReconcileIdentity(
        MomentContributor contributor,
        string? twitchUserId,
        string identityKey
    )
    {
        if (
            string.IsNullOrWhiteSpace(twitchUserId)
            || (
                !string.IsNullOrWhiteSpace(contributor.TwitchUserId)
                && !string.Equals(
                    contributor.TwitchUserId,
                    twitchUserId.Trim(),
                    StringComparison.Ordinal
                )
            )
        )
        {
            return false;
        }
        var changed =
            !string.Equals(contributor.TwitchUserId, twitchUserId.Trim(), StringComparison.Ordinal)
            || contributor.IdentityKey != identityKey;
        contributor.TwitchUserId = twitchUserId.Trim();
        contributor.IdentityKey = identityKey;
        return changed;
    }

    private static bool ReconcileIdentity(MomentVote vote, string? twitchUserId, string identityKey)
    {
        if (
            string.IsNullOrWhiteSpace(twitchUserId)
            || (
                !string.IsNullOrWhiteSpace(vote.TwitchUserId)
                && !string.Equals(vote.TwitchUserId, twitchUserId.Trim(), StringComparison.Ordinal)
            )
        )
        {
            return false;
        }
        var changed =
            !string.Equals(vote.TwitchUserId, twitchUserId.Trim(), StringComparison.Ordinal)
            || vote.IdentityKey != identityKey;
        vote.TwitchUserId = twitchUserId.Trim();
        vote.IdentityKey = identityKey;
        return changed;
    }

    private static async Task<T> RetryPersistenceAsync<T>(
        Func<Task<T>> action,
        CancellationToken ct
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception exception)
                when (attempt < _persistenceRetryCount && IsPersistenceCollision(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 5), ct);
            }
        }
    }

    private static async Task RetryPersistenceAsync(Func<Task> action, CancellationToken ct) =>
        await RetryPersistenceAsync(
            async () =>
            {
                await action();
                return true;
            },
            ct
        );

    private static bool IsPersistenceCollision(Exception exception) =>
        MainDatabaseFailureClassifier.IsRetryableTransactionContention(exception);

    private static MomentResult<T> Succeeded<T>(T value) => new MomentResult<T>.Succeeded(value);

    private static MomentResult<T> Rejected<T>(MomentRejection rejection) =>
        new MomentResult<T>.Rejected(rejection);

    private sealed record CaptureDecision(
        Guid PublicId,
        string HostLogin,
        MomentCandidateState State,
        bool MarkerFallbackEnabled,
        bool Created
    );

    private sealed record VoteDecision(MomentResult<MomentView> Result, bool Changed = false);

    private sealed record FinalizationDecision(
        MomentResult<MomentView> Result,
        bool Changed = false
    );

    private sealed record ModerationDecision(
        MomentRejection? Rejection = null,
        bool WasIdempotent = false
    );

    private sealed record ModerationResult(
        MomentResult<ModeratorMomentView> Result,
        bool Changed = false
    );
}
