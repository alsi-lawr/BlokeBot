using System.Data.Common;
using System.Diagnostics;
using BlokeBot.Eventing;
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

    internal required PointsGiveawaySchedulerFailureClassification Classification
    {
        get;
        init;
    }

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
    internal static bool IsTransient(Exception exception) =>
        exception is DbException or DbUpdateException or TimeoutException;

    internal static bool IsNotificationFailure(Exception exception) =>
        exception
            is DbException
                or DbUpdateException
                or HttpRequestException
                or IOException
                or ObserverFanOutEscalationException
                or OperationCanceledException
                or TimeoutException;

    internal static PointsGiveawaySchedulerFailureClassification ClassifyUnhealthy(
        Exception exception
    ) =>
        exception
            is ArgumentException
                or InvalidOperationException
                or NotSupportedException
                or UnreachableException
                or PointsGiveawayDrawCommitAmbiguousException
                or PointsGiveawayDrawPostCommitException
                or PointsGiveawayExpirationCommitAmbiguousException
                or PointsGiveawayExpirationPostCommitException
            ? PointsGiveawaySchedulerFailureClassification.Terminal
            : PointsGiveawaySchedulerFailureClassification.Unexpected;
}
