using AngleSharp.Html.Dom;
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
            page.Find("iframe").HasAttribute("sandbox").ShouldBeFalse();
            page.Find("iframe").ClassList.ShouldContain("overlay-preview-frame");
            var previewTabs = page.Find("[aria-label='Preview state']");
            previewTabs.QuerySelectorAll(".segmented-motion__tab").Length.ShouldBe(2);
            previewTabs
                .QuerySelector(".segmented-motion__tab--active")
                .ShouldNotBeNull()
                .TextContent.Trim()
                .ShouldBe("Live");
            page.FindAll("[data-private-url-reveal]").ShouldBeEmpty();
            page.Markup.ShouldNotContain(seed.PrivateAccessKey);
            page.Markup.ShouldContain("Open OBS Browser Sources appear here when connected.");
        });

        page.Find("[aria-label='Preview state']")
            .QuerySelectorAll(".segmented-motion__tab")
            .Single(value => value.TextContent.Trim() == "Representative")
            .Click();

        page.WaitForAssertion(() =>
        {
            page.Find("[aria-label='Preview state']")
                .QuerySelector(".segmented-motion__tab--active")
                .ShouldNotBeNull()
                .TextContent.Trim()
                .ShouldBe("Representative");
            page.FindAll(".segmented-motion__button").ShouldBeEmpty();
        });

        page.Find("button").TextContent.ShouldNotBeNull();
        page.Find("aside[aria-labelledby='overlay-inventory-title'] button.btn-secondary").Click();

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
        HostFeatureFlags.Shoutouts.Contains(HostFeatureFlags.Overlays).ShouldBeFalse();

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
        source.ShouldContain("Open OBS Browser Sources appear here when connected.");
        source.ShouldContain("Visual configuration");
        source.ShouldContain("Empty overlays have no visual settings");
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
        code.ShouldContain("No Browser Source is connected");
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

    [Test]
    public async Task GuessingEditor_OffersEverySampleAndRecoversWhenParentIsOff()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedGuessingAsync(database);
        await using (var context = UiTestContextFactory.Create(database, seed.HostId))
        {
            context.Services.AddSingleton<IModeratorAuthorityService>(
                new GrantedModeratorAuthority()
            );
            context.Services.AddBlokeBotOverlays();

            var page = context.Render<OverlaysPage>();

            page.WaitForAssertion(() =>
            {
                page.Find("iframe")
                    .GetAttribute("src")
                    .ShouldBe($"/overlays/preview/{seed.OverlayId:D}");
                page.FindAll("[aria-label='Guessing overlay sample state'] button")
                    .Select(button => button.TextContent.Trim())
                    .ShouldBe(["No round", "Open", "Closed", "Result"]);
                page.Markup.ShouldContain("Show the number of guesses");
                page.Markup.ShouldContain("Result animation duration");
            });

            page.FindAll("[aria-label='Guessing overlay sample state'] button")
                .Single(button => button.TextContent.Trim() == "Result")
                .Click();
            page.WaitForAssertion(() =>
                page.Find("iframe")
                    .GetAttribute("src")
                    .ShouldBe(
                        $"/overlays/preview/{seed.OverlayId:D}?mode=representative&sample=completed"
                    )
            );
        }

        await using (var db = await database.CreateDbContextAsync())
        {
            await db
                .Hosts.Where(host => host.Id == seed.HostId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(host => host.EnabledFeatures, HostFeatureFlags.Overlays)
                );
        }
        await using (var context = UiTestContextFactory.Create(database, seed.HostId))
        {
            context.Services.AddSingleton<IModeratorAuthorityService>(
                new GrantedModeratorAuthority()
            );
            context.Services.AddBlokeBotOverlays();

            var page = context.Render<OverlaysPage>();

            page.WaitForAssertion(() =>
            {
                var type = (IHtmlSelectElement)page.Find("#overlay-type");
                type.Value.ShouldBe(OverlayType.Guessing.ToString());
                type.TextContent.ShouldContain("Guessing round");
                type.IsDisabled.ShouldBeTrue();
                page.Find("[data-overlay-disabled-recovery]")
                    .TextContent.ShouldContain("Turn Guessing game on in Channel setup");
                page.FindAll("iframe").ShouldBeEmpty();
                page.FindAll("button")
                    .Single(button => button.TextContent.Trim() == "Save overlay")
                    .HasAttribute("disabled")
                    .ShouldBeTrue();
            });
        }
    }

    [Test]
    public async Task GuessingPreviewControls_RenderExplicitPressedValuesAcrossSelectionChanges()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedGuessingAsync(database);
        await using var context = UiTestContextFactory.Create(database, seed.HostId);
        context.Services.AddSingleton<IModeratorAuthorityService>(new GrantedModeratorAuthority());
        context.Services.AddBlokeBotOverlays();

        var page = context.Render<OverlaysPage>();

        page.WaitForAssertion(() =>
        {
            PressedValue(page, "Preview state", "Live").ShouldBe("true");
            PressedValue(page, "Preview state", "Representative").ShouldBe("false");
            page.FindAll("[aria-label='Guessing overlay sample state'] button")
                .Select(button => button.GetAttribute("aria-pressed"))
                .ShouldAllBe(value => value == "false");
        });

        FindButton(page, "Guessing overlay sample state", "Result").Click();

        page.WaitForAssertion(() =>
        {
            PressedValue(page, "Preview state", "Live").ShouldBe("false");
            PressedValue(page, "Preview state", "Representative").ShouldBe("true");
            PressedValue(page, "Guessing overlay sample state", "Result").ShouldBe("true");
            page.FindAll("[aria-label='Guessing overlay sample state'] button")
                .Where(button => button.TextContent.Trim() != "Result")
                .Select(button => button.GetAttribute("aria-pressed"))
                .ShouldAllBe(value => value == "false");
        });

        page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save overlay")
            .Click();
        page.WaitForAssertion(() =>
        {
            PressedValue(page, "Preview state", "Representative").ShouldBe("true");
            PressedValue(page, "Guessing overlay sample state", "Result").ShouldBe("true");
            page.Find("iframe")
                .GetAttribute("src")
                .ShouldEndWith("?mode=representative&sample=completed");
        });

        FindButton(page, "Preview state", "Live").Click();

        page.WaitForAssertion(() =>
        {
            PressedValue(page, "Preview state", "Live").ShouldBe("true");
            PressedValue(page, "Preview state", "Representative").ShouldBe("false");
            page.FindAll("[aria-label='Guessing overlay sample state'] button")
                .Select(button => button.GetAttribute("aria-pressed"))
                .ShouldAllBe(value => value == "false");
        });
    }

    [Test]
    public async Task SelectingAndResettingAVisualOverlay_RendersTheCurrentCssAndIdentity()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedGuessingAsync(database);
        var styledId = Guid.Parse("1fd78bd8-044d-432f-b231-b38691fb626a");
        await using (var db = await database.CreateDbContextAsync())
        {
            db.OverlayInstances.Add(
                new OverlayInstance
                {
                    PublicId = styledId,
                    HostId = seed.HostId,
                    Name = "Styled guessing",
                    Type = OverlayType.Guessing,
                    IsEnabled = true,
                    ConfigurationJson =
                        """{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":8,"appearance":{"x":200,"y":680,"width":1500,"height":280,"css":".accent { fill: #f472b6; }"}}""",
                    AccessKeyDigest = OverlayAccessKeyDigest.Compute(
                        "styled-component-test-key-000000000000000"
                    ),
                    KeyVersion = 1,
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        await using var context = UiTestContextFactory.Create(database, seed.HostId);
        context.Services.AddSingleton<IModeratorAuthorityService>(new GrantedModeratorAuthority());
        context.Services.AddBlokeBotOverlays();
        var page = context.Render<OverlaysPage>();

        page.WaitForAssertion(() =>
            page.FindAll("[aria-label='Saved overlays'] button").Count.ShouldBe(2)
        );
        page.FindAll("[aria-label='Saved overlays'] button")
            .Single(button =>
                button.TextContent.Contains("Styled guessing", StringComparison.Ordinal)
            )
            .Click();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-appearance-preview]")
                .GetAttribute("data-overlay-id")
                .ShouldBe(styledId.ToString());
            page.Find("[data-appearance-css]")
                .GetAttribute("value")
                .ShouldBe(".accent { fill: #f472b6; }");
            page.Find("[data-appearance-editor]")
                .GetAttribute("data-rendered-css")
                .ShouldBe(".accent { fill: #f472b6; }");
        });

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Reset").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("[data-appearance-css]").GetAttribute("value").ShouldBe(string.Empty);
            page.Find("[data-appearance-editor]")
                .GetAttribute("data-rendered-css")
                .ShouldBe(string.Empty);
        });

        FindButton(page, "Guessing overlay sample state", "Result").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("[data-appearance-preview]")
                .GetAttribute("data-overlay-id")
                .ShouldBe(styledId.ToString());
            page.Find("[data-appearance-preview]")
                .GetAttribute("src")
                .ShouldEndWith("?mode=representative&sample=completed");
        });
    }

    [Test]
    public async Task GiveawayEditor_OffersAllSamplesAndRecoversWhenPointsIsOff()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedGiveawayAsync(database);
        await using (var context = UiTestContextFactory.Create(database, seed.HostId))
        {
            context.Services.AddSingleton<IModeratorAuthorityService>(
                new GrantedModeratorAuthority()
            );
            context.Services.AddBlokeBotOverlays();
            var page = context.Render<OverlaysPage>();

            page.WaitForAssertion(() =>
            {
                page.FindAll("[aria-label='Giveaway overlay sample state'] button")
                    .Select(button => button.TextContent.Trim())
                    .ShouldBe(["Open", "Ending", "Winners", "Cancelled"]);
                page.Markup.ShouldContain("Entrant count");
                page.Markup.ShouldContain("Close-time countdown");
                page.Markup.ShouldContain("Current join command");
            });
            FindButton(page, "Giveaway overlay sample state", "Winners").Click();
            page.WaitForAssertion(() =>
                page.Find("iframe")
                    .GetAttribute("src")
                    .ShouldBe(
                        $"/overlays/preview/{seed.OverlayId:D}?mode=representative&sample=completed"
                    )
            );
        }

        await using (var db = await database.CreateDbContextAsync())
        {
            await db
                .Hosts.Where(host => host.Id == seed.HostId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(host => host.EnabledFeatures, HostFeatureFlags.Overlays)
                );
        }
        await using (var context = UiTestContextFactory.Create(database, seed.HostId))
        {
            context.Services.AddSingleton<IModeratorAuthorityService>(
                new GrantedModeratorAuthority()
            );
            context.Services.AddBlokeBotOverlays();
            var page = context.Render<OverlaysPage>();

            page.WaitForAssertion(() =>
            {
                var type = (IHtmlSelectElement)page.Find("#overlay-type");
                type.Value.ShouldBe(OverlayType.Giveaway.ToString());
                type.TextContent.ShouldContain("Points giveaway");
                type.IsDisabled.ShouldBeTrue();
                page.Find("[data-overlay-disabled-recovery]")
                    .TextContent.ShouldContain("Turn Points on in Channel setup");
                page.FindAll("iframe").ShouldBeEmpty();
                page.FindAll("button")
                    .Single(button => button.TextContent.Trim() == "Save overlay")
                    .HasAttribute("disabled")
                    .ShouldBeTrue();
            });
        }
    }

    private static string? PressedValue(
        IRenderedComponent<OverlaysPage> page,
        string groupLabel,
        string buttonLabel
    ) => FindButton(page, groupLabel, buttonLabel).GetAttribute("aria-pressed");

    private static AngleSharp.Dom.IElement FindButton(
        IRenderedComponent<OverlaysPage> page,
        string groupLabel,
        string buttonLabel
    ) =>
        page.FindAll($"[aria-label='{groupLabel}'] button")
            .Single(button => button.TextContent.Trim() == buttonLabel);

    private static string SourcePath(string fileName) =>
        Path.GetFullPath(
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

    private static async Task<OverlaySeed> SeedGuessingAsync(SqliteBlokeBotDbFactory database)
    {
        const string PrivateAccessKey = "guessing-component-test-key-00000000000000";
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "guessing-streamer-id",
            Login = "guessing-streamer",
            DisplayName = "Guessing Streamer",
            EnabledFeatures = HostFeatureFlags.Overlays | HostFeatureFlags.Guessing,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var overlay = new OverlayInstance
        {
            PublicId = Guid.Parse("93a5d74f-470e-4df3-920c-3f4932425a0d"),
            HostId = host.Id,
            Name = "Guessing round",
            Type = OverlayType.Guessing,
            IsEnabled = true,
            ConfigurationJson =
                """{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":8}""",
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

    private static async Task<OverlaySeed> SeedGiveawayAsync(SqliteBlokeBotDbFactory database)
    {
        const string PrivateAccessKey = "giveaway-component-test-key-0000000000000";
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "giveaway-streamer-id",
            Login = "giveaway-streamer",
            DisplayName = "Giveaway Streamer",
            EnabledFeatures = HostFeatureFlags.Overlays | HostFeatureFlags.Points,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var overlay = new OverlayInstance
        {
            PublicId = Guid.Parse("45949b52-282f-4133-b423-18d511690e70"),
            HostId = host.Id,
            Name = "Points giveaway",
            Type = OverlayType.Giveaway,
            IsEnabled = true,
            ConfigurationJson =
                """{"schemaVersion":1,"title":"Community giveaway","showEntrantCount":true,"showCountdown":true,"showJoinCommand":true}""",
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
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }
}
