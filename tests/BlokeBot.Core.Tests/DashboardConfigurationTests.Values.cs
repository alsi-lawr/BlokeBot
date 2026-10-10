using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardConfigurationTests
{
    private static DashboardValueReader ValueReader(
        Fixture fixture,
        CustomStoredValueChanges changes,
        ICustomCommandViewerResolver? resolver = null
    ) =>
        new(
            fixture.Database,
            new(fixture.Database, fixture.Moderator),
            new(fixture.Database, changes),
            resolver ?? new ViewerResolver(),
            changes,
            NullLogger<DashboardValueReader>.Instance
        );

    private static DashboardWidgetConfiguration.Value ValueConfiguration(
        CustomValueDefinitionDraft definition,
        string? key = null
    ) =>
        new(
            Configured(new DashboardTarget.Value(definition.Id)),
            key is null
                ? new DashboardValueSelection.Scalar()
                : new DashboardValueSelection.DictionaryEntry(key)
        );

    private static async Task<DashboardValueState.Available> DisplayAsync(
        DashboardValueSession session
    )
    {
        await session.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return session.State.ShouldBeOfType<DashboardValueState.Available>();
    }

    [Test]
    public async Task DashboardValues_CommittedEditsDefaultsResetAndDeletionRefreshOnlyBoundTargets()
    {
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync()
        );
        var changes = StoredValueTestChanges.Create();
        var values = new CustomStoredValueService(fixture.Database, changes);
        var scalar = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "Total",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "3"
        );
        var dictionary = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "Labels",
            CustomValueScope.Global,
            CustomValueKind.Dictionary,
            "fallback"
        );
        var reader = ValueReader(fixture, changes);
        int scalarUpdates = 0,
            selectedUpdates = 0,
            otherUpdates = 0;
        using var scalarSession = reader.CreateSession(() =>
        {
            scalarUpdates++;
            return ValueTask.CompletedTask;
        });
        using var selected = reader.CreateSession(() =>
        {
            selectedUpdates++;
            return ValueTask.CompletedTask;
        });
        using var other = reader.CreateSession(() =>
        {
            otherUpdates++;
            return ValueTask.CompletedTask;
        });
        await scalarSession.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(scalar), _ct);
        await selected.BindAsync(
            fixture.Owner,
            fixture.Host,
            ValueConfiguration(dictionary, "one"),
            _ct
        );
        await other.BindAsync(
            fixture.Owner,
            fixture.Host,
            ValueConfiguration(dictionary, "two"),
            _ct
        );
        (await DisplayAsync(scalarSession)).Value.Value.ShouldBe("3");
        (await DisplayAsync(selected)).Value.Value.ShouldBe("fallback");
        var initialOther = otherUpdates;
        (
            _ = await values.SaveValueAsync(
                fixture.Host.Value,
                (await DisplayAsync(selected)).Value,
                CustomValueKind.Number,
                "12",
                _ct
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        (await DisplayAsync(selected)).Value.Kind.ShouldBe(CustomValueKind.Number);
        (await DisplayAsync(selected)).Value.Value.ShouldBe("12");
        otherUpdates.ShouldBe(initialOther);
        scalarUpdates.ShouldBe(1);
        (
            await values.ResetValueAsync(
                fixture.Host.Value,
                (await DisplayAsync(selected)).Value,
                _ct
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        (await DisplayAsync(selected)).Value.Value.ShouldBe("fallback");
        (await DisplayAsync(selected)).Value.Kind.ShouldBe(CustomValueKind.Text);
        (
            await values.SaveDefinitionAsync(
                fixture.Host.Value,
                dictionary with
                {
                    Name = "Names",
                    Default = "new default",
                },
                _ct
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        (await DisplayAsync(selected)).Definition.Name.ShouldBe("Names");
        (await DisplayAsync(selected)).Value.Value.ShouldBe("new default");
        (await DisplayAsync(other)).Value.Value.ShouldBe("new default");
        (
            _ = await values.SaveValueAsync(
                fixture.Host.Value,
                (await DisplayAsync(scalarSession)).Value,
                CustomValueKind.Number,
                "88",
                _ct
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        (
            await values.SaveDefinitionAsync(fixture.Host.Value, scalar with { Default = "7" }, _ct)
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        (await DisplayAsync(scalarSession)).Value.Value.ShouldBe("88");
        var current = (await values.DefinitionAsync(fixture.Host.Value, scalar.Id, _ct))!;
        var deletion = await values.AffectedValuesAsync(fixture.Host.Value, scalar.Id, _ct);
        (
            await values.DeleteDefinitionAsync(fixture.Host.Value, current, deletion, _ct)
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        await scalarSession.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        scalarSession
            .State.ShouldBeOfType<DashboardValueState.Unavailable>()
            .Reason.ShouldBe(DashboardValueUnavailableReason.Missing);
        (await DisplayAsync(other)).Value.Value.ShouldBe("new default");
        selectedUpdates.ShouldBeGreaterThan(1);
    }

    [Test]
    public async Task DashboardValues_ViewerSelectionIsPerSessionAndLateResolutionCannotRebindHost()
    {
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync()
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
        var resolver = new ViewerResolver();
        using var first = ValueReader(fixture, changes, resolver)
            .CreateSession(() => ValueTask.CompletedTask);
        using var second = ValueReader(fixture, changes)
            .CreateSession(() => ValueTask.CompletedTask);
        await first.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(definition), _ct);
        first
            .State.ShouldBeOfType<DashboardValueState.Unavailable>()
            .Reason.ShouldBe(DashboardValueUnavailableReason.ViewerRequired);
        await second.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(definition), _ct);
        await second.SelectViewerAsync("second", _ct);
        resolver.Delay = true;
        var old = first.SelectViewerAsync("old", _ct);
        await resolver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        resolver.Delay = false;
        await first.SelectViewerAsync("first", _ct);
        resolver.Release.SetResult(new CustomCommandViewerResolution.Found(new("old-id", "old")));
        await old;
        (await DisplayAsync(first)).Value.Target.ViewerId.ShouldBe("first-id");
        (await DisplayAsync(second)).Value.Target.ViewerId.ShouldBe("second-id");
        (
            _ = await values.SaveValueAsync(
                fixture.Host.Value,
                (await DisplayAsync(first)).Value,
                CustomValueKind.Number,
                "15",
                _ct
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        (await DisplayAsync(first)).Value.Value.ShouldBe("15");
        (await DisplayAsync(second)).Value.Value.ShouldBe("0");
        await first.BindAsync(
            Session(fixture.Other),
            fixture.Other,
            ValueConfiguration(definition),
            _ct
        );
        first.Viewer.ShouldBeNull();
        first
            .State.ShouldBeOfType<DashboardValueState.Unavailable>()
            .Reason.ShouldBe(DashboardValueUnavailableReason.Missing);
        await second.SelectViewerAsync("missing", _ct);
        second
            .State.ShouldBeOfType<DashboardValueState.Unavailable>()
            .Reason.ShouldBe(DashboardValueUnavailableReason.ViewerNotFound);
        _ = await values.SaveValueAsync(
            fixture.Host.Value,
            (
                await values.ValueAsync(
                    fixture.Host.Value,
                    new(definition.Id, "second-id", ""),
                    _ct
                )
            )!,
            CustomValueKind.Number,
            "9",
            _ct
        );
        second
            .State.ShouldBeOfType<DashboardValueState.Unavailable>()
            .Reason.ShouldBe(DashboardValueUnavailableReason.ViewerNotFound);
    }

    [Test]
    public async Task DashboardValues_DisposalAndFailingObserverDoNotEraseIndependentDisplays()
    {
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
        bool fail = false;
        int disposedUpdates = 0;
        using var failing = ValueReader(fixture, changes)
            .CreateSession(() =>
                fail
                    ? ValueTask.FromException(new IOException("Finite failing widget"))
                    : ValueTask.CompletedTask
            );
        using var healthy = ValueReader(fixture, changes)
            .CreateSession(() => ValueTask.CompletedTask);
        var disposed = ValueReader(fixture, changes)
            .CreateSession(() =>
            {
                disposedUpdates++;
                return ValueTask.CompletedTask;
            });
        await failing.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(definition), _ct);
        await healthy.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(definition), _ct);
        await disposed.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(definition), _ct);
        disposed.Dispose();
        fail = true;
        (
            _ = await values.SaveValueAsync(
                fixture.Host.Value,
                (await DisplayAsync(healthy)).Value,
                CustomValueKind.Number,
                "6",
                _ct
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        (await DisplayAsync(healthy)).Value.Value.ShouldBe("6");
        disposedUpdates.ShouldBe(1);
        (await DisplayAsync(disposed)).Value.Value.ShouldBe("0");
        fixture.Moderator.Outcome =
            new BlokeBot.Core.Auth.Moderation.ModeratorAuthorityOutcome.Revoked();
        using var denied = ValueReader(fixture, changes)
            .CreateSession(() => ValueTask.CompletedTask);
        await denied.BindAsync(
            Session(fixture.Host, BlokeBot.Core.Auth.Sessions.AuthRole.Moderator),
            fixture.Host,
            ValueConfiguration(definition),
            _ct
        );
        denied
            .State.ShouldBeOfType<DashboardValueState.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Revoked);
        (await DisplayAsync(healthy)).Value.Value.ShouldBe("6");
    }

    private sealed class ViewerResolver : ICustomCommandViewerResolver
    {
        internal bool Delay { get; set; }
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<CustomCommandViewerResolution> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CustomCommandViewerResolution> ResolveAsync(string login, CancellationToken ct)
        {
            if (Delay)
            {
                _ = Entered.TrySetResult();
                return Release.Task;
            }
            return Task.FromResult<CustomCommandViewerResolution>(
                login == "missing"
                    ? new CustomCommandViewerResolution.NotFound()
                    : new CustomCommandViewerResolution.Found(new($"{login}-id", login))
            );
        }
    }
}
