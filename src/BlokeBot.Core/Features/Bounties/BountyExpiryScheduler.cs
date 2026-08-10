using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bounties;

internal sealed class BountyExpiryScheduler(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BountyService bountyService,
    BountyPauseObserver pauseObserver,
    BountyExpirySchedulerPolicy policy,
    TimeProvider timeProvider,
    ILogger<BountyExpiryScheduler> log
) : BackgroundService
{
    private static readonly BountyActor _actor = new("BlokeBot.BountyExpiryScheduler", "blokebot");
    private const string _reason = "Automatically expired after reaching its expiry time.";

    private readonly BountyExpirySchedulerPolicy _policy = ValidPolicy(policy);
    private long _lastBountyId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await PollOnceAsync(stoppingToken);
            using var timer = new PeriodicTimer(_policy.PollInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PollOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async Task PollOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<BountyExpiryCandidate> candidates;
        try
        {
            candidates = await LoadOverdueAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogError(
                "Bounty expiry polling failed with {FailureType}; a later poll will retry.",
                exception.GetType().FullName
            );
            return;
        }

        foreach (var candidate in candidates)
        {
            await ExpireSafelyAsync(candidate, ct);
        }
    }

    internal Task<BountyResult<BountyView>> ExpireAsync(
        BountyExpiryCandidate candidate,
        CancellationToken ct
    ) =>
        bountyService.TransitionAsync(
            candidate.HostId,
            new TransitionBountyCommand(
                BountyExpiryOperationId.Create(candidate.BountyPublicId, candidate.ExpiresAtUtc),
                candidate.BountyPublicId,
                candidate.Revision,
                BountyTransitionAction.Expire,
                _actor,
                _reason
            ),
            ct
        );

    private async Task ExpireSafelyAsync(BountyExpiryCandidate candidate, CancellationToken ct)
    {
        try
        {
            var result = await ExpireAsync(candidate, ct);
            switch (result)
            {
                case BountyResult<BountyView>.Succeeded { WasIdempotent: false }:
                    log.LogInformation(
                        "Expired overdue bounty {BountyPublicId} for host {HostId}.",
                        candidate.BountyPublicId,
                        candidate.HostId
                    );
                    return;
                case BountyResult<BountyView>.Succeeded:
                    log.LogDebug(
                        "Overdue bounty {BountyPublicId} for host {HostId} was already expired by this operation.",
                        candidate.BountyPublicId,
                        candidate.HostId
                    );
                    return;
                case BountyResult<BountyView>.Rejected rejected:
                    LogRejection(candidate, rejected.Reason);
                    return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogError(
                "Automatic expiry failed for bounty {BountyPublicId} on host {HostId} with {FailureType}; a later poll will retry.",
                candidate.BountyPublicId,
                candidate.HostId,
                exception.GetType().FullName
            );
        }
    }

    private async Task<IReadOnlyList<BountyExpiryCandidate>> LoadOverdueAsync(CancellationToken ct)
    {
        await pauseObserver.RecoverAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var candidates = await Query(db, now, _lastBountyId).ToListAsync(ct);
        if (candidates.Count == 0 && _lastBountyId != 0)
        {
            _lastBountyId = 0;
            candidates = await Query(db, now, _lastBountyId).ToListAsync(ct);
        }

        if (candidates.Count > 0)
        {
            _lastBountyId = candidates[^1].DatabaseId;
        }

        return candidates;
    }

    private IQueryable<BountyExpiryCandidate> Query(
        BlokeBotDbContext db,
        DateTime now,
        long afterBountyId
    ) =>
        (
            from bounty in db.Bounties.AsNoTracking()
            where
                bounty.Id > afterBountyId
                && (bounty.Status == BountyStatus.Funding || bounty.Status == BountyStatus.Accepted)
                && bounty.ExpiresAtUtc <= now
            orderby bounty.Id
            select new BountyExpiryCandidate(
                bounty.Id,
                bounty.HostId,
                bounty.PublicId,
                bounty.ExpiresAtUtc,
                bounty.Revision
            )
        ).Take(_policy.BatchSize);

    private void LogRejection(BountyExpiryCandidate candidate, BountyRejection rejection)
    {
        if (
            rejection
            is BountyRejection.FeatureDisabled
                or BountyRejection.NotFound
                or BountyRejection.StaleRevision
                or BountyRejection.InvalidTransition
                or BountyRejection.Invalid
        )
        {
            log.LogDebug(
                "Automatic expiry was not applied to bounty {BountyPublicId} on host {HostId} because of {RejectionType}.",
                candidate.BountyPublicId,
                candidate.HostId,
                rejection.GetType().Name
            );
            return;
        }

        log.LogWarning(
            "Automatic expiry was rejected for bounty {BountyPublicId} on host {HostId} because of {RejectionType}; a later poll will retry.",
            candidate.BountyPublicId,
            candidate.HostId,
            rejection.GetType().Name
        );
    }

    private static BountyExpirySchedulerPolicy ValidPolicy(BountyExpirySchedulerPolicy value)
    {
        value.EnsureValid();
        return value;
    }
}

internal sealed record BountyExpiryCandidate(
    long DatabaseId,
    int HostId,
    Guid BountyPublicId,
    DateTime ExpiresAtUtc,
    long Revision
);
