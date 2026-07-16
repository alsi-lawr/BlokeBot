using System.Diagnostics;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PublicChat;

internal sealed partial class EfPublicChatOutbox
{
    private async ValueTask<PublicChatClaimUpdate> ExecuteStateTransitionAsync(
        Func<BlokeBotDbContext, CancellationToken, Task<int>> transition,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return Changed(await transition(db, cancellationToken));
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

    private static int? HttpStatusCode(PublicChatHttpStatus status)
    {
        return status.Match<int?>(known => known.Value, () => null);
    }

    private static bool IsSqliteContention(Exception exception)
    {
        return exception switch
        {
            SqliteException { SqliteErrorCode: 5 or 6 } => true,
            DbUpdateException { InnerException: { } inner } => IsSqliteContention(inner),
            _ => false,
        };
    }

    private static bool IsClaimSlotContention(Exception exception)
    {
        return exception switch
        {
            SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 } => true,
            DbUpdateException { InnerException: { } inner } => IsClaimSlotContention(inner),
            _ => false,
        };
    }

    private static PublicChatClaimUpdate Changed(int rowCount)
    {
        return rowCount switch
        {
            0 => new PublicChatClaimUpdate.OwnershipLost(),
            1 => new PublicChatClaimUpdate.Applied(),
            _ => throw new UnreachableException(
                $"A public chat claim transition changed {rowCount} rows."
            ),
        };
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left <= right ? left : right;
    }

    private static DateTime Max(DateTime left, DateTime right)
    {
        return left >= right ? left : right;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
    {
        return left >= right ? left : right;
    }

    private static DateTime SubtractOrMinimum(DateTime value, TimeSpan duration)
    {
        return duration.Ticks >= value.Ticks - DateTime.MinValue.Ticks
            ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
            : value - duration;
    }

    private static DateTimeOffset AddOrMaximum(DateTimeOffset value, TimeSpan duration)
    {
        return duration.Ticks >= DateTimeOffset.MaxValue.UtcTicks - value.UtcTicks
            ? DateTimeOffset.MaxValue
            : value.Add(duration);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
