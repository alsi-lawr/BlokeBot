using System.Globalization;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bounties;

internal sealed partial class BountyService
{
    private static readonly BountyActor _pauseRecoveryActor = new(
        "BlokeBot.BountyPauseRecovery",
        "blokebot"
    );

    internal async Task ReconcilePauseAsync(
        int hostId,
        BountyPauseRecoveryCause cause,
        CancellationToken ct
    )
    {
        var recoveredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        _ = await RetryPersistenceAsync(
            async () =>
            {
                await ReconcilePauseAttemptAsync(hostId, recoveredAtUtc, cause, ct);
                return true;
            },
            ct
        );
    }

    private async Task ReconcilePauseAttemptAsync(
        int hostId,
        DateTime recoveredAtUtc,
        BountyPauseRecoveryCause cause,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction =
            await MainDatabaseWriteTransaction.StartImmediateWithBoundedAdmissionAsync(
                db,
                TimeSpan.FromSeconds(_immediateTransactionAdmissionTimeoutSeconds),
                ct
            );
        var host = await db.Hosts.SingleOrDefaultAsync(value => value.Id == hostId, ct);
        if (host is null)
        {
            return;
        }
        if (
            !host.EnabledFeatures.Contains(_requiredFeatures)
            || host.BountiesPausedAtUtc is not { } pausedAtUtc
        )
        {
            return;
        }

        var pausedFor = recoveredAtUtc - pausedAtUtc;
        if (pausedFor > TimeSpan.Zero)
        {
            var active = await db
                .Bounties.Where(value =>
                    value.HostId == hostId
                    && (
                        value.Status == BountyStatus.Funding
                        || value.Status == BountyStatus.Accepted
                    )
                )
                .ToArrayAsync(ct);
            foreach (var bounty in active)
            {
                AdjustPausedDeadline(db, bounty, pausedAtUtc, recoveredAtUtc, cause);
            }
        }

        host.BountiesPausedAtUtc = null;
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static void AdjustPausedDeadline(
        BlokeBotDbContext db,
        Bounty bounty,
        DateTime pausedAtUtc,
        DateTime recoveredAtUtc,
        BountyPauseRecoveryCause cause
    )
    {
        var previousExpiry = bounty.ExpiresAtUtc;
        bounty.ExpiresAtUtc = previousExpiry.Add(recoveredAtUtc - pausedAtUtc);
        bounty.Revision++;
        bounty.UpdatedAtUtc = recoveredAtUtc;

        var operationId = BountyPauseAdjustmentOperationId.Create(
            bounty.PublicId,
            pausedAtUtc,
            recoveredAtUtc
        );
        var reason =
            $"Automatic pause recovery because {cause.Describe()}. Deadline moved from {Format(previousExpiry)} to {Format(bounty.ExpiresAtUtc)} for the pause interval {Format(pausedAtUtc)} to {Format(recoveredAtUtc)}.";
        AddAudit(
            db,
            bounty,
            operationId,
            BountyAuditAction.PauseAdjusted,
            bounty.Status,
            bounty.Status,
            _pauseRecoveryActor,
            reason,
            Fingerprint(
                bounty.PublicId.ToString("N"),
                Format(pausedAtUtc),
                Format(recoveredAtUtc),
                Format(previousExpiry),
                Format(bounty.ExpiresAtUtc),
                cause.Describe()
            ),
            recoveredAtUtc
        );
    }

    private static string Format(DateTime value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
