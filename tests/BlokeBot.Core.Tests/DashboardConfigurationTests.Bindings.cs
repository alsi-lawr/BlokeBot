using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardConfigurationTests
{
    [Test]
    public async Task ForeignAndDeletedTargetsRemainNonOperationalWithoutLosingWidgets()
    {
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync()
        );
        await using var db = fixture.Database.CreateDbContext();
        var local = new TwitchPollTemplate
        {
            HostId = fixture.Host.Value,
            Title = "Local",
            DurationSeconds = 60,
        };
        var foreign = new TwitchPollTemplate
        {
            HostId = fixture.Other.Value,
            Title = "Foreign",
            DurationSeconds = 60,
        };
        _ = db.TwitchPollTemplates.Add(local);
        _ = db.TwitchPollTemplates.Add(foreign);
        _ = await db.SaveChangesAsync();
        var document = Document(
            new DashboardWidgetConfiguration.Poll(Configured(new DashboardTarget.Poll(local.Id))),
            new DashboardWidgetConfiguration.Poll(Configured(new DashboardTarget.Poll(foreign.Id))),
            new DashboardWidgetConfiguration.Poll(
                new DashboardBinding.Unconfigured(DashboardBindingIssue.Ambiguous, "Import")
            )
        );
        var dashboard = await fixture.CreateAsync();
        var saved = Saved(
            await fixture.Service.SaveAsync(fixture.Owner, Save(fixture, dashboard, document), _ct)
        );
        var states = Value(
            await fixture.Bindings.ReadAsync(fixture.Owner, fixture.Host, document, _ct)
        );
        states[0].Availability.ShouldBe(DashboardBindingAvailability.Ready);
        states[1].Availability.ShouldBe(DashboardBindingAvailability.Missing);
        states[2].Availability.ShouldBe(DashboardBindingAvailability.Unconfigured);
        _ = await db
            .TwitchPollTemplates.Where(template => template.Id == local.Id)
            .ExecuteDeleteAsync();
        states = Value(
            await fixture.Bindings.ReadAsync(fixture.Owner, fixture.Host, document, _ct)
        );
        states[0].Availability.ShouldBe(DashboardBindingAvailability.Missing);
        var persisted = await fixture.ReadAsync(saved.Id);
        persisted.Document.Widgets.ShouldBe(document.Widgets);
        _ = await db
            .Hosts.Where(host => host.Id == fixture.Host.Value)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(host => host.EnabledFeatures, HostFeatureFlags.None)
            );
        states = Value(
            await fixture.Bindings.ReadAsync(fixture.Owner, fixture.Host, document, _ct)
        );
        states[0].Availability.ShouldBe(DashboardBindingAvailability.FeatureDisabled);
        states[2].Availability.ShouldBe(DashboardBindingAvailability.Unconfigured);
        var retained = Saved(
            await fixture.Service.SaveAsync(
                fixture.Owner,
                Save(fixture, persisted, document, "Still available"),
                _ct
            )
        );
        retained.Document.Widgets.ShouldBe(document.Widgets);
        Value(await fixture.Service.ListAsync(fixture.Owner, fixture.Host, _ct)).ShouldNotBeEmpty();
    }
}
