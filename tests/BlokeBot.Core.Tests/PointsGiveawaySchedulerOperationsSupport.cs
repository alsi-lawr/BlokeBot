using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public abstract partial class PointsGiveawaySchedulerTestBase
{
    private protected sealed class RecordingSchedulerOperations : IPointsGiveawaySchedulerOperations
    {
        public IReadOnlyList<PointsGiveawaySchedule> Active { get; init; } = [];

        public Action? BeforeLoadResult { get; init; }

        public Exception? DrawException { get; init; }

        public Queue<
            Result<IReadOnlyList<PointsGiveawaySchedule>, PointsGiveawaySchedulerTransientFailure>
        > LoadOutcomes { get; } = [];

        public Queue<
            Result<Option<string>, PointsGiveawaySchedulerNotificationFailure>
        > UpdateOutcomes { get; } = [];

        public Queue<
            Result<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerTransientFailure>
        > DrawOutcomes { get; } = [];

        public Queue<
            Result<Option<string>, PointsGiveawaySchedulerNotificationFailure>
        > DrawNotificationOutcomes { get; } = [];

        public Queue<
            Result<PointsGiveawayExpirationOutcome, PointsGiveawaySchedulerTransientFailure>
        > ExpirationOutcomes { get; } = [];

        public Queue<
            Result<
                PointsGiveawayChangeNotificationCompleted,
                PointsGiveawaySchedulerNotificationFailure
            >
        > ChangeNotificationOutcomes { get; } = [];

        public int LoadAttempts { get; private set; }

        public int UpdateAttempts { get; private set; }

        public int DrawAttempts { get; private set; }

        public int DrawNotificationAttempts { get; private set; }

        public int ExpirationAttempts { get; private set; }

        public int ChangeNotificationAttempts { get; private set; }

        public IO<
            IReadOnlyList<PointsGiveawaySchedule>,
            PointsGiveawaySchedulerTransientFailure
        > LoadActive()
        {
            return IO<
                IReadOnlyList<PointsGiveawaySchedule>,
                PointsGiveawaySchedulerTransientFailure
            >.Create(_ =>
            {
                LoadAttempts++;
                BeforeLoadResult?.Invoke();
                return ValueTask.FromResult(Next(LoadOutcomes, Active));
            });
        }

        public IO<Option<string>, PointsGiveawaySchedulerNotificationFailure> BuildUpdate(
            int giveawayId,
            DateTime endsAtUtc
        )
        {
            return IO<Option<string>, PointsGiveawaySchedulerNotificationFailure>.Create(_ =>
            {
                UpdateAttempts++;
                return ValueTask.FromResult(Next(UpdateOutcomes, Option<string>.None));
            });
        }

        public IO<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerTransientFailure> Draw(
            int giveawayId
        )
        {
            return IO<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerTransientFailure>.Create(
                _ =>
                {
                    DrawAttempts++;
                    if (DrawException is { } exception)
                    {
                        return ValueTask.FromException<
                            Result<
                                PointsGiveawayDrawOutcome,
                                PointsGiveawaySchedulerTransientFailure
                            >
                        >(exception);
                    }

                    return ValueTask.FromResult(
                        Next(DrawOutcomes, new PointsGiveawayDrawOutcome.Missing())
                    );
                }
            );
        }

        public IO<Option<string>, PointsGiveawaySchedulerNotificationFailure> BuildDrawNotification(
            PointsGiveawayDrawOutcome outcome
        )
        {
            return IO<Option<string>, PointsGiveawaySchedulerNotificationFailure>.Create(_ =>
            {
                DrawNotificationAttempts++;
                return ValueTask.FromResult(Next(DrawNotificationOutcomes, Option<string>.None));
            });
        }

        public IO<PointsGiveawayExpirationOutcome, PointsGiveawaySchedulerTransientFailure> Expire(
            int giveawayId
        )
        {
            return IO<
                PointsGiveawayExpirationOutcome,
                PointsGiveawaySchedulerTransientFailure
            >.Create(_ =>
            {
                ExpirationAttempts++;
                return ValueTask.FromResult(
                    Next(ExpirationOutcomes, PointsGiveawayExpirationOutcome.Expired)
                );
            });
        }

        public IO<
            PointsGiveawayChangeNotificationCompleted,
            PointsGiveawaySchedulerNotificationFailure
        > NotifyChanged()
        {
            return IO<
                PointsGiveawayChangeNotificationCompleted,
                PointsGiveawaySchedulerNotificationFailure
            >.Create(_ =>
            {
                ChangeNotificationAttempts++;
                return ValueTask.FromResult(
                    Next(
                        ChangeNotificationOutcomes,
                        new PointsGiveawayChangeNotificationCompleted()
                    )
                );
            });
        }

        private static Result<TValue, TError> Next<TValue, TError>(
            Queue<Result<TValue, TError>> outcomes,
            TValue defaultValue
        )
        {
            return outcomes.TryDequeue(out var outcome)
                ? outcome
                : Result<TValue, TError>.Success(defaultValue);
        }
    }
}
