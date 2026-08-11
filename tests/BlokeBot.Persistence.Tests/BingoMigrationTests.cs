using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class BingoMigrationTests
{
    private const string _previousMigration = "20260810091437_v0.9.0_CommunityProgression";
    private const string _concurrencyMigration = "20260810150628_v0.9.0_BingoConcurrency";
    private const string _opaqueAssignmentMigration =
        "20260810154030_v0.9.0_BingoOpaqueAssignments";

    [Test]
    public async Task Upgrade_PreservesExistingHostsAsOptInAndPersistsRevisionedIssuedState()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_previousMigration);
            _ = await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures,
                     CommandsAliasesConfigured, AutomationGeneration, TimeZoneId, CreatedAtUtc)
                VALUES
                    (1, 'host-id', 'host', 'Host', 0, 0, 0, 0, 'UTC', '2026-08-10T00:00:00Z');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        var host = await upgraded.Hosts.SingleAsync();
        ((host.EnabledFeatures & HostFeatureFlags.Bingo) == HostFeatureFlags.Bingo).ShouldBeFalse();
        host.BingoPausedAtUtc.ShouldBeNull();
        host.BingoAcceptEventsAfterUtc.ShouldBeNull();
        var template = new BingoTemplate
        {
            HostId = host.Id,
            PublicId = Guid.NewGuid(),
            CreationOperationId = Guid.NewGuid(),
            Name = "Migration template",
            CurrentRevision = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        var revision = new BingoTemplateRevision
        {
            HostId = host.Id,
            OperationId = Guid.NewGuid(),
            Template = template,
            Revision = 1,
            Dimension = 3,
            LinePointsReward = "0",
            FullCardPointsReward = "0",
            CreatedByTwitchUserId = "host-id",
            CreatedByLogin = "host",
            CreatedAtUtc = DateTime.UtcNow,
        };
        for (var index = 0; index < 9; index++)
        {
            revision.Squares.Add(
                new BingoSquare
                {
                    HostId = host.Id,
                    Key = $"square-{index}",
                    SortOrder = index,
                    Title = $"Square {index}",
                    Kind = BingoSquareKind.Manual,
                }
            );
        }
        var game = new BingoGame
        {
            HostId = host.Id,
            PublicId = Guid.NewGuid(),
            CreationOperationId = Guid.NewGuid(),
            TemplateRevision = revision,
            TemplateName = template.Name,
            TemplateRevisionNumber = 1,
            Dimension = 3,
            Seed = "migration-seed",
            Mode = BingoGameMode.Shared,
            Status = BingoGameStatus.Issued,
            LinePointsReward = "0",
            FullCardPointsReward = "0",
            CreatedAtUtc = DateTime.UtcNow,
            IssuedAtUtc = DateTime.UtcNow,
        };
        _ = upgraded.BingoTemplates.Add(template);
        _ = upgraded.BingoGames.Add(game);
        _ = await upgraded.SaveChangesAsync();

        var stored = await upgraded
            .BingoGames.Include(value => value.TemplateRevision)
                .ThenInclude(value => value!.Squares)
            .SingleAsync();
        stored.Dimension.ShouldBe(3);
        stored.TemplateRevisionNumber.ShouldBe(1);
        stored.Seed.ShouldBe("migration-seed");
        stored
            .TemplateRevision!.Squares.Select(value => value.Kind)
            .ShouldAllBe(value => value == BingoSquareKind.Manual);

        _ = upgraded.BingoGames.Add(
            new BingoGame
            {
                HostId = host.Id,
                PublicId = Guid.NewGuid(),
                CreationOperationId = Guid.NewGuid(),
                TemplateRevisionId = stored.TemplateRevisionId,
                TemplateName = stored.TemplateName,
                TemplateRevisionNumber = stored.TemplateRevisionNumber,
                Dimension = stored.Dimension,
                Seed = "second-active-game",
                Mode = BingoGameMode.Shared,
                Status = BingoGameStatus.Joining,
                LinePointsReward = "0",
                FullCardPointsReward = "0",
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await Should.ThrowAsync<DbUpdateException>(() => upgraded.SaveChangesAsync());
    }

    [Test]
    public async Task Downgrade_WithMaterializedUniqueLayout_IsRefusedWithoutChangingAuthoritativeState()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        var initializer = new BlokeBotDatabaseInitializer(factory);
        await initializer.InitializeAsync(default);

        var cardPublicId = Guid.Parse("9fc50e5a-101e-4e89-82ed-b8feba82afe2");
        var legacyAssignmentKey = "viewer:alice-id";
        var squareKeys = Enumerable.Range(0, 9).Select(value => $"square-{value}").ToArray();
        string[] expectedLayout =
        [
            "square-6",
            "square-0",
            "square-5",
            "square-3",
            "square-8",
            "square-2",
            "square-7",
            "square-4",
            "square-1",
        ];
        BingoIssuedLayout
            .Generate("legacy-seed", 1, 3, legacyAssignmentKey, squareKeys)
            .ShouldBe(expectedLayout);
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "host",
                DisplayName = "Host",
                EnabledFeatures = HostFeatureFlags.Bingo,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            var template = new BingoTemplate
            {
                HostId = host.Id,
                PublicId = Guid.NewGuid(),
                CreationOperationId = Guid.NewGuid(),
                Name = "Legacy migration template",
                CurrentRevision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            var revision = new BingoTemplateRevision
            {
                HostId = host.Id,
                OperationId = Guid.NewGuid(),
                Template = template,
                Revision = 1,
                Dimension = 3,
                LinePointsReward = "0",
                FullCardPointsReward = "0",
                CreatedByTwitchUserId = "host-id",
                CreatedByLogin = "host",
                CreatedAtUtc = DateTime.UtcNow,
            };
            revision.Squares.AddRange(
                squareKeys.Select(
                    (key, index) =>
                        new BingoSquare
                        {
                            HostId = host.Id,
                            Key = key,
                            SortOrder = index,
                            Title = $"Square {index}",
                            Kind = BingoSquareKind.Manual,
                        }
                )
            );
            var game = new BingoGame
            {
                HostId = host.Id,
                PublicId = Guid.NewGuid(),
                CreationOperationId = Guid.NewGuid(),
                TemplateRevision = revision,
                TemplateName = template.Name,
                TemplateRevisionNumber = 1,
                Dimension = 3,
                Seed = "legacy-seed",
                Mode = BingoGameMode.UniquePerViewer,
                Status = BingoGameStatus.Issued,
                LinePointsReward = "0",
                FullCardPointsReward = "0",
                CreatedAtUtc = DateTime.UtcNow,
                IssuedAtUtc = DateTime.UtcNow,
            };
            var card = new BingoCard
            {
                HostId = host.Id,
                Game = game,
                PublicId = cardPublicId,
                AssignmentKey = legacyAssignmentKey,
                AssignmentName = "Alice Display",
                IssuedAtUtc = DateTime.UtcNow,
            };
            _ = seed.BingoTemplates.Add(template);
            _ = seed.BingoGames.Add(game);
            _ = seed.BingoCards.Add(card);
            _ = await seed.SaveChangesAsync();
            seed.BingoMarks.AddRange(
                Enumerable
                    .Range(0, 3)
                    .Select(position => new BingoMark
                    {
                        HostId = host.Id,
                        GameId = game.Id,
                        Card = card,
                        SquareKey = expectedLayout[position],
                        Position = position,
                        IsActive = true,
                        FirstMarkedAtUtc = DateTime.UtcNow,
                        ChangedAtUtc = DateTime.UtcNow,
                    })
            );
            _ = seed.BingoWins.Add(
                new BingoWin
                {
                    HostId = host.Id,
                    Game = game,
                    Card = card,
                    PublicId = Guid.NewGuid(),
                    Kind = BingoWinKind.Row,
                    RuleIndex = 0,
                    RuleKey = "row:0",
                    PointsReward = "0",
                    CompletedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        await initializer.InitializeAsync(default);
        await initializer.InitializeAsync(default);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using (var downgrade = await factory.CreateDbContextAsync())
            {
                var refusal = await Should.ThrowAsync<SqliteException>(() =>
                    downgrade.GetService<IMigrator>().MigrateAsync(_concurrencyMigration)
                );
                refusal.SqliteErrorCode.ShouldBe(SQLitePCL.raw.SQLITE_CONSTRAINT);
                refusal.SqliteExtendedErrorCode.ShouldBe(SQLitePCL.raw.SQLITE_CONSTRAINT_TRIGGER);
            }
            await AssertMaterializedCardPreservedAsync(
                factory,
                cardPublicId,
                squareKeys,
                expectedLayout
            );
        }
    }

    [Test]
    public async Task Downgrade_WithoutMaterializedUniqueLayouts_Succeeds()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        var initializer = new BlokeBotDatabaseInitializer(factory);
        await initializer.InitializeAsync(default);

        await using (var downgrade = await factory.CreateDbContextAsync())
        {
            await downgrade.GetService<IMigrator>().MigrateAsync(_concurrencyMigration);
            (await downgrade.Database.GetAppliedMigrationsAsync()).ShouldNotContain(
                _opaqueAssignmentMigration
            );
        }

        await initializer.InitializeAsync(default);
        await using var upgraded = await factory.CreateDbContextAsync();
        (await upgraded.Database.GetAppliedMigrationsAsync()).ShouldContain(
            _opaqueAssignmentMigration
        );
    }

    private static async Task AssertMaterializedCardPreservedAsync(
        SqliteBlokeBotDbFactory factory,
        Guid cardPublicId,
        IReadOnlyCollection<string> squareKeys,
        IReadOnlyList<string> expectedLayout
    )
    {
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.Database.GetAppliedMigrationsAsync()).ShouldContain(
            _opaqueAssignmentMigration
        );
        var migrated = await verify
            .BingoCards.Include(value => value.Marks)
            .Include(value => value.Wins)
            .SingleAsync(value => value.PublicId == cardPublicId);
        migrated.AssignmentKey.ShouldBe(BingoCardAssignmentKey.Opaque(cardPublicId));
        migrated.AssignmentKey.ShouldNotContain("alice", Case.Insensitive);
        var restoredLayout = BingoIssuedLayout.Restore(migrated.IssuedLayout!, 3, squareKeys);
        restoredLayout.ShouldBe(expectedLayout);
        restoredLayout
            .Take(3)
            .ShouldBe(
                migrated.Marks.OrderBy(value => value.Position).Select(value => value.SquareKey)
            );
        var win = migrated.Wins.Single();
        win.Kind.ShouldBe(BingoWinKind.Row);
        win.RuleIndex.ShouldBe(0);
        win.RuleKey.ShouldBe("row:0");
    }
}
