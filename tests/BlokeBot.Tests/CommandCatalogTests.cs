using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class CommandCatalogTests
{
    [Test]
    public async Task Alias_resolver_resolves_known_alias_and_ignores_unknown_alias()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    Kind = AppCommandKind.Points,
                    Alias = "points",
                }
            );
            await db.SaveChangesAsync();
        }
        var resolver = new AppCommandAliasResolver(dbFactory);

        var known = await resolver.ResolveAsync("streamer", "points", CancellationToken.None);
        var unknown = await resolver.ResolveAsync("streamer", "missing", CancellationToken.None);

        known.ShouldNotBeNull();
        known.HostId.ShouldBe(hostId);
        known.Kind.ShouldBe(AppCommandKind.Points);
        known.GuessRoundProfileId.ShouldBeNull();
        unknown.ShouldBeNull();
    }

    [Test]
    public async Task Alias_resolver_returns_guessing_profile_ownership()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var profile = new GuessRoundProfile
            {
                HostId = hostId,
                Name = "Final score",
                Slug = "final-score",
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
        var resolver = new AppCommandAliasResolver(dbFactory);

        var resolution = await resolver.ResolveAsync("streamer", "score", CancellationToken.None);

        await using var verify = await dbFactory.CreateDbContextAsync();
        var profileId = await verify.Profiles.Select(x => x.Id).SingleAsync(CancellationToken.None);
        resolution.ShouldNotBeNull();
        resolution.Kind.ShouldBe(AppCommandKind.Start);
        resolution.GuessRoundProfileId.ShouldBe(profileId);
    }

    [Test]
    public async Task Alias_registry_rejects_duplicate_aliases_inside_owned_commands()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using var db = await dbFactory.CreateDbContextAsync();
        var registry = new CommandAliasRegistry();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            registry.ReplaceAliasesAsync(
                db,
                hostId,
                new HashSet<AppCommandKind> { AppCommandKind.Points, AppCommandKind.GivePoints },
                [
                    new CommandAliasDraft(AppCommandKind.Points, "points"),
                    new CommandAliasDraft(AppCommandKind.GivePoints, "POINTS"),
                ],
                CancellationToken.None
            )
        );
    }

    [Test]
    public async Task Alias_registry_rejects_cross_feature_collisions()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using var db = await dbFactory.CreateDbContextAsync();
        db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = hostId,
                Kind = AppCommandKind.Start,
                Alias = "play",
            }
        );
        await db.SaveChangesAsync();
        var registry = new CommandAliasRegistry();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            registry.ReplaceAliasesAsync(
                db,
                hostId,
                new HashSet<AppCommandKind> { AppCommandKind.Points },
                [new CommandAliasDraft(AppCommandKind.Points, "play")],
                CancellationToken.None
            )
        );
    }

    [Test]
    public async Task Alias_registry_rejects_collisions_across_guessing_profiles()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using var db = await dbFactory.CreateDbContextAsync();
        var defaultProfile = new GuessRoundProfile
        {
            HostId = hostId,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
            ReplySettings = new BotReplySettings(),
        };
        var specialProfile = new GuessRoundProfile
        {
            HostId = hostId,
            Name = "Special",
            Slug = "special",
            ReplySettings = new BotReplySettings(),
        };
        db.Profiles.AddRange(defaultProfile, specialProfile);
        await db.SaveChangesAsync();
        db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = hostId,
                GuessRoundProfileId = defaultProfile.Id,
                Kind = AppCommandKind.Start,
                Alias = "play",
            }
        );
        await db.SaveChangesAsync();
        var registry = new CommandAliasRegistry();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            registry.ReplaceAliasesAsync(
                db,
                hostId,
                GuessingAppCommandKindMap.AppKinds,
                [new CommandAliasDraft(AppCommandKind.Start, "play")],
                CancellationToken.None,
                specialProfile.Id
            )
        );
    }

    [Test]
    public void Strategy_catalogs_describe_every_feature_command_kind_once()
    {
        GuessingCatalog()
            .Descriptors.Select(x => x.Kind)
            .ShouldBe(Enum.GetValues<GuessCommandKind>(), ignoreOrder: true);
        PointsCatalog()
            .Descriptors.Select(x => x.Kind)
            .ShouldBe(Enum.GetValues<PointsCommandKind>(), ignoreOrder: true);
    }

    [Test]
    public void Strategy_catalogs_declare_default_aliases()
    {
        var guessing = GuessingCatalog();
        var points = PointsCatalog();

        guessing
            .Descriptors.Single(x => x.Kind == GuessCommandKind.Start)
            .DefaultAliases.ShouldBe(["startguessing"]);
        guessing
            .Descriptors.Single(x => x.Kind == GuessCommandKind.Guess)
            .DefaultAliases.ShouldBe(["guess"]);
        points
            .Descriptors.Single(x => x.Kind == PointsCommandKind.Points)
            .DefaultAliases.ShouldBe(["points"]);
        points
            .Descriptors.Single(x => x.Kind == PointsCommandKind.Giveaway)
            .DefaultAliases.ShouldBe(["giveaway"]);
    }

    [Test]
    public void Strategy_catalogs_declare_moderator_only_commands()
    {
        var guessing = GuessingCatalog();
        var points = PointsCatalog();

        guessing
            .Descriptors.Single(x => x.Kind == GuessCommandKind.Start)
            .RequiresModerator.ShouldBeTrue();
        points
            .Descriptors.Single(x => x.Kind == PointsCommandKind.AddPoints)
            .RequiresModerator.ShouldBeTrue();
        points
            .Descriptors.Single(x => x.Kind == PointsCommandKind.Points)
            .RequiresModerator.ShouldBeFalse();
        guessing
            .Descriptors.Single(x => x.Kind == GuessCommandKind.Guess)
            .RequiresModerator.ShouldBeFalse();
    }

    [Test]
    public void Persisted_app_command_kind_maps_explicitly_to_feature_command_kinds()
    {
        GuessingAppCommandKindMap.ToAppKind(GuessCommandKind.Guess).ShouldBe(AppCommandKind.Guess);
        PointsAppCommandKindMap.ToAppKind(PointsCommandKind.Gamble).ShouldBe(AppCommandKind.Gamble);

        GuessingAppCommandKindMap
            .TryFromAppKind(AppCommandKind.Win, out var guessingKind)
            .ShouldBeTrue();
        guessingKind.ShouldBe(GuessCommandKind.Win);
        PointsAppCommandKindMap
            .TryFromAppKind(AppCommandKind.Giveaway, out var pointsKind)
            .ShouldBeTrue();
        pointsKind.ShouldBe(PointsCommandKind.Giveaway);

        var guessingKinds = GuessingAppCommandKindMap.AppKinds;
        var pointsKinds = PointsAppCommandKindMap.AppKinds;
        guessingKinds.Intersect(pointsKinds).ShouldBeEmpty();
        guessingKinds
            .Concat(pointsKinds)
            .ShouldBe(Enum.GetValues<AppCommandKind>(), ignoreOrder: true);
    }

    [Test]
    public async Task Point_balance_failures_are_typed_not_message_codes()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var balances = new PointBalanceService(dbFactory);

        var insufficient = await balances.RemoveAsync(
            hostId,
            "viewer",
            PointAmount.ParseAbsolute("10"),
            "admin",
            "test",
            CancellationToken.None
        );
        var invalid = await balances.AddAsync(
            hostId,
            "viewer",
            PointAmount.Zero,
            "admin",
            "test",
            CancellationToken.None
        );

        insufficient.Success.ShouldBeFalse();
        insufficient.FailureReason.ShouldBe(PointOperationFailureReason.InsufficientBalance);
        insufficient.Message.ShouldBeEmpty();
        invalid.Success.ShouldBeFalse();
        invalid.FailureReason.ShouldBe(PointOperationFailureReason.InvalidAmount);
        invalid.Message.ShouldBeEmpty();
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

    private static CommandStrategyCatalog<
        GuessCommandKind,
        AppCommandRouteState
    > GuessingCatalog() =>
        new([
            new StartGuessingCommandStrategy(null!, null!),
            new StopGuessingCommandStrategy(null!, null!),
            new WinGuessingCommandStrategy(null!, null!),
            new GuessCommandStrategy(null!, null!),
            new AvailableGuessesCommandStrategy(null!),
        ]);

    private static CommandStrategyCatalog<
        PointsCommandKind,
        AppCommandRouteState
    > PointsCatalog() =>
        new([
            new PointsBalanceCommandStrategy(null!, null!),
            new GivePointsCommandStrategy(null!, null!, null!),
            new AddPointsCommandStrategy(null!, null!, null!),
            new RemovePointsCommandStrategy(null!, null!),
            new GambleCommandStrategy(null!, null!, null!),
            new StartGiveawayCommandStrategy(null!, null!),
            new JoinGiveawayCommandStrategy(null!, null!),
            new EndGiveawayCommandStrategy(null!, null!),
            new CancelGiveawayCommandStrategy(null!, null!),
        ]);
}
