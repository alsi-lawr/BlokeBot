using System.Reflection;
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
    public async Task Hosts_enable_all_features_by_default_and_publish_changes_when_toggled()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var events = new EventBus<AppEventKind>();
        var publishCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            _ =>
            {
                publishCount++;
                return Task.CompletedTask;
            }
        );
        var service = new HostFeatureService(dbFactory, new HostedChannelChangeNotifier(events));

        (await service.LoadAsync(hostId, CancellationToken.None)).ShouldBe(HostFeatureFlags.All);

        await service.SetEnabledAsync(
            hostId,
            HostFeatureFlags.Guessing,
            enabled: false,
            CancellationToken.None
        );

        (await service.LoadAsync(hostId, CancellationToken.None)).ShouldBe(
            HostFeatureFlags.Points | HostFeatureFlags.CustomCommands
        );
        publishCount.ShouldBe(1);

        await service.SetEnabledAsync(
            hostId,
            HostFeatureFlags.Guessing,
            enabled: true,
            CancellationToken.None
        );

        (await service.LoadAsync(hostId, CancellationToken.None)).ShouldBe(HostFeatureFlags.All);
        publishCount.ShouldBe(2);
    }

    [Test]
    public async Task Command_route_resolvers_ignore_aliases_for_disabled_features()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", HostFeatureFlags.Points);
        await SeedAliasAsync(dbFactory, hostId, AppCommandKind.Start, "startguessing");
        await SeedAliasAsync(dbFactory, hostId, AppCommandKind.Points, "points");
        var features = new HostFeatureService(
            dbFactory,
            new HostedChannelChangeNotifier(new EventBus<AppEventKind>())
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

        await features.SetEnabledAsync(
            hostId,
            HostFeatureFlags.Guessing,
            enabled: true,
            CancellationToken.None
        );
        await features.SetEnabledAsync(
            hostId,
            HostFeatureFlags.Points,
            enabled: false,
            CancellationToken.None
        );

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
        disabledPoints.ShouldBeNull();
    }

    [Test]
    public async Task Guessing_route_resolver_preserves_alias_profile_ownership()
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
            new HostedChannelChangeNotifier(new EventBus<AppEventKind>())
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
        route.State.GuessRoundProfileId.ShouldBe(profileId);
    }

    private static TwitchCommandContext CommandContext(string channel, string commandName)
    {
        var constructor = typeof(TwitchCommandContext)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 4);

        return (TwitchCommandContext)
            constructor.Invoke([
                new TwitchChatMessage(
                    "viewer",
                    channel,
                    $"!{commandName}",
                    $":viewer!u@h PRIVMSG #{channel} :!{commandName}",
                    new Dictionary<string, string>()
                ),
                commandName,
                new EmptyServiceProvider(),
                new Func<string, CancellationToken, ValueTask>((_, _) => ValueTask.CompletedTask),
            ]);
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

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
