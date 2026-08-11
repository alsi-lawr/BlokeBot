using System.Globalization;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointBalanceIOTests
{
    [Test]
    public async Task BalanceMutation_Creating_RemainsLazyUntilExecution()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var operation = new PointBalanceService(dbFactory).Add(
            hostId,
            "viewer",
            PointAmount.ParseAbsolute("10"),
            "streamer",
            "test"
        );

        await using (var before = await dbFactory.CreateDbContextAsync())
        {
            (await before.PointBalances.CountAsync()).ShouldBe(0);
        }

        var result = await operation.ExecuteAsync(CancellationToken.None);

        result.Match(static _ => true, static _ => false).ShouldBeTrue();
        await using var after = await dbFactory.CreateDbContextAsync();
        (await after.PointBalances.SingleAsync()).Amount.ShouldBe("10");
    }

    [Test]
    public async Task Add_CommittingPublishesTypedPointAwardWithStableLedgerKey()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using (var seed = await dbFactory.CreateDbContextAsync())
        {
            _ = seed.PointsSettings.Add(
                new PointsSettings { HostId = hostId, PointLabel = "beans" }
            );
            _ = await seed.SaveChangesAsync();
        }
        var presenter = new RecordingEventPresenter();

        var result = await new PointBalanceService(dbFactory, [presenter])
            .Add(hostId, "viewer", PointAmount.ParseAbsolute("10"), "streamer", "test")
            .ExecuteAsync(CancellationToken.None);

        result.Match(static _ => true, static _ => false).ShouldBeTrue();
        var presentation = presenter
            .Presentations.ShouldHaveSingleItem()
            .ShouldBeOfType<OverlayEventPresentation.PointAward>();
        presentation.HostId.ShouldBe(hostId);
        presentation.Recipient.ShouldBe("viewer");
        presentation.Amount.ShouldBe("10");
        presentation.PointLabel.ShouldBe("beans");
        long.Parse(presentation.SourceKey, CultureInfo.InvariantCulture).ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task CancelledBalanceMutation_Executing_PropagatesWithoutPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var operation = new PointBalanceService(dbFactory).Add(
            hostId,
            "viewer",
            PointAmount.ParseAbsolute("10"),
            "streamer",
            "test"
        );
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            operation.ExecuteAsync(cancellation.Token).AsTask()
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointBalances.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task UnexpectedPersistenceFault_Executing_PropagatesUnchanged()
    {
        var expected = new InvalidOperationException("unexpected persistence fault");
        var operation = new PointBalanceService(new ThrowingDbContextFactory(expected)).Add(
            1,
            "viewer",
            PointAmount.ParseAbsolute("10"),
            "streamer",
            "test"
        );

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() =>
            operation.ExecuteAsync(CancellationToken.None).AsTask()
        );

        thrown.ShouldBeSameAs(expected);
    }

    [Test]
    public async Task MaximumBalance_AddingPoints_ReturnsCapExceededWithoutMutation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using (var seed = await dbFactory.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "viewer",
                    Amount = PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture),
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        var result = await new PointBalanceService(dbFactory)
            .Add(hostId, "viewer", PointAmount.ParseAbsolute("1"), "streamer", "test")
            .ExecuteAsync(CancellationToken.None);

        _ = result
            .Match(
                static _ =>
                    throw new InvalidOperationException("Expected the point cap to reject add."),
                static failure => failure
            )
            .ShouldBeOfType<PointBalanceMutationFailure.CapExceeded>();
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointBalances.SingleAsync()).Amount.ShouldBe(
            PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture)
        );
        (await db.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task RefundableRequestReservation_ReducesAvailableCreditCapacity()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var reserved = new PointAmount(10);
        await using (var seed = await dbFactory.CreateDbContextAsync())
        {
            var board = new RequestBoard
            {
                HostId = hostId,
                Slug = "requests",
                Title = "Requests",
                PointCost = reserved.ToString(),
                RefundPolicy = RequestBoardRefundPolicy.RejectedOrWithdrawn,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.RequestBoards.Add(board);
            _ = seed.RequestSubmissions.Add(
                new RequestSubmission
                {
                    HostId = hostId,
                    Board = board,
                    OperationId = Guid.NewGuid(),
                    SubmitterLogin = "viewer",
                    Title = "Reserved request",
                    NormalizedTitle = "reserved request",
                    Status = RequestSubmissionStatus.Pending,
                    PointReservationState = RequestPointReservationState.Reserved,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "viewer",
                    Amount = (PointAmount.MaximumValue - reserved.Value).ToString(
                        CultureInfo.InvariantCulture
                    ),
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        var result = await new PointBalanceService(dbFactory)
            .Add(hostId, "viewer", new PointAmount(1), "streamer", "test")
            .ExecuteAsync(CancellationToken.None);

        _ = result
            .Match(
                static _ => throw new InvalidOperationException("Expected the credit to fail."),
                static failure => failure
            )
            .ShouldBeOfType<PointBalanceMutationFailure.CapExceeded>();
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task MissingBalance_Deleting_ReturnsUnknownUserWithoutMutation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);

        var result = await new PointBalanceService(dbFactory)
            .DeleteBalance(hostId, "missing", "streamer", "test")
            .ExecuteAsync(CancellationToken.None);

        _ = result
            .Match(
                static _ =>
                    throw new InvalidOperationException("Expected an unknown-user failure."),
                static failure => failure
            )
            .ShouldBeOfType<PointBalanceMutationFailure.UnknownUser>();
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointBalances.CountAsync()).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class ThrowingDbContextFactory(Exception exception)
        : IDbContextFactory<BlokeBotDbContext>
    {
        public BlokeBotDbContext CreateDbContext() => throw exception;

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromException<BlokeBotDbContext>(exception);
    }

    private sealed class RecordingEventPresenter : IOverlayEventPresenter
    {
        internal List<OverlayEventPresentation> Presentations { get; } = [];

        public Task PresentAsync(
            OverlayEventPresentation presentation,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Presentations.Add(presentation);
            return Task.CompletedTask;
        }
    }
}
