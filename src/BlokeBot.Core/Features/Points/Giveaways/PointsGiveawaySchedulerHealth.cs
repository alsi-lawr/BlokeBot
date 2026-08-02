using System.Data.Common;
using System.Diagnostics;
using BlokeBot.Eventing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.Giveaways;

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
    private PointsGiveawaySchedulerUnhealthyReport() { }

    internal required PointsGiveawaySchedulerFailureClassification Classification { get; init; }

    internal required Exception Cause { get; init; }

    internal Type FailureType => Cause.GetType();

    internal sealed record Rehydration : PointsGiveawaySchedulerUnhealthyReport;

    internal sealed record Giveaway : PointsGiveawaySchedulerUnhealthyReport
    {
        internal required int GiveawayId { get; init; }

        internal required PointsGiveawaySchedulerOperation Operation { get; init; }
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
    internal static bool IsTransient(Exception exception) =>
        exception switch
        {
            SqliteException sqliteException => IsTransient(sqliteException),
            DbUpdateException { InnerException: SqliteException sqliteException } => IsTransient(
                sqliteException
            ),
            TimeoutException => true,
            _ => false,
        };

    internal static bool IsNotificationFailure(Exception exception) =>
        IsTransient(exception)
        || exception
            is HttpRequestException
                or IOException
                or ObserverFanOutEscalationException
                or OperationCanceledException;

    internal static PointsGiveawaySchedulerFailureClassification ClassifyUnhealthy(
        Exception exception
    ) =>
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

    private static bool IsTransient(SqliteException exception) =>
        exception.SqliteErrorCode is SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED;
}
