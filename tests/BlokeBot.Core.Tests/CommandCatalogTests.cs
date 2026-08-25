using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

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
            _ = db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    Kind = AppCommandKind.Points,
                    Alias = "points",
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var resolver = new AppCommandAliasResolver(dbFactory);

        var known = await resolver.ResolveAsync("streamer", "points", CancellationToken.None);
        var unknown = await resolver.ResolveAsync("streamer", "missing", CancellationToken.None);

        _ = known.ShouldNotBeNull();
        known.HostId.ShouldBe(hostId);
        known.Kind.ShouldBe(AppCommandKind.Points);
        _ = known.Scope.ShouldBeOfType<CommandAliasScope.Global>();
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
        var resolver = new AppCommandAliasResolver(dbFactory);

        var resolution = await resolver.ResolveAsync("streamer", "score", CancellationToken.None);

        await using var verify = await dbFactory.CreateDbContextAsync();
        var profileId = await verify
            .Profiles.Select(static x => x.Id)
            .SingleAsync(CancellationToken.None);
        _ = resolution.ShouldNotBeNull();
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

        _ = await Should.ThrowAsync<InvalidOperationException>(() =>
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
        _ = db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = hostId,
                Kind = AppCommandKind.Start,
                Alias = "play",
            }
        );
        _ = await db.SaveChangesAsync();
        var registry = new CommandAliasRegistry();

        _ = await Should.ThrowAsync<InvalidOperationException>(() =>
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
        _ = await db.SaveChangesAsync();
        _ = db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = hostId,
                GuessRoundProfileId = defaultProfile.Id,
                Kind = AppCommandKind.Start,
                Alias = "play",
            }
        );
        _ = await db.SaveChangesAsync();
        var registry = new CommandAliasRegistry();

        _ = await Should.ThrowAsync<InvalidOperationException>(() =>
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
        _ = db.Profiles.Add(profile);
        _ = await db.SaveChangesAsync();
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
        _ = await db.SaveChangesAsync();

        var aliases = await db
            .CommandAliases.AsNoTracking()
            .OrderBy(static alias => alias.Alias)
            .ToListAsync(CancellationToken.None);
        aliases.Select(static alias => alias.Alias).ShouldBe(["balance", "score"]);
        aliases.Single(static alias => alias.Alias == "balance").GuessRoundProfileId.ShouldBeNull();
        aliases
            .Single(static alias => alias.Alias == "score")
            .GuessRoundProfileId.ShouldBe(profile.Id);
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

        _ = insufficient
            .Match<PointBalanceMutationFailure>(
                static _ => throw new InvalidOperationException("Expected insufficient balance."),
                static failure => failure
            )
            .ShouldBeOfType<PointBalanceMutationFailure.InsufficientBalance>();
        _ = invalid
            .Match<PointBalanceMutationFailure>(
                static _ => throw new InvalidOperationException("Expected invalid amount."),
                static failure => failure
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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
