using AngleSharp.Dom;
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
            cut.Markup.ShouldNotContain("RETAINED PRIVATE COLLECTIVE");
            cut.Markup.ShouldNotContain("PRIVATE ACTOR");
        });
    }

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
            _ = page.Find("#collective-workspace-goal-panel");
        });

        Tab(page, "raid").Click();

        page.WaitForAssertion(() =>
        {
            navigation.Uri.ShouldEndWith("/collectives#raid");
            navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
            Tab(page, "raid").GetAttribute("aria-selected").ShouldBe("true");
            _ = page.Find("#collective-workspace-raid-panel");
        });

        navigation.NavigateTo("/collectives#goal");

        page.WaitForAssertion(() =>
        {
            Tab(page, "goal").GetAttribute("aria-selected").ShouldBe("true");
            _ = page.Find("#collective-workspace-goal-panel");
        });

        navigation.NavigateTo("/collectives#raid");

        page.WaitForAssertion(() =>
        {
            Tab(page, "raid").GetAttribute("aria-selected").ShouldBe("true");
            _ = page.Find("#collective-workspace-raid-panel");
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
    }

    [Test]
    public async Task GoalAbsent_HidesTheSourceControlAndStillSavesNotifications()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var workspace = await SeedWorkspaceAsync(database, withGoal: false);
        using var context = CreateContext(database, workspace);

        var page = RenderAt(context, "goal");

        SetNotification(page, CollectiveLocalNotification.ModeratorsAndOwner);
        Save(page);

        page.WaitForAssertion(() => _ = page.Find("[role='status']"));
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

        page.WaitForAssertion(() => _ = page.Find("[role='status']"));
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

        page.WaitForAssertion(() => _ = page.Find("[role='status']"));
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

        page.WaitForAssertion(() => _ = page.Find("[role='status']"));
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

    [Test]
    public async Task PendingNotificationEdit_ReportsTheConflictWhenAnotherSessionSavedFirst()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var workspace = await SeedWorkspaceAsync(database);
        using var context = CreateContext(database, workspace);
        var page = RenderAt(context, "goal");

        SetNotification(page, CollectiveLocalNotification.ModeratorsAndOwner);
        _ = (
            await workspace.Service.SaveLocalSettingsAsync(
                new(
                    Guid.NewGuid(),
                    workspace.CollectiveId,
                    0,
                    CollectiveLocalNotification.Moderators,
                    new(workspace.HostId, "streamer-id", "streamer", true)
                ),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        page.Find("#collective-goal-source").Change(workspace.BountyPublicId.ToString());
        SourceAction(page).Click();

        page.WaitForAssertion(() => _ = page.Find("[role='status']"));
        Save(page);

        page.WaitForAssertion(() => _ = page.Find("[role='alert']"));
        (await StoredNotificationAsync(database)).ShouldBe(CollectiveLocalNotification.Moderators);
        (await StoredNotificationRevisionAsync(database)).ShouldBe(1);
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

    private static async Task<CollectiveLocalNotification?> StoredNotificationAsync(
        SqliteBlokeBotDbFactory database
    )
    {
        await using var db = await database.CreateDbContextAsync();
        return (await db.CollectiveLocalSettings.SingleOrDefaultAsync())?.Notification;
    }

    private static async Task<long?> StoredNotificationRevisionAsync(
        SqliteBlokeBotDbFactory database
    )
    {
        await using var db = await database.CreateDbContextAsync();
        return (await db.CollectiveLocalSettings.SingleOrDefaultAsync())?.Revision;
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
        return new(hostId, created.CollectiveId, service, bountyPublicId);
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

    private sealed record SeededWorkspace(
        int HostId,
        CollectiveId CollectiveId,
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

        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelByIdAsync(
            int hostId,
            string twitchUserId,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Unavailable()
            );

        public Task<FollowedLiveChannelsOutcome> LoadFollowedLiveChannelsAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<FollowedLiveChannelsOutcome>(
                new FollowedLiveChannelsOutcome.Unavailable()
            );

        public Task<bool> HasFollowedLiveAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(false);

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
