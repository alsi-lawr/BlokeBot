using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class BingoMigrationTests
{
    private const string _previousMigration = "20260810091437_v0.9.0_CommunityProgression";

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
}
