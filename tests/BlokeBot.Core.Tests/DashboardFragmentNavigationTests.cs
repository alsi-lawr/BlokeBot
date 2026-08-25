using AngleSharp.Html.Dom;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Core.Hosting;
using BlokeBot.Persistence.Models;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class DashboardFragmentNavigationTests
{
    [Test]
    public async Task OverlaysRefresh_RestoresTheFragmentTabAndMountsOnlyThatPanel()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedOverlayHostAsync(database);
        await using var context = CreateOverlayContext(database, hostId);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/overlays#cues");

        var page = context.Render<OverlaysPage>();

        page.WaitForAssertion(() =>
        {
            page.Find("#overlays-cues-tab").GetAttribute("aria-selected").ShouldBe("true");
            page.Find("#overlays-cues-panel").GetAttribute("hidden").ShouldBeNull();
            page.FindAll("#overlays-sources-panel").ShouldBeEmpty();
            page.FindAll("#overlays-media-panel").ShouldBeEmpty();
        });
    }

    [Test]
    public async Task OverlaysBarePath_NormalizesToSources()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedOverlayHostAsync(database);
        await using var context = CreateOverlayContext(database, hostId);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/overlays");

        var page = context.Render<OverlaysPage>();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/overlays#sources");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
            page.Find("#overlays-sources-tab").GetAttribute("aria-selected").ShouldBe("true");
        });
    }

    [Test]
    public async Task OverlayTabSwitches_KeepVisitedPanelsMountedWithUnsavedState()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedOverlayHostAsync(database);
        await using var context = CreateOverlayContext(database, hostId);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/overlays#cues");
        var page = context.Render<OverlaysPage>();
        page.WaitForAssertion(() => page.Find("#cue-name").ShouldNotBeNull());

        page.Find("#cue-name").Input("Unsaved cue draft");
        page.Find("#overlays-media-tab").Click();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/overlays#media");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
            page.Find("#overlays-media-panel").GetAttribute("hidden").ShouldBeNull();
            _ = page.Find("#overlays-cues-panel").GetAttribute("hidden").ShouldNotBeNull();
            _ = page.Find("#media-name").ShouldNotBeNull();
            page.FindAll("#overlays-sources-panel").ShouldBeEmpty();
        });

        navigation.NavigateTo("/overlays#cues");

        page.WaitForAssertion(() =>
        {
            page.Find("#overlays-cues-panel").GetAttribute("hidden").ShouldBeNull();
            _ = page.Find("#overlays-media-panel").GetAttribute("hidden").ShouldNotBeNull();
            ((IHtmlInputElement)page.Find("#cue-name")).Value.ShouldBe("Unsaved cue draft");
        });
    }

    [Test]
    public async Task GuessingRefresh_RestoresHistoryAndLeaderboardFragmentsWithLazyLoads()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedGuessingAsync(dbFactory);
        await using var context = CreateGuessingContext(dbFactory, hostId);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/guessing#history");

        var page = context.Render<GuessingDashboard>();

        page.WaitForAssertion(() =>
        {
            page.Find("#guessing-history-tab").GetAttribute("aria-selected").ShouldBe("true");
            _ = page.Find("#guessing-history-panel");
            page.FindAll("#guessing-live-panel").ShouldBeEmpty();
        });

        navigation.NavigateTo("/guessing#leaderboard");

        page.WaitForAssertion(() =>
        {
            page.Find("#guessing-leaderboard-tab").GetAttribute("aria-selected").ShouldBe("true");
            _ = page.Find("#guessing-leaderboard-panel");
        });

        navigation.NavigateTo("/guessing#live");

        page.WaitForAssertion(() =>
        {
            page.Find("#guessing-live-tab").GetAttribute("aria-selected").ShouldBe("true");
            _ = page.Find("#guessing-live-panel").ShouldNotBeNull();
        });
    }

    [Test]
    public async Task GuessingBarePath_NormalizesToLiveAndSelectionPushesHistory()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedGuessingAsync(dbFactory);
        await using var context = CreateGuessingContext(dbFactory, hostId);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/guessing");

        var page = context.Render<GuessingDashboard>();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/guessing#live");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
        });

        page.Find("#guessing-history-tab").Click();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/guessing#history");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
            page.Find("#guessing-history-tab").GetAttribute("aria-selected").ShouldBe("true");
        });
    }

    [Test]
    public async Task CustomCommandsRefresh_RestoresTheMessageLibraryFragment()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedCustomCommandsAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/custom-commands/settings#message-library");

        var page = context.Render<CustomCommandSettingsPage>();

        page.WaitForAssertion(() =>
        {
            page.Find(".studio").GetAttribute("data-active-fragment").ShouldBe("message-library");
            page.FindAll("[data-selected-editor]").ShouldBeEmpty();
        });
    }

    [Test]
    public async Task CustomCommandsBareOrUnknownFragment_NormalizesToCommandsAndPushesOnSelect()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedCustomCommandsAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/custom-commands/settings#not-a-tab");

        var page = context.Render<CustomCommandSettingsPage>();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/custom-commands/settings#commands");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
            page.Find(".studio").GetAttribute("data-active-fragment").ShouldBe("commands");
        });

        page.Find("#custom-command-message-library-tab").Click();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/custom-commands/settings#message-library");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
            page.Find(".studio").GetAttribute("data-active-fragment").ShouldBe("message-library");
            page.FindAll("[data-selected-editor]").ShouldBeEmpty();
        });

        navigation.NavigateTo("/custom-commands/settings#commands");

        page.WaitForAssertion(() =>
        {
            page.Find(".studio").GetAttribute("data-active-fragment").ShouldBe("commands");
            page.FindAll("[data-selected-editor='reply']").ShouldBeEmpty();
        });
    }

    [Test]
    public async Task PlayQueueBarePath_NormalizesToSetupAndSelectingRunPushesOneFragmentEntry()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreatePlayQueueContextAsync(database);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/queues");

        var page = context.Render<PlayQueuesPage>();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/queues#setup");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
            page.Find("#queue-pane-setup-tab").GetAttribute("aria-selected").ShouldBe("true");
        });

        page.Find("#queue-pane-run-tab").Click();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/queues#run");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
            page.Find("#queue-pane-run-tab").GetAttribute("aria-selected").ShouldBe("true");
            _ = page.Find("#queue-pane-run-panel");
        });

        navigation.NavigateTo("/queues#setup");

        page.WaitForAssertion(() =>
        {
            page.Find("#queue-pane-setup-tab").GetAttribute("aria-selected").ShouldBe("true");
            _ = page.Find("#queue-slug");
        });
    }

    [Test]
    public async Task PlayQueueDirectRunFragment_OpensTheRunPaneOnFirstRender()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreatePlayQueueContextAsync(database);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/queues#run");

        var page = context.Render<PlayQueuesPage>();

        page.WaitForAssertion(() =>
        {
            page.Find("#queue-pane-run-tab").GetAttribute("aria-selected").ShouldBe("true");
            page.Find("#queue-pane-run-panel")
                .GetAttribute("aria-labelledby")
                .ShouldBe("queue-pane-run-tab");
        });
    }

    [Test]
    public async Task RequestBoardFragments_LinkTheSetupAndReviewPanes()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var context = await CreateRequestBoardContextAsync(database);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/requests#review");

        var page = context.Render<RequestBoardsPage>();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/requests#review");
            page.Find("#request-board-pane-review-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
            page.Find("#request-board-pane-review-panel").GetAttribute("role").ShouldBe("tabpanel");
        });

        page.Find("#request-board-pane-setup-tab").Click();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/requests#setup");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
            page.Find("#request-board-pane-setup-tab")
                .GetAttribute("aria-selected")
                .ShouldBe("true");
        });
    }

    private static async Task<BunitContext> CreatePlayQueueContextAsync(
        SqliteBlokeBotDbFactory database
    )
    {
        var hostId = await SeedFeatureHostAsync(database);
        var queues = new PlayQueueService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = await queues.ConfigureAsync(
            hostId,
            new ConfigurePlayQueueCommand(
                "squad",
                "Community squad",
                "Example game",
                4,
                true,
                PlayQueueSelectionMode.JoinOrder,
                false,
                120,
                30,
                15,
                [],
                []
            ),
            CancellationToken.None
        );
        var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(queues);
        _ = context.Services.AddSingleton<IPrivateLobbyDelivery>(new UnusedPrivateLobbyDelivery());
        return context;
    }

    private static async Task<BunitContext> CreateRequestBoardContextAsync(
        SqliteBlokeBotDbFactory database
    )
    {
        var hostId = await SeedFeatureHostAsync(database);
        var boards = new RequestBoardService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = await boards.ConfigureAsync(
            hostId,
            new ConfigureRequestBoardCommand(
                "clips",
                "Clip reviews",
                "Share a clip.",
                true,
                "0",
                RequestBoardRefundPolicy.Never,
                3,
                0,
                5,
                true,
                [
                    new RequestBoardFieldCommand(
                        "clip",
                        "Clip",
                        RequestBoardFieldKind.Url,
                        true,
                        2048
                    ),
                ]
            ),
            CancellationToken.None
        );
        var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(boards);
        return context;
    }

    private static async Task<int> SeedFeatureHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class UnusedPrivateLobbyDelivery : IPrivateLobbyDelivery
    {
        public Task<IReadOnlyList<PrivateLobbyDeliveryOutcome>> DeliverAsync(
            string hostLogin,
            string lobbyCode,
            IReadOnlyList<PrivateLobbyRecipient> recipients,
            CancellationToken ct
        ) => throw new NotSupportedException("Fragment navigation never delivers a lobby message.");
    }

    private static BunitContext CreateOverlayContext(SqliteBlokeBotDbFactory database, int hostId)
    {
        var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton<IModeratorAuthorityService>(
            new GrantedModeratorAuthority()
        );
        _ = context
            .Services.AddBlokeBotPlayWithViewers()
            .AddBlokeBotBounties()
            .AddBlokeBotCommunityProgression()
            .AddBlokeBotOverlays();
        return context;
    }

    private static BunitContext CreateGuessingContext(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        var context = UiTestContextFactory.Create(dbFactory, hostId);
        _ = context.Services.AddSingleton<IPublicChatMessageSender>(new IgnoringChatSender());
        _ = context.Services.AddSingleton<GuessingDashboardService>();
        _ = context.Services.AddSingleton<GuessingHistoryService>();
        _ = context.Services.AddSingleton<GuessingChangeNotifier>();
        _ = context.Services.AddSingleton<PointBalanceService>();
        _ = context.Services.AddSingleton<PointsChangeNotifier>();
        _ = context.Services.AddSingleton<GuessingRoundService>();
        return context;
    }

    private static async Task<int> SeedOverlayHostAsync(
        SqliteBlokeBotDbFactory database,
        HostFeatureFlags features = HostFeatureFlags.Overlays
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = features,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<int> SeedGuessingAsync(
        SqliteBlokeBotDbFactory dbFactory,
        HostFeatureFlags features = HostFeatureFlags.All
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = features,
            CreatedAtUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc),
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        _ = db.Profiles.Add(
            new GuessRoundProfile
            {
                HostId = host.Id,
                Name = "Private round",
                Slug = "private-round",
                IsDefault = true,
                ReplySettings = ReplySettingsMapper.ToEntity(GuessingDefaults.Replies()),
            }
        );
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<int> SeedCustomCommandsAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class GrantedModeratorAuthority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }

    private sealed class IgnoringChatSender : IPublicChatMessageSender
    {
        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PublicChatSendOutcome>(new PublicChatSendOutcome.Accepted());
    }
}
