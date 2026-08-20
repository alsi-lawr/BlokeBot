using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationTransferGuessingTests
{
    [Test]
    public async Task Replace_PreservesMappedAndHistoryBoundIdsAndDeletesOnlyUnusedAbsentProfile()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        int historyId;
        int mappedId;
        int unusedId;
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "destination",
                DisplayName = "Destination",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            var history = Profile(hostId, "History", "history", true);
            history.Rounds.Add(
                new GuessRound
                {
                    HostId = hostId,
                    Status = GuessRoundStatus.Closed,
                    StartedAtUtc = DateTime.UtcNow.AddHours(-1),
                    ClosedAtUtc = DateTime.UtcNow,
                }
            );
            var mapped = Profile(hostId, "Pack", "pack", false);
            var unused = Profile(hostId, "Unused", "unused", false);
            seed.Profiles.AddRange(history, mapped, unused);
            _ = await seed.SaveChangesAsync();
            historyId = history.Id;
            mappedId = mapped.Id;
            unusedId = unused.Id;
        }
        var section = new GuessingSectionV1([
            ImportedProfile("profile-0001", "Renamed Pack", "renamed-pack", false),
        ]);
        var preview = await new ConfigurationImportPreviewService(database).PreviewAsync(
            Document(section),
            new(
                hostId,
                [new(ConfigurationSectionId.Guessing, ImportConflictStrategy.ReplaceSection, [])],
                new HashSet<HostFeatureFlags>()
            ),
            CancellationToken.None
        );
        var conflict = preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Conflicts.Single();
        conflict.ImportedId.ShouldBe($"target-{historyId}");
        conflict.AllowedResolutions.ShouldBe([
            ImportConflictResolution.Retain,
            ImportConflictResolution.Abort,
        ]);

        await using (var db = await database.CreateDbContextAsync())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var issues = await GuessingConfigurationTransferAdapter.StageAsync(
                db,
                hostId,
                section,
                new(
                    ConfigurationSectionId.Guessing,
                    ImportConflictStrategy.ReplaceSection,
                    [
                        new(
                            $"target-{historyId}",
                            ImportConflictResolution.Retain,
                            TargetId: historyId
                        ),
                        new("profile-0001", ImportConflictResolution.Replace, TargetId: mappedId),
                    ]
                ),
                CancellationToken.None
            );
            issues.ShouldBeEmpty();
            _ = await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verify = await database.CreateDbContextAsync();
        var profiles = await verify.Profiles.OrderBy(x => x.Id).ToArrayAsync();
        profiles.Select(x => x.Id).ShouldBe([historyId, mappedId]);
        profiles.Single(x => x.Id == mappedId).Name.ShouldBe("Renamed Pack");
        profiles.Single(x => x.Id == mappedId).Slug.ShouldBe("renamed-pack");
        (await verify.Rounds.SingleAsync()).GuessRoundProfileId.ShouldBe(historyId);
        (await verify.Profiles.AnyAsync(x => x.Id == unusedId)).ShouldBeFalse();
    }

    [Test]
    public async Task Replace_WithoutRetainDecision_RejectsBeforeMutation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "destination",
                DisplayName = "Destination",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            var history = Profile(hostId, "History", "history", true);
            history.Rounds.Add(
                new GuessRound
                {
                    HostId = hostId,
                    Status = GuessRoundStatus.Closed,
                    StartedAtUtc = DateTime.UtcNow,
                    ClosedAtUtc = DateTime.UtcNow,
                }
            );
            _ = seed.Profiles.Add(history);
            _ = await seed.SaveChangesAsync();
        }
        await using var db = await database.CreateDbContextAsync();
        var issues = await GuessingConfigurationTransferAdapter.StageAsync(
            db,
            hostId,
            new([ImportedProfile("profile-0001", "New", "new", false)]),
            new(ConfigurationSectionId.Guessing, ImportConflictStrategy.ReplaceSection, []),
            CancellationToken.None
        );

        issues.Single().Message.ShouldContain("Retain it or abort");
        db.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Test]
    public async Task AddMissing_MatchingProfile_RemainsUnchanged()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        int profileId;
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "destination",
                DisplayName = "Destination",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            var profile = Profile(hostId, "Pack", "pack", true);
            _ = seed.Profiles.Add(profile);
            _ = await seed.SaveChangesAsync();
            profileId = profile.Id;
        }

        await using (var db = await database.CreateDbContextAsync())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var issues = await GuessingConfigurationTransferAdapter.StageAsync(
                db,
                hostId,
                new([ImportedProfile("profile-0001", "Pack", "pack", false)]),
                new(ConfigurationSectionId.Guessing, ImportConflictStrategy.AddMissing, []),
                CancellationToken.None
            );
            issues.ShouldBeEmpty();
            _ = await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verify = await database.CreateDbContextAsync();
        var stored = await verify.Profiles.SingleAsync();
        stored.Id.ShouldBe(profileId);
        stored.Slug.ShouldBe("pack");
        stored.IsDefault.ShouldBeTrue();
        stored.Revision.ShouldBe(0);
    }

    private static GuessRoundProfile Profile(
        int hostId,
        string name,
        string slug,
        bool isDefault
    ) =>
        new()
        {
            HostId = hostId,
            Name = name,
            Slug = slug,
            IsDefault = isDefault,
            ReplySettings = new(),
            Options = [new() { Name = "answer", ReplyText = "answer" }],
        };

    private static GuessingProfileV1 ImportedProfile(
        string id,
        string name,
        string slug,
        bool isDefault
    ) =>
        new(
            id,
            name,
            slug,
            isDefault,
            "0",
            [],
            new("", "", "", "", "", "", "", "", "", "", "", "", ""),
            [new("answer", "answer", ReplyDeliveryTarget.Chat)]
        );

    private static ConfigurationDocumentV1 Document(GuessingSectionV1 guessing) =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            DateTimeOffset.UtcNow,
            new("source", "0.12.0"),
            new(Guessing: guessing)
        );
}
