using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardConfigurationTests
{
    [Test]
    public async Task Sqlite_MigrationsAndTypedConfigurationRoundTrip() =>
        await MigrationsAndRoundTripAsync(await PointProviderFixture.SqliteFileAsync());

    [Test, Explicit]
    public async Task PostgreSql_MigrationsAndTypedConfigurationRoundTrip() =>
        await MigrationsAndRoundTripAsync(await PointProviderFixture.PostgreSqlAsync());

    private static async Task MigrationsAndRoundTripAsync(PointProviderFixture database)
    {
        await using (var current = database.CreateDbContext())
        {
            var connection = current.Database.GetConnectionString()!;
            var sqlite = current.Database.IsSqlite();
            _ = await current.Database.EnsureDeletedAsync();
            var options = new DbContextOptionsBuilder<BlokeBotDbContext>();
            _ = sqlite
                ? options.UseSqlite(connection)
                : options.UseNpgsql(
                    connection,
                    postgres => postgres.MigrationsAssembly("BlokeBot.Persistence.PostgreSql")
                );
            _ = options.AddInterceptors(new WeeklyAnnouncementMigrationInterceptor());
            await using var migrating = new BlokeBotDbContext(options.Options);
            await migrating.Database.MigrateAsync();
        }
        await using var fixture = await Fixture.CreateAsync(database);
        var first = await fixture.CreateAsync("First dashboard");
        var second = await fixture.CreateAsync("Second dashboard");
        var document = Document(
            new DashboardWidgetConfiguration.Automation(
                Configured(
                    new DashboardTarget.Automation(new(Guid.NewGuid()), new(Guid.NewGuid()))
                ),
                new DashboardTextOverride.Replace(string.Empty)
            ),
            new DashboardWidgetConfiguration.Automation(
                new DashboardBinding.Unconfigured(DashboardBindingIssue.Ambiguous, "Imported flow"),
                new DashboardTextOverride.Inherit()
            ),
            new DashboardWidgetConfiguration.Value(
                Configured(new DashboardTarget.Value(17)),
                new DashboardValueSelection.DictionaryEntry("tea")
            ),
            new DashboardWidgetConfiguration.Value(
                _unconfigured,
                new DashboardValueSelection.Scalar()
            ),
            new DashboardWidgetConfiguration.Countdown(
                Configured(new DashboardTarget.Countdown("Tea timer")),
                TimeSpan.FromMinutes(5)
            ),
            new DashboardWidgetConfiguration.Poll(Configured(new DashboardTarget.Poll(23))),
            new DashboardWidgetConfiguration.Prediction(
                Configured(new DashboardTarget.Prediction(31))
            ),
            new DashboardWidgetConfiguration.Giveaway(Configured(new DashboardTarget.Giveaway())),
            new DashboardWidgetConfiguration.Guessing(Configured(new DashboardTarget.Guessing(41)))
        );
        document = document with
        {
            Desktop = document.Desktop with { Columns = 16, CellHeight = 80 },
            Phone = document.Phone! with
            {
                Columns = 5,
                CellHeight = 96,
                Placements =
                [
                    .. document.Phone.Placements.Select(p =>
                        p with
                        {
                            Placement = p.Placement with
                            {
                                Column = 1,
                                Row = (p.Placement.Row * 2) + 1,
                                Height = 4,
                            },
                        }
                    ),
                ],
            },
        };
        var saved = Saved(
            await fixture.Service.SaveAsync(fixture.Owner, Save(fixture, first, document), _ct)
        );
        var loaded = await fixture.ReadAsync(first.Id);
        loaded.Id.ShouldBe(first.Id);
        loaded.Name.ShouldBe(saved.Name);
        loaded.Revision.ShouldBe(saved.Revision);
        DashboardArrangements.Equivalent(loaded.Document, document).ShouldBeTrue();
        loaded.Document.Widgets.Length.ShouldBe(document.Widgets.Length);
        for (var index = 0; index < document.Widgets.Length; index++)
        {
            loaded.Document.Widgets[index].ShouldBe(document.Widgets[index]);
        }
        (await fixture.ReadAsync(second.Id)).Document.Widgets.ShouldBeEmpty();
        Value(await fixture.Service.ListAsync(fixture.Owner, fixture.Host, _ct))
            .Select(dashboard => dashboard.Id)
            .ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }
}
