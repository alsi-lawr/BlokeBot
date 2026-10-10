using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardConfigurationTests
{
    [Test]
    public async Task DashboardValueUi_EventsReturnBeforeStalledRead_HiddenResyncAndRebindRejectOldRead()
    {
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync()
        );
        var changes = StoredValueTestChanges.Create();
        var values = new CustomStoredValueService(fixture.Database, changes);
        var first = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "First",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "1"
        );
        var second = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "Second",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "2"
        );
        var factory = new PausableDashboardDbFactory(fixture.Database);
        var reader = new DashboardValueReader(
            factory,
            new(factory, fixture.Moderator),
            new(factory, changes),
            new ViewerResolver(),
            changes,
            NullLogger<DashboardValueReader>.Instance
        );
        var updates = 0;
        using var session = reader.CreateSession(() =>
        {
            _ = Interlocked.Increment(ref updates);
            return ValueTask.CompletedTask;
        });
        await session.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(first), _ct);
        factory.PauseNext();
        await changes
            .PublishCommittedAsync([
                new CustomStoredValueChange.Definition(fixture.Host.Value, first.Id),
            ])
            .WaitAsync(TimeSpan.FromSeconds(5));
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The old read is stalled, not the event publisher or a new binding.
        await session.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(second), _ct);
        factory.Release.SetResult();
        await session.WhenIdleAsync();
        (await DisplayAsync(session)).Definition.Id.ShouldBe(second.Id);
        (await DisplayAsync(session)).Value.Value.ShouldBe("2");
        session.SetVisible(false);
        var hiddenReads = factory.Reads;
        for (var i = 0; i < 50; i++)
        {
            await changes.PublishCommittedAsync([
                new CustomStoredValueChange.Definition(fixture.Host.Value, second.Id),
            ]);
        }
        await session.WhenIdleAsync();
        factory.Reads.ShouldBe(hiddenReads);
        session.SetVisible(true);
        await session.WhenIdleAsync();
        factory.Reads.ShouldBeGreaterThan(hiddenReads);
        var resumedReads = factory.Reads;
        await Task.Delay(50, _ct);
        factory.Reads.ShouldBe(resumedReads);
        var beforeDispose = updates;
        session.Dispose();
        await changes.PublishCommittedAsync([
            new CustomStoredValueChange.Definition(fixture.Host.Value, second.Id),
        ]);
        await session.WhenIdleAsync();
        updates.ShouldBe(beforeDispose);
        factory.Reads.ShouldBe(resumedReads);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DashboardValueUi_SnapshottedOldCallbackCannotRefreshReboundOrDisposedSession(
        bool dispose
    )
    {
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync()
        );
        var changes = StoredValueTestChanges.Create();
        var values = new CustomStoredValueService(fixture.Database, changes);
        var first = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "First",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "1"
        );
        var second = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "Second",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "2"
        );
        var factory = new PausableDashboardDbFactory(fixture.Database);
        var reader = new DashboardValueReader(
            factory,
            new(factory, fixture.Moderator),
            new(factory, changes),
            new ViewerResolver(),
            changes,
            NullLogger<DashboardValueReader>.Instance
        );
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var blocker = changes.Subscribe(
            new CustomStoredValueChange.Definition(fixture.Host.Value, first.Id),
            ObserverIdentity.Named("snapshot-first"),
            async (_, _) =>
            {
                entered.SetResult();
                await release.Task;
            }
        );
        var updates = 0;
        using var session = reader.CreateSession(() =>
        {
            _ = Interlocked.Increment(ref updates);
            return ValueTask.CompletedTask;
        });
        await session.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(first), _ct);
        var publication = changes.PublishCommittedAsync([
            new CustomStoredValueChange.Definition(fixture.Host.Value, first.Id),
        ]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (dispose)
        {
            session.Dispose();
        }
        else
        {
            await session.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(second), _ct);
        }
        var reads = factory.Reads;
        var callbacks = updates;
        release.SetResult();
        await publication.WaitAsync(TimeSpan.FromSeconds(5));
        await session.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        factory.Reads.ShouldBe(reads);
        updates.ShouldBe(callbacks);
        if (!dispose)
        {
            (await DisplayAsync(session)).Definition.Id.ShouldBe(second.Id);
        }
    }

    private sealed class PausableDashboardDbFactory(IDbContextFactory<BlokeBotDbContext> inner)
        : IDbContextFactory<BlokeBotDbContext>
    {
        private int _pause;
        private int _reads;
        internal int Reads => Volatile.Read(ref _reads);
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void PauseNext() => _ = Interlocked.Exchange(ref _pause, 1);

        public BlokeBotDbContext CreateDbContext() => inner.CreateDbContext();

        public async Task<BlokeBotDbContext> CreateDbContextAsync(CancellationToken ct = default)
        {
            _ = Interlocked.Increment(ref _reads);
            if (Interlocked.Exchange(ref _pause, 0) == 1)
            {
                Entered.SetResult();
                await Release.Task.WaitAsync(ct);
            }
            return await inner.CreateDbContextAsync(ct);
        }
    }
}
