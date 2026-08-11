using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class CompetitionPrivacyTests
{
    [Test]
    public async Task ViewerExportAndErasure_CoverEntrantsPrivateContactRewardsAuditsAndEvents()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var now = DateTime.UtcNow;
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "host",
                DisplayName = "Host",
                EnabledFeatures = HostFeatureFlags.Competitions,
                CreatedAtUtc = now,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            var competition = new Competition
            {
                HostId = host.Id,
                PublicId = Guid.NewGuid(),
                CreationOperationId = Guid.NewGuid(),
                Name = "Cup",
                Format = CompetitionFormat.RoundRobin,
                EntryKind = CompetitionEntryKind.Individual,
                Status = CompetitionStatus.Completed,
                Seeding = CompetitionSeeding.Random,
                Tiebreak = CompetitionTiebreak.ScoreDifferenceThenScoreFor,
                Capacity = 8,
                TeamSize = 1,
                Seed = "seed",
                AlgorithmVersion = "v1",
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            var entrant = new CompetitionEntrant
            {
                HostId = host.Id,
                PublicId = Guid.NewGuid(),
                RegistrationOperationId = Guid.NewGuid(),
                Name = "Viewer Name",
                RegisteredAtUtc = now,
                Members =
                [
                    new()
                    {
                        HostId = host.Id,
                        TwitchUserId = "viewer-id",
                        Login = "viewer",
                        DisplayName = "Viewer Name",
                        PrivateContact = "PRIVATE CONTACT",
                    },
                ],
            };
            competition.Entrants.Add(entrant);
            competition.Audits.Add(
                new CompetitionAudit
                {
                    HostId = host.Id,
                    OperationId = Guid.NewGuid(),
                    Action = CompetitionAuditAction.Completed,
                    ActorTwitchUserId = "viewer-id",
                    ActorLogin = "viewer",
                    PrivateReason = "PRIVATE REASON",
                    OccurredAtUtc = now,
                }
            );
            competition.Events.Add(
                new CompetitionDomainEvent
                {
                    HostId = host.Id,
                    CompetitionPublicId = competition.PublicId,
                    OperationKey = "completed",
                    SchemaVersion = 1,
                    Kind = CompetitionEventKind.Completed,
                    PublicPayload = "{\"winner\":\"viewer\"}",
                    OccurredAtUtc = now,
                }
            );
            _ = seed.Competitions.Add(competition);
            _ = await seed.SaveChangesAsync();
            _ = seed.CompetitionRewardReceipts.Add(
                new CompetitionRewardReceipt
                {
                    HostId = host.Id,
                    CompetitionId = competition.Id,
                    EntrantId = entrant.Id,
                    TwitchUserId = "viewer-id",
                    Login = "viewer",
                    Kind = CompetitionRewardKind.Placement,
                    RewardKey = "placement:1",
                    Placement = 1,
                    PointsGranted = "100",
                    GrantedAtUtc = now,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        var subject = PrivacySubject.Create("viewer-id", "viewer");
        await using (var exportDb = await factory.CreateDbContextAsync())
        {
            var export = await ViewerPrivacyService.ExportAsync(exportDb, subject, null, default);
            export.Sections.Keys.ShouldContain("competitions.entrants");
            export.Sections.Keys.ShouldContain("competitions.members");
            export.Sections.Keys.ShouldContain("competitions.rewards");
            export.Sections.Keys.ShouldContain("competitions.moderation-audits");
        }
        await using (var eraseDb = await factory.CreateDbContextAsync())
        {
            var report = await ViewerPrivacyService.EraseAsync(eraseDb, subject, null, default);
            report.TotalChangedRows.ShouldBeGreaterThan(0);
        }
        await using var verify = await factory.CreateDbContextAsync();
        var entrantAfter = await verify.CompetitionEntrants.SingleAsync();
        entrantAfter.Name.ShouldBe(ViewerPrivacyService.ErasedToken);
        var memberAfter = await verify.CompetitionEntrantMembers.SingleAsync();
        memberAfter.TwitchUserId.ShouldBe(ViewerPrivacyService.ErasedToken);
        memberAfter.Login.ShouldBe(ViewerPrivacyService.ErasedToken);
        memberAfter.PrivateContact.ShouldBeEmpty();
        (await verify.CompetitionRewardReceipts.SingleAsync()).Login.ShouldBe(
            ViewerPrivacyService.ErasedToken
        );
        (await verify.CompetitionAudits.SingleAsync()).PrivateReason.ShouldBeEmpty();
        (await verify.CompetitionEvents.CountAsync()).ShouldBe(0);
    }
}
