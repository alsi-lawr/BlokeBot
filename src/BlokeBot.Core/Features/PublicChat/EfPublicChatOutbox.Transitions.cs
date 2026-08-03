using System.Diagnostics;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PublicChat;

internal sealed partial class EfPublicChatOutbox
{
    private async ValueTask<PublicChatClaimUpdate> ExecuteStateTransitionAsync(
        Func<BlokeBotDbContext, CancellationToken, Task<int>> transition,
        PublicChatClaimedMessage message,
        AutomaticRaidShoutoutResultCode automaticRaidResult,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            var changed = Changed(await transition(db, cancellationToken));
            if (changed is not PublicChatClaimUpdate.Applied)
            {
                await transaction.RollbackAsync(cancellationToken);
                return changed;
            }

            var alertCreated = await RecordAutomaticRaidTerminalAsync(
                db,
                message,
                automaticRaidResult,
                completedAt,
                cancellationToken
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (alertCreated && events is not null)
            {
                _ = await events.PublishAsync(AppEventKind.AlertsChanged, cancellationToken);
            }
            return changed;
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

    private static int? HttpStatusCode(PublicChatHttpStatus status) =>
        status.Match<int?>(known => known.Value, () => null);

    private static bool IsSqliteContention(Exception exception) =>
        exception switch
        {
            SqliteException { SqliteErrorCode: 5 or 6 } => true,
            DbUpdateException { InnerException: { } inner } => IsSqliteContention(inner),
            _ => false,
        };

    private static bool IsClaimSlotContention(Exception exception) =>
        exception switch
        {
            SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 } => true,
            DbUpdateException { InnerException: { } inner } => IsClaimSlotContention(inner),
            _ => false,
        };

    private static PublicChatClaimUpdate Changed(int rowCount) =>
        rowCount switch
        {
            0 => new PublicChatClaimUpdate.OwnershipLost(),
            1 => new PublicChatClaimUpdate.Applied(),
            _ => throw new UnreachableException(
                $"A public chat claim transition changed {rowCount} rows."
            ),
        };

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private static DateTime SubtractOrMinimum(DateTime value, TimeSpan duration) =>
        duration.Ticks >= value.Ticks - DateTime.MinValue.Ticks
            ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
            : value - duration;

    private static DateTimeOffset AddOrMaximum(DateTimeOffset value, TimeSpan duration) =>
        duration.Ticks >= DateTimeOffset.MaxValue.UtcTicks - value.UtcTicks
            ? DateTimeOffset.MaxValue
            : value.Add(duration);

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
