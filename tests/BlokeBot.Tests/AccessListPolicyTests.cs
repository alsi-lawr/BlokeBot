using BlokeBot.Eventing;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class AccessListPolicyTests
{
    [Test]
    public async Task Site_access_allows_by_default_and_blacklist_precedes_default_allow()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = CreateSiteAccessService(dbFactory);

        (await service.CanCreateHostAsync("Viewer", CancellationToken.None)).ShouldBeTrue();

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
    public async Task Site_access_whitelist_mode_requires_allow_entry()
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
    public async Task Site_access_mode_selects_active_list_without_removing_inactive_entries()
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
    public async Task Site_access_bot_admin_bypasses_access_list_policy()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = CreateSiteAccessService(dbFactory, botAdmins: ["admin"]);

        await service.SetWhitelistEnabledAsync(true, CancellationToken.None);
        await service.AddEntryAsync(AccessListEntryKind.Blacklist, "admin", CancellationToken.None);

        (await service.CanCreateHostAsync("admin", CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task Host_moderator_access_allows_by_default_until_disabled()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(new EventBus<AppEventKind>())
        );

        (
            await service.CanModeratorAccessAsync(hostId, "moderator", CancellationToken.None)
        ).ShouldBeTrue();

        await service.SetModsEnabledAsync(hostId, false, CancellationToken.None);

        (
            await service.CanModeratorAccessAsync(hostId, "moderator", CancellationToken.None)
        ).ShouldBeFalse();
    }

    [Test]
    public async Task Host_moderator_restrictive_mode_requires_allow_entry()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(new EventBus<AppEventKind>())
        );

        await service.SetAllowModsByDefaultAsync(hostId, false, CancellationToken.None);
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
    public async Task Host_moderator_mode_selects_active_list_without_removing_inactive_entries()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(new EventBus<AppEventKind>())
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

        await service.SetAllowModsByDefaultAsync(hostId, false, CancellationToken.None);

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
    }

    [Test]
    public async Task Host_moderator_default_allow_ignores_whitelist_entries()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(new EventBus<AppEventKind>())
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
    public async Task Host_moderator_access_is_scoped_to_host()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHostId = await SeedHostAsync(dbFactory, "first");
        var secondHostId = await SeedHostAsync(dbFactory, "second");
        var service = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(new EventBus<AppEventKind>())
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
    public async Task Host_moderator_access_changes_notify_hosted_channel_changes()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var events = new EventBus<AppEventKind>();
        var eventCount = 0;
        events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            _ =>
            {
                eventCount++;
                return Task.CompletedTask;
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
        await service.SetModsEnabledAsync(hostId, false, CancellationToken.None);
        await service.SetAllowModsByDefaultAsync(hostId, false, CancellationToken.None);

        eventCount.ShouldBe(4);
    }

    private static SiteAccessService CreateSiteAccessService(
        SqliteBlokeBotDbFactory dbFactory,
        string[]? botAdmins = null
    )
    {
        var events = new EventBus<AppEventKind>();
        return new SiteAccessService(
            dbFactory,
            new BotAdminService(
                Options.Create(new BlokeBotOptions { BotAdmins = botAdmins ?? [] })
            ),
            new SiteAccessChangeNotifier(events)
        );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
