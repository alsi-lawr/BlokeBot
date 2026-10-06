using System.Data.Common;
using System.Globalization;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class RequestBoardServiceTests
{
    [Test]
    public async Task Sqlite_RefundRangeRejectionRollsBackClosureEventsBalanceAndLedger() =>
        await RefundRangeAsync(await PointProviderFixture.SqliteAsync());

    [Test, Explicit]
    public async Task PostgreSql_RefundRangeRejectionRollsBackClosureEventsBalanceAndLedger() =>
        await RefundRangeAsync(await PointProviderFixture.PostgreSqlAsync());

    [Test, Explicit]
    public async Task PostgreSql_ConcurrentReservationForcesFreshCreditExposureAndRefundsOnce()
    {
        var interleaving = new ReservationInterleaving();
        await using var database = await PointProviderFixture.PostgreSqlAsync(interleaving);
        var hostId = await database.SeedHostAsync();
        await using (var seed = database.CreateDbContext())
        {
            (await seed.Hosts.SingleAsync()).EnabledFeatures = HostFeatureFlags.All;
            _ = seed.PointBalances.Add(
                new()
                {
                    HostId = hostId,
                    Login = "viewer",
                    Amount = (PointAmount.MaximumValue - 25).ToString(CultureInfo.InvariantCulture),
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var boards = new RequestBoardService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = Success(await boards.ConfigureAsync(hostId, Board(pointCost: "25"), default));
        PublicRequestSubmissionView? reserved = null;
        interleaving.Reserve = async () =>
            reserved = Success(
                await boards.SubmitAsync(
                    hostId,
                    "games",
                    Submission(Guid.NewGuid(), "viewer", "Concurrent request"),
                    default
                )
            ).Value;
        var credit = await new PointBalanceService(database)
            .Add(hostId, "viewer", new(50), "streamer", "concurrent reservation")
            .ExecuteAsync(default);
        credit
            .Match(_ => false, failure => failure is PointBalanceMutationFailure.CapExceeded)
            .ShouldBeTrue();
        interleaving.SnapshotReads.ShouldBe(2);
        await using (var verify = database.CreateDbContext())
        {
            (await verify.PointBalances.SingleAsync()).Amount.ShouldBe(
                (PointAmount.MaximumValue - 50).ToString(CultureInfo.InvariantCulture)
            );
            (await verify.PointLedgerEntries.CountAsync()).ShouldBe(1);
            (await verify.RequestSubmissions.SingleAsync()).PointReservationState.ShouldBe(
                RequestPointReservationState.Reserved
            );
        }
        _ = Success(
            await boards.WithdrawAsync(
                hostId,
                reserved!.Id,
                RequestBoardTestActor.ForLogin("viewer"),
                default
            )
        );
        _ = Success(
            await boards.WithdrawAsync(
                hostId,
                reserved.Id,
                RequestBoardTestActor.ForLogin("viewer"),
                default
            )
        );
        await using var final = database.CreateDbContext();
        (await final.PointBalances.SingleAsync()).Amount.ShouldBe(
            (PointAmount.MaximumValue - 25).ToString(CultureInfo.InvariantCulture)
        );
        (
            await final.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.RequestRefund
            )
        ).ShouldBe(1);
    }

    private sealed class ReservationInterleaving : DbCommandInterceptor
    {
        internal Func<Task>? Reserve { get; set; }
        internal int SnapshotReads { get; private set; }
        private bool _reserved;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Reserve is null
                || !command.CommandText.Contains("bounty_pledges", StringComparison.Ordinal)
            )
            {
                return result;
            }
            SnapshotReads++;
            if (!_reserved)
            {
                _reserved = true;
                await Reserve();
            }
            return result;
        }
    }

    private static async Task RefundRangeAsync(PointProviderFixture database)
    {
        await using var owned = database;
        var hostId = await database.SeedHostAsync();
        await using (var seed = database.CreateDbContext())
        {
            (await seed.Hosts.SingleAsync()).EnabledFeatures = HostFeatureFlags.All;
            _ = seed.PointBalances.Add(
                new()
                {
                    HostId = hostId,
                    Login = "viewer",
                    Amount = "100",
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var service = new RequestBoardService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = Success(await service.ConfigureAsync(hostId, Board(pointCost: "25"), default));
        var submitted = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "viewer", "Request"),
                default
            )
        ).Value;
        int events;
        await using (var drift = database.CreateDbContext())
        {
            (await drift.PointBalances.SingleAsync()).Amount = PointAmount.MaximumValue.ToString(
                CultureInfo.InvariantCulture
            );
            _ = await drift.SaveChangesAsync();
            events = await drift.RequestBoardEvents.CountAsync();
        }
        _ = Rejection(
                await service.WithdrawAsync(
                    hostId,
                    submitted.Id,
                    RequestBoardTestActor.ForLogin("viewer"),
                    default
                )
            )
            .ShouldBeOfType<RequestBoardRejection.Invalid>();
        await using var verify = database.CreateDbContext();
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe(
            PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture)
        );
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(1);
        (await verify.RequestBoardEvents.CountAsync()).ShouldBe(events);
        var unchanged = await verify.RequestSubmissions.SingleAsync();
        unchanged.Status.ShouldBe(submitted.Status);
        unchanged.PointReservationState.ShouldBe(RequestPointReservationState.Reserved);
    }
}
