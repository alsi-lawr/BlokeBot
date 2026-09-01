using System.Diagnostics;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

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
        await using var reportOperation = await BeginAlertReportOperationAsync(cancellationToken);
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

            var alertChange = await RecordAutomaticRaidTerminalAsync(
                reportOperation,
                db,
                message,
                automaticRaidResult,
                completedAt,
                cancellationToken
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await PublishCommittedAlertAsync(reportOperation, alertChange);
            return changed;
        }
        catch (Exception exception) when (IsDatabaseContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

    private static int? HttpStatusCode(PublicChatHttpStatus status) =>
        status.Match<int?>(static known => known.Value, static () => null);

    private static bool IsDatabaseContention(Exception exception) =>
        MainDatabaseFailureClassifier.IsContention(exception);

    private static bool IsClaimSlotContention(Exception exception) =>
        MainDatabaseFailureClassifier.Classify(exception) == MainDatabaseFailureKind.UniqueConflict;

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
