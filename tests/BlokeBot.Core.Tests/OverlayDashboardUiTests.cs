using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosting;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class OverlayDashboardUiTests
{
    [Test]
    public async Task SelectedHostPage_RendersOneEditorAndOpaquePreviewWithoutPrivateKey()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedAsync(database);
        await using var context = UiTestContextFactory.Create(database, seed.HostId);
        context.Services.AddSingleton<IModeratorAuthorityService>(new GrantedModeratorAuthority());
        context.Services.AddBlokeBotOverlays();

        var page = context.Render<OverlaysPage>();

        page.WaitForAssertion(() =>
        {
            page.FindAll("[data-overlay-editor]").Count.ShouldBe(1);
            page.Find("iframe")
                .GetAttribute("src")
                .ShouldBe($"/overlays/preview/{seed.OverlayId:D}");
            page.Find("iframe").ClassList.ShouldContain("overlay-preview-frame");
            page.FindAll(".segmented-motion__tab").Count.ShouldBe(2);
            page.Find(".segmented-motion__tab--active").TextContent.Trim().ShouldBe("Live");
            page.FindAll("[data-private-url-reveal]").ShouldBeEmpty();
            page.Markup.ShouldNotContain(seed.PrivateAccessKey);
            page.Markup.ShouldContain("Approximate diagnostic presence excludes");
        });

        page.FindAll(".segmented-motion__tab")
            .Single(value => value.TextContent.Trim() == "Representative")
            .Click();

        page.WaitForAssertion(() =>
        {
            page.Find(".segmented-motion__tab--active")
                .TextContent.Trim()
                .ShouldBe("Representative");
            page.FindAll(".segmented-motion__button").ShouldBeEmpty();
        });

        page.Find("button").TextContent.ShouldNotBeNull();
        page.FindAll("button").Single(value => value.TextContent.Trim() == "New").Click();

        page.WaitForAssertion(() =>
        {
            page.Markup.ShouldContain("New overlay — not saved");
            page.Markup.ShouldContain("Nothing has been saved yet");
            page.FindAll("[data-overlay-editor]").Count.ShouldBe(1);
            var feedback = page.Find("[data-overlay-feedback='success']");
            feedback.ClassList.ShouldContain("text-slate-950");
            feedback.ClassList.ShouldNotContain("text-blue-900");
        });
    }

    [Test]
    public void FeatureCatalog_ExposesAnIndependentOverlaySwitchIncludedInAll()
    {
        HostFeatureFlags.All.Contains(HostFeatureFlags.Overlays).ShouldBeTrue();
        HostFeatureFlags.NativeTwitch.Contains(HostFeatureFlags.Overlays).ShouldBeFalse();

        var card = HostFeatureCatalog
            .Cards(HostFeatureFlags.Overlays)
            .Single(value => value.Feature == HostFeatureFlags.Overlays);

        card.Enabled.ShouldBeTrue();
        card.Name.ShouldBe("Overlays");
    }

    [Test]
    public void SharedRenderer_ConstrainsCredentialModeAndNeverRequiresAPrivateKey()
    {
        var publicDocument = OverlayBrowserSourceDocument.Render(
            PathString.Empty,
            "/overlay/private/state",
            "/overlay/private/events",
            OverlayBrowserSourceCredentials.Omit,
            liveEnabled: true
        );
        var previewDocument = OverlayBrowserSourceDocument.Render(
            new PathString("/blokebot"),
            "/overlays/preview/opaque-id/state",
            "/overlays/preview/opaque-id/events",
            OverlayBrowserSourceCredentials.SameOrigin,
            liveEnabled: false
        );

        publicDocument.ShouldContain("data-credentials=\"omit\"");
        publicDocument.ShouldContain("data-live-enabled=\"true\"");
        previewDocument.ShouldContain("data-credentials=\"same-origin\"");
        previewDocument.ShouldContain("data-live-enabled=\"false\"");
        previewDocument.ShouldContain(
            "data-state-url=\"/blokebot/overlays/preview/opaque-id/state\""
        );
        previewDocument.ShouldContain("viewBox=\"0 0 1920 1080\"");
        previewDocument.ShouldNotContain("accessKey", Case.Insensitive);
    }

    [Test]
    public void DashboardSource_UsesWideSingleEditorOpaquePreviewAndExplicitSafetyCopy()
    {
        var source = File.ReadAllText(SourcePath("OverlaysPage.razor"));
        var code = File.ReadAllText(SourcePath("OverlaysPage.razor.cs"));
        var styles = File.ReadAllText(SourcePath("OverlaysPage.razor.css"));

        source.ShouldContain("Width=\"DashboardPageWidth.Wide\"");
        source.ShouldContain("data-overlay-editor");
        source.ShouldContain("New overlay — not saved");
        source.ShouldContain("1920");
        source.ShouldContain("1080");
        source.ShouldContain("Approximate diagnostic presence excludes");
        source.ShouldContain("Visual configuration");
        source.ShouldContain("has no visual fields");
        source.ShouldContain("Rotate private URL");
        source.ShouldContain("Send test pulse");
        source.ShouldContain("data-private-url-reveal");
        source.ShouldContain("overlay-preview-frame");
        source.ShouldContain("text-slate-950");
        source.ShouldContain("text-slate-700");
        source.ShouldNotContain("text-blue-900");
        source.ShouldNotContain("text-red-800");
        source.ShouldNotContain("text-amber-900");
        source.ShouldNotContain("text-amber-950");
        styles.ShouldContain("background: transparent");
        styles.ShouldContain("color-scheme: only light");
        code.ShouldContain("$\"/overlays/preview/{_selected.Id:D}{mode}\"");
        code.ShouldContain("segmented-motion__tab segmented-motion__tab--active");
        code.ShouldNotContain("segmented-motion__button");
        code.ShouldNotContain("PrivateAccess.AccessKey");
        code.ShouldContain("RunSelectedHostMutationAsync");
        code.ShouldContain("_publisher.PublishTest");
        code.ShouldContain("No live client is connected");
        code.ShouldContain("SetFailure(rejected.Reason.Message)");
    }

    [Test]
    public void ProductionClient_HasOneCredentialSwitchAndBoundedTestPulse()
    {
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "root.dataset.credentials === \"same-origin\""
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "root.dataset.liveEnabled !== \"false\""
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("showTestPulse()");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("}, 1500);");
        OverlayBrowserSourceAssets.JavaScript.ShouldNotContain("setInterval");
    }

    private static string SourcePath(string fileName)
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "BlokeBot.Core",
                "Features",
                "Overlays",
                fileName
            )
        );
    }

    private static async Task<OverlaySeed> SeedAsync(SqliteBlokeBotDbFactory database)
    {
        const string PrivateAccessKey = "component-test-overlay-key-0000000000000000";
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.Overlays,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var overlay = new OverlayInstance
        {
            PublicId = Guid.Parse("a255f385-c006-4a86-936b-6fd7393e0508"),
            HostId = host.Id,
            Name = "Main stream",
            Type = OverlayType.Empty,
            IsEnabled = true,
            ConfigurationJson = """{"schemaVersion":1}""",
            AccessKeyDigest = OverlayAccessKeyDigest.Compute(PrivateAccessKey),
            KeyVersion = 1,
            Revision = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.OverlayInstances.Add(overlay);
        await db.SaveChangesAsync();
        return new OverlaySeed(host.Id, overlay.PublicId, PrivateAccessKey);
    }

    private sealed record OverlaySeed(int HostId, Guid OverlayId, string PrivateAccessKey);

    private sealed class GrantedModeratorAuthority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        )
        {
            return Task.FromResult<ModeratorAuthorityOutcome>(
                new ModeratorAuthorityOutcome.Granted()
            );
        }
    }
}
