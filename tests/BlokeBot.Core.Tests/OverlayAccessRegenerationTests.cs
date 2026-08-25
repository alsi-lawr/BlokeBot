using BlokeBot.Core.Features.Overlays;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferOverlayCredentialTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AlertResolvesOnlyAfterTheFinalMarkedSourceIsRemovedOrRegenerated(
        bool deleteFinalSource
    )
    {
        await using var fixture = await Fixture.CreateAsync();
        _ = await fixture.ImportAsync(Document("First source", "Second source"));
        var imported = await fixture.ListOverlaysAsync();
        var first = imported.Single(value => value.Name == "First source");
        var second = imported.Single(value => value.Name == "Second source");

        var firstRotation = (
            await fixture.OverlayService.RotateKeyAsync(
                fixture.Session,
                new(first.Id, first.Revision),
                CancellationToken.None
            )
        ).SucceededValue();

        firstRotation.Instance.RequiresAccessKeyRegeneration.ShouldBeFalse();
        _ = (
            await fixture.Resolver.ResolveAsync(
                firstRotation.PrivateAccess.AccessKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.Resolved>();
        _ = (
            await fixture.Alerts.LoadStateAsync(fixture.HostId, CancellationToken.None)
        ).Active.ShouldHaveSingleItem();

        if (deleteFinalSource)
        {
            _ = (
                await fixture.OverlayService.DeleteAsync(
                    fixture.Session,
                    new(second.Id, second.Revision),
                    CancellationToken.None
                )
            ).SucceededValue();
        }
        else
        {
            var finalRotation = (
                await fixture.OverlayService.RotateKeyAsync(
                    fixture.Session,
                    new(second.Id, second.Revision),
                    CancellationToken.None
                )
            ).SucceededValue();
            _ = (
                await fixture.Resolver.ResolveAsync(
                    finalRotation.PrivateAccess.AccessKey,
                    CancellationToken.None
                )
            ).ShouldBeOfType<OverlayResolutionResult.Resolved>();
        }

        var resolved = await fixture.Alerts.LoadStateAsync(fixture.HostId, CancellationToken.None);
        resolved.Active.ShouldBeEmpty();
        var history = resolved.History.ShouldHaveSingleItem();
        history.AcknowledgedByLogin.ShouldBe("destination");
        history.Message.ShouldNotContain("/overlay/");
        history.Message.ShouldNotContain(firstRotation.PrivateAccess.AccessKey);
    }

    [Test]
    public async Task CanceledOrRejectedRegeneration_PreservesTheMarkedSourceAndAlertUntilRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        _ = await fixture.ImportAsync(Document("Retry source"));
        var imported = await fixture.ListOverlaysAsync();
        var source = imported.ShouldHaveSingleItem();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.OverlayService.RotateKeyAsync(
                fixture.Session,
                new(source.Id, source.Revision),
                canceled.Token
            )
        );
        _ = (
            await fixture.OverlayService.RotateKeyAsync(
                fixture.Session,
                new(source.Id, new OverlayRevision(source.Revision.Value + 1)),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceKeyRotation>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.Conflict>();

        var unchanged = await fixture.LoadOnlyOverlayAsync();
        unchanged.RequiresAccessKeyRegeneration.ShouldBeTrue();
        unchanged.Revision.ShouldBe(source.Revision.Value);
        _ = (
            await fixture.Alerts.LoadStateAsync(fixture.HostId, CancellationToken.None)
        ).Active.ShouldHaveSingleItem();

        var retried = (
            await fixture.OverlayService.RotateKeyAsync(
                fixture.Session,
                new(source.Id, source.Revision),
                CancellationToken.None
            )
        ).SucceededValue();
        retried.Instance.RequiresAccessKeyRegeneration.ShouldBeFalse();
        (
            await fixture.Alerts.LoadStateAsync(fixture.HostId, CancellationToken.None)
        ).Active.ShouldBeEmpty();
    }

    [Test]
    public async Task FailedRegenerationCommit_RollsBackTheKeyAndAlertResolutionBeforeRetry()
    {
        var failure = new FailKeyRotationSaveInterceptor();
        await using var fixture = await Fixture.CreateAsync(failure);
        _ = await fixture.ImportAsync(Document("Commit retry source"));
        var source = (await fixture.ListOverlaysAsync()).ShouldHaveSingleItem();
        var before = await fixture.LoadOnlyOverlayAsync();

        _ = await Should.ThrowAsync<DbUpdateException>(() =>
            fixture.OverlayService.RotateKeyAsync(
                fixture.Session,
                new(source.Id, source.Revision),
                CancellationToken.None
            )
        );

        var rolledBack = await fixture.LoadOnlyOverlayAsync();
        rolledBack.AccessKeyDigest.ShouldBe(before.AccessKeyDigest);
        rolledBack.RequiresAccessKeyRegeneration.ShouldBeTrue();
        rolledBack.Revision.ShouldBe(before.Revision);
        _ = (
            await fixture.Alerts.LoadStateAsync(fixture.HostId, CancellationToken.None)
        ).Active.ShouldHaveSingleItem();

        failure.Enabled = false;
        _ = (
            await fixture.OverlayService.RotateKeyAsync(
                fixture.Session,
                new(source.Id, source.Revision),
                CancellationToken.None
            )
        ).SucceededValue();
        (
            await fixture.Alerts.LoadStateAsync(fixture.HostId, CancellationToken.None)
        ).Active.ShouldBeEmpty();
    }
}
