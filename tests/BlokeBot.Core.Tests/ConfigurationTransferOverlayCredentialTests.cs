using System.Text;
using BlokeBot.Core.Features.Overlays;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferOverlayCredentialTests
{
    [Test]
    public async Task NewImport_PersistsAnUnavailableSourceAndReportsOneExactManualFollowUp()
    {
        await using var fixture = await Fixture.CreateAsync();

        var applied = await fixture.ImportAsync(Document("Imported scene"));

        applied.ActivationId.ShouldBeNull();
        var followUp = applied.ManualFollowUps.ShouldHaveSingleItem();
        followUp.Code.ShouldBe(OverlayAccessRegeneration.FollowUpCode);
        followUp.LinkPath.ShouldBe(OverlayAccessRegeneration.LinkPath);
        followUp.Reason.ShouldContain("Imported scene");
        followUp.Reason.ShouldContain("Generate private URL");
        followUp.Title.ShouldNotContain("/overlay/");
        followUp.Reason.ShouldNotContain("/overlay/");
        applied.PostCommitFailures.ShouldBeEmpty();

        await using var db = await fixture.Database.CreateDbContextAsync();
        var imported = await db.OverlayInstances.AsNoTracking().SingleAsync();
        imported.RequiresAccessKeyRegeneration.ShouldBeTrue();
        imported.AccessKeyDigest.Length.ShouldBe(OverlayAccessKeyDigest.Size);
        imported.KeyVersion.ShouldBe(1);
        var guessedFromPersistedDigest = Convert
            .ToBase64String(imported.AccessKeyDigest)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        guessedFromPersistedDigest.Length.ShouldBe(43);
        _ = (
            await fixture.Resolver.ResolveAsync(guessedFromPersistedDigest, CancellationToken.None)
        ).ShouldBeOfType<OverlayResolutionResult.NotFound>();

        var alert = (
            await fixture.Alerts.LoadStateAsync(fixture.HostId, CancellationToken.None)
        ).Active.ShouldHaveSingleItem();
        alert.Source.ShouldBe(OverlayAccessRegeneration.AlertSource);
        alert.SourceKey.ShouldBe(OverlayAccessRegeneration.AlertSourceKey);
        alert.LinkPath.ShouldBe(OverlayAccessRegeneration.LinkPath);
        alert.Message.ShouldContain("Imported scene");
        alert.Message.ShouldNotContain("/overlay/");

        var exported = await fixture.ExportAsync();
        var json = Encoding.UTF8.GetString(exported.Json);
        json.ShouldNotContain("AccessKey", Case.Insensitive);
        json.ShouldNotContain("digest", Case.Insensitive);
        json.ShouldNotContain("regeneration", Case.Insensitive);
        json.ShouldNotContain(Convert.ToHexString(imported.AccessKeyDigest), Case.Insensitive);
    }

    [Test]
    public async Task FlaggedSource_RejectsAPreviouslyCapturedAccessKey()
    {
        await using var fixture = await Fixture.CreateAsync();
        var capturedKey = new string('c', 43);
        await fixture.SeedOverlayAsync(
            "Captured source",
            OverlayAccessKeyDigest.Compute(capturedKey),
            requiresRegeneration: true
        );

        _ = (
            await fixture.Resolver.ResolveAsync(capturedKey, CancellationToken.None)
        ).ShouldBeOfType<OverlayResolutionResult.NotFound>();
    }

    [Test]
    public async Task MatchedAndRepeatedImports_PreserveCredentialsAndKeepOneActiveAlert()
    {
        await using var matched = await Fixture.CreateAsync();
        var existingKey = new string('m', 43);
        var existingDigest = OverlayAccessKeyDigest.Compute(existingKey);
        await matched.SeedOverlayAsync("Main scene", existingDigest, requiresRegeneration: false);

        var matchedApplied = await matched.ImportAsync(Document(" main scene "));

        matchedApplied.ManualFollowUps.ShouldBeEmpty();
        var preserved = await matched.LoadOnlyOverlayAsync();
        preserved.AccessKeyDigest.ShouldBe(existingDigest);
        preserved.RequiresAccessKeyRegeneration.ShouldBeFalse();
        preserved.KeyVersion.ShouldBe(1);
        _ = (
            await matched.Resolver.ResolveAsync(existingKey, CancellationToken.None)
        ).ShouldBeOfType<OverlayResolutionResult.Resolved>();
        (
            await matched.Alerts.LoadStateAsync(matched.HostId, CancellationToken.None)
        ).Active.ShouldBeEmpty();

        await using var repeated = await Fixture.CreateAsync();
        _ = await repeated.ImportAsync(Document("Imported scene"));
        var first = await repeated.LoadOnlyOverlayAsync();

        var repeatedApplied = await repeated.ImportAsync(Document(" imported scene "));

        var second = await repeated.LoadOnlyOverlayAsync();
        second.AccessKeyDigest.ShouldBe(first.AccessKeyDigest);
        second.RequiresAccessKeyRegeneration.ShouldBeTrue();
        second.KeyVersion.ShouldBe(first.KeyVersion);
        _ = repeatedApplied.ManualFollowUps.ShouldHaveSingleItem();
        var active = (
            await repeated.Alerts.LoadStateAsync(repeated.HostId, CancellationToken.None)
        ).Active;
        active.ShouldHaveSingleItem().OccurrenceCount.ShouldBe(2);
    }

    [Test]
    public async Task CanceledImport_RollsBackBeforeRetryCreatesOneSourceAndAlert()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.ImportOutcomeAsync(Document("Retry import"), canceled.Token)
        );
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (await db.OverlayInstances.CountAsync()).ShouldBe(0);
            (await db.DurableAlerts.CountAsync()).ShouldBe(0);
            (await db.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
        }

        var retried = await fixture.ImportAsync(Document("Retry import"));

        _ = retried.ManualFollowUps.ShouldHaveSingleItem();
        _ = (await fixture.ListOverlaysAsync()).ShouldHaveSingleItem();
        _ = (
            await fixture.Alerts.LoadStateAsync(fixture.HostId, CancellationToken.None)
        ).Active.ShouldHaveSingleItem();
    }
}
