using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AccessListPolicyTests
{
    [Test]
    public async Task DefaultSiteAccess_AddingBlacklistedLogin_DeniesNormalizedLogin()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = CreateSiteAccessService(dbFactory);

        (await service.CanCreateHostAsync("Viewer", CancellationToken.None)).ShouldBeTrue();

        await service.AddEntryAsync(AccessListEntryKind.Blacklist, " ", CancellationToken.None);
        await service.AddEntryAsync(
            AccessListEntryKind.Blacklist,
            " Viewer ",
            CancellationToken.None
        );
        await service.AddEntryAsync(
            AccessListEntryKind.Blacklist,
            "viewer",
            CancellationToken.None
        );

        (await service.CanCreateHostAsync("viewer", CancellationToken.None)).ShouldBeFalse();
        var state = await service.LoadAdminStateAsync(CancellationToken.None);
        state.Blacklist.ShouldBe(["viewer"]);
    }

    [Test]
    public async Task WhitelistSiteAccess_CheckingUnlistedAndListedLogin_RequiresAllowEntry()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = CreateSiteAccessService(dbFactory);

        await service.SetWhitelistEnabledAsync(true, CancellationToken.None);
        (await service.CanCreateHostAsync("viewer", CancellationToken.None)).ShouldBeFalse();

        await service.AddEntryAsync(
            AccessListEntryKind.Whitelist,
            "viewer",
            CancellationToken.None
        );
        (await service.CanCreateHostAsync("viewer", CancellationToken.None)).ShouldBeTrue();

        await service.AddEntryAsync(
            AccessListEntryKind.Blacklist,
            "viewer",
            CancellationToken.None
        );
        (await service.CanCreateHostAsync("viewer", CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task StoredSiteAccessLists_SwitchingActiveMode_PreservesEntriesAndSelectsPolicy()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = CreateSiteAccessService(dbFactory);

        await service.AddEntryAsync(
            AccessListEntryKind.Whitelist,
            "viewer",
            CancellationToken.None
        );
        await service.AddEntryAsync(
            AccessListEntryKind.Blacklist,
            "viewer",
            CancellationToken.None
        );

        (await service.CanCreateHostAsync("viewer", CancellationToken.None)).ShouldBeFalse();

        await service.SetWhitelistEnabledAsync(true, CancellationToken.None);

        (await service.CanCreateHostAsync("viewer", CancellationToken.None)).ShouldBeTrue();
        var state = await service.LoadAdminStateAsync(CancellationToken.None);
        state.Whitelist.ShouldBe(["viewer"]);
        state.Blacklist.ShouldBe(["viewer"]);
    }

    [Test]
    public async Task BotAdminInRestrictedSite_CheckingAccess_BypassesLists()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = CreateSiteAccessService(dbFactory, botAdmins: ["admin"]);

        await service.SetWhitelistEnabledAsync(true, CancellationToken.None);
        await service.AddEntryAsync(AccessListEntryKind.Blacklist, "admin", CancellationToken.None);

        (await service.CanCreateHostAsync("admin", CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task DefaultHostModeratorAccess_DisablingModerators_DeniesAccess()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
        );

        (
            await service.CanModeratorAccessAsync(hostId, "moderator", CancellationToken.None)
        ).ShouldBeTrue();

        await service.DisableModeratorAccessAsync(hostId, CancellationToken.None);

        (
            await service.CanModeratorAccessAsync(hostId, "moderator", CancellationToken.None)
        ).ShouldBeFalse();

        await service.EnableModeratorAccessAsync(hostId, CancellationToken.None);

        (
            await service.CanModeratorAccessAsync(hostId, "moderator", CancellationToken.None)
        ).ShouldBeTrue();
    }

    [Test]
    public async Task RestrictedHostModeratorAccess_AddingAllowedLogin_AllowsOnlyNormalizedEntry()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
        );

        await SaveModeratorAccessAsync(service, hostId, allowModsByDefault: false);
        await service.AddEntryAsync(
            hostId,
            AccessListEntryKind.Whitelist,
            "AllowedMod",
            CancellationToken.None
        );
        await service.AddEntryAsync(
            hostId,
            AccessListEntryKind.Whitelist,
            " allowedmod ",
            CancellationToken.None
        );

        (
            await service.CanModeratorAccessAsync(hostId, "allowedmod", CancellationToken.None)
        ).ShouldBeTrue();
        (
            await service.CanModeratorAccessAsync(hostId, "othermod", CancellationToken.None)
        ).ShouldBeFalse();
        var state = await service.LoadAsync(hostId, CancellationToken.None);
        state.Whitelist.ShouldBe(["allowedmod"]);
    }

    [Test]
    public async Task StoredHostModeratorLists_SwitchingActiveMode_PreservesEntriesAndSelectsPolicy()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
        );

        await service.AddEntryAsync(
            hostId,
            AccessListEntryKind.Whitelist,
            "moderator",
            CancellationToken.None
        );
        await service.AddEntryAsync(
            hostId,
            AccessListEntryKind.Blacklist,
            "moderator",
            CancellationToken.None
        );

        (
            await service.CanModeratorAccessAsync(hostId, "moderator", CancellationToken.None)
        ).ShouldBeFalse();

        var state = await service.LoadAsync(hostId, CancellationToken.None);
        state.AllowModsByDefault.ShouldBeTrue();
        state.Whitelist.ShouldBe(["moderator"]);
        state.Blacklist.ShouldBe(["moderator"]);

        await SaveModeratorAccessAsync(service, hostId, allowModsByDefault: false);

        (
            await service.CanModeratorAccessAsync(hostId, "moderator", CancellationToken.None)
        ).ShouldBeTrue();
        (
            await service.CanModeratorAccessAsync(hostId, "othermod", CancellationToken.None)
        ).ShouldBeFalse();
        state = await service.LoadAsync(hostId, CancellationToken.None);
        state.AllowModsByDefault.ShouldBeFalse();
        state.Whitelist.ShouldBe(["moderator"]);
        state.Blacklist.ShouldBe(["moderator"]);

        await SaveModeratorAccessAsync(service, hostId, allowModsByDefault: true);

        (
            await service.CanModeratorAccessAsync(hostId, "othermod", CancellationToken.None)
        ).ShouldBeTrue();
    }

    [Test]
    public async Task DefaultAllowHostModeratorAccess_WithWhitelist_AllowsUnlistedModerator()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
        );

        await service.AddEntryAsync(
            hostId,
            AccessListEntryKind.Whitelist,
            "moderator",
            CancellationToken.None
        );

        (
            await service.CanModeratorAccessAsync(hostId, "othermod", CancellationToken.None)
        ).ShouldBeTrue();
    }

    [Test]
    public async Task HostScopedModeratorBlacklist_CheckingTwoHosts_DeniesOnlyConfiguredHost()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHostId = await SeedHostAsync(dbFactory, "first");
        var secondHostId = await SeedHostAsync(dbFactory, "second");
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
        );

        await service.AddEntryAsync(
            firstHostId,
            AccessListEntryKind.Blacklist,
            "moderator",
            CancellationToken.None
        );

        (
            await service.CanModeratorAccessAsync(firstHostId, "moderator", CancellationToken.None)
        ).ShouldBeFalse();
        (
            await service.CanModeratorAccessAsync(secondHostId, "moderator", CancellationToken.None)
        ).ShouldBeTrue();
    }

    [Test]
    public async Task HostModeratorAccessChanges_MutatingPolicy_PublishesEachChange()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var events = TestEventBus.Create<AppEventKind>();
        var eventCount = 0;
        _ = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.AccessListPolicy"),
            (_, _) =>
            {
                eventCount++;
                return ValueTask.CompletedTask;
            }
        );
        var service = new HostModAccessService(dbFactory, new HostedChannelChangeNotifier(events));

        await service.AddEntryAsync(
            hostId,
            AccessListEntryKind.Blacklist,
            "moderator",
            CancellationToken.None
        );
        await service.RemoveEntryAsync(
            hostId,
            AccessListEntryKind.Blacklist,
            "moderator",
            CancellationToken.None
        );
        await service.DisableModeratorAccessAsync(hostId, CancellationToken.None);
        await SaveModeratorAccessAsync(service, hostId, allowModsByDefault: false);

        eventCount.ShouldBe(4);
    }

    [Test]
    public async Task MissingHost_SavingModeratorAccess_ReturnsTypedFailureWithoutSettings()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
        );
        var command = ValidSaveCommand(42, allowModsByDefault: false);

        var result = await service
            .SaveModeratorAccess(command)
            .ExecuteAsync(CancellationToken.None);

        _ = result
            .Match<HostModAccessSaveFailure?>(static _ => null, static failure => failure)
            .ShouldBeOfType<HostModAccessSaveFailure.HostNotFound>();
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.HostModAccessSettings.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ValidCommand_SavingModeratorAccess_PersistsAndReportsNotification()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var events = TestEventBus.Create<AppEventKind>();
        _ = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.HostConfig.Success"),
            static (_, _) => ValueTask.CompletedTask
        );
        var service = new HostModAccessService(dbFactory, new HostedChannelChangeNotifier(events));
        var command = ValidSaveCommand(hostId, allowModsByDefault: false);

        var result = await service
            .SaveModeratorAccess(command)
            .ExecuteAsync(CancellationToken.None);

        var saved = result.Match(
            static success => success,
            static failure => throw new InvalidOperationException(failure.Message)
        );
        saved.HostId.ShouldBe(hostId);
        _ = saved.Mode.ShouldBeOfType<HostModeratorAccessMode.AllowlistOnly>();
        saved.NotifiedObserverCount.ShouldBe(1);
        (
            await service.LoadAsync(hostId, CancellationToken.None)
        ).AllowModsByDefault.ShouldBeFalse();
    }

    [Test]
    public async Task RuntimeNotificationFailure_SavingModeratorAccess_RestoresPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var eventing = TestEventBus.CreateContinueAndRecord<AppEventKind>();
        var events = eventing.Events;
        var notificationCount = 0;
        _ = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.HostConfig.Runtime"),
            (_, _) =>
            {
                notificationCount++;
                return notificationCount == 1
                    ? ValueTask.FromException(new InvalidOperationException("runtime unavailable"))
                    : ValueTask.CompletedTask;
            }
        );
        var service = new HostModAccessService(dbFactory, new HostedChannelChangeNotifier(events));
        var command = ValidSaveCommand(hostId, allowModsByDefault: false);

        var result = await service
            .SaveModeratorAccess(command)
            .ExecuteAsync(CancellationToken.None);

        result
            .Match<HostModAccessSaveFailure?>(_ => null, failure => failure)
            .ShouldBe(new HostModAccessSaveFailure.RuntimeNotificationFailed(1, 0));
        notificationCount.ShouldBe(2);
        eventing
            .Reports.ShouldHaveSingleItem()
            .FailureType.ShouldBe(typeof(InvalidOperationException).FullName);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            (await db.HostModAccessSettings.AnyAsync(x => x.HostId == hostId)).ShouldBeFalse();
        }

        (await service.LoadAsync(hostId, CancellationToken.None)).AllowModsByDefault.ShouldBeTrue();
    }

    private static SiteAccessService CreateSiteAccessService(
        SqliteBlokeBotDbFactory dbFactory,
        string[]? botAdmins = null
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        return new SiteAccessService(
            dbFactory,
            new BotAdminService(
                BotAdminSettings.FromOptions(new BlokeBotOptions { BotAdmins = botAdmins ?? [] })
            ),
            new SiteAccessChangeNotifier(events)
        );
    }

    private static async Task SaveModeratorAccessAsync(
        HostModAccessService service,
        int hostId,
        bool allowModsByDefault
    )
    {
        var result = await service
            .SaveModeratorAccess(ValidSaveCommand(hostId, allowModsByDefault))
            .ExecuteAsync(CancellationToken.None);
        _ = result.Match(
            static _ => true,
            static failure => throw new InvalidOperationException(failure.Message)
        );
    }

    private static HostModAccessSaveCommand ValidSaveCommand(int hostId, bool allowModsByDefault) =>
        HostModAccessSaveValidator
            .Validate(hostId, HostModeratorAccessMode.FromAllowModsByDefault(allowModsByDefault))
            .Match(
                static command => command,
                static errors => throw new InvalidOperationException(errors[0].Message)
            );

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
