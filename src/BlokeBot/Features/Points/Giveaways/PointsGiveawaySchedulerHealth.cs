using System.Data.Common;
using System.Diagnostics;
using BlokeBot.Eventing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.Giveaways;

internal enum PointsGiveawaySchedulerFailureClassification
{
    Terminal,
    Unexpected,
}

internal enum PointsGiveawaySchedulerOperation
{
    Schedule,
    Draw,
    Expire,
}

internal abstract record PointsGiveawaySchedulerUnhealthyReport
{
    private protected PointsGiveawaySchedulerUnhealthyReport() { }

    internal required PointsGiveawaySchedulerFailureClassification Classification { get; init; }

    internal required Exception Cause { get; init; }

    internal Type FailureType => Cause.GetType();

    private protected abstract void Seal();

    internal sealed record Rehydration : PointsGiveawaySchedulerUnhealthyReport
    {
        private protected override void Seal() { }
    }

    internal sealed record Giveaway : PointsGiveawaySchedulerUnhealthyReport
    {
        internal required int GiveawayId { get; init; }

        internal required PointsGiveawaySchedulerOperation Operation { get; init; }

        private protected override void Seal() { }
    }
}

internal sealed class PointsGiveawaySchedulerUnhealthyException(
    PointsGiveawaySchedulerUnhealthyReport report
) : Exception("The points giveaway scheduler is unhealthy.", report.Cause)
{
    internal PointsGiveawaySchedulerUnhealthyReport Report { get; } = report;
}

internal static class PointsGiveawaySchedulerFailureClassifier
{
    internal static bool IsTransient(Exception exception)
    {
        return exception switch
        {
            SqliteException sqliteException => IsTransient(sqliteException),
            DbUpdateException { InnerException: SqliteException sqliteException } => IsTransient(
                sqliteException
            ),
            TimeoutException => true,
            _ => false,
        };
    }

    internal static bool IsNotificationFailure(Exception exception)
    {
        return IsTransient(exception)
            || exception
                is HttpRequestException
                    or IOException
                    or ObserverFanOutEscalationException
                    or OperationCanceledException;
    }

    internal static PointsGiveawaySchedulerFailureClassification ClassifyUnhealthy(
        Exception exception
    )
    {
        return
            exception
                is ArgumentException
                    or InvalidOperationException
                    or NotSupportedException
                    or UnreachableException
                    or DbException
                    or DbUpdateException
                    or PointsGiveawayDrawCommitAmbiguousException
                    or PointsGiveawayDrawPostCommitException
                    or PointsGiveawayExpirationCommitAmbiguousException
                    or PointsGiveawayExpirationPostCommitException
            ? PointsGiveawaySchedulerFailureClassification.Terminal
            : PointsGiveawaySchedulerFailureClassification.Unexpected;
    }

    private static bool IsTransient(SqliteException exception)
    {
        return exception.SqliteErrorCode
            is SQLitePCL.raw.SQLITE_BUSY
                or SQLitePCL.raw.SQLITE_LOCKED;
    }
}
