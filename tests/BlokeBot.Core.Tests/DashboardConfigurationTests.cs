using BlokeBot.Core.Features.Dashboards;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardConfigurationTests
{
    [Test]
    public async Task Sqlite_CompetingSavesPreserveWinnerAndLosingLocalDocument()
    {
        var interleaving = new SaveInterleaving();
        await CompetingSavesAsync(
            await PointProviderFixture.SqliteFileAsync(interleaving),
            interleaving
        );
    }

    [Test, Explicit]
    public async Task PostgreSql_CompetingSavesPreserveWinnerAndLosingLocalDocument()
    {
        var interleaving = new SaveInterleaving();
        await CompetingSavesAsync(
            await PointProviderFixture.PostgreSqlAsync(interleaving),
            interleaving
        );
    }

    private static async Task CompetingSavesAsync(
        PointProviderFixture database,
        SaveInterleaving interleaving
    )
    {
        await using var fixture = await Fixture.CreateAsync(database);
        var initial = await fixture.CreateAsync();
        interleaving.Coordinate = true;
        var first = Save(
            fixture,
            initial,
            Document(new DashboardWidgetConfiguration.Poll(_unconfigured)),
            "First"
        );
        var second = Save(
            fixture,
            initial,
            Document(new DashboardWidgetConfiguration.Giveaway(_unconfigured)),
            "Second"
        );
        var results = await Task.WhenAll(
            fixture.Service.SaveAsync(fixture.Owner, first, _ct),
            fixture.Service.SaveAsync(fixture.Owner, second, _ct)
        );
        var winner = results.OfType<DashboardSaveOutcome.Saved>().Single().Dashboard;
        var conflict = results.OfType<DashboardSaveOutcome.Conflict>().Single();
        conflict.Attempted.ShouldBe(winner.Name == first.Name ? second : first);
        conflict
            .Attempted.Document.Widgets.Single()
            .Configuration.ShouldBe(
                winner.Name == first.Name
                    ? second.Document.Widgets.Single().Configuration
                    : first.Document.Widgets.Single().Configuration
            );
        conflict.Current.Name.ShouldBe(winner.Name);
        conflict.Current.Revision.ShouldBe(winner.Revision);
        var persisted = await fixture.ReadAsync(initial.Id);
        persisted.Name.ShouldBe(winner.Name);
        persisted.Revision.Value.ShouldBe(initial.Revision.Value + 1);
        persisted.Document.Widgets.Single().ShouldBe(winner.Document.Widgets.Single());
    }

    [Test]
    public async Task Sqlite_PostUpdateFailureRollsBackWholeAggregate() =>
        await RollbackAsync(
            await PointProviderFixture.SqliteFileAsync(
                new SaveInterleaving { FailAfterUpdate = true }
            )
        );

    [Test, Explicit]
    public async Task PostgreSql_PostUpdateFailureRollsBackWholeAggregate() =>
        await RollbackAsync(
            await PointProviderFixture.PostgreSqlAsync(
                new SaveInterleaving { FailAfterUpdate = true }
            )
        );

    private static async Task RollbackAsync(PointProviderFixture database)
    {
        await using var fixture = await Fixture.CreateAsync(database);
        var original = await fixture.CreateAsync();
        var command = Save(
            fixture,
            original,
            Document(new DashboardWidgetConfiguration.Prediction(_unconfigured)),
            "Not committed"
        );
        _ = await Should.ThrowAsync<IOException>(() =>
            fixture.Service.SaveAsync(fixture.Owner, command, _ct)
        );
        var current = await fixture.ReadAsync(original.Id);
        current.Name.ShouldBe(original.Name);
        current.Revision.ShouldBe(original.Revision);
        current.Document.Widgets.ShouldBeEmpty();
        command.Document.Widgets.ShouldNotBeEmpty();
    }

    [Test]
    public async Task DeleteAndStaleRenameCannotEraseNewerSavedWidgets()
    {
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync()
        );
        var original = await fixture.CreateAsync();
        var saved = Saved(
            await fixture.Service.SaveAsync(
                fixture.Owner,
                Save(
                    fixture,
                    original,
                    Document(new DashboardWidgetConfiguration.Guessing(_unconfigured))
                ),
                _ct
            )
        );
        (
            await fixture.Service.DeleteAsync(
                fixture.Owner,
                fixture.Host,
                original.Id,
                original.Revision,
                _ct
            )
        )
            .ShouldBeOfType<DashboardResult<DashboardId>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Conflict);
        (
            await fixture.Service.RenameAsync(
                fixture.Owner,
                fixture.Host,
                original.Id,
                original.Revision,
                "Stale",
                _ct
            )
        )
            .ShouldBeOfType<DashboardSaveOutcome.Conflict>()
            .Current.Revision.ShouldBe(saved.Revision);
        var renamed = Saved(
            await fixture.Service.RenameAsync(
                fixture.Owner,
                fixture.Host,
                saved.Id,
                saved.Revision,
                "Renamed",
                _ct
            )
        );
        renamed.Document.Widgets.Single().ShouldBe(saved.Document.Widgets.Single());
        Value(
                await fixture.Service.DeleteAsync(
                    fixture.Owner,
                    fixture.Host,
                    renamed.Id,
                    renamed.Revision,
                    _ct
                )
            )
            .ShouldBe(renamed.Id);
        var afterDelete = await fixture.Service.SaveAsync(
            fixture.Owner,
            Save(fixture, renamed, renamed.Document),
            _ct
        );
        afterDelete
            .ShouldBeOfType<DashboardSaveOutcome.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Missing);
        (await fixture.Service.GetAsync(fixture.Owner, fixture.Host, renamed.Id, _ct))
            .ShouldBeOfType<DashboardResult<DashboardSnapshot>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Missing);
    }

    [Test]
    public async Task DeletionBetweenReadAuthorizationAndQueryReturnsMissing()
    {
        var window = new ReadWindow();
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync(window)
        );
        var dashboard = await fixture.CreateAsync();
        window.Pause = true;
        var read = fixture.Service.GetAsync(fixture.Owner, fixture.Host, dashboard.Id, _ct);
        await window.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            Value(
                    await fixture.Service.DeleteAsync(
                        fixture.Owner,
                        fixture.Host,
                        dashboard.Id,
                        dashboard.Revision,
                        _ct
                    )
                )
                .ShouldBe(dashboard.Id);
        }
        finally
        {
            window.Continue.SetResult();
        }
        (await read)
            .ShouldBeOfType<DashboardResult<DashboardSnapshot>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Missing);
    }
}
