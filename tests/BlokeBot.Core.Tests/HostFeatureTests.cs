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
        var service = TestHostFeatureServices.Create(
            dbFactory,
            new HostedChannelChangeNotifier(events),
            []
        );

        (await LoadFeaturesAsync(service, hostId)).ShouldBe(HostFeatureFlags.All);

        _ = await service.DisableAsync(hostId, HostFeatureFlags.Guessing, CancellationToken.None);

        var afterVisibleFeatureDisabled = await LoadFeaturesAsync(service, hostId);
        afterVisibleFeatureDisabled.ShouldBe(HostFeatureFlags.All & ~HostFeatureFlags.Guessing);
        afterVisibleFeatureDisabled!.Value.Contains(HostFeatureFlags.Automations).ShouldBeTrue();
        publishCount.ShouldBe(1);

        _ = await service.EnableAsync(hostId, HostFeatureFlags.Guessing, CancellationToken.None);

        (await LoadFeaturesAsync(service, hostId)).ShouldBe(HostFeatureFlags.All);
        publishCount.ShouldBe(2);
    }

    [Test]
    public void NewHostModel_DefaultsEveryChatToolOff()
    {
        new BotHost().EnabledFeatures.ShouldBe(HostFeatureFlags.None);
        ((ulong)HostFeatureFlags.RaidCollaboration).ShouldBe(1UL << 18);
        ((ulong)HostFeatureFlags.CooperativeGame).ShouldBe(1UL << 19);
        ((ulong)HostFeatureFlags.Collectives).ShouldBe(1UL << 20);
    }

    [Test]
    public async Task AutomationFeature_Toggling_PreservesUnknownBits()
    {
        var unknown = (HostFeatureFlags)(1UL << 48);
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", unknown);
        var service = TestHostFeatureServices.Create(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
        );

        _ = await service.EnableAsync(hostId, HostFeatureFlags.Automations, CancellationToken.None);
        (await LoadFeaturesAsync(service, hostId)).ShouldBe(unknown | HostFeatureFlags.Automations);

        _ = await service.DisableAsync(
            hostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        (await LoadFeaturesAsync(service, hostId)).ShouldBe(unknown);

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
        var service = TestHostFeatureServices.Create(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [observer]
        );

        _ = await service.DisableAsync(hostId, HostFeatureFlags.Polls, CancellationToken.None);
        _ = await service.DisableAsync(hostId, HostFeatureFlags.Polls, CancellationToken.None);
        _ = await service.EnableAsync(hostId, HostFeatureFlags.Polls, CancellationToken.None);

        (await LoadFeaturesAsync(service, hostId)).ShouldBe(HostFeatureFlags.All);
        observer.Changes.ShouldBe([
            (hostId, HostFeatureFlags.Polls, HostFeatureActivationState.Disabled),
            (hostId, HostFeatureFlags.Polls, HostFeatureActivationState.Enabled),
        ]);
    }

    [Test]
    public async Task EnabledAndDisabledFeatures_ResolvingCommandAliases_ReturnsOnlyEnabledRoutes()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", HostFeatureFlags.Points);
        await SeedAliasAsync(dbFactory, hostId, AppCommandKind.Start, "startguessing");
        await SeedAliasAsync(dbFactory, hostId, AppCommandKind.Points, "points");
        var features = TestHostFeatureServices.Create(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
        );
        var aliases = new AppCommandAliasResolver(dbFactory);
        var guessing = new GuessingCommandRouteResolver(aliases, features, dbFactory);
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

        _ = await features.EnableAsync(hostId, HostFeatureFlags.Guessing, CancellationToken.None);
        _ = await features.DisableAsync(hostId, HostFeatureFlags.Points, CancellationToken.None);

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
        var features = TestHostFeatureServices.Create(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
        );
        var resolver = new GuessingCommandRouteResolver(
            new AppCommandAliasResolver(dbFactory),
            features,
            dbFactory
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

    [Test]
    public async Task SharedNonStartAlias_ResolvingDuringClosedRound_UsesRoundProfile()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        int activeProfileId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var defaultProfile = new GuessRoundProfile
            {
                HostId = hostId,
                Name = "Default",
                Slug = "default",
                IsDefault = true,
                ReplySettings = new(),
            };
            var activeProfile = new GuessRoundProfile
            {
                HostId = hostId,
                Name = "Active",
                Slug = "active",
                ReplySettings = new(),
            };
            db.Profiles.AddRange(defaultProfile, activeProfile);
            _ = await db.SaveChangesAsync();
            activeProfileId = activeProfile.Id;
            db.CommandAliases.AddRange(
                new CommandAlias
                {
                    HostId = hostId,
                    GuessRoundProfileId = defaultProfile.Id,
                    Kind = AppCommandKind.Guess,
                    Alias = "predict",
                },
                new CommandAlias
                {
                    HostId = hostId,
                    GuessRoundProfileId = activeProfile.Id,
                    Kind = AppCommandKind.Guess,
                    Alias = "predict",
                }
            );
            var now = DateTime.UtcNow;
            _ = db.Rounds.Add(
                new GuessRound
                {
                    HostId = hostId,
                    GuessRoundProfileId = activeProfile.Id,
                    Status = GuessRoundStatus.Closed,
                    StartedAtUtc = now.AddMinutes(-1),
                    ClosedAtUtc = now,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var features = TestHostFeatureServices.Create(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
        );
        var resolver = new GuessingCommandRouteResolver(
            new AppCommandAliasResolver(dbFactory),
            features,
            dbFactory
        );

        var route = await resolver.ResolveAsync(
            CommandContext("streamer", "predict"),
            CancellationToken.None
        );

        var resolved = route
            .ShouldBeOfType<CommandRouteResolution<
                GuessCommandKind,
                AppCommandRouteState
            >.Resolved>()
            .Route;
        resolved.Kind.ShouldBe(GuessCommandKind.Guess);
        resolved.State.ShouldBe(new AppCommandRouteState.GuessingProfile(hostId, activeProfileId));
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

    private sealed class RecordingNativeTwitchFeatureChangeObserver : IHostFeatureActivationObserver
    {
        internal List<(
            int HostId,
            HostFeatureFlags Feature,
            HostFeatureActivationState State
        )> Changes { get; } = [];

        public ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
            HostFeatureActivationChange change,
            CancellationToken cancellationToken
        )
        {
            Changes.Add((change.HostId, change.Feature, change.State));
            return ValueTask.FromResult<HostFeatureAutomaticWorkResult>(
                new HostFeatureAutomaticWorkResult.Complete()
            );
        }
    }
}
