using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class CommandCatalogTests
{
    [Test]
    public async Task Catalog_resolves_known_alias_and_ignores_unknown_alias()
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
        var catalog = new AppCommandCatalog(dbFactory);

        var known = await catalog.ResolveAsync("streamer", "points", CancellationToken.None);
        var unknown = await catalog.ResolveAsync("streamer", "missing", CancellationToken.None);

        known.ShouldNotBeNull();
        known.HostId.ShouldBe(hostId);
        known.Kind.ShouldBe(AppCommandKind.Points);
        unknown.ShouldBeNull();
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
    public void Catalog_describes_every_command_kind_once()
    {
        AppCommandCatalog
            .Descriptors.Select(x => x.Kind)
            .ShouldBe(Enum.GetValues<AppCommandKind>(), ignoreOrder: true);
    }

    [Test]
    public void Catalog_declares_feature_mappings_and_default_aliases()
    {
        var guessing = AppCommandCatalog.ForFeature(AppCommandFeature.Guessing);
        var points = AppCommandCatalog.ForFeature(AppCommandFeature.Points);

        guessing.Select(x => x.Kind)
            .ShouldBe(
                [
                    AppCommandKind.Start,
                    AppCommandKind.Stop,
                    AppCommandKind.Win,
                    AppCommandKind.Guess,
                    AppCommandKind.Guesses,
                ],
                ignoreOrder: true
            );
        points.Select(x => x.Kind)
            .ShouldBe(
                [
                    AppCommandKind.Points,
                    AppCommandKind.GivePoints,
                    AppCommandKind.AddPoints,
                    AppCommandKind.RemovePoints,
                    AppCommandKind.Gamble,
                    AppCommandKind.Giveaway,
                    AppCommandKind.Join,
                    AppCommandKind.EndGiveaway,
                    AppCommandKind.CancelGiveaway,
                ],
                ignoreOrder: true
            );

        AppCommandCatalog.Describe(AppCommandKind.Start).DefaultAliases.ShouldBe(["startguessing"]);
        AppCommandCatalog.Describe(AppCommandKind.Points).DefaultAliases.ShouldBe(["points"]);
        AppCommandCatalog.ToGuessingKind(AppCommandKind.Guess).ShouldBe(GuessCommandKind.Guess);
        AppCommandCatalog.FromGuessingKind(GuessCommandKind.Win).ShouldBe(AppCommandKind.Win);
        AppCommandCatalog.ToPointsKind(AppCommandKind.Gamble).ShouldBe(PointsCommandKind.Gamble);
        AppCommandCatalog.FromPointsKind(PointsCommandKind.Giveaway)
            .ShouldBe(AppCommandKind.Giveaway);
    }

    [Test]
    public void Catalog_declares_moderator_only_commands()
    {
        AppCommandCatalog.RequiresModerator(AppCommandKind.Start).ShouldBeTrue();
        AppCommandCatalog.RequiresModerator(AppCommandKind.AddPoints).ShouldBeTrue();
        AppCommandCatalog.RequiresModerator(AppCommandKind.Points).ShouldBeFalse();
        AppCommandCatalog.RequiresModerator(AppCommandKind.Guess).ShouldBeFalse();
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

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login
    )
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
