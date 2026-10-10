using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Dashboards;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardConfigurationTests
{
    [Test]
    public async Task CurrentModeratorOutcomeAndSelectedChannelFenceEveryConfigurationOperation()
    {
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync()
        );
        var original = await fixture.CreateAsync();
        var moderator = Session(fixture.Host, AuthRole.Moderator);
        var permitted = Saved(
            await fixture.Service.SaveAsync(
                moderator,
                Save(
                    fixture,
                    original,
                    Document(new DashboardWidgetConfiguration.Poll(_unconfigured))
                ),
                _ct
            )
        );
        fixture.Moderator.Outcome = new ModeratorAuthorityOutcome.Revoked();
        var revoked = await fixture.Service.SaveAsync(
            moderator,
            Save(fixture, permitted, DashboardDocument.Empty),
            _ct
        );
        revoked
            .ShouldBeOfType<DashboardSaveOutcome.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Revoked);
        (
            await fixture.Service.DeleteAsync(
                moderator,
                fixture.Host,
                permitted.Id,
                permitted.Revision,
                _ct
            )
        )
            .ShouldBeOfType<DashboardResult<DashboardId>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Revoked);
        (await fixture.Service.CreateAsync(moderator, fixture.Host, "Forbidden", _ct))
            .ShouldBeOfType<DashboardResult<DashboardSnapshot>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Revoked);
        fixture.Moderator.Outcome = new ModeratorAuthorityOutcome.Unavailable();
        (
            await fixture.Service.RenameAsync(
                moderator,
                fixture.Host,
                permitted.Id,
                permitted.Revision,
                "Forbidden",
                _ct
            )
        )
            .ShouldBeOfType<DashboardSaveOutcome.Rejected>()
            .Reason.ShouldBe(DashboardRejection.AuthorityUnavailable);
        (await fixture.Service.ListAsync(moderator, fixture.Host, _ct))
            .ShouldBeOfType<DashboardResult<IReadOnlyList<DashboardSummary>>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.AuthorityUnavailable);
        var otherOperator = Session(fixture.Other);
        (
            await fixture.Service.SaveAsync(
                otherOperator,
                Save(fixture, permitted, DashboardDocument.Empty),
                _ct
            )
        )
            .ShouldBeOfType<DashboardSaveOutcome.Rejected>()
            .Reason.ShouldBe(DashboardRejection.HostMismatch);
        (await fixture.Service.GetAsync(otherOperator, fixture.Other, permitted.Id, _ct))
            .ShouldBeOfType<DashboardResult<DashboardSnapshot>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Missing);
        (
            await fixture.Service.DeleteAsync(
                otherOperator,
                fixture.Other,
                permitted.Id,
                permitted.Revision,
                _ct
            )
        )
            .ShouldBeOfType<DashboardResult<DashboardId>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Missing);
        Value(await fixture.Service.ListAsync(otherOperator, fixture.Other, _ct)).ShouldBeEmpty();
        (await fixture.ReadAsync(original.Id))
            .Document.Widgets.Single()
            .ShouldBe(permitted.Document.Widgets.Single());
        Value(await fixture.Service.ListAsync(fixture.Owner, fixture.Host, _ct)).Count.ShouldBe(1);
    }
}
