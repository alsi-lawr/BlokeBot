using System.Globalization;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.MomentAttachments;

internal sealed class MomentAttachmentService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IModeratorAuthorityService moderatorAuthority,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider
)
{
    private const int _persistenceRetryCount = 20;

    public async Task<MomentAttachmentSectionView> GetManagementAsync(
        int hostId,
        MomentAttachmentDestination destination,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken);
        if (host is null)
        {
            return Unavailable(MomentAttachmentSectionAvailability.DestinationUnavailable);
        }

        var disabledParents = DisabledParents(host.EnabledFeatures, destination);
        if (disabledParents.Length > 0)
        {
            return new(
                MomentAttachmentSectionAvailability.ParentDisabled,
                disabledParents,
                null,
                [],
                []
            );
        }

        var resolved = await ResolveDestinationAsync(
            db,
            hostId,
            destination,
            publicOnly: false,
            cancellationToken
        );
        if (resolved is null)
        {
            return Unavailable(MomentAttachmentSectionAvailability.DestinationUnavailable);
        }

        var attached = await LoadAttachedAsync(db, host, resolved, cancellationToken);
        var attachedIds = await DestinationAttachments(db, hostId, resolved)
            .Select(x => x.MomentCandidateId)
            .ToHashSetAsync(cancellationToken);
        var candidates = await db
            .MomentCandidates.AsNoTracking()
            .Include(x => x.TwitchClip)
            .Include(x => x.TwitchStreamMarker)
            .Where(x => x.HostId == hostId && x.State == MomentCandidateState.Approved)
            .OrderByDescending(x => x.ApprovedAtUtc)
            .ThenByDescending(x => x.CapturedAtUtc)
            .ThenBy(x => x.PublicId)
            .ToArrayAsync(cancellationToken);

        return new(
            MomentAttachmentSectionAvailability.Available,
            string.Empty,
            resolved.View,
            attached,
            candidates
                .Select(candidate =>
                    Project(host.Login, candidate, attachedIds.Contains(candidate.Id))
                )
                .ToArray()
        );
    }

    public async Task<MomentAttachmentPublicProjection?> GetPublicAsync(
        string hostLogin,
        MomentAttachmentDestination destination,
        CancellationToken cancellationToken
    )
    {
        var normalizedLogin = CommunityInput.NormalizeLogin(hostLogin);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Login == normalizedLogin, cancellationToken);
        if (host is null || DisabledParents(host.EnabledFeatures, destination).Length > 0)
        {
            return null;
        }

        var resolved = await ResolveDestinationAsync(
            db,
            host.Id,
            destination,
            publicOnly: true,
            cancellationToken
        );
        return resolved is null
            ? null
            : new(resolved.View, await LoadAttachedAsync(db, host, resolved, cancellationToken));
    }

    public Task<MomentAttachmentMutationOutcome> AttachAsync(
        AuthenticatedSession session,
        int hostId,
        MomentAttachmentDestination destination,
        Guid momentId,
        CancellationToken cancellationToken
    ) =>
        MutateAsync(
            session,
            hostId,
            destination,
            async (db, resolved, ct) =>
            {
                var moment = await db.MomentCandidates.SingleOrDefaultAsync(
                    x =>
                        x.HostId == hostId
                        && x.PublicId == momentId
                        && x.State == MomentCandidateState.Approved,
                    ct
                );
                if (moment is null)
                {
                    return new MutationDecision(
                        new MomentAttachmentMutationOutcome.Rejected(
                            new MomentAttachmentRejection.MomentUnavailable()
                        )
                    );
                }

                if (
                    await DestinationAttachments(db, hostId, resolved)
                        .AnyAsync(x => x.MomentCandidateId == moment.Id, ct)
                )
                {
                    return new MutationDecision(
                        new MomentAttachmentMutationOutcome.Succeeded(WasIdempotent: true)
                    );
                }

                _ = db.MomentAttachments.Add(
                    resolved.CreateAttachment(
                        hostId,
                        moment.Id,
                        timeProvider.GetUtcNow().UtcDateTime
                    )
                );
                _ = await db.SaveChangesAsync(ct);
                return new MutationDecision(
                    new MomentAttachmentMutationOutcome.Succeeded(WasIdempotent: false),
                    Changed: true
                );
            },
            cancellationToken
        );

    public Task<MomentAttachmentMutationOutcome> DetachAsync(
        AuthenticatedSession session,
        int hostId,
        MomentAttachmentDestination destination,
        Guid momentId,
        CancellationToken cancellationToken
    ) =>
        MutateAsync(
            session,
            hostId,
            destination,
            async (db, resolved, ct) =>
            {
                var momentCandidateId = await db
                    .MomentCandidates.Where(x => x.HostId == hostId && x.PublicId == momentId)
                    .Select(x => (long?)x.Id)
                    .SingleOrDefaultAsync(ct);
                if (momentCandidateId is null)
                {
                    return new MutationDecision(
                        new MomentAttachmentMutationOutcome.Succeeded(WasIdempotent: true)
                    );
                }

                var deleted = await DestinationAttachments(db, hostId, resolved)
                    .Where(x => x.MomentCandidateId == momentCandidateId.Value)
                    .ExecuteDeleteAsync(ct);
                return new MutationDecision(
                    new MomentAttachmentMutationOutcome.Succeeded(WasIdempotent: deleted == 0),
                    Changed: deleted > 0
                );
            },
            cancellationToken
        );

    private async Task<MomentAttachmentMutationOutcome> MutateAsync(
        AuthenticatedSession session,
        int hostId,
        MomentAttachmentDestination destination,
        Func<
            BlokeBotDbContext,
            ResolvedDestination,
            CancellationToken,
            Task<MutationDecision>
        > mutate,
        CancellationToken cancellationToken
    )
    {
        await using (var gateDb = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var features = await gateDb
                .Hosts.AsNoTracking()
                .Where(x => x.Id == hostId)
                .Select(x => (HostFeatureFlags?)x.EnabledFeatures)
                .SingleOrDefaultAsync(cancellationToken);
            if (features is null)
            {
                return new MomentAttachmentMutationOutcome.Rejected(
                    new MomentAttachmentRejection.DestinationUnavailable()
                );
            }

            var disabledParents = DisabledParents(features.Value, destination);
            if (disabledParents.Length > 0)
            {
                return new MomentAttachmentMutationOutcome.Rejected(
                    new MomentAttachmentRejection.ParentDisabled(disabledParents)
                );
            }
        }

        if (!await IsAuthorizedAsync(session, hostId, cancellationToken))
        {
            return new MomentAttachmentMutationOutcome.Rejected(
                new MomentAttachmentRejection.Unauthorized()
            );
        }

        var decision = await RetryPersistenceAsync(
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                await using var transaction = await db.Database.BeginTransactionAsync(
                    cancellationToken
                );
                var features = await db
                    .Hosts.Where(x => x.Id == hostId)
                    .Select(x => (HostFeatureFlags?)x.EnabledFeatures)
                    .SingleOrDefaultAsync(cancellationToken);
                if (features is null)
                {
                    return new MutationDecision(
                        new MomentAttachmentMutationOutcome.Rejected(
                            new MomentAttachmentRejection.DestinationUnavailable()
                        )
                    );
                }

                var disabledParents = DisabledParents(features.Value, destination);
                if (disabledParents.Length > 0)
                {
                    return new MutationDecision(
                        new MomentAttachmentMutationOutcome.Rejected(
                            new MomentAttachmentRejection.ParentDisabled(disabledParents)
                        )
                    );
                }

                var resolved = await ResolveDestinationAsync(
                    db,
                    hostId,
                    destination,
                    publicOnly: false,
                    cancellationToken
                );
                if (resolved is null)
                {
                    return new MutationDecision(
                        new MomentAttachmentMutationOutcome.Rejected(
                            new MomentAttachmentRejection.DestinationUnavailable()
                        )
                    );
                }

                var result = await mutate(db, resolved, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            },
            cancellationToken
        );
        if (decision.Changed)
        {
            _ = await events.PublishAsync(AppEventKind.MomentAttachmentsChanged, cancellationToken);
        }
        return decision.Outcome;
    }

    private async Task<bool> IsAuthorizedAsync(
        AuthenticatedSession session,
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var selectedHost = session.State.Match<BotHostChoice?>(
            static _ => null,
            static selected => selected.Selection.Current,
            static _ => null
        );
        if (
            !session.IsAuthenticated
            || session.IsBotAccount
            || string.IsNullOrWhiteSpace(session.UserId)
            || selectedHost?.Id != hostId
            || selectedHost.Role is AuthRole.Bot
        )
        {
            return false;
        }

        var authority = await moderatorAuthority.AuthorizeAsync(session, hostId, cancellationToken);
        return authority.Match(_ => true, _ => false, _ => false, _ => false);
    }

    private static async Task<ResolvedDestination?> ResolveDestinationAsync(
        BlokeBotDbContext db,
        int hostId,
        MomentAttachmentDestination destination,
        bool publicOnly,
        CancellationToken cancellationToken
    ) =>
        destination switch
        {
            MomentAttachmentDestination.Bounty value => await ResolveBountyAsync(
                db,
                hostId,
                value.Id,
                publicOnly,
                cancellationToken
            ),
            MomentAttachmentDestination.Achievement value => await ResolveAchievementAsync(
                db,
                hostId,
                value.Id,
                publicOnly,
                cancellationToken
            ),
            MomentAttachmentDestination.TournamentResult value => await ResolveResultAsync(
                db,
                hostId,
                value.Id,
                publicOnly,
                cancellationToken
            ),
            _ => null,
        };

    private static async Task<ResolvedDestination?> ResolveBountyAsync(
        BlokeBotDbContext db,
        int hostId,
        Guid publicId,
        bool publicOnly,
        CancellationToken cancellationToken
    )
    {
        var bounty = await db
            .Bounties.AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.HostId == hostId
                    && x.PublicId == publicId
                    && (!publicOnly || x.Visibility == BountyVisibility.Public),
                cancellationToken
            );
        return bounty is null
            ? null
            : ResolvedDestination.ForBounty(
                bounty.Id,
                new(
                    "Bounty",
                    bounty.Title,
                    "Viewer-funded challenge",
                    bounty.Status.ToString(),
                    bounty.Visibility.ToString()
                )
            );
    }

    private static async Task<ResolvedDestination?> ResolveAchievementAsync(
        BlokeBotDbContext db,
        int hostId,
        CommunityDefinitionId definitionId,
        bool publicOnly,
        CancellationToken cancellationToken
    )
    {
        var row = await (
            from definition in db.CommunityDefinitions.AsNoTracking()
            join season in db.CommunitySeasons.AsNoTracking()
                on new { definition.HostId, Id = definition.SeasonId } equals new
                {
                    season.HostId,
                    season.Id,
                }
            where
                definition.HostId == hostId
                && definition.PublicId == definitionId.Value
                && definition.Kind == CommunityDefinitionKind.Achievement
                && (
                    !publicOnly
                    || (
                        season.Visibility == CommunityVisibility.Public
                        && season.Status != CommunitySeasonStatus.Draft
                    )
                )
            select new { Definition = definition, Season = season }
        ).SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : ResolvedDestination.ForAchievement(
                row.Definition.Id,
                new(
                    "Achievement",
                    row.Definition.Name,
                    row.Season.Name,
                    row.Season.Status.ToString(),
                    row.Season.Visibility.ToString()
                )
            );
    }

    private static async Task<ResolvedDestination?> ResolveResultAsync(
        BlokeBotDbContext db,
        int hostId,
        CompetitionMatchId matchId,
        bool publicOnly,
        CancellationToken cancellationToken
    )
    {
        var match = await db
            .CompetitionMatches.AsNoTracking()
            .Include(x => x.Competition)
            .Include(x => x.EntrantA)
            .Include(x => x.EntrantB)
            .SingleOrDefaultAsync(
                x =>
                    x.HostId == hostId
                    && x.PublicId == matchId.Value
                    && x.Status == CompetitionMatchStatus.Confirmed
                    && (!publicOnly || x.Competition.Status != CompetitionStatus.Draft),
                cancellationToken
            );
        if (match?.Competition is null || match.EntrantA is null || match.EntrantB is null)
        {
            return null;
        }

        var score =
            $"{match.ScoreA?.ToString(CultureInfo.InvariantCulture) ?? "–"}–{match.ScoreB?.ToString(CultureInfo.InvariantCulture) ?? "–"}";
        return ResolvedDestination.ForResult(
            match.Id,
            new(
                "Confirmed result",
                $"Round {match.Round} · {match.EntrantA.Name} {score} {match.EntrantB.Name}",
                match.Competition.Name,
                "Confirmed",
                "Public"
            )
        );
    }

    private static IQueryable<MomentAttachment> DestinationAttachments(
        BlokeBotDbContext db,
        int hostId,
        ResolvedDestination destination
    ) =>
        db.MomentAttachments.Where(x =>
            x.HostId == hostId
            && x.BountyId == destination.BountyId
            && x.CommunityDefinitionId == destination.CommunityDefinitionId
            && x.CompetitionMatchId == destination.CompetitionMatchId
        );

    private static async Task<IReadOnlyList<MomentAttachmentMomentView>> LoadAttachedAsync(
        BlokeBotDbContext db,
        BotHost host,
        ResolvedDestination destination,
        CancellationToken cancellationToken
    )
    {
        var attachments = await DestinationAttachments(db, host.Id, destination)
            .AsNoTracking()
            .Include(x => x.MomentCandidate)
                .ThenInclude(x => x.TwitchClip)
            .Include(x => x.MomentCandidate)
                .ThenInclude(x => x.TwitchStreamMarker)
            .Where(x => x.MomentCandidate.State == MomentCandidateState.Approved)
            .OrderByDescending(x => x.AttachedAtUtc)
            .ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);
        return attachments
            .Select(x => Project(host.Login, x.MomentCandidate, isAttached: true))
            .ToArray();
    }

    private static MomentAttachmentMomentView Project(
        string hostLogin,
        MomentCandidate candidate,
        bool isAttached
    ) =>
        new(
            candidate.PublicId,
            candidate.PublicTitle,
            candidate.PublicCategory,
            candidate.StreamIdentity,
            candidate.TwitchClip?.FinalUrl ?? candidate.TwitchStreamMarker?.MarkerUrl,
            $"/moments/{Uri.EscapeDataString(hostLogin)}/streams/{Uri.EscapeDataString(candidate.StreamIdentity)}",
            candidate.CapturedAtUtc,
            candidate.ApprovedAtUtc ?? candidate.CapturedAtUtc,
            isAttached
        );

    private static string DisabledParents(
        HostFeatureFlags enabled,
        MomentAttachmentDestination destination
    )
    {
        var disabled = new List<string>(2);
        if (!enabled.Contains(HostFeatureFlags.Moments))
        {
            disabled.Add("Moments");
        }

        var (feature, label) = destination switch
        {
            MomentAttachmentDestination.Bounty => (HostFeatureFlags.Bounties, "Bounties"),
            MomentAttachmentDestination.Achievement => (
                HostFeatureFlags.CommunityProgression,
                "Community progression"
            ),
            MomentAttachmentDestination.TournamentResult => (
                HostFeatureFlags.Competitions,
                "Tournaments & leagues"
            ),
            _ => (HostFeatureFlags.None, "destination feature"),
        };
        if (feature == HostFeatureFlags.None || !enabled.Contains(feature))
        {
            disabled.Add(label);
        }

        return disabled.Count switch
        {
            0 => string.Empty,
            1 => disabled[0],
            _ => $"{disabled[0]} and {disabled[1]}",
        };
    }

    private static MomentAttachmentSectionView Unavailable(
        MomentAttachmentSectionAvailability availability
    ) => new(availability, string.Empty, null, [], []);

    private static async Task<T> RetryPersistenceAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken
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
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 5), cancellationToken);
            }
        }
    }

    private static bool IsPersistenceCollision(Exception exception) =>
        exception switch
        {
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED,
            } => true,
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT,
                SqliteExtendedErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE,
            } => true,
            DbUpdateException { InnerException: { } inner } => IsPersistenceCollision(inner),
            _ => false,
        };

    private sealed record MutationDecision(
        MomentAttachmentMutationOutcome Outcome,
        bool Changed = false
    );

    private sealed record ResolvedDestination(
        long? BountyId,
        long? CommunityDefinitionId,
        long? CompetitionMatchId,
        MomentAttachmentDestinationView View
    )
    {
        internal static ResolvedDestination ForBounty(
            long id,
            MomentAttachmentDestinationView view
        ) => new(id, null, null, view);

        internal static ResolvedDestination ForAchievement(
            long id,
            MomentAttachmentDestinationView view
        ) => new(null, id, null, view);

        internal static ResolvedDestination ForResult(
            long id,
            MomentAttachmentDestinationView view
        ) => new(null, null, id, view);

        internal MomentAttachment CreateAttachment(
            int hostId,
            long candidateId,
            DateTime attachedAtUtc
        ) =>
            new()
            {
                HostId = hostId,
                MomentCandidateId = candidateId,
                BountyId = BountyId,
                CommunityDefinitionId = CommunityDefinitionId,
                CompetitionMatchId = CompetitionMatchId,
                AttachedAtUtc = attachedAtUtc,
            };
    }
}
