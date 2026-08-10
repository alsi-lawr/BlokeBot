using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Core.Features.Bounties;

internal sealed class BountyService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider,
    IEnumerable<IBountyCompletionObserver>? completionObservers = null
)
{
    private const int _eventSchemaVersion = 1;
    private const int _maximumEventPayloadLength = 1024;
    private const int _persistenceRetryCount = 20;
    private readonly IBountyCompletionObserver[] _completionObservers =
    [
        .. completionObservers ?? [],
    ];

    public async Task<BountyResult<BountyView>> CreateAsync(
        int hostId,
        CreateBountyCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<BountyView>(new BountyRejection.FeatureDisabled());
        }

        var rejection = Validate(command);
        if (rejection is not null)
        {
            return Rejected<BountyView>(rejection);
        }

        var normalized = command with { Actor = Normalize(command.Actor) };
        var result = await RetryPersistenceAsync(
            () => CreateAttemptAsync(hostId, normalized, ct),
            ct
        );
        if (result is BountyResult<BountyView>.Succeeded { WasIdempotent: false })
        {
            await PublishChangesAsync(pointsChanged: false, ct);
        }

        return result;
    }

    public async Task<BountyResult<BountyPledgeView>> PledgeAsync(
        int hostId,
        PledgeBountyCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<BountyPledgeView>(new BountyRejection.FeatureDisabled());
        }

        var rejection = Validate(command);
        if (rejection is not null)
        {
            return Rejected<BountyPledgeView>(rejection);
        }

        var normalized = command with { Contributor = Normalize(command.Contributor) };
        var result = await RetryPersistenceAsync(
            () => PledgeAttemptAsync(hostId, normalized, ct),
            ct
        );
        if (result is BountyResult<BountyPledgeView>.Succeeded { WasIdempotent: false })
        {
            await PublishChangesAsync(pointsChanged: true, ct);
        }

        return result;
    }

    public async Task<BountyResult<BountyView>> TransitionAsync(
        int hostId,
        TransitionBountyCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<BountyView>(new BountyRejection.FeatureDisabled());
        }

        var rejection = Validate(command);
        if (rejection is not null)
        {
            return Rejected<BountyView>(rejection);
        }

        var normalized = command with { Actor = Normalize(command.Actor) };
        var result = await RetryPersistenceAsync(
            () => TransitionAttemptAsync(hostId, normalized, ct),
            ct
        );
        if (result is BountyResult<BountyView>.Succeeded { WasIdempotent: false })
        {
            var pointsChanged =
                command.Action
                is BountyTransitionAction.Complete
                    or BountyTransitionAction.Fail
                    or BountyTransitionAction.Cancel
                    or BountyTransitionAction.Reject
                    or BountyTransitionAction.Expire;
            await PublishChangesAsync(pointsChanged, ct);
        }

        if (
            command.Action == BountyTransitionAction.Complete
            && result is BountyResult<BountyView>.Succeeded completed
            && completed.Value.ResolvedAtUtc is { } completedAtUtc
        )
        {
            foreach (var observer in _completionObservers)
            {
                await observer.BountyCompletedAsync(
                    hostId,
                    completed.Value.PublicId,
                    new DateTimeOffset(DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc)),
                    ct
                );
            }
        }

        return result;
    }

    public async Task<BountyResult<BountyView>> ExtendAsync(
        int hostId,
        ExtendBountyCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<BountyView>(new BountyRejection.FeatureDisabled());
        }

        var rejection = Validate(command);
        if (rejection is not null)
        {
            return Rejected<BountyView>(rejection);
        }

        var normalized = command with { Actor = Normalize(command.Actor) };
        var result = await RetryPersistenceAsync(
            () => ExtendAttemptAsync(hostId, normalized, ct),
            ct
        );
        if (result is BountyResult<BountyView>.Succeeded { WasIdempotent: false })
        {
            await PublishChangesAsync(pointsChanged: false, ct);
        }

        return result;
    }

    public async Task<BountyView?> GetAsync(int hostId, Guid publicId, CancellationToken ct)
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var bounty = await db
            .Bounties.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.PublicId == publicId,
                ct
            );
        return bounty is null ? null : await ToViewAsync(db, bounty, ct);
    }

    public async Task<IReadOnlyList<BountyModeratorView>> GetModeratorBoardAsync(
        int hostId,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostLogin = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId)
            .Select(value => value.Login)
            .SingleOrDefaultAsync(ct);
        if (hostLogin is null)
        {
            return [];
        }

        var bounties = await db
            .Bounties.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value =>
                value.Status == BountyStatus.Completed
                || value.Status == BountyStatus.Failed
                || value.Status == BountyStatus.Expired
                || value.Status == BountyStatus.Cancelled
            )
            .ThenBy(value => value.ExpiresAtUtc)
            .ThenByDescending(value => value.CreatedAtUtc)
            .ToListAsync(ct);
        var result = new List<BountyModeratorView>(bounties.Count);
        foreach (var bounty in bounties)
        {
            var audits = await db
                .BountyModerationAudits.AsNoTracking()
                .Where(value => value.HostId == hostId && value.BountyId == bounty.Id)
                .OrderByDescending(value => value.OccurredAtUtc)
                .ThenByDescending(value => value.Id)
                .Select(value => new BountyModerationAuditView(
                    value.Action,
                    value.FromStatus,
                    value.ToStatus,
                    value.ActorTwitchUserId,
                    value.ActorLogin,
                    value.Reason,
                    value.BountyRevision,
                    value.OccurredAtUtc
                ))
                .ToListAsync(ct);
            result.Add(
                new BountyModeratorView(await ToViewAsync(db, bounty, hostLogin, ct), audits)
            );
        }

        return result;
    }

    public async Task<IReadOnlyList<BountyView>> GetPublicBoardAsync(
        string hostLogin,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostLogin, ct))
        {
            return [];
        }

        var login = CommunityInput.NormalizeLogin(hostLogin);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Login == login, ct);
        if (host is null)
        {
            return [];
        }

        var bounties = await db
            .Bounties.AsNoTracking()
            .Where(value => value.HostId == host.Id && value.Visibility == BountyVisibility.Public)
            .ToListAsync(ct);
        var views = new List<BountyView>(bounties.Count);
        foreach (
            var bounty in bounties
                .OrderBy(value => IsTerminal(value.Status))
                .ThenBy(value => value.ExpiresAtUtc)
                .ThenByDescending(value => value.CreatedAtUtc)
        )
        {
            views.Add(await ToViewAsync(db, bounty, host.Login, ct));
        }

        return views;
    }

    public async Task<BountyView?> GetPublicAsync(
        string hostLogin,
        Guid publicId,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostLogin, ct))
        {
            return null;
        }

        var login = CommunityInput.NormalizeLogin(hostLogin);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await (
            from bounty in db.Bounties.AsNoTracking()
            join host in db.Hosts.AsNoTracking() on bounty.HostId equals host.Id
            where
                host.Login == login
                && bounty.PublicId == publicId
                && bounty.Visibility == BountyVisibility.Public
            select new { Bounty = bounty, HostLogin = host.Login }
        ).SingleOrDefaultAsync(ct);
        return row is null ? null : await ToViewAsync(db, row.Bounty, row.HostLogin, ct);
    }

    public async Task<IReadOnlyList<BountyEventView>> GetEventsAsync(
        int hostId,
        long afterEventId,
        int count,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return [];
        }

        var boundedCount = Math.Clamp(count, 1, BountyLimits.MaximumEventReadCount);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .BountyEvents.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.Id > afterEventId
                && value.Bounty.Visibility == BountyVisibility.Public
            )
            .OrderBy(value => value.Id)
            .Take(boundedCount)
            .Select(value => new BountyEventView(
                value.Id,
                value.HostId,
                value.BountyPublicId,
                value.SchemaVersion,
                value.Kind,
                value.PublicPayload,
                value.OccurredAtUtc
            ))
            .ToListAsync(ct);
    }

    private async Task<BountyResult<BountyView>> CreateAttemptAsync(
        int hostId,
        CreateBountyCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        if (!await FeatureIsEnabledAsync(db, hostId, ct))
        {
            return Rejected<BountyView>(new BountyRejection.FeatureDisabled());
        }
        var fingerprint = CommandFingerprint(command);
        var existing = await db.Bounties.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.CreationOperationId == command.OperationId,
            ct
        );
        if (existing is not null)
        {
            return existing.CreationFingerprint == fingerprint
                ? new BountyResult<BountyView>.Succeeded(await ToViewAsync(db, existing, ct), true)
                : Rejected<BountyView>(
                    new BountyRejection.Conflict(
                        "That operation ID belongs to another bounty creation."
                    )
                );
        }

        var host = await db.Hosts.SingleOrDefaultAsync(
            value =>
                value.Id == hostId
                && (value.EnabledFeatures & _requiredFeatures) == _requiredFeatures,
            ct
        );
        if (host is null)
        {
            return Rejected<BountyView>(new BountyRejection.FeatureDisabled());
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (command.ExpiresAtUtc <= now)
        {
            return Rejected<BountyView>(
                new BountyRejection.Invalid("The expiry must be in the future.")
            );
        }
        var bounty = new Bounty
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            CreationOperationId = command.OperationId,
            CreationFingerprint = fingerprint,
            Title = command.Title.Trim(),
            Description = command.Description.Trim(),
            Status = BountyStatus.Proposed,
            Visibility = command.Visibility,
            FailurePledgePolicy = command.FailurePledgePolicy,
            RewardDistribution = command.RewardDistribution,
            FundingTarget = command.FundingTarget.ToString(),
            PledgedAmount = PointAmount.Zero.ToString(),
            CompletionReward = command.CompletionReward.ToString(),
            ExpiresAtUtc = command.ExpiresAtUtc,
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _ = db.Bounties.Add(bounty);
        _ = await db.SaveChangesAsync(ct);
        AddAudit(
            db,
            bounty,
            command.OperationId,
            BountyAuditAction.Created,
            BountyStatus.Proposed,
            BountyStatus.Proposed,
            command.Actor,
            command.Reason,
            fingerprint,
            now
        );
        AddEvent(
            db,
            bounty,
            $"create:{command.OperationId:N}",
            BountyEventKind.Created,
            new
            {
                bounty.PublicId,
                bounty.Title,
                bounty.Status,
                FundingTarget = bounty.FundingTarget,
                bounty.ExpiresAtUtc,
            },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Succeeded(ToView(bounty, host.Login));
    }

    private async Task<BountyResult<BountyPledgeView>> PledgeAttemptAsync(
        int hostId,
        PledgeBountyCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        if (!await FeatureIsEnabledAsync(db, hostId, ct))
        {
            return Rejected<BountyPledgeView>(new BountyRejection.FeatureDisabled());
        }
        var fingerprint = CommandFingerprint(command);
        var existing = await db
            .BountyPledges.Include(value => value.Bounty)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.OperationId == command.OperationId,
                ct
            );
        if (existing is not null)
        {
            return existing.CommandFingerprint == fingerprint
                ? new BountyResult<BountyPledgeView>.Succeeded(ToPledgeView(existing), true)
                : Rejected<BountyPledgeView>(
                    new BountyRejection.Conflict(
                        "That operation ID belongs to another bounty pledge."
                    )
                );
        }

        var bounty = await db.Bounties.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.PublicId == command.BountyPublicId,
            ct
        );
        if (bounty is null)
        {
            return Rejected<BountyPledgeView>(new BountyRejection.NotFound());
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (bounty.Status != BountyStatus.Funding || bounty.ExpiresAtUtc <= now)
        {
            return Rejected<BountyPledgeView>(new BountyRejection.FundingClosed());
        }

        var target = PointAmount.ParseAbsolute(bounty.FundingTarget);
        var pledged = PointAmount.ParseAbsolute(bounty.PledgedAmount);
        var remaining = target.Subtract(pledged);
        if (remaining.IsZero)
        {
            return Rejected<BountyPledgeView>(new BountyRejection.FundingClosed());
        }

        var amount = command.RequestedAmount <= remaining ? command.RequestedAmount : remaining;
        var balance = await LoadBalanceAsync(db, hostId, command.Contributor.Login, now, ct);
        var current = PointAmount.ParseAbsolute(balance.Amount);
        if (current < amount)
        {
            return Rejected<BountyPledgeView>(
                new BountyRejection.InsufficientPoints(current, amount)
            );
        }
        if (
            !await PointCreditCapacity.IsExposureWithinLimitAsync(
                db,
                hostId,
                command.Contributor.Login,
                current,
                ct
            )
        )
        {
            return Rejected<BountyPledgeView>(
                new BountyRejection.PointCapExceeded(command.Contributor.Login)
            );
        }

        var pledge = new BountyPledge
        {
            HostId = hostId,
            BountyId = bounty.Id,
            Bounty = bounty,
            OperationId = command.OperationId,
            CommandFingerprint = fingerprint,
            ContributorTwitchUserId = command.Contributor.TwitchUserId,
            ContributorLogin = command.Contributor.Login,
            Amount = amount.ToString(),
            State = BountyPledgeState.Reserved,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _ = db.BountyPledges.Add(pledge);
        var nextPledged = pledged.Add(amount);
        bounty.PledgedAmount = nextPledged.ToString();
        if (
            !await db.BountyPledges.AnyAsync(
                value =>
                    value.HostId == hostId
                    && value.BountyId == bounty.Id
                    && value.ContributorLogin == command.Contributor.Login,
                ct
            )
        )
        {
            bounty.ContributorCount++;
        }
        bounty.Revision++;
        bounty.UpdatedAtUtc = now;
        var nextBalance = current.Subtract(amount);
        balance.Amount = nextBalance.ToString();
        balance.UpdatedAtUtc = now;
        _ = await db.SaveChangesAsync(ct);

        AddLedger(
            db,
            hostId,
            PointLedgerKind.BountyPledgeReservation,
            command.Contributor.Login,
            -amount.Value,
            nextBalance,
            command.Contributor.Login,
            pledge.Id,
            null,
            $"bounty:{bounty.PublicId:N}:pledge:{command.OperationId:N}",
            "Bounty pledge reservation",
            now
        );
        AddEvent(
            db,
            bounty,
            $"pledge:{command.OperationId:N}",
            BountyEventKind.Pledged,
            new
            {
                bounty.PublicId,
                pledge.Id,
                pledge.ContributorLogin,
                Amount = amount.ToString(),
                PledgedAmount = nextPledged.ToString(),
                FundingTarget = target.ToString(),
            },
            now
        );
        if (nextPledged == target)
        {
            AddEvent(
                db,
                bounty,
                $"pledge:{command.OperationId:N}:target",
                BountyEventKind.FundingTargetReached,
                new { bounty.PublicId, FundingTarget = target.ToString() },
                now
            );
        }

        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Succeeded(ToPledgeView(pledge));
    }

    private async Task<BountyResult<BountyView>> TransitionAttemptAsync(
        int hostId,
        TransitionBountyCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        if (!await FeatureIsEnabledAsync(db, hostId, ct))
        {
            return Rejected<BountyView>(new BountyRejection.FeatureDisabled());
        }
        var fingerprint = CommandFingerprint(command);
        var existingAudit = await db
            .BountyModerationAudits.Include(value => value.Bounty)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.OperationId == command.OperationId,
                ct
            );
        if (existingAudit is not null)
        {
            return existingAudit.CommandFingerprint == fingerprint
                ? new BountyResult<BountyView>.Succeeded(
                    await ToViewAsync(db, existingAudit.Bounty, ct),
                    true
                )
                : Rejected<BountyView>(
                    new BountyRejection.Conflict(
                        "That operation ID belongs to another bounty transition."
                    )
                );
        }

        var bounty = await db
            .Bounties.Include(value => value.Pledges)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.PublicId == command.BountyPublicId,
                ct
            );
        if (bounty is null)
        {
            return Rejected<BountyView>(new BountyRejection.NotFound());
        }

        if (bounty.Revision != command.ExpectedRevision)
        {
            return Rejected<BountyView>(new BountyRejection.StaleRevision(bounty.Revision));
        }

        var target = BountyLifecycle.Target(bounty.Status, command.Action);
        if (target is null)
        {
            return Rejected<BountyView>(
                new BountyRejection.InvalidTransition(bounty.Status, command.Action)
            );
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (command.Action == BountyTransitionAction.OpenFunding && bounty.ExpiresAtUtc <= now)
        {
            return Rejected<BountyView>(
                new BountyRejection.Invalid("An expired bounty cannot open for funding.")
            );
        }
        if (command.Action == BountyTransitionAction.Expire && bounty.ExpiresAtUtc > now)
        {
            return Rejected<BountyView>(
                new BountyRejection.Invalid("The bounty has not reached its expiry time.")
            );
        }
        if (command.Action == BountyTransitionAction.Accept && bounty.ExpiresAtUtc <= now)
        {
            return Rejected<BountyView>(
                new BountyRejection.Invalid("An expired bounty cannot be accepted.")
            );
        }

        var accounting = await ApplyTerminalAccountingAsync(db, bounty, target.Value, now, ct);
        if (accounting is not null)
        {
            return Rejected<BountyView>(accounting);
        }

        var previous = bounty.Status;
        bounty.Status = target.Value;
        bounty.Revision++;
        bounty.UpdatedAtUtc = now;
        if (target == BountyStatus.Accepted)
        {
            bounty.AcceptedAtUtc = now;
        }
        if (IsTerminal(target.Value))
        {
            bounty.ResolvedAtUtc = now;
        }

        AddAudit(
            db,
            bounty,
            command.OperationId,
            AuditAction(command.Action),
            previous,
            target.Value,
            command.Actor,
            command.Reason,
            fingerprint,
            now
        );
        AddEvent(
            db,
            bounty,
            $"transition:{command.OperationId:N}",
            EventKind(command.Action),
            new
            {
                bounty.PublicId,
                PreviousStatus = previous,
                Status = target.Value,
                bounty.Revision,
            },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Succeeded(await ToViewAsync(db, bounty, ct));
    }

    private async Task<BountyResult<BountyView>> ExtendAttemptAsync(
        int hostId,
        ExtendBountyCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        if (!await FeatureIsEnabledAsync(db, hostId, ct))
        {
            return Rejected<BountyView>(new BountyRejection.FeatureDisabled());
        }
        var fingerprint = CommandFingerprint(command);
        var existingAudit = await db
            .BountyModerationAudits.Include(value => value.Bounty)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.OperationId == command.OperationId,
                ct
            );
        if (existingAudit is not null)
        {
            return existingAudit.CommandFingerprint == fingerprint
                ? new BountyResult<BountyView>.Succeeded(
                    await ToViewAsync(db, existingAudit.Bounty, ct),
                    true
                )
                : Rejected<BountyView>(
                    new BountyRejection.Conflict(
                        "That operation ID belongs to another bounty transition."
                    )
                );
        }

        var bounty = await db.Bounties.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.PublicId == command.BountyPublicId,
            ct
        );
        if (bounty is null)
        {
            return Rejected<BountyView>(new BountyRejection.NotFound());
        }
        if (bounty.Revision != command.ExpectedRevision)
        {
            return Rejected<BountyView>(new BountyRejection.StaleRevision(bounty.Revision));
        }
        if (!BountyLifecycle.CanExtend(bounty.Status))
        {
            return Rejected<BountyView>(
                new BountyRejection.Invalid("Only funding or accepted bounties can be extended.")
            );
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (command.ExpiresAtUtc <= now || command.ExpiresAtUtc <= bounty.ExpiresAtUtc)
        {
            return Rejected<BountyView>(
                new BountyRejection.Invalid(
                    "An extension must move the expiry later and keep it in the future."
                )
            );
        }

        var previousExpiry = bounty.ExpiresAtUtc;
        bounty.ExpiresAtUtc = command.ExpiresAtUtc;
        bounty.Revision++;
        bounty.UpdatedAtUtc = now;
        AddAudit(
            db,
            bounty,
            command.OperationId,
            BountyAuditAction.Extended,
            bounty.Status,
            bounty.Status,
            command.Actor,
            command.Reason,
            fingerprint,
            now
        );
        AddEvent(
            db,
            bounty,
            $"extend:{command.OperationId:N}",
            BountyEventKind.Extended,
            new
            {
                bounty.PublicId,
                PreviousExpiresAtUtc = previousExpiry,
                bounty.ExpiresAtUtc,
                bounty.Revision,
            },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Succeeded(await ToViewAsync(db, bounty, ct));
    }

    private static BountyRejection? Validate(CreateBountyCommand command) =>
        command.OperationId == Guid.Empty
            ? new BountyRejection.Invalid("A creation operation ID is required.")
        : Validate(command.Actor) is { } actorRejection ? actorRejection
        : command.Title.Trim().Length is < 1 or > BountyLimits.MaximumTitleLength
            ? new BountyRejection.Invalid(
                $"The title must be between 1 and {BountyLimits.MaximumTitleLength} characters."
            )
        : command.Description.Trim().Length > BountyLimits.MaximumDescriptionLength
            ? new BountyRejection.Invalid(
                $"The description cannot exceed {BountyLimits.MaximumDescriptionLength} characters."
            )
        : command.FundingTarget.IsZero
            ? new BountyRejection.Invalid("The funding target must be greater than zero.")
        : command.ExpiresAtUtc.Kind != DateTimeKind.Utc
            ? new BountyRejection.Invalid("The expiry must be expressed in UTC.")
        : ValidateReason(command.Reason);

    private static BountyRejection? Validate(PledgeBountyCommand command) =>
        command.OperationId == Guid.Empty
            ? new BountyRejection.Invalid("A pledge operation ID is required.")
        : command.BountyPublicId == Guid.Empty
            ? new BountyRejection.Invalid("A bounty identity is required.")
        : command.RequestedAmount.IsZero
            ? new BountyRejection.Invalid("The pledge amount must be greater than zero.")
        : Validate(command.Contributor);

    private static BountyRejection? Validate(TransitionBountyCommand command) =>
        command.OperationId == Guid.Empty
            ? new BountyRejection.Invalid("A transition operation ID is required.")
        : command.BountyPublicId == Guid.Empty
            ? new BountyRejection.Invalid("A bounty identity is required.")
        : command.ExpectedRevision < 1
            ? new BountyRejection.Invalid("A positive expected revision is required.")
        : Validate(command.Actor) ?? ValidateReason(command.Reason);

    private static BountyRejection? Validate(ExtendBountyCommand command) =>
        command.OperationId == Guid.Empty
            ? new BountyRejection.Invalid("An extension operation ID is required.")
        : command.BountyPublicId == Guid.Empty
            ? new BountyRejection.Invalid("A bounty identity is required.")
        : command.ExpectedRevision < 1
            ? new BountyRejection.Invalid("A positive expected revision is required.")
        : command.ExpiresAtUtc.Kind != DateTimeKind.Utc
            ? new BountyRejection.Invalid("The expiry must be expressed in UTC.")
        : Validate(command.Actor) ?? ValidateReason(command.Reason);

    private static BountyRejection? Validate(BountyActor actor)
    {
        var userId = actor.TwitchUserId.Trim();
        var login = CommunityInput.NormalizeLogin(actor.Login);
        return userId.Length is < 1 or > 128
                ? new BountyRejection.Invalid("A Twitch user identity is required.")
            : !CommunityInput.IsValidLogin(login)
                ? new BountyRejection.Invalid("A valid Twitch login is required.")
            : null;
    }

    private static BountyRejection? ValidateReason(string reason) =>
        reason.Trim().Length > BountyLimits.MaximumReasonLength
            ? new BountyRejection.Invalid(
                $"The reason cannot exceed {BountyLimits.MaximumReasonLength} characters."
            )
            : null;

    private async Task<BountyRejection?> ApplyTerminalAccountingAsync(
        BlokeBotDbContext db,
        Bounty bounty,
        BountyStatus target,
        DateTime now,
        CancellationToken ct
    )
    {
        var reserved = bounty
            .Pledges.Where(pledge => pledge.State == BountyPledgeState.Reserved)
            .OrderBy(pledge => pledge.Id)
            .ToArray();
        if (reserved.Length == 0)
        {
            return null;
        }

        if (BountyLifecycle.RefundsPledges(target, bounty.FailurePledgePolicy))
        {
            foreach (
                var group in reserved.GroupBy(
                    pledge => pledge.ContributorLogin,
                    StringComparer.Ordinal
                )
            )
            {
                var balance = await LoadBalanceAsync(db, bounty.HostId, group.Key, now, ct);
                var current = PointAmount.ParseAbsolute(balance.Amount);
                if (
                    !await PointCreditCapacity.IsExposureWithinLimitAsync(
                        db,
                        bounty.HostId,
                        group.Key,
                        current,
                        ct
                    )
                )
                {
                    return new BountyRejection.PointCapExceeded(group.Key);
                }
            }

            foreach (var pledge in reserved)
            {
                var amount = PointAmount.ParseAbsolute(pledge.Amount);
                var balance = await LoadBalanceAsync(
                    db,
                    bounty.HostId,
                    pledge.ContributorLogin,
                    now,
                    ct
                );
                var current = PointAmount.ParseAbsolute(balance.Amount);
                var next = current.Add(amount);
                balance.Amount = next.ToString();
                balance.UpdatedAtUtc = now;
                pledge.State = BountyPledgeState.Refunded;
                pledge.UpdatedAtUtc = now;
                AddLedger(
                    db,
                    bounty.HostId,
                    PointLedgerKind.BountyPledgeRefund,
                    pledge.ContributorLogin,
                    amount.Value,
                    next,
                    null,
                    pledge.Id,
                    null,
                    $"bounty:{bounty.PublicId:N}:refund:{pledge.OperationId:N}",
                    "Bounty pledge refund",
                    now
                );
            }
            AddEvent(
                db,
                bounty,
                $"accounting:{bounty.Revision + 1}:refund",
                BountyEventKind.PledgesRefunded,
                new
                {
                    bounty.PublicId,
                    PledgeCount = reserved.Length,
                    Amount = reserved
                        .Aggregate(
                            BigInteger.Zero,
                            (total, pledge) =>
                                total + PointAmount.ParseAbsolute(pledge.Amount).Value
                        )
                        .ToString(CultureInfo.InvariantCulture),
                },
                now
            );
            return null;
        }

        if (!BountyLifecycle.ConsumesPledges(target, bounty.FailurePledgePolicy))
        {
            return null;
        }

        foreach (var pledge in reserved)
        {
            var balance = await LoadBalanceAsync(
                db,
                bounty.HostId,
                pledge.ContributorLogin,
                now,
                ct
            );
            pledge.State = BountyPledgeState.Consumed;
            pledge.UpdatedAtUtc = now;
            AddLedger(
                db,
                bounty.HostId,
                PointLedgerKind.BountyPledgeConsumption,
                pledge.ContributorLogin,
                BigInteger.Zero,
                PointAmount.ParseAbsolute(balance.Amount),
                null,
                pledge.Id,
                null,
                $"bounty:{bounty.PublicId:N}:consume:{pledge.OperationId:N}",
                "Bounty pledge consumed",
                now
            );
        }
        AddEvent(
            db,
            bounty,
            $"accounting:{bounty.Revision + 1}:consume",
            BountyEventKind.PledgesConsumed,
            new { bounty.PublicId, PledgeCount = reserved.Length },
            now
        );

        if (target != BountyStatus.Completed)
        {
            return null;
        }

        _ = await db.SaveChangesAsync(ct);
        return await ApplyCompletionRewardsAsync(db, bounty, reserved, now, ct);
    }

    private static async Task<BountyRejection?> ApplyCompletionRewardsAsync(
        BlokeBotDbContext db,
        Bounty bounty,
        IReadOnlyList<BountyPledge> pledges,
        DateTime now,
        CancellationToken ct
    )
    {
        var reward = PointAmount.ParseAbsolute(bounty.CompletionReward);
        if (reward.IsZero)
        {
            return null;
        }

        var shares = BountyRewardAllocator.Allocate(
            pledges
                .Select(pledge => new BountyContribution(
                    pledge.ContributorTwitchUserId,
                    pledge.ContributorLogin,
                    PointAmount.ParseAbsolute(pledge.Amount).Value,
                    pledge.CreatedAtUtc
                ))
                .ToArray(),
            reward.Value,
            bounty.RewardDistribution
        );
        var balances = new Dictionary<string, PointBalance>(StringComparer.Ordinal);
        foreach (var group in shares.GroupBy(share => share.Login, StringComparer.Ordinal))
        {
            var balance = await LoadBalanceAsync(db, bounty.HostId, group.Key, now, ct);
            var current = PointAmount.ParseAbsolute(balance.Amount);
            var total = group.Aggregate(BigInteger.Zero, (sum, share) => sum + share.Amount);
            if (
                !await PointCreditCapacity.CanCreditAsync(
                    db,
                    bounty.HostId,
                    group.Key,
                    current,
                    total,
                    ct
                )
            )
            {
                return new BountyRejection.PointCapExceeded(group.Key);
            }
            balances[group.Key] = balance;
        }

        var rewards = shares
            .Select(share => new BountyContributorReward
            {
                HostId = bounty.HostId,
                BountyId = bounty.Id,
                Bounty = bounty,
                TwitchUserId = share.TwitchUserId,
                Login = share.Login,
                Amount = share.Amount.ToString(CultureInfo.InvariantCulture),
                CreatedAtUtc = now,
            })
            .ToArray();
        db.BountyContributorRewards.AddRange(rewards);
        _ = await db.SaveChangesAsync(ct);
        foreach (var contributorReward in rewards)
        {
            var amount = new PointAmount(
                BigInteger.Parse(contributorReward.Amount, CultureInfo.InvariantCulture)
            );
            var balance = balances[contributorReward.Login];
            var current = PointAmount.ParseAbsolute(balance.Amount);
            var next = current.Add(amount);
            balance.Amount = next.ToString();
            balance.UpdatedAtUtc = now;
            AddLedger(
                db,
                bounty.HostId,
                PointLedgerKind.BountyCompletionReward,
                contributorReward.Login,
                amount.Value,
                next,
                null,
                null,
                contributorReward.Id,
                $"bounty:{bounty.PublicId:N}:reward:{contributorReward.Id}",
                "Bounty completion reward",
                now
            );
        }
        AddEvent(
            db,
            bounty,
            $"accounting:{bounty.Revision + 1}:rewards",
            BountyEventKind.RewardsDistributed,
            new
            {
                bounty.PublicId,
                RecipientCount = rewards.Length,
                Amount = reward.ToString(),
                Distribution = bounty.RewardDistribution,
            },
            now
        );
        return null;
    }

    private static async Task<PointBalance> LoadBalanceAsync(
        BlokeBotDbContext db,
        int hostId,
        string login,
        DateTime now,
        CancellationToken ct
    )
    {
        var normalized = CommunityInput.NormalizeLogin(login);
        var balance = await db.PointBalances.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.Login == normalized,
            ct
        );
        if (balance is not null)
        {
            return balance;
        }

        balance = new PointBalance
        {
            HostId = hostId,
            Login = normalized,
            Amount = PointAmount.Zero.ToString(),
            UpdatedAtUtc = now,
        };
        _ = db.PointBalances.Add(balance);
        return balance;
    }

    private static void AddLedger(
        BlokeBotDbContext db,
        int hostId,
        PointLedgerKind kind,
        string login,
        BigInteger delta,
        PointAmount balanceAfter,
        string? actorLogin,
        long? bountyPledgeId,
        long? bountyRewardId,
        string operationKey,
        string note,
        DateTime now
    ) =>
        db.PointLedgerEntries.Add(
            new PointLedgerEntry
            {
                HostId = hostId,
                CreatedAtUtc = now,
                Kind = kind,
                Login = CommunityInput.NormalizeLogin(login),
                Delta = delta.ToString(CultureInfo.InvariantCulture),
                BalanceAfter = balanceAfter.ToString(),
                ActorLogin = actorLogin is null ? null : CommunityInput.NormalizeLogin(actorLogin),
                BountyPledgeId = bountyPledgeId,
                BountyRewardId = bountyRewardId,
                OperationKey = operationKey,
                Note = note,
            }
        );

    private static void AddAudit(
        BlokeBotDbContext db,
        Bounty bounty,
        Guid operationId,
        BountyAuditAction action,
        BountyStatus from,
        BountyStatus to,
        BountyActor actor,
        string reason,
        string commandFingerprint,
        DateTime now
    ) =>
        db.BountyModerationAudits.Add(
            new BountyModerationAudit
            {
                HostId = bounty.HostId,
                BountyId = bounty.Id,
                Bounty = bounty,
                OperationId = operationId,
                CommandFingerprint = commandFingerprint,
                Action = action,
                FromStatus = from,
                ToStatus = to,
                ActorTwitchUserId = actor.TwitchUserId,
                ActorLogin = actor.Login,
                Reason = reason.Trim(),
                BountyRevision = bounty.Revision,
                OccurredAtUtc = now,
            }
        );

    private static void AddEvent(
        BlokeBotDbContext db,
        Bounty bounty,
        string operationKey,
        BountyEventKind kind,
        object payload,
        DateTime now
    )
    {
        var json = JsonSerializer.Serialize(payload);
        if (json.Length > _maximumEventPayloadLength)
        {
            throw new InvalidOperationException("The bounty event payload exceeds its bound.");
        }

        _ = db.BountyEvents.Add(
            new BountyDomainEvent
            {
                HostId = bounty.HostId,
                BountyId = bounty.Id,
                Bounty = bounty,
                BountyPublicId = bounty.PublicId,
                OperationKey = operationKey,
                SchemaVersion = _eventSchemaVersion,
                Kind = kind,
                PublicPayload = json,
                OccurredAtUtc = now,
            }
        );
    }

    private static BountyAuditAction AuditAction(BountyTransitionAction action) =>
        action switch
        {
            BountyTransitionAction.OpenFunding => BountyAuditAction.FundingOpened,
            BountyTransitionAction.Accept => BountyAuditAction.Accepted,
            BountyTransitionAction.Complete => BountyAuditAction.Completed,
            BountyTransitionAction.Fail => BountyAuditAction.Failed,
            BountyTransitionAction.Cancel => BountyAuditAction.Cancelled,
            BountyTransitionAction.Reject => BountyAuditAction.Rejected,
            BountyTransitionAction.Expire => BountyAuditAction.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

    private static BountyEventKind EventKind(BountyTransitionAction action) =>
        action switch
        {
            BountyTransitionAction.OpenFunding => BountyEventKind.FundingOpened,
            BountyTransitionAction.Accept => BountyEventKind.Accepted,
            BountyTransitionAction.Complete => BountyEventKind.Completed,
            BountyTransitionAction.Fail => BountyEventKind.Failed,
            BountyTransitionAction.Cancel or BountyTransitionAction.Reject =>
                BountyEventKind.Cancelled,
            BountyTransitionAction.Expire => BountyEventKind.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

    private static BountyActor Normalize(BountyActor actor) =>
        new(actor.TwitchUserId.Trim(), CommunityInput.NormalizeLogin(actor.Login));

    private static string CommandFingerprint(CreateBountyCommand command) =>
        Fingerprint(
            command.Title.Trim(),
            command.Description.Trim(),
            command.FundingTarget.ToString(),
            command.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture),
            command.CompletionReward.ToString(),
            PersistedEnumTokens<BountyVisibility>.Format(command.Visibility),
            PersistedEnumTokens<BountyFailurePledgePolicy>.Format(command.FailurePledgePolicy),
            PersistedEnumTokens<BountyRewardDistribution>.Format(command.RewardDistribution),
            command.Actor.TwitchUserId,
            command.Actor.Login,
            command.Reason.Trim()
        );

    private static string CommandFingerprint(PledgeBountyCommand command) =>
        Fingerprint(
            command.BountyPublicId.ToString("N"),
            command.Contributor.TwitchUserId,
            command.Contributor.Login,
            command.RequestedAmount.ToString()
        );

    private static string CommandFingerprint(TransitionBountyCommand command) =>
        Fingerprint(
            command.BountyPublicId.ToString("N"),
            command.ExpectedRevision.ToString(CultureInfo.InvariantCulture),
            command.Action.ToString(),
            command.Actor.TwitchUserId,
            command.Actor.Login,
            command.Reason.Trim()
        );

    private static string CommandFingerprint(ExtendBountyCommand command) =>
        Fingerprint(
            command.BountyPublicId.ToString("N"),
            command.ExpectedRevision.ToString(CultureInfo.InvariantCulture),
            command.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture),
            command.Actor.TwitchUserId,
            command.Actor.Login,
            command.Reason.Trim()
        );

    private static string Fingerprint(params IReadOnlyList<string> values)
    {
        var canonical = new StringBuilder();
        foreach (var value in values)
        {
            _ = canonical
                .Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(value);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static BountyPledgeView ToPledgeView(BountyPledge pledge) =>
        new(
            pledge.Id,
            pledge.Bounty?.PublicId ?? Guid.Empty,
            pledge.ContributorLogin,
            PointAmount.ParseAbsolute(pledge.Amount),
            pledge.State,
            pledge.CreatedAtUtc
        );

    private static async Task<BountyView> ToViewAsync(
        BlokeBotDbContext db,
        Bounty bounty,
        CancellationToken ct
    )
    {
        var hostLogin = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == bounty.HostId)
            .Select(value => value.Login)
            .SingleAsync(ct);
        return await ToViewAsync(db, bounty, hostLogin, ct);
    }

    private static async Task<BountyView> ToViewAsync(
        BlokeBotDbContext db,
        Bounty bounty,
        string hostLogin,
        CancellationToken ct
    )
    {
        var pledges = await db
            .BountyPledges.AsNoTracking()
            .Where(value => value.HostId == bounty.HostId && value.BountyId == bounty.Id)
            .Select(value => new { value.ContributorLogin, value.Amount })
            .ToListAsync(ct);
        var contributors = pledges
            .GroupBy(value => value.ContributorLogin, StringComparer.Ordinal)
            .Select(group => new BountyContributorView(
                group.Key,
                new PointAmount(
                    group.Aggregate(
                        BigInteger.Zero,
                        (total, pledge) =>
                            total + BigInteger.Parse(pledge.Amount, CultureInfo.InvariantCulture)
                    )
                )
            ))
            .OrderByDescending(value => value.PledgedAmount.Value)
            .ThenBy(value => value.Login, StringComparer.Ordinal)
            .ToArray();
        var terminalHistory = await db
            .BountyModerationAudits.AsNoTracking()
            .Where(value =>
                value.HostId == bounty.HostId
                && value.BountyId == bounty.Id
                && (
                    value.ToStatus == BountyStatus.Completed
                    || value.ToStatus == BountyStatus.Failed
                    || value.ToStatus == BountyStatus.Expired
                    || value.ToStatus == BountyStatus.Cancelled
                )
            )
            .OrderBy(value => value.OccurredAtUtc)
            .ThenBy(value => value.Id)
            .Select(value => new BountyPublicHistoryView(
                value.Action,
                value.ToStatus,
                value.OccurredAtUtc
            ))
            .ToListAsync(ct);
        return ToView(bounty, hostLogin) with
        {
            Contributors = contributors,
            TerminalHistory = terminalHistory,
        };
    }

    private static BountyView ToView(Bounty bounty, string hostLogin) =>
        new(
            bounty.PublicId,
            bounty.HostId,
            hostLogin,
            bounty.Title,
            bounty.Description,
            bounty.Status,
            bounty.Visibility,
            bounty.FailurePledgePolicy,
            bounty.RewardDistribution,
            PointAmount.ParseAbsolute(bounty.FundingTarget),
            PointAmount.ParseAbsolute(bounty.PledgedAmount),
            PointAmount.ParseAbsolute(bounty.CompletionReward),
            bounty.ContributorCount,
            bounty.ExpiresAtUtc,
            bounty.Revision,
            bounty.CreatedAtUtc,
            bounty.UpdatedAtUtc,
            bounty.AcceptedAtUtc,
            bounty.ResolvedAtUtc,
            [],
            []
        );

    private static bool IsTerminal(BountyStatus status) =>
        status
            is BountyStatus.Completed
                or BountyStatus.Failed
                or BountyStatus.Expired
                or BountyStatus.Cancelled;

    private Task<bool> FeatureIsEnabledAsync(int hostId, CancellationToken ct) =>
        HostFeatureAvailability.IsEnabledAsync(dbFactory, hostId, _requiredFeatures, ct);

    private Task<bool> FeatureIsEnabledAsync(string hostLogin, CancellationToken ct) =>
        HostFeatureAvailability.IsEnabledAsync(dbFactory, hostLogin, _requiredFeatures, ct);

    private static Task<bool> FeatureIsEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        db.Hosts.AnyAsync(
            value =>
                value.Id == hostId
                && (value.EnabledFeatures & _requiredFeatures) == _requiredFeatures,
            ct
        );

    private async Task PublishChangesAsync(bool pointsChanged, CancellationToken ct)
    {
        _ = await events.PublishAsync(AppEventKind.BountiesChanged, ct);
        if (pointsChanged)
        {
            _ = await events.PublishAsync(AppEventKind.PointsChanged, ct);
        }
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

    private static BountyResult<T> Succeeded<T>(T value) => new BountyResult<T>.Succeeded(value);

    private static BountyResult<T> Rejected<T>(BountyRejection rejection) =>
        new BountyResult<T>.Rejected(rejection);

    private static HostFeatureFlags _requiredFeatures =>
        HostFeatureFlags.Bounties | HostFeatureFlags.Points;

    private sealed class ImmediateTransaction(
        SqliteTransaction providerTransaction,
        IDbContextTransaction contextTransaction
    ) : IAsyncDisposable
    {
        public static async Task<ImmediateTransaction> StartAsync(
            BlokeBotDbContext db,
            CancellationToken ct
        )
        {
            await db.Database.OpenConnectionAsync(ct);
            var connection =
                db.Database.GetDbConnection() as SqliteConnection
                ?? throw new InvalidOperationException("Bounty persistence requires SQLite.");
            var providerTransaction = connection.BeginTransaction(deferred: false);
            try
            {
                var contextTransaction =
                    await db.Database.UseTransactionAsync(providerTransaction, ct)
                    ?? throw new InvalidOperationException(
                        "The immediate SQLite transaction could not be attached."
                    );
                return new ImmediateTransaction(providerTransaction, contextTransaction);
            }
            catch
            {
                await providerTransaction.DisposeAsync();
                throw;
            }
        }

        public Task CommitAsync(CancellationToken ct) => contextTransaction.CommitAsync(ct);

        public async ValueTask DisposeAsync()
        {
            await contextTransaction.DisposeAsync();
            await providerTransaction.DisposeAsync();
        }
    }
}
