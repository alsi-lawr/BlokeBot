using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class CommandCatalogTests
{
    [Test]
    public async Task KnownAndUnknownAliases_Resolving_ReturnsKnownRouteAndNull()
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
        known.Scope.ShouldBeOfType<CommandAliasScope.Global>();
        unknown.ShouldBeNull();
    }

    [Test]
    public async Task ProfileOwnedGuessingAlias_Resolving_ReturnsProfileOwnership()
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
        resolution.Scope.ShouldBeOfType<CommandAliasScope.Profile>().ProfileId.ShouldBe(profileId);
    }

    [Test]
    public async Task DuplicateAliasesWithinReplacement_UpdatingRegistry_Rejects()
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
                new CommandAliasScope.Global(),
                CancellationToken.None
            )
        );
    }

    [Test]
    public async Task AliasOwnedByOtherFeature_UpdatingRegistry_Rejects()
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
                new CommandAliasScope.Global(),
                CancellationToken.None
            )
        );
    }

    [Test]
    public async Task AliasOwnedByOtherGuessingProfile_UpdatingRegistry_Rejects()
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
                new CommandAliasScope.Profile(specialProfile.Id),
                CancellationToken.None
            )
        );
    }

    [Test]
    public async Task GlobalAndProfileScopes_ReplacingAliases_PersistTheirDistinctStorageKeys()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using var db = await dbFactory.CreateDbContextAsync();
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
        var registry = new CommandAliasRegistry();

        await registry.ReplaceAliasesAsync(
            db,
            hostId,
            PointsAppCommandKindMap.AppKinds,
            [new CommandAliasDraft(AppCommandKind.Points, "balance")],
            new CommandAliasScope.Global(),
            CancellationToken.None
        );
        await registry.ReplaceAliasesAsync(
            db,
            hostId,
            GuessingAppCommandKindMap.AppKinds,
            [new CommandAliasDraft(AppCommandKind.Start, "score")],
            new CommandAliasScope.Profile(profile.Id),
            CancellationToken.None
        );
        await db.SaveChangesAsync();

        var aliases = await db
            .CommandAliases.AsNoTracking()
            .OrderBy(alias => alias.Alias)
            .ToListAsync(CancellationToken.None);
        aliases.Select(alias => alias.Alias).ShouldBe(["balance", "score"]);
        aliases.Single(alias => alias.Alias == "balance").GuessRoundProfileId.ShouldBeNull();
        aliases.Single(alias => alias.Alias == "score").GuessRoundProfileId.ShouldBe(profile.Id);
    }

    [Test]
    public void FeatureStrategyCatalogs_ReadingDefaults_ExposeExpectedAliases()
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
    public void FeatureStrategyCatalogs_ReadingPermissions_ExposeModeratorRequirements()
    {
        var guessing = GuessingCatalog();
        var points = PointsCatalog();

        guessing
            .Find(GuessCommandKind.Start)
            .Match(_ => throw new InvalidOperationException(), found => found.Strategy.Access)
            .ShouldBeOfType<CommandStrategyAccess<
                GuessCommandKind,
                AppCommandRouteState
            >.ModeratorOnly>();
        points
            .Find(PointsCommandKind.AddPoints)
            .Match(_ => throw new InvalidOperationException(), found => found.Strategy.Access)
            .ShouldBeOfType<CommandStrategyAccess<
                PointsCommandKind,
                AppCommandRouteState
            >.ModeratorOnly>();
        points
            .Find(PointsCommandKind.Points)
            .Match(_ => throw new InvalidOperationException(), found => found.Strategy.Access)
            .ShouldBeOfType<CommandStrategyAccess<
                PointsCommandKind,
                AppCommandRouteState
            >.Everyone>();
        guessing
            .Find(GuessCommandKind.Guess)
            .Match(_ => throw new InvalidOperationException(), found => found.Strategy.Access)
            .ShouldBeOfType<CommandStrategyAccess<
                GuessCommandKind,
                AppCommandRouteState
            >.Everyone>();
    }

    [Test]
    public void FeatureAndPersistedCommandKinds_Mapping_MapsSupportedKindsWithoutOverlap()
    {
        GuessingAppCommandKindMap.ToAppKind(GuessCommandKind.Guess).ShouldBe(AppCommandKind.Guess);
        PointsAppCommandKindMap.ToAppKind(PointsCommandKind.Gamble).ShouldBe(AppCommandKind.Gamble);

        GuessingAppCommandKindMap
            .FromAppKind(AppCommandKind.Win)
            .Match(kind => kind, () => throw new InvalidOperationException())
            .ShouldBe(GuessCommandKind.Win);
        PointsAppCommandKindMap
            .FromAppKind(AppCommandKind.Giveaway)
            .Match(kind => kind, () => throw new InvalidOperationException())
            .ShouldBe(PointsCommandKind.Giveaway);
        GuessingAppCommandKindMap
            .FromAppKind(AppCommandKind.Giveaway)
            .Match(_ => false, () => true)
            .ShouldBeTrue();
        PointsAppCommandKindMap
            .FromAppKind(AppCommandKind.Win)
            .Match(_ => false, () => true)
            .ShouldBeTrue();

        var guessingKinds = GuessingAppCommandKindMap.AppKinds;
        var pointsKinds = PointsAppCommandKindMap.AppKinds;
        guessingKinds.Intersect(pointsKinds).ShouldBeEmpty();
    }

    [Test]
    public async Task InvalidBalanceMutation_ReturningFailure_UsesTypedReasonWithoutMessageCode()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var balances = new PointBalanceService(dbFactory);

        var insufficient = await balances
            .Remove(hostId, "viewer", PointAmount.ParseAbsolute("10"), "admin", "test")
            .ExecuteAsync(CancellationToken.None);
        var invalid = await balances
            .Add(hostId, "viewer", PointAmount.Zero, "admin", "test")
            .ExecuteAsync(CancellationToken.None);

        insufficient
            .Match<PointBalanceMutationFailure>(
                _ => throw new InvalidOperationException("Expected insufficient balance."),
                failure => failure
            )
            .ShouldBeOfType<PointBalanceMutationFailure.InsufficientBalance>();
        invalid
            .Match<PointBalanceMutationFailure>(
                _ => throw new InvalidOperationException("Expected invalid amount."),
                failure => failure
            )
            .ShouldBeOfType<PointBalanceMutationFailure.InvalidAmount>();
    }

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
            new GambleCommandStrategy(null!, null!, null!, null!, null!),
            new StartGiveawayCommandStrategy(null!, null!),
            new JoinGiveawayCommandStrategy(null!, null!),
            new EndGiveawayCommandStrategy(null!, null!),
            new CancelGiveawayCommandStrategy(null!, null!),
        ]);
}
