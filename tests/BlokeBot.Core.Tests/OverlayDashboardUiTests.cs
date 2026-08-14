using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosting;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class OverlayDashboardUiTests
{
    [Test]
    public async Task SelectedHostPage_DoesNotExposeThePrivateOverlayKey()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedAsync(database);
        await using var context = UiTestContextFactory.Create(database, seed.HostId);
        _ = context.Services.AddSingleton<IModeratorAuthorityService>(
            new GrantedModeratorAuthority()
        );
        _ = context
            .Services.AddBlokeBotPlayWithViewers()
            .AddBlokeBotBounties()
            .AddBlokeBotCommunityProgression()
            .AddBlokeBotOverlays();

        var page = context.Render<OverlaysPage>();

        page.WaitForAssertion(() =>
        {
            page.Find("iframe")
                .GetAttribute("src")
                .ShouldBe($"/overlays/preview/{seed.OverlayId:D}");
            page.FindAll("[data-private-url-reveal]").ShouldBeEmpty();
            page.Markup.ShouldNotContain(seed.PrivateAccessKey);
        });
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
        previewDocument.ShouldNotContain("accessKey", Case.Insensitive);
    }

    [Test]
    public async Task GuessingPreviewSelection_ChangesThePreviewMode()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedGuessingAsync(database);
        await using var context = UiTestContextFactory.Create(database, seed.HostId);
        _ = context.Services.AddSingleton<IModeratorAuthorityService>(
            new GrantedModeratorAuthority()
        );
        _ = context
            .Services.AddBlokeBotPlayWithViewers()
            .AddBlokeBotBounties()
            .AddBlokeBotCommunityProgression()
            .AddBlokeBotOverlays();

        var page = context.Render<OverlaysPage>();

        _ = page.WaitForElement("iframe");

        await page.InvokeAsync(() =>
            FindButton(page, "Guessing overlay sample state", "Result").Click()
        );

        page.WaitForAssertion(() =>
            page.Find("iframe")
                .GetAttribute("src")
                .ShouldEndWith("?mode=representative&sample=completed")
        );

        await page.InvokeAsync(() => FindButton(page, "Preview state", "Live").Click());

        page.WaitForAssertion(() =>
            (page.Find("iframe").GetAttribute("src") ?? string.Empty).ShouldNotContain(
                "mode=representative"
            )
        );
    }

    [Test]
    public async Task SelectingAndResettingAVisualOverlay_RendersTheCurrentCssAndIdentity()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedGuessingAsync(database);
        var styledId = Guid.Parse("1fd78bd8-044d-432f-b231-b38691fb626a");
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = db.OverlayInstances.Add(
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
            _ = await db.SaveChangesAsync();
        }

        await using var context = UiTestContextFactory.Create(database, seed.HostId);
        _ = context.Services.AddSingleton<IModeratorAuthorityService>(
            new GrantedModeratorAuthority()
        );
        _ = context
            .Services.AddBlokeBotPlayWithViewers()
            .AddBlokeBotBounties()
            .AddBlokeBotCommunityProgression()
            .AddBlokeBotOverlays();
        var page = context.Render<OverlaysPage>();

        page.WaitForAssertion(() =>
            page.FindAll("[aria-label='Saved overlays'] .studio-rail__item")
                .Any(button =>
                    button.TextContent.Contains("Styled guessing", StringComparison.Ordinal)
                )
                .ShouldBeTrue()
        );
        page.FindAll("[aria-label='Saved overlays'] .studio-rail__item")
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
        });

        page.Find("[data-appearance-fields] button").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-appearance-css]").GetAttribute("value").ShouldBe(string.Empty)
        );

        await page.InvokeAsync(() =>
            FindButton(page, "Guessing overlay sample state", "Result").Click()
        );
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
    public async Task DisabledInheritedParent_KeepsSavedEditorRecoveryAndHidesNewTypeDiscovery()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedCommunityGoalAsync(database, HostFeatureFlags.Overlays);
        await using var context = UiTestContextFactory.Create(database, seed.HostId);
        _ = context.Services.AddSingleton<IModeratorAuthorityService>(
            new GrantedModeratorAuthority()
        );
        _ = context
            .Services.AddBlokeBotPlayWithViewers()
            .AddBlokeBotBounties()
            .AddBlokeBotCommunityProgression()
            .AddBlokeBotOverlays();

        var page = context.Render<OverlaysPage>();

        page.WaitForAssertion(() =>
        {
            _ = page.FindAll("[data-overlay-disabled-recovery]").ShouldHaveSingleItem();
            page.FindAll("[data-appearance-preview]").ShouldBeEmpty();
            page.Find("#overlay-type")
                .QuerySelectorAll("option")
                .Any(option => option.GetAttribute("value") == OverlayType.CommunityGoal.ToString())
                .ShouldBeTrue();
        });

        page.Find("[data-action='new-overlay']").Click();
        page.WaitForAssertion(() =>
            page.Find("#overlay-type")
                .QuerySelectorAll("option")
                .Any(option => option.GetAttribute("value") == OverlayType.CommunityGoal.ToString())
                .ShouldBeFalse()
        );
    }

    private static AngleSharp.Dom.IElement FindButton(
        IRenderedComponent<OverlaysPage> page,
        string groupLabel,
        string buttonLabel
    ) =>
        page.FindAll($"[aria-label='{groupLabel}'] button")
            .Single(button => button.TextContent.Trim() == buttonLabel);

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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
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
        _ = db.OverlayInstances.Add(overlay);
        _ = await db.SaveChangesAsync();
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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
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
        _ = db.OverlayInstances.Add(overlay);
        _ = await db.SaveChangesAsync();
        return new OverlaySeed(host.Id, overlay.PublicId, PrivateAccessKey);
    }

    private static async Task<OverlaySeed> SeedCommunityGoalAsync(
        SqliteBlokeBotDbFactory database,
        HostFeatureFlags features
    )
    {
        const string PrivateAccessKey = "community-goal-component-key-0000000000000";
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "community-streamer-id",
            Login = "community-streamer",
            DisplayName = "Community Streamer",
            EnabledFeatures = features,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var overlay = new OverlayInstance
        {
            PublicId = Guid.Parse("996a3163-69c8-40f1-aaef-a4fc8632c6f2"),
            HostId = host.Id,
            Name = "Community goal",
            Type = OverlayType.CommunityGoal,
            IsEnabled = true,
            ConfigurationJson = OverlayConfiguration.CommunityGoalV1.Default.ToPersistenceJson(),
            AccessKeyDigest = OverlayAccessKeyDigest.Compute(PrivateAccessKey),
            KeyVersion = 1,
            Revision = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        _ = db.OverlayInstances.Add(overlay);
        _ = await db.SaveChangesAsync();
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
