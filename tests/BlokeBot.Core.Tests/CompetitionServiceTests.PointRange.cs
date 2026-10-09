using System.Globalization;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class CompetitionServiceTests
{
    [Test]
    public async Task Sqlite_PlacementRangeRejectionRollsBackEarlierRewardAndCompletion() =>
        await PlacementRangeAsync(await PointProviderFixture.SqliteAsync());

    [Test, Explicit]
    public async Task PostgreSql_PlacementRangeRejectionRollsBackEarlierRewardAndCompletion() =>
        await PlacementRangeAsync(await PointProviderFixture.PostgreSqlAsync());

    private static async Task PlacementRangeAsync(PointProviderFixture database)
    {
        await using var owned = database;
        var hostId = await database.SeedHostAsync();
        await using (var seed = database.CreateDbContext())
        {
            (await seed.Hosts.SingleAsync()).EnabledFeatures = HostFeatureFlags.All;
            _ = await seed.SaveChangesAsync();
        }
        await using (var seed = database.CreateDbContext())
        {
            var season = new CommunitySeason
            {
                HostId = hostId,
                PublicId = Guid.NewGuid(),
                Name = "Season",
                Status = CommunitySeasonStatus.Draft,
                StartsAtUtc = _now.UtcDateTime,
                EndsAtUtc = _now.AddDays(1).UtcDateTime,
                Revision = 1,
                CreationOperationId = Guid.NewGuid(),
                UpdatedAtUtc = _now.UtcDateTime,
                CreatedAtUtc = _now.UtcDateTime,
            };
            foreach (var key in new[] { "winner", "runner-up", "two-wins" })
            {
                season.Definitions.Add(
                    new CommunityDefinition
                    {
                        HostId = hostId,
                        PublicId = Guid.NewGuid(),
                        Key = key,
                        Name = key,
                        Kind = CommunityDefinitionKind.Achievement,
                        Scope = CommunityProgressScope.Viewer,
                        CompletionMode = CommunityCompletionMode.OneTime,
                        EventRule = CommunityEventRuleKind.ExternalGrant,
                        Increment = CommunityProgressIncrement.Occurrence,
                        Target = 1,
                        ResetCadence = CommunityResetCadence.None,
                        ResetLocalTime = "00:00",
                        ScheduleRevision = 1,
                        CreatedAtUtc = _now.UtcDateTime,
                    }
                );
            }
            _ = seed.CommunitySeasons.Add(season);
            _ = await seed.SaveChangesAsync();
        }
        var service = new CompetitionService(
            database,
            TestEventBus.Create<AppEventKind>(),
            new RecordingGrants(),
            [],
            new FixedTimeProvider(_now)
        );
        var completion = await PrepareRewardingCompletionAsync(service, hostId);
        int ledgers;
        int receipts;
        string? winnerBalance;
        await using (var drift = database.CreateDbContext())
        {
            var runnerUp = await drift.PointBalances.SingleOrDefaultAsync(value =>
                value.Login == "two"
            );
            if (runnerUp is null)
            {
                _ = drift.PointBalances.Add(
                    new()
                    {
                        HostId = hostId,
                        Login = "two",
                        Amount = PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture),
                        UpdatedAtUtc = DateTime.UtcNow,
                    }
                );
            }
            else
            {
                runnerUp.Amount = PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture);
            }
            _ = await drift.SaveChangesAsync();
            winnerBalance = (
                await drift.PointBalances.SingleOrDefaultAsync(value => value.Login == "one")
            )?.Amount;
            ledgers = await drift.PointLedgerEntries.CountAsync();
            receipts = await drift.CompetitionRewardReceipts.CountAsync();
        }
        _ = (
            await service.CompleteAsync(hostId, completion, default)
        ).ShouldBeOfType<CompetitionOutcome.Invalid>();
        await using var verify = database.CreateDbContext();
        (
            (await verify.PointBalances.SingleOrDefaultAsync(value => value.Login == "one"))?.Amount
        ).ShouldBe(winnerBalance);
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(ledgers);
        (await verify.CompetitionRewardReceipts.CountAsync()).ShouldBe(receipts);
        var competition = await verify.Competitions.SingleAsync();
        competition.Status.ShouldBe(CompetitionStatus.Running);
        competition.Revision.ShouldBe(completion.ExpectedRevision);
    }
}
