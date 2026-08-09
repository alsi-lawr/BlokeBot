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

namespace BlokeBot.Core.Tests;

public sealed class HostFeatureTests
{
    [Test]
    public async Task ExistingHostFeatureToggle_LoadingAndChangingFeatures_PreservesOtherBitsAndPublishesChanges()
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

        var afterVisibleFeatureDisabled = await LoadFeaturesAsync(service, hostId);
        afterVisibleFeatureDisabled.ShouldBe(HostFeatureFlags.All & ~HostFeatureFlags.Guessing);
        afterVisibleFeatureDisabled!.Value.Contains(HostFeatureFlags.Automations).ShouldBeTrue();
        publishCount.ShouldBe(1);

        await service.EnableAsync(hostId, HostFeatureFlags.Guessing, CancellationToken.None);

        (await LoadFeaturesAsync(service, hostId)).ShouldBe(HostFeatureFlags.All);
        publishCount.ShouldBe(2);
    }

    [Test]
    public void NewHostModel_DefaultsEveryChatToolOff()
    {
        new BotHost().EnabledFeatures.ShouldBe(HostFeatureFlags.None);
        HostFeatureCatalog
            .Cards(HostFeatureFlags.None)
            .ShouldAllBe(static feature => !feature.Enabled);
        HostFeatureCatalog.Features.Count.ShouldBe(13);
        HostFeatureCatalog.Features.ShouldBeUnique();
        HostFeatureCatalog.Features.ShouldContain(HostFeatureFlags.Automations);
        HostFeatureCatalog
            .Cards(HostFeatureFlags.Automations)
            .ShouldAllBe(static card => !card.Enabled);
        HostFeatureCatalog
            .Cards(HostFeatureFlags.All)
            .Select(static card => card.Feature)
            .ShouldBe(
                HostFeatureCatalog.Features.Where(static feature =>
                    feature != HostFeatureFlags.Automations
                )
            );
    }

    [Test]
    public async Task EveryCatalogFeature_TogglesIndependentlyAndPreservesUnknownBits()
    {
        var unknown = (HostFeatureFlags)(1UL << 48);
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", unknown);
        var service = new HostFeatureService(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
        );

        foreach (var feature in HostFeatureCatalog.Features)
        {
            await service.EnableAsync(hostId, feature, CancellationToken.None);
            (await LoadFeaturesAsync(service, hostId)).ShouldBe(unknown | feature);

            await service.DisableAsync(hostId, feature, CancellationToken.None);
            (await LoadFeaturesAsync(service, hostId)).ShouldBe(unknown);
        }

        _ = await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            service.EnableAsync(
                hostId,
                HostFeatureFlags.NativeTwitchFeatures,
                CancellationToken.None
            )
        );
        (await LoadFeaturesAsync(service, hostId)).ShouldBe(unknown);
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

        await service.DisableAsync(hostId, HostFeatureFlags.Shoutouts, CancellationToken.None);
        await service.DisableAsync(hostId, HostFeatureFlags.Shoutouts, CancellationToken.None);
        await service.EnableAsync(hostId, HostFeatureFlags.Shoutouts, CancellationToken.None);

        (await LoadFeaturesAsync(service, hostId)).ShouldBe(HostFeatureFlags.All);
        observer.Changes.ShouldBe([
            (hostId, HostFeatureFlags.Shoutouts, NativeTwitchFeatureState.Disabled),
            (hostId, HostFeatureFlags.Shoutouts, NativeTwitchFeatureState.Enabled),
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

        _ = disabledGuessing.ShouldBeOfType<CommandRouteResolution<
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
        _ = disabledPoints.ShouldBeOfType<CommandRouteResolution<
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
            _ = db.Profiles.Add(profile);
            _ = await db.SaveChangesAsync();
            _ = db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    GuessRoundProfileId = profile.Id,
                    Kind = AppCommandKind.Start,
                    Alias = "score",
                }
            );
            _ = await db.SaveChangesAsync();
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
        var profileId = await verify
            .Profiles.Select(static x => x.Id)
            .SingleAsync(CancellationToken.None);
        var resolved = route
            .ShouldBeOfType<CommandRouteResolution<
                GuessCommandKind,
                AppCommandRouteState
            >.Resolved>()
            .Route;
        resolved.Kind.ShouldBe(GuessCommandKind.Start);
        resolved.State.ShouldBe(new AppCommandRouteState.GuessingProfile(hostId, profileId));
    }

    private static ChatCommandContext CommandContext(string channel, string commandName) =>
        TestCommandContext.Create("viewer", channel, commandName);

    private static async Task SeedAliasAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        AppCommandKind kind,
        string alias
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        _ = db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = hostId,
                Kind = kind,
                Alias = alias,
            }
        );
        _ = await db.SaveChangesAsync();
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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<HostFeatureFlags?> LoadFeaturesAsync(
        HostFeatureService service,
        int hostId
    )
    {
        var features = await service.Load(hostId).RunAsync(CancellationToken.None);
        return features.Match<HostFeatureFlags?>(static value => value, static () => null);
    }

    private sealed class RecordingNativeTwitchFeatureChangeObserver
        : INativeTwitchFeatureChangeObserver
    {
        internal List<(
            int HostId,
            HostFeatureFlags Feature,
            NativeTwitchFeatureState State
        )> Changes { get; } = [];

        public Task NativeTwitchFeatureChangedAsync(
            int hostId,
            HostFeatureFlags feature,
            NativeTwitchFeatureState state,
            CancellationToken cancellationToken
        )
        {
            Changes.Add((hostId, feature, state));
            return Task.CompletedTask;
        }
    }
}
