using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class MomentConvergenceMigrationTests
{
    private const string _momentHubMigration = "20260730051231_v0.4.0_Moments";

    [Test]
    public async Task CorrectionMigration_ReconcilesLoginDuplicatesAndEnforcesOperationKeys()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        long candidateId;
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_momentHubMigration);
            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, CreatedAtUtc)
                VALUES (1, 'host-id', 'host', 'Host', 0, '2026-07-30T00:00:00Z');
                """
            );
            var candidate = new MomentCandidate
            {
                PublicId = Guid.NewGuid(),
                HostId = 1,
                StreamIdentity = "stream",
                State = MomentCandidateState.ClipReady,
                CapturedAtUtc = DateTime.UtcNow,
                LastCapturedAtUtc = DateTime.UtcNow,
            };
            before.MomentCandidates.Add(candidate);
            await before.SaveChangesAsync();
            candidateId = candidate.Id;

            await before.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO moment_contributors
                    (CandidateId, IdentityKey, TwitchUserId, NormalizedLogin, DisplayName,
                     CaptureCount, FirstCapturedAtUtc, LastCapturedAtUtc)
                VALUES
                    ({candidateId}, {"login:viewer"}, NULL, {"viewer"}, {"Viewer"}, 2,
                     {new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc)},
                     {new DateTime(2026, 7, 30, 10, 1, 0, DateTimeKind.Utc)}),
                    ({candidateId}, {"id:viewer-id"}, {"viewer-id"}, {"viewer"}, {"Viewer"}, 3,
                     {new DateTime(2026, 7, 30, 10, 2, 0, DateTimeKind.Utc)},
                     {new DateTime(2026, 7, 30, 10, 3, 0, DateTimeKind.Utc)});

                INSERT INTO moment_votes
                    (CandidateId, IdentityKey, TwitchUserId, NormalizedLogin, CreatedAtUtc)
                VALUES
                    ({candidateId}, {"login:voter"}, NULL, {"voter"},
                     {new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc)}),
                    ({candidateId}, {"id:voter-id"}, {"voter-id"}, {"voter"},
                     {new DateTime(2026, 7, 30, 10, 1, 0, DateTimeKind.Utc)});
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        var contributor = await verify.MomentContributors.SingleAsync();
        contributor.IdentityKey.ShouldBe("id:viewer-id");
        contributor.TwitchUserId.ShouldBe("viewer-id");
        contributor.CaptureCount.ShouldBe(5);
        contributor.FirstCapturedAtUtc.ShouldBe(
            new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc)
        );
        contributor.LastCapturedAtUtc.ShouldBe(
            new DateTime(2026, 7, 30, 10, 3, 0, DateTimeKind.Utc)
        );
        var vote = await verify.MomentVotes.SingleAsync();
        vote.IdentityKey.ShouldBe("id:voter-id");
        vote.TwitchUserId.ShouldBe("voter-id");
        vote.CreatedAtUtc.ShouldBe(new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc));

        verify.MomentEvents.Add(Event(candidateId, "moment:approval", DateTime.UtcNow));
        await verify.SaveChangesAsync();
        verify.MomentEvents.Add(
            Event(candidateId, "moment:approval", DateTime.UtcNow.AddSeconds(1))
        );
        await Should.ThrowAsync<DbUpdateException>(() => verify.SaveChangesAsync());
    }

    private static MomentDomainEvent Event(long candidateId, string operationKey, DateTime now) =>
        new()
        {
            HostId = 1,
            CandidateId = candidateId,
            OperationKey = operationKey,
            SchemaVersion = 1,
            Kind = MomentEventKind.Approved,
            StreamIdentity = "stream",
            PublicPayload = "{}",
            OccurredAtUtc = now,
        };
}
