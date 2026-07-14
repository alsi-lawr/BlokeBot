using BlokeBot.Eventing;
using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class HostFeatureTests
{
    [Test]
    public async Task NewHostAndFeatureToggle_LoadingAndChangingFeatures_DefaultsAllAndPublishesChanges()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var events = TestEventBus.Create<AppEventKind>();
        var publishCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.HostFeature"),
            (_, _) =>
            {
                publishCount++;
                return ValueTask.CompletedTask;
            }
        );
        var service = new HostFeatureService(dbFactory, new HostedChannelChangeNotifier(events));

        (await service.LoadAsync(hostId, CancellationToken.None)).ShouldBe(HostFeatureFlags.All);

        await service.DisableAsync(hostId, HostFeatureFlags.Guessing, CancellationToken.None);

        (await service.LoadAsync(hostId, CancellationToken.None)).ShouldBe(
            HostFeatureFlags.Points | HostFeatureFlags.CustomCommands
        );
        publishCount.ShouldBe(1);

        await service.EnableAsync(hostId, HostFeatureFlags.Guessing, CancellationToken.None);

        (await service.LoadAsync(hostId, CancellationToken.None)).ShouldBe(HostFeatureFlags.All);
        publishCount.ShouldBe(2);
    }

    [Test]
    public async Task EnabledAndDisabledFeatures_ResolvingCommandAliases_ReturnsOnlyEnabledRoutes()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", HostFeatureFlags.Points);
        await SeedAliasAsync(dbFactory, hostId, AppCommandKind.Start, "startguessing");
        await SeedAliasAsync(dbFactory, hostId, AppCommandKind.Points, "points");
        var features = new HostFeatureService(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
        );
        var aliases = new AppCommandAliasResolver(dbFactory);
        var guessing = new GuessingCommandRouteResolver(aliases, features);
        var points = new PointsCommandRouteResolver(aliases, features);

        var disabledGuessing = await guessing.ResolveAsync(
            CommandContext("streamer", "startguessing"),
            CancellationToken.None
        );
        var enabledPoints = await points.ResolveAsync(
            CommandContext("streamer", "points"),
            CancellationToken.None
        );

        disabledGuessing.ShouldBeNull();
        enabledPoints.ShouldNotBeNull();
        enabledPoints.Kind.ShouldBe(PointsCommandKind.Points);
        enabledPoints.State.ShouldBe(new AppCommandRouteState.Host(hostId));

        await features.EnableAsync(hostId, HostFeatureFlags.Guessing, CancellationToken.None);
        await features.DisableAsync(hostId, HostFeatureFlags.Points, CancellationToken.None);

        var enabledGuessing = await guessing.ResolveAsync(
            CommandContext("streamer", "startguessing"),
            CancellationToken.None
        );
        var disabledPoints = await points.ResolveAsync(
            CommandContext("streamer", "points"),
            CancellationToken.None
        );

        enabledGuessing.ShouldNotBeNull();
        enabledGuessing.Kind.ShouldBe(GuessCommandKind.Start);
        enabledGuessing.State.ShouldBe(new AppCommandRouteState.Host(hostId));
        disabledPoints.ShouldBeNull();
    }

    [Test]
    public async Task ProfileOwnedGuessingAlias_ResolvingRoute_PreservesProfileId()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var profile = new GuessRoundProfile
            {
                HostId = hostId,
                Name = "Score",
                Slug = "score",
                IsDefault = true,
                ReplySettings = new BotReplySettings(),
            };
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();
            db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    GuessRoundProfileId = profile.Id,
                    Kind = AppCommandKind.Start,
                    Alias = "score",
                }
            );
            await db.SaveChangesAsync();
        }
        var features = new HostFeatureService(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
        );
        var resolver = new GuessingCommandRouteResolver(
            new AppCommandAliasResolver(dbFactory),
            features
        );

        var route = await resolver.ResolveAsync(
            CommandContext("streamer", "score"),
            CancellationToken.None
        );

        await using var verify = await dbFactory.CreateDbContextAsync();
        var profileId = await verify.Profiles.Select(x => x.Id).SingleAsync(CancellationToken.None);
        route.ShouldNotBeNull();
        route.Kind.ShouldBe(GuessCommandKind.Start);
        route.State.ShouldBe(new AppCommandRouteState.GuessingProfile(hostId, profileId));
    }

    private static ChatCommandContext CommandContext(string channel, string commandName)
    {
        return TestCommandContext.Create("viewer", channel, commandName);
    }

    private static async Task SeedAliasAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        AppCommandKind kind,
        string alias
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = hostId,
                Kind = kind,
                Alias = alias,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login,
        HostFeatureFlags enabledFeatures = HostFeatureFlags.All
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            EnabledFeatures = enabledFeatures,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
