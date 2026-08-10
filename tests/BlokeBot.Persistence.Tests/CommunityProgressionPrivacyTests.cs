using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class CommunityProgressionPrivacyTests
{
    [Test]
    public async Task ViewerExportAndErasure_CoverProgressRewardsSnapshotsAuditsAndPublicEvents()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var now = DateTime.UtcNow;
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "host",
                DisplayName = "Host",
                EnabledFeatures = HostFeatureFlags.CommunityProgression,
                CreatedAtUtc = now,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            var season = new CommunitySeason
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                CreationOperationId = Guid.NewGuid(),
                Name = "Season",
                Status = CommunitySeasonStatus.Open,
                Visibility = CommunityVisibility.Public,
                StartsAtUtc = now.AddDays(-1),
                EndsAtUtc = now.AddDays(1),
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            var definition = new CommunityDefinition
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                Season = season,
                Key = "achievement",
                Name = "Achievement",
                Kind = CommunityDefinitionKind.Achievement,
                Scope = CommunityProgressScope.Viewer,
                CompletionMode = CommunityCompletionMode.OneTime,
                EventRule = CommunityEventRuleKind.ChatMessage,
                Increment = CommunityProgressIncrement.Occurrence,
                Target = 1,
                ResetCadence = CommunityResetCadence.None,
                ResetLocalTime = "00:00",
                ScheduleRevision = 1,
                CreatedAtUtc = now,
            };
            var reward = new CommunityRewardDefinition
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                Season = season,
                Key = "title",
                Kind = CommunityRewardKind.Title,
                Name = "Title",
                PresentationToken = "title",
                CreatedAtUtc = now,
            };
            seed.AddRange(season, definition, reward);
            _ = await seed.SaveChangesAsync();
            var completion = new CommunityCompletion
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                SeasonId = season.Id,
                DefinitionId = definition.Id,
                SubjectKey = "viewer:viewer-id",
                ViewerTwitchUserId = "viewer-id",
                ViewerLogin = "viewer",
                ViewerDisplayName = "Viewer Name",
                DefinitionKey = definition.Key,
                DefinitionName = definition.Name,
                Sequence = 1,
                SourceOperationKey = "source",
                CompletedAtUtc = now,
            };
            _ = seed.CommunityCompletions.Add(completion);
            _ = await seed.SaveChangesAsync();
            seed.AddRange(
                new CommunityProgress
                {
                    HostId = hostId,
                    SeasonId = season.Id,
                    DefinitionId = definition.Id,
                    SubjectKey = "viewer:viewer-id",
                    ViewerTwitchUserId = "viewer-id",
                    ViewerLogin = "viewer",
                    ViewerDisplayName = "Viewer Name",
                    Amount = 1,
                    CompletionCount = 1,
                    UpdatedAtUtc = now,
                },
                new CommunityRewardUnlock
                {
                    HostId = hostId,
                    RewardDefinitionId = reward.Id,
                    ViewerTwitchUserId = "viewer-id",
                    ViewerLogin = "viewer",
                    ViewerDisplayName = "Viewer Name",
                    CompletionId = completion.Id,
                    GrantedAtUtc = now,
                },
                new CommunityEquippedReward
                {
                    HostId = hostId,
                    Kind = CommunityRewardKind.Title,
                    RewardDefinitionId = reward.Id,
                    ViewerTwitchUserId = "viewer-id",
                    ViewerLogin = "viewer",
                    LastOperationId = Guid.NewGuid(),
                    EquippedAtUtc = now,
                },
                new CommunitySeasonStanding
                {
                    HostId = hostId,
                    SeasonId = season.Id,
                    ViewerTwitchUserId = "viewer-id",
                    ViewerLogin = "viewer",
                    ViewerDisplayName = "Viewer Name",
                    CompletedCount = 1,
                    Rank = 1,
                    SnapshottedAtUtc = now,
                },
                new CommunityAudit
                {
                    HostId = hostId,
                    SeasonId = season.Id,
                    Action = "Moderated",
                    OperationKey = "audit",
                    ActorTwitchUserId = "viewer-id",
                    ActorLogin = "viewer",
                    PrivateNote = "private viewer note",
                    OccurredAtUtc = now,
                },
                new CommunityDomainEvent
                {
                    HostId = hostId,
                    SeasonId = season.Id,
                    Kind = CommunityEventKind.Completed,
                    OperationKey = "event",
                    PublicPayload = "{\"viewer\":\"viewer\"}",
                    OccurredAtUtc = now,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        await using var db = await factory.CreateDbContextAsync();
        var subject = PrivacySubject.Create("viewer-id", "viewer");
        var export = await ViewerPrivacyService.ExportAsync(db, subject, hostId, default);
        var erased = await ViewerPrivacyService.EraseAsync(db, subject, hostId, default);

        export.Sections.Keys.ShouldContain("community.progress");
        export.Sections.Keys.ShouldContain("community.completions");
        export.Sections.Keys.ShouldContain("community.reward-unlocks");
        erased.TotalChangedRows.ShouldBeGreaterThan(0);
        (await db.CommunityProgress.CountAsync()).ShouldBe(0);
        (await db.CommunityRewardUnlocks.CountAsync()).ShouldBe(0);
        (await db.CommunityEquippedRewards.CountAsync()).ShouldBe(0);
        var completionAfter = await db.CommunityCompletions.SingleAsync();
        completionAfter.ViewerTwitchUserId.ShouldBe(ViewerPrivacyService.ErasedToken);
        completionAfter.SubjectKey.ShouldStartWith("erased:");
        var standingAfter = await db.CommunitySeasonStandings.SingleAsync();
        standingAfter.ViewerTwitchUserId.ShouldStartWith("erased:");
        (await db.CommunityEvents.CountAsync()).ShouldBe(0);
        var auditAfter = await db.CommunityAudits.SingleAsync();
        auditAfter.ActorLogin.ShouldBe(ViewerPrivacyService.ErasedToken);
        auditAfter.PrivateNote.ShouldBeEmpty();
    }
}
