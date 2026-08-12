using AngleSharp.Dom;
using BlokeBot.Core.Features.RaidCollaboration;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class RaidCollaborationUiTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task BarePath_NormalizesToHubAndSelectingSettingsPushesOneHistoryEntry()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/raid-collaboration");

        var page = context.Render<RaidCollaborationPage>();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/raid-collaboration#hub");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
            page.Find("#raid-workspace-hub-tab").GetAttribute("aria-selected").ShouldBe("true");
            page.Find("#raid-workspace-hub-tab").GetAttribute("href").ShouldBe("#hub");
            page.Find("#raid-workspace-settings-tab").GetAttribute("href").ShouldBe("#settings");
            page.Find("#raid-workspace-hub-panel").GetAttribute("role").ShouldBe("tabpanel");
            _ = page.Find("[data-raid-history]");
            page.FindAll("[data-raid-settings]").ShouldBeEmpty();
        });

        page.Find("#raid-workspace-settings-tab").Click();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/raid-collaboration#settings");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
            page.Find("#raid-workspace-settings-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            page.Find("#raid-workspace-settings-panel")
                .GetAttribute("aria-labelledby")
                .ShouldBe("raid-workspace-settings-tab");
            _ = page.Find("[data-raid-settings]");
            page.FindAll("[data-raid-history]").ShouldBeEmpty();
        });
    }

    [Test]
    public async Task DirectSettingsFragment_OpensSettingsAndBrowserHistoryMovesBetweenWorkspaces()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/raid-collaboration#settings");

        var page = context.Render<RaidCollaborationPage>();

        page.WaitForAssertion(() =>
        {
            page.Find("#raid-workspace-settings-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            _ = page.Find("#raid-language");
        });

        navigation.NavigateTo("/raid-collaboration#hub");

        page.WaitForAssertion(() =>
        {
            page.Find("#raid-workspace-hub-tab").GetAttribute("aria-selected").ShouldBe("true");
            _ = page.Find("[data-raid-history]");
        });

        navigation.NavigateTo("/raid-collaboration#settings");

        page.WaitForAssertion(() =>
        {
            page.Find("#raid-workspace-settings-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            _ = page.Find("#raid-language");
        });
    }

    [Test]
    public async Task Settings_SaveArbitraryLanguageCategoriesAndApprovedChannelChanges()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database);
        var page = RenderSettings(context);

        page.Find("#raid-language").Input("nan-hani-tw");
        page.Find("#raid-categories").Input("Outer Wilds");
        page.Find("#raid-categories").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        page.Find("[data-action='remove-raid-category']").Click();
        Button(page, "Add channel").Click();
        page.Find("#raid-channel-login-1").Input("teacupmage");
        page.Find("#raid-channel-name-1").Input("TeacupMage");
        page.Find("#raid-channel-clip-1").Input("clip-9");
        page.FindAll("button").First(button => button.TextContent.Trim() == "Remove").Click();
        page.Find("#raid-welcome-enabled").Click();
        Save(page, "success");

        await using var verify = await database.CreateDbContextAsync();
        var settings = await verify.RaidCollaborationSettings.SingleAsync();
        settings.Language.ShouldBe("nan-hani-tw");
        settings.EligibleCategories.ShouldBe("Outer Wilds");
        settings.WelcomeEnabled.ShouldBeFalse();
        var approved = await verify.ApprovedRaidChannels.SingleAsync();
        approved.Login.ShouldBe("teacupmage");
        approved.DisplayName.ShouldBe("TeacupMage");
        approved.ApprovedClipId.ShouldBe("clip-9");
    }

    [Test]
    public async Task BlankLanguage_SavesAsAnyLanguageAndKeepsEveryOtherValue()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateContextAsync(database);
        var page = RenderSettings(context);

        page.Find("#raid-language").Input(string.Empty);
        Save(page, "success");

        await using var verify = await database.CreateDbContextAsync();
        var settings = await verify.RaidCollaborationSettings.SingleAsync();
        settings.Language.ShouldBeEmpty();
        settings.EligibleCategories.ShouldBe("Celeste");
        settings.DeduplicationWindowMinutes.ShouldBe(45);
        settings.RelationshipCooldownHours.ShouldBe(24);
        settings.WelcomeMessage.ShouldBe("Welcome in, {display_name}!");
        (await verify.ApprovedRaidChannels.SingleAsync()).Login.ShouldBe("cozyworkshop");
    }

    private static IRenderedComponent<RaidCollaborationPage> RenderSettings(BunitContext context)
    {
        context
            .Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/raid-collaboration#settings");
        var page = context.Render<RaidCollaborationPage>();
        page.WaitForAssertion(() => _ = page.Find("#raid-language"));
        return page;
    }

    private static void Save(IRenderedComponent<RaidCollaborationPage> page, string outcome)
    {
        page.FindAll("button")
            .Single(button => button.TextContent.StartsWith("Save", StringComparison.Ordinal))
            .Click();
        page.WaitForAssertion(() => _ = page.Find($"[data-save-feedback='{outcome}']"));
    }

    private static IElement Button(IRenderedComponent<RaidCollaborationPage> page, string label) =>
        page.FindAll("button").Single(button => button.TextContent.Trim() == label);

    private static async Task<BunitContext> CreateContextAsync(
        SqliteBlokeBotDbFactory database,
        HostFeatureFlags features = HostFeatureFlags.RaidCollaboration
    )
    {
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "streamer-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = features,
                CreatedAtUtc = _now.UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            _ = db.RaidCollaborationSettings.Add(
                new RaidCollaborationSettings
                {
                    HostId = hostId,
                    WelcomeEnabled = true,
                    WelcomeMessage = "Welcome in, {display_name}!",
                    DeduplicationWindowMinutes = 45,
                    Language = "en",
                    EligibleCategories = "Celeste",
                    RelationshipCooldownHours = 24,
                    UpdatedAtUtc = _now.UtcDateTime,
                }
            );
            _ = db.ApprovedRaidChannels.Add(
                new ApprovedRaidChannel
                {
                    HostId = hostId,
                    Login = "cozyworkshop",
                    DisplayName = "CozyWorkshop",
                    ApprovedAtUtc = _now.UtcDateTime,
                    UpdatedAtUtc = _now.UtcDateTime,
                }
            );
            db.RaidCollaborationHistory.AddRange(
                History(hostId, "maplepixel", RaidDirection.Incoming, _now.AddHours(-1)),
                History(hostId, "cozyworkshop", RaidDirection.Outgoing, _now.AddHours(-2)),
                History(hostId, "pixelknight", RaidDirection.Incoming, _now.AddHours(-3)),
                History(hostId, "teacupmage", RaidDirection.Outgoing, _now.AddHours(-4)),
                History(hostId, "orbitalowl", RaidDirection.Incoming, _now.AddHours(-5))
            );
            _ = await db.SaveChangesAsync();
        }

        var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(
            new RaidCollaborationService(
                database,
                new OfflineRaidProvider(),
                new UnusedWelcomeSender(),
                new IdleShoutouts(),
                new AutomaticRaidShoutoutRunner(
                    database,
                    new UnusedAutomaticDelivery(),
                    TimeProvider.System
                ),
                [],
                TestEventBus.Create<AppEventKind>(),
                TimeProvider.System
            )
        );
        return context;
    }

    private static RaidCollaborationHistoryEntry History(
        int hostId,
        string login,
        RaidDirection direction,
        DateTimeOffset occurredAt
    ) =>
        new()
        {
            HostId = hostId,
            ProviderMessageId = $"{login}-{occurredAt:O}",
            Direction = direction,
            OtherTwitchUserId = $"{login}-id",
            OtherLogin = login,
            OtherDisplayName = login,
            ViewerCount = 93,
            Category = "Celeste",
            ProviderStreamId = $"{login}-stream",
            OccurredAtUtc = occurredAt.UtcDateTime,
            RecordedAtUtc = occurredAt.UtcDateTime,
        };

    private sealed class OfflineRaidProvider : IRaidCollaborationProvider
    {
        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
            int hostId,
            string login,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Offline(login)
            );

        public Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
            int hostId,
            string targetTwitchUserId,
            string targetLogin,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("The workspace tests never start a raid.");

        public Task<bool> HasRaidManagementAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(true);
    }

    private sealed class UnusedWelcomeSender : IRaidWelcomeSender
    {
        public Task<bool> SendAsync(
            int hostId,
            string hostLogin,
            string providerMessageId,
            string message,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("The workspace tests never deliver a welcome.");
    }

    private sealed class IdleShoutouts : IShoutoutDashboardOperations
    {
        public Task<ShoutoutDashboardState> LoadAsync(
            int hostId,
            string? targetLogin,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new ShoutoutDashboardState(null, new ShoutoutTargetCooldownReadiness.Unknown(), [])
            );

        public Task<ShoutoutOperationOutcome> SendAsync(
            int hostId,
            string targetLogin,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("The workspace tests never send a shoutout.");
    }

    private sealed class UnusedAutomaticDelivery : IAutomaticRaidShoutoutDelivery
    {
        public Task<AutomaticRaidShoutoutDeliveryResult> DeliverAsync(
            AutomaticRaidShoutoutDeliveryRequest request,
            CancellationToken cancellationToken
        ) =>
            throw new NotSupportedException(
                "The workspace tests never deliver an automatic shoutout."
            );
    }
}
