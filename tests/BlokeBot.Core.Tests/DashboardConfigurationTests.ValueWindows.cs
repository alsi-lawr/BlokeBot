using System.Data.Common;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardConfigurationTests
{
    [Test]
    public async Task DashboardValues_LateReadCannotReplaceNewViewerOrDisposedSession()
    {
        var window = new ValueReadWindow();
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync(window)
        );
        var changes = StoredValueTestChanges.Create();
        var values = new CustomStoredValueService(fixture.Database, changes);
        var definition = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "Score",
            CustomValueScope.User,
            CustomValueKind.Number,
            "0"
        );
        var first = (
            await values.ValueAsync(fixture.Host.Value, new(definition.Id, "first-id", ""), _ct)
        )!;
        _ = await values.SaveValueAsync(
            fixture.Host.Value,
            first,
            CustomValueKind.Number,
            "11",
            _ct
        );
        var second = (
            await values.ValueAsync(fixture.Host.Value, new(definition.Id, "second-id", ""), _ct)
        )!;
        _ = await values.SaveValueAsync(
            fixture.Host.Value,
            second,
            CustomValueKind.Number,
            "22",
            _ct
        );
        await SeedRecentViewersAsync(fixture);
        int updates = 0;
        using var display = ValueReader(fixture, changes)
            .CreateSession(() =>
            {
                updates++;
                return ValueTask.CompletedTask;
            });
        var readsBefore = window.ValueReads;
        await display.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(definition), _ct);
        await display.SelectViewerAsync(Viewer("missing"), _ct);
        window.ValueReads.ShouldBe(readsBefore);
        window.PauseNext = true;
        var stale = display.SelectViewerAsync(Viewer("first"), _ct);
        await window.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await display.SelectViewerAsync(Viewer("second"), _ct);
        (await DisplayAsync(display)).Value.Value.ShouldBe("22");
        window.Release.SetResult();
        await stale;
        (await DisplayAsync(display)).Value.Value.ShouldBe("22");
        var disposeWindow = new ValueReadWindow();
        await using var disposeDatabase = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync(disposeWindow)
        );
        await SeedRecentViewersAsync(disposeDatabase);
        var disposeValues = new CustomStoredValueService(disposeDatabase.Database, changes);
        var disposeDefinition = await CustomCommandExecutionTests.DeclareAsync(
            disposeValues,
            disposeDatabase.Host.Value,
            "Score",
            CustomValueScope.User,
            CustomValueKind.Number,
            "0"
        );
        var disposed = ValueReader(disposeDatabase, changes)
            .CreateSession(() =>
            {
                updates++;
                return ValueTask.CompletedTask;
            });
        await disposed.BindAsync(
            disposeDatabase.Owner,
            disposeDatabase.Host,
            ValueConfiguration(disposeDefinition),
            _ct
        );
        disposeWindow.PauseNext = true;
        var late = disposed.SelectViewerAsync(Viewer("first"), _ct);
        await disposeWindow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var beforeDispose = updates;
        disposed.Dispose();
        disposeWindow.Release.SetResult();
        await late;
        updates.ShouldBe(beforeDispose);
        disposed.Viewer.ShouldBeNull();
    }

    [Test]
    public async Task DashboardValues_LateReadCannotReplaceNewHostAndBinding()
    {
        var window = new ValueReadWindow();
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync(window)
        );
        var changes = StoredValueTestChanges.Create();
        var values = new CustomStoredValueService(fixture.Database, changes);
        var original = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "Original",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "11"
        );
        var other = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Other.Value,
            "Other",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "22"
        );
        using var display = ValueReader(fixture, changes)
            .CreateSession(() => ValueTask.CompletedTask);
        await display.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(original), _ct);
        window.PauseNext = true;
        var stale = display.RefreshAsync(_ct);
        await window.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await display.BindAsync(
            Session(fixture.Other),
            fixture.Other,
            ValueConfiguration(other),
            _ct
        );
        (await DisplayAsync(display)).Value.Value.ShouldBe("22");
        window.Release.SetResult();
        await stale;
        (await DisplayAsync(display)).Definition.Name.ShouldBe("Other");
        (await DisplayAsync(display)).Value.Value.ShouldBe("22");
        var oldValue = (
            await values.ValueAsync(fixture.Host.Value, new(original.Id, "", ""), _ct)
        )!;
        _ = await values.SaveValueAsync(
            fixture.Host.Value,
            oldValue,
            CustomValueKind.Number,
            "33",
            _ct
        );
        (await DisplayAsync(display)).Value.Value.ShouldBe("22");
    }

    [Test]
    public async Task DashboardValues_PostCommitCancellationAndObserverFailureCannotRelabelSavedEdit()
    {
        var cancellation = new CancellationTokenSource();
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync()
        );
        var changes = StoredValueTestChanges.Create();
        var values = new CustomStoredValueService(fixture.Database, changes);
        var definition = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "Total",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "0"
        );
        var original = (
            await values.ValueAsync(fixture.Host.Value, new(definition.Id, "", ""), _ct)
        )!;
        using var canceller = changes.Subscribe(
            new CustomStoredValueChange.Target(fixture.Host.Value, original.Target),
            ObserverIdentity.Named("DashboardValues.Cancellation"),
            (_, _) =>
            {
                cancellation.Cancel();
                throw new IOException("Finite postcommit observer failure");
            }
        );
        using var display = ValueReader(fixture, changes)
            .CreateSession(() => ValueTask.CompletedTask);
        await display.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(definition), _ct);
        (
            await values.SaveValueAsync(
                fixture.Host.Value,
                original,
                CustomValueKind.Number,
                "44",
                cancellation.Token
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        (await DisplayAsync(display)).Value.Value.ShouldBe("44");
        (await values.ValueAsync(fixture.Host.Value, original.Target, _ct))!.Value.ShouldBe("44");
        cancellation.Dispose();
    }

    private sealed class ValueReadWindow : DbCommandInterceptor
    {
        internal bool PauseNext { get; set; }
        internal int ValueReads { get; private set; }
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData data,
            InterceptionResult<DbDataReader> result,
            CancellationToken ct = default
        )
        {
            if (
                command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("custom_stored_values", StringComparison.Ordinal)
            )
            {
                ValueReads++;
                if (PauseNext)
                {
                    PauseNext = false;
                    Entered.SetResult();
                    await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
                }
            }
            return result;
        }
    }
}
