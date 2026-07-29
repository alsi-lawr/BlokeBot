using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

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
        var service = new HostFeatureService(
            dbFactory,
            new HostedChannelChangeNotifier(events),
            []
        );

        (await LoadFeaturesAsync(service, hostId)).ShouldBe(HostFeatureFlags.All);

        await service.DisableAsync(hostId, HostFeatureFlags.Guessing, CancellationToken.None);

        (await LoadFeaturesAsync(service, hostId)).ShouldBe(
            HostFeatureFlags.Points
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.NativeTwitch
        );
        publishCount.ShouldBe(1);

        await service.EnableAsync(hostId, HostFeatureFlags.Guessing, CancellationToken.None);

        (await LoadFeaturesAsync(service, hostId)).ShouldBe(HostFeatureFlags.All);
        publishCount.ShouldBe(2);
    }

    [Test]
    public async Task NativeTwitchSwitch_Toggling_PersistsAndNotifiesLifecycleObservers()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var observer = new RecordingNativeTwitchFeatureChangeObserver();
        var service = new HostFeatureService(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [observer]
        );

        await service.DisableAsync(hostId, HostFeatureFlags.NativeTwitch, CancellationToken.None);
        await service.DisableAsync(hostId, HostFeatureFlags.NativeTwitch, CancellationToken.None);
        await service.EnableAsync(hostId, HostFeatureFlags.NativeTwitch, CancellationToken.None);

        (await LoadFeaturesAsync(service, hostId)).ShouldBe(HostFeatureFlags.All);
        observer.Changes.ShouldBe([
            (hostId, NativeTwitchFeatureState.Disabled),
            (hostId, NativeTwitchFeatureState.Enabled),
        ]);
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
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
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

        disabledGuessing.ShouldBeOfType<CommandRouteResolution<
            GuessCommandKind,
            AppCommandRouteState
        >.Unresolved>();
        var pointsRoute = enabledPoints
            .ShouldBeOfType<CommandRouteResolution<
                PointsCommandKind,
                AppCommandRouteState
            >.Resolved>()
            .Route;
        pointsRoute.Kind.ShouldBe(PointsCommandKind.Points);
        pointsRoute.State.ShouldBe(new AppCommandRouteState.Host(hostId));

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

        var guessingRoute = enabledGuessing
            .ShouldBeOfType<CommandRouteResolution<
                GuessCommandKind,
                AppCommandRouteState
            >.Resolved>()
            .Route;
        guessingRoute.Kind.ShouldBe(GuessCommandKind.Start);
        guessingRoute.State.ShouldBe(new AppCommandRouteState.Host(hostId));
        disabledPoints.ShouldBeOfType<CommandRouteResolution<
            PointsCommandKind,
            AppCommandRouteState
        >.Unresolved>();
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
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
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
        var resolved = route
            .ShouldBeOfType<CommandRouteResolution<
                GuessCommandKind,
                AppCommandRouteState
            >.Resolved>()
            .Route;
        resolved.Kind.ShouldBe(GuessCommandKind.Start);
        resolved.State.ShouldBe(new AppCommandRouteState.GuessingProfile(hostId, profileId));
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

    private static async Task<HostFeatureFlags?> LoadFeaturesAsync(
        HostFeatureService service,
        int hostId
    )
    {
        var features = await service.Load(hostId).RunAsync(CancellationToken.None);
        return features.Match<HostFeatureFlags?>(value => value, () => null);
    }

    private sealed class RecordingNativeTwitchFeatureChangeObserver
        : INativeTwitchFeatureChangeObserver
    {
        internal List<(int HostId, NativeTwitchFeatureState State)> Changes { get; } = [];

        public Task NativeTwitchFeatureChangedAsync(
            int hostId,
            NativeTwitchFeatureState state,
            CancellationToken cancellationToken
        )
        {
            Changes.Add((hostId, state));
            return Task.CompletedTask;
        }
    }
}
