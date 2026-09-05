using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.ViewerPortal;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPortalFailureIsolationTests
{
    [Test]
    public async Task FailedFeature_DoesNotHideSuccessfulOwnerDataOrDisturbCatalogueOrder()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var context = new ViewerPortalTestContext(database, new FailedDatabase());
        var host = await context.HostAsync(
            "alpha",
            HostFeatureFlags.Bingo | HostFeatureFlags.Points
        );
        await context.PointsAsync(host, "viewer", "20");

        var snapshot = await context.Catalogue.ReadAsync(
            await context.ChannelAsync("alpha"),
            new PortalIdentity.Anonymous(),
            default
        );

        _ = snapshot.Features[0].Outcome.ShouldBeOfType<PortalSummaryOutcome.Unavailable>();
        snapshot
            .Features[0]
            .Descriptor.GetFallbackLink(snapshot.Host, new PortalIdentity.Anonymous())
            .ShouldNotBeNull()
            .Href.ShouldBe("/bingo/alpha");
        snapshot
            .Features[1]
            .Outcome.ShouldBeOfType<PortalSummaryOutcome.Available>()
            .Summary.Headline.ShouldBe("viewer");
        snapshot.RecentActivity.ShouldBeEmpty();
    }

    [Test]
    public async Task TimedOutOwner_DoesNotBlockSuccessfulFeature()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var blocked = new BlockedDatabase(database);
        var context = new ViewerPortalTestContext(database, blocked);
        var host = await context.HostAsync(
            "alpha",
            HostFeatureFlags.Bingo | HostFeatureFlags.Points
        );
        await context.PointsAsync(host, "viewer", "20");

        var snapshot = await context.Catalogue.ReadAsync(
            await context.ChannelAsync("alpha"),
            new PortalIdentity.Anonymous(),
            default
        );

        _ = snapshot.Features[0].Outcome.ShouldBeOfType<PortalSummaryOutcome.Unavailable>();
        _ = snapshot.Features[1].Outcome.ShouldBeOfType<PortalSummaryOutcome.Available>();
        await blocked.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task CallerCancellation_RemainsCancellationRatherThanAFeatureFailure()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var blocked = new BlockedDatabase(database);
        var context = new ViewerPortalTestContext(database, blocked);
        _ = await context.HostAsync("alpha", HostFeatureFlags.Bingo);
        using var cancellation = new CancellationTokenSource();
        var read = context.Catalogue.ReadAsync(
            await context.ChannelAsync("alpha"),
            new PortalIdentity.Anonymous(),
            cancellation.Token
        );
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await cancellation.CancelAsync();

        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await read);
    }

    [Test]
    public async Task HostRecreatedDuringOwnerReads_DiscardsRetainedKeyResultsAndAdmitsNewlyResolvedKey()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var blocked = new BlockedDatabase(database);
        var context = new ViewerPortalTestContext(database, blocked);
        var provisioning = new BotHostProvisioningService(
            database,
            context.Changes,
            [],
            context.Clock
        );
        var original = await provisioning.EnsureHostAsync(
            "alpha",
            "original-id",
            "Original",
            null,
            default
        );
        var features = TestHostFeatureServices.Create(database, context.Changes, [], context.Clock);
        _ = await features.EnableAsync(original, HostFeatureFlags.Bingo, default);
        _ = await features.EnableAsync(original, HostFeatureFlags.Points, default);
        var retained = await context.ChannelAsync("alpha");
        var reading = context.Catalogue.ReadAsync(
            retained,
            new PortalIdentity.Anonymous(),
            default
        );
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var directory = Directory.CreateTempSubdirectory("blokebot-portal-recreate-");
        try
        {
            var options = Options.Create(
                new BlokeBotOptions
                {
                    DatabasePath = Path.Combine(directory.FullName, "blokebot.db"),
                }
            );
            using var maintenance = new OverlayMediaMaintenanceService(
                database,
                options,
                new SystemOverlayMediaFileDeletion(),
                context.Clock,
                NullLogger<OverlayMediaMaintenanceService>.Instance
            );
            var removal = new BotHostRemovalService(
                database,
                context.Changes,
                options,
                maintenance,
                context.Clock,
                NullLogger<BotHostRemovalService>.Instance
            );
            (await removal.RemoveAsync(original, default)).Removed.ShouldBeTrue();
            var replacement = await provisioning.EnsureHostAsync(
                "alpha",
                "replacement-id",
                "Replacement",
                null,
                default
            );
            replacement.ShouldNotBe(original);
            _ = await features.EnableAsync(replacement, HostFeatureFlags.Bingo, default);
            _ = await features.EnableAsync(replacement, HostFeatureFlags.Points, default);
            await context.PointsAsync(replacement, "replacement-viewer", "50");
            blocked.Release.TrySetResult().ShouldBeTrue();

            (await reading).Features.ShouldBeEmpty();
            var fresh = await context.Catalogue.ReadAsync(
                await context.ChannelAsync("alpha"),
                new PortalIdentity.Anonymous(),
                default
            );
            fresh
                .Features.Single(value => value.Descriptor.Feature == HostFeatureFlags.Points)
                .Outcome.ShouldBeOfType<PortalSummaryOutcome.Available>()
                .Summary.Headline.ShouldBe("replacement-viewer");
        }
        finally
        {
            _ = blocked.Release.TrySetResult();
            directory.Delete(recursive: true);
        }
    }

    private sealed class FailedDatabase : IDbContextFactory<BlokeBotDbContext>
    {
        public BlokeBotDbContext CreateDbContext() =>
            throw new IOException("Private database connection failure");

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) =>
            Task.FromException<BlokeBotDbContext>(
                new IOException("Private database connection failure")
            );
    }

    private sealed class BlockedDatabase(SqliteBlokeBotDbFactory database)
        : IDbContextFactory<BlokeBotDbContext>
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlokeBotDbContext CreateDbContext() => database.CreateDbContext();

        public async Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            _ = Started.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _ = Cancelled.TrySetResult();
                throw;
            }
            return await database.CreateDbContextAsync(cancellationToken);
        }
    }
}
