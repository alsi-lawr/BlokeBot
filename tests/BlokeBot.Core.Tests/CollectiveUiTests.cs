using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Collectives;
using BlokeBot.Core.Features.RaidCollaboration;
using BlokeBot.Persistence.Models;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class CollectiveUiTests
{
    [Test]
    public async Task SignedInDirectRoute_WhenDisabledShowsRecoveryWithoutRetainedData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.None,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            _ = db.Collectives.Add(
                new Collective
                {
                    PublicId = Guid.NewGuid(),
                    CreationOperationId = Guid.NewGuid(),
                    Name = "RETAINED PRIVATE COLLECTIVE",
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Memberships =
                    [
                        new()
                        {
                            HostId = hostId,
                            Role = CollectiveMembershipRole.Coordinator,
                            Status = CollectiveMembershipStatus.Active,
                            AcceptWorkAfterUtc = DateTime.UtcNow,
                            InvitedAtUtc = DateTime.UtcNow,
                            RespondedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow,
                        },
                    ],
                    Audits =
                    [
                        new()
                        {
                            OperationId = "private-audit-marker",
                            Action = CollectiveAuditAction.Created,
                            ActingHostId = hostId,
                            AffectedHostId = hostId,
                            ActorLogin = "PRIVATE ACTOR",
                            OccurredAtUtc = DateTime.UtcNow,
                        },
                    ],
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var service = new CollectiveService(
            database,
            new UnavailableRaidProvider(),
            TimeProvider.System
        );
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var cut = context.Render<CollectivesPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-collectives-disabled-recovery]")
                .TextContent.ShouldContain("Channel setup");
            cut.Markup.ShouldContain("Nothing missed is repeated");
            cut.Markup.ShouldNotContain("RETAINED PRIVATE COLLECTIVE");
            cut.Markup.ShouldNotContain("PRIVATE ACTOR");
            cut.FindAll("input[type='checkbox']").ShouldBeEmpty();
        });
    }

    [Test]
    public void CollectivesRoute_HasUsefulOptInAuthorityPrivacyAndRecoveryHelp() =>
        PageHelpButton.HasUsefulHelpForPath("/collectives").ShouldBeTrue();

    [Test]
    public async Task WorkflowFragments_KeepUrlSelectionAndContentInSyncThroughHistory()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var workspace = await SeedWorkspaceAsync(database);
        using var context = CreateContext(database, workspace);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/collectives#goal");

        var page = context.Render<CollectivesPage>();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/collectives#goal");
            Tab(page, "goal").GetAttribute("aria-selected").ShouldBe("true");
            page.Find("#collective-workspace-goal-panel")
                .TextContent.ShouldContain("Cross-channel goal coordination");
        });
        page.FindAll("[role='tab']")
            .Select(tab => tab.GetAttribute("href"))
            .ShouldBe(["#tournament", "#raid", "#goal"]);

        Tab(page, "raid").Click();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/collectives#raid");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
            Tab(page, "raid").GetAttribute("aria-selected").ShouldBe("true");
            page.Find("#collective-workspace-raid-panel").TextContent.ShouldContain("Raid relay");
        });

        navigation.NavigateTo("/collectives#goal");

        page.WaitForAssertion(() =>
        {
            Tab(page, "goal").GetAttribute("aria-selected").ShouldBe("true");
            page.Find("#collective-workspace-goal-panel")
                .TextContent.ShouldContain("Cross-channel goal coordination");
        });

        navigation.NavigateTo("/collectives#raid");

        page.WaitForAssertion(() =>
        {
            Tab(page, "raid").GetAttribute("aria-selected").ShouldBe("true");
            page.Find("#collective-workspace-raid-panel").TextContent.ShouldContain("Raid relay");
        });
    }

    [Test]
    public async Task BareCollectivesPath_NormalizesToTheTournamentFragmentWithoutAHistoryEntry()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var workspace = await SeedWorkspaceAsync(database);
        using var context = CreateContext(database, workspace);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/collectives");

        var page = context.Render<CollectivesPage>();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/collectives#tournament");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
            Tab(page, "tournament").GetAttribute("aria-selected").ShouldBe("true");
        });
        var valueSelector = page.Find(".collective-summary__select");
        valueSelector.HasAttribute("href").ShouldBeFalse();
        valueSelector.GetAttribute("role").ShouldBeNull();
    }

    [Test]
    public async Task LocalSettings_RenderOneControlSetInOneStickySaveBoundaryAndTabOrder()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var workspace = await SeedWorkspaceAsync(database);
        using var context = CreateContext(database, workspace);

        var page = RenderAt(context, "goal");

        page.FindAll("#collective-notification").Count.ShouldBe(1);
        page.FindAll("#collective-goal-source").Count.ShouldBe(1);
        page.FindAll("[data-collective-local-settings]").Count.ShouldBe(1);
        page.FindAll("[data-save-scope]").Count.ShouldBe(1);
        page.FindAll("button")
            .Count(button => button.TextContent.Trim() == "Save local settings")
            .ShouldBe(1);
        LocalSettingsTabOrder(page)
            .ShouldBe([
                "collective-goal-source",
                "Use this source",
                "collective-notification",
                "Save local settings",
            ]);

        var region = page.Find("[data-save-scope]");
        region.GetAttribute("data-save-scope").ShouldBe("editor");
        var boundary = region.Closest("[data-sticky-save-scope]").ShouldNotBeNull();
        boundary.ClassList.ShouldContain("collective-sidecar__body");
        _ = boundary.QuerySelector("[data-collective-local-settings]").ShouldNotBeNull();
        region.Closest("details").ShouldBeNull();
        region.Closest("[hidden]").ShouldBeNull();
        page.FindAll(".collective-sidecar__body [inert]").ShouldBeEmpty();
        page.Find(".collective-mobile-management").TextContent.ShouldContain("partner");
    }

    [Test]
    public void NarrowLayoutCss_KeepsTheOneLocalSettingsSetReachableAtOrBelow64Rem()
    {
        var css = Whitespace()
            .Replace(File.ReadAllText(Path.Combine(StyleSourceRoot(), "collectives.css")), " ");
        var narrow = css[css.IndexOf("@media (max-width: 64rem)", StringComparison.Ordinal)..];
        narrow = narrow[..narrow.IndexOf("@media (max-width: 40rem)", StringComparison.Ordinal)];

        css.ShouldNotContain("container-type");
        css.ShouldContain(".collective-local-settings__head { display: none; }");
        narrow.ShouldContain(".collective-members { display: none; }");
        narrow.ShouldContain(
            ".collective-sidecar { border-top: 1px solid var(--app-border); order: 2; }"
        );
        narrow.ShouldContain(
            ".collective-sidecar > header, .collective-sidecar__authority > .collective-sidecar__label, .collective-sidecar__authority > h3, .collective-sidecar__authority > .collective-private, .collective-sidecar__body > .collective-sidecar__shared, .collective-sidecar__body > .collective-sidecar__audit { display: none; }"
        );
        narrow.ShouldContain(
            ".collective-local-settings__head { align-items: center; display: grid;"
        );
        Regex.Count(narrow, "display: none").ShouldBe(2);
    }

    [Test]
    public async Task GoalSource_AppearsOnlyOnTheGoalFragmentWithAnExistingGoal()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var workspace = await SeedWorkspaceAsync(database);

        foreach (var workflow in new[] { "tournament", "raid" })
        {
            using var other = CreateContext(database, workspace);
            var page = RenderAt(other, workflow);
            page.FindAll("#collective-goal-source").ShouldBeEmpty();
            page.FindAll("button")
                .ShouldNotContain(button => button.TextContent.Trim() == "Use this source");
            page.FindAll("#collective-notification").Count.ShouldBe(1);
        }

        using var goal = CreateContext(database, workspace);
        RenderAt(goal, "goal").FindAll("#collective-goal-source").Count.ShouldBe(1);
    }

    [Test]
    public async Task GoalAbsent_HidesTheSourceControlAndStillSavesNotifications()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var workspace = await SeedWorkspaceAsync(database, withGoal: false);
        using var context = CreateContext(database, workspace);

        var page = RenderAt(context, "goal");

        page.FindAll("#collective-goal-source").ShouldBeEmpty();
        page.Find("#collective-workspace-goal-panel")
            .TextContent.ShouldContain("No shared goal is configured.");
        LocalSettingsTabOrder(page).ShouldBe(["collective-notification", "Save local settings"]);

        SetNotification(page, CollectiveLocalNotification.ModeratorsAndOwner);
        Save(page);

        page.WaitForAssertion(() =>
            page.Find("[role='status']").TextContent.ShouldContain("Collective saved.")
        );
        (await StoredNotificationAsync(database)).ShouldBe(
            CollectiveLocalNotification.ModeratorsAndOwner
        );
    }

    [Test]
    public async Task NotificationOnlyChange_SavesThroughSaveLocalSettingsAndLeavesTheSourceAlone()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var workspace = await SeedWorkspaceAsync(database);
        using var context = CreateContext(database, workspace);
        var page = RenderAt(context, "goal");

        SaveButton(page).HasAttribute("disabled").ShouldBeTrue();
        SetNotification(page, CollectiveLocalNotification.ModeratorsAndOwner);

        SaveButton(page).HasAttribute("disabled").ShouldBeFalse();
        Save(page);

        page.WaitForAssertion(() =>
            page.Find("[role='status']").TextContent.ShouldContain("Collective saved.")
        );
        (await StoredNotificationAsync(database)).ShouldBe(
            CollectiveLocalNotification.ModeratorsAndOwner
        );
        (await StoredGoalSourceAsync(database)).ShouldBeNull();
    }

    [Test]
    public async Task GoalSourceOnlyChange_SavesThroughSetGoalSourceAndLeavesNotificationsAlone()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var workspace = await SeedWorkspaceAsync(database);
        using var context = CreateContext(database, workspace);
        var page = RenderAt(context, "goal");

        SourceAction(page).HasAttribute("disabled").ShouldBeTrue();
        page.Find("#collective-goal-source").Change(workspace.BountyPublicId.ToString());
        SourceAction(page).Click();

        page.WaitForAssertion(() =>
            page.Find("[role='status']").TextContent.ShouldContain("Collective saved.")
        );
        (await StoredGoalSourceAsync(database)).ShouldBe(workspace.BountyPublicId);
        (await StoredNotificationAsync(database)).ShouldBeNull();
        SaveButton(page).HasAttribute("disabled").ShouldBeTrue();
    }

    [Test]
    public async Task MixedChanges_ReachTheirOwnServiceCallsAndKeepBothEditsPending()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var workspace = await SeedWorkspaceAsync(database);
        using var context = CreateContext(database, workspace);
        var page = RenderAt(context, "goal");

        SetNotification(page, CollectiveLocalNotification.ModeratorsAndOwner);
        page.Find("#collective-goal-source").Change(workspace.BountyPublicId.ToString());
        SourceAction(page).Click();

        page.WaitForAssertion(() =>
            page.Find("[role='status']").TextContent.ShouldContain("Collective saved.")
        );
        (await StoredGoalSourceAsync(database)).ShouldBe(workspace.BountyPublicId);
        (await StoredNotificationAsync(database)).ShouldBeNull();
        SaveButton(page).HasAttribute("disabled").ShouldBeFalse();

        Save(page);

        page.WaitForAssertion(() => SaveButton(page).HasAttribute("disabled").ShouldBeTrue());
        (await StoredNotificationAsync(database)).ShouldBe(
            CollectiveLocalNotification.ModeratorsAndOwner
        );
        (await StoredGoalSourceAsync(database)).ShouldBe(workspace.BountyPublicId);
    }

    private static IRenderedComponent<CollectivesPage> RenderAt(
        BunitContext context,
        string workflow
    )
    {
        context
            .Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/collectives#{workflow}");
        var page = context.Render<CollectivesPage>();
        page.WaitForAssertion(() =>
            Tab(page, workflow).GetAttribute("aria-selected").ShouldBe("true")
        );
        return page;
    }

    private static IElement Tab(IRenderedComponent<CollectivesPage> page, string key) =>
        page.Find($"#collective-workspace-{key}-tab");

    private static IElement SaveButton(IRenderedComponent<CollectivesPage> page) =>
        page.Find("[data-save-scope] button");

    private static IElement SourceAction(IRenderedComponent<CollectivesPage> page) =>
        page.FindAll("[data-collective-local-settings] button")
            .Single(button => button.TextContent.Trim() == "Use this source");

    private static void SetNotification(
        IRenderedComponent<CollectivesPage> page,
        CollectiveLocalNotification notification
    ) => page.Find("#collective-notification").Change(notification.ToString());

    private static void Save(IRenderedComponent<CollectivesPage> page) => SaveButton(page).Click();

    private static string[] LocalSettingsTabOrder(IRenderedComponent<CollectivesPage> page) =>
        [
            .. page.FindAll(".collective-sidecar__body select, .collective-sidecar__body button")
                .Select(element =>
                    element.TagName == "SELECT" ? element.Id! : element.TextContent.Trim()
                ),
        ];

    private static async Task<CollectiveLocalNotification?> StoredNotificationAsync(
        SqliteBlokeBotDbFactory database
    )
    {
        await using var db = await database.CreateDbContextAsync();
        return (await db.CollectiveLocalSettings.SingleOrDefaultAsync())?.Notification;
    }

    private static async Task<Guid?> StoredGoalSourceAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var total = await db.CollectiveGoalHostTotals.SingleOrDefaultAsync(value =>
            value.SourceBountyPublicId != Guid.Empty
        );
        return total?.SourceBountyPublicId;
    }

    private static BunitContext CreateContext(
        SqliteBlokeBotDbFactory database,
        SeededWorkspace workspace
    )
    {
        var context = UiTestContextFactory.Create(database, workspace.HostId);
        _ = context.Services.AddSingleton(workspace.Service);
        return context;
    }

    private static async Task<SeededWorkspace> SeedWorkspaceAsync(
        SqliteBlokeBotDbFactory database,
        bool withGoal = true
    )
    {
        var hostId = await SeedCollectiveHostAsync(database, "streamer");
        var partnerId = await SeedCollectiveHostAsync(database, "partner");
        var bountyPublicId = Guid.NewGuid();
        await SeedPublicBountyAsync(database, hostId, bountyPublicId);
        var service = new CollectiveService(
            database,
            new UnavailableRaidProvider(),
            TimeProvider.System
        );
        var authority = new CollectiveAuthority(hostId, "streamer-id", "streamer", true);
        var created = (
            await service.CreateAsync(new(Guid.NewGuid(), "Cosy Circuit", authority), default)
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        _ = await service.InviteAsync(
            new(Guid.NewGuid(), created.CollectiveId, partnerId, authority),
            default
        );
        _ = await service.AcceptInvitationAsync(
            new(
                Guid.NewGuid(),
                created.CollectiveId,
                new(partnerId, "partner-id", "partner", true)
            ),
            default
        );
        if (withGoal)
        {
            _ = (
                await service.ConfigureGoalAsync(
                    new(
                        Guid.NewGuid(),
                        created.CollectiveId,
                        "Comfort kits",
                        "kit",
                        20,
                        DateTime.UtcNow.AddDays(3),
                        [],
                        authority
                    ),
                    default
                )
            ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        }
        return new(hostId, service, bountyPublicId);
    }

    private static async Task<int> SeedCollectiveHostAsync(
        SqliteBlokeBotDbFactory database,
        string login
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures =
                HostFeatureFlags.Collectives | HostFeatureFlags.Bounties | HostFeatureFlags.Points,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedPublicBountyAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        Guid publicId
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.Bounties.Add(
            new Bounty
            {
                HostId = hostId,
                PublicId = publicId,
                CreationOperationId = Guid.NewGuid(),
                CreationFingerprint = Guid.NewGuid().ToString("N"),
                Title = "Comfort kit fund",
                Status = BountyStatus.Funding,
                Visibility = BountyVisibility.Public,
                FundingTarget = "20",
                PledgedAmount = "6",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(5),
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static string StyleSourceRoot([CallerFilePath] string testFile = "") =>
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(testFile)!,
                "..",
                "..",
                "src",
                "BlokeBot.Core",
                "Styles",
                "features"
            )
        );

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed record SeededWorkspace(
        int HostId,
        CollectiveService Service,
        Guid BountyPublicId
    );

    private sealed class UnavailableRaidProvider : IRaidCollaborationProvider
    {
        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
            int hostId,
            string login,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Unavailable()
            );

        public Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
            int hostId,
            string targetTwitchUserId,
            string targetLogin,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<ConfirmedRaidStartOutcome>(
                new ConfirmedRaidStartOutcome.ProviderRejected()
            );

        public Task<bool> HasRaidManagementAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(false);
    }
}
