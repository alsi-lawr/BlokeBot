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
    public async Task Preview_ExplicitMappingWinsBeforeSlugAndRefreshesCountsAndConflicts()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        int automaticId;
        int mappedId;
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
            var automatic = Profile(hostId, "Imported", "imported", true);
            var mapped = Profile(hostId, "History target", "history-target", false);
            mapped.Rounds.Add(
                new GuessRound
                {
                    HostId = hostId,
                    Status = GuessRoundStatus.Closed,
                    StartedAtUtc = DateTime.UtcNow.AddHours(-1),
                    ClosedAtUtc = DateTime.UtcNow,
                }
            );
            seed.Profiles.AddRange(automatic, mapped);
            _ = await seed.SaveChangesAsync();
            automaticId = automatic.Id;
            mappedId = mapped.Id;
        }
        var section = new GuessingSectionV1([
            ImportedProfile("imported-profile", "Imported", "imported", true),
        ]);
        var service = new ConfigurationImportPreviewService(database);

        var automaticPreview = await service.PreviewAsync(
            Document(section),
            new(
                hostId,
                [new(ConfigurationSectionId.Guessing, ImportConflictStrategy.ReplaceSection, [])],
                new HashSet<HostFeatureFlags>()
            ),
            CancellationToken.None
        );
        var automaticSection = automaticPreview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single();
        automaticSection.Counts.ShouldBe(new(0, 1, 1, 0));
        automaticSection.Conflicts.Single().ImportedId.ShouldBe($"target-{mappedId}");
        var choices = automaticSection.GuessingProfileMappings.Single().ExistingTargets;
        choices.ShouldContain(x => x.TargetId == automaticId);
        choices.ShouldContain(x => x.TargetId == mappedId);

        var mappedPreview = await service.PreviewAsync(
            Document(section),
            new(
                hostId,
                [
                    new(
                        ConfigurationSectionId.Guessing,
                        ImportConflictStrategy.ReplaceSection,
                        [
                            new(
                                "imported-profile",
                                ImportConflictResolution.Replace,
                                TargetId: mappedId
                            ),
                        ]
                    ),
                ],
                new HashSet<HostFeatureFlags>()
            ),
            CancellationToken.None
        );

        var mappedSection = mappedPreview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single();
        mappedSection.Counts.ShouldBe(new(0, 1, 0, 1));
        mappedSection.Conflicts.ShouldBeEmpty();
    }

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

    [Test]
    public async Task FormatOneExportImport_SharedAndUniqueAliases_RoundTripByProfile()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int sourceHostId;
        int destinationHostId;
        GuessingSectionV1 exported;
        await using (var source = await database.CreateDbContextAsync())
        {
            var sourceHost = new BotHost
            {
                Login = "source",
                DisplayName = "Source",
                CreatedAtUtc = DateTime.UtcNow,
            };
            var destinationHost = new BotHost
            {
                Login = "destination",
                DisplayName = "Destination",
                CreatedAtUtc = DateTime.UtcNow,
            };
            source.Hosts.AddRange(sourceHost, destinationHost);
            _ = await source.SaveChangesAsync();
            sourceHostId = sourceHost.Id;
            destinationHostId = destinationHost.Id;
            var standard = Profile(sourceHostId, "Standard", "standard", true);
            var special = Profile(sourceHostId, "Special", "special", false);
            source.Profiles.AddRange(standard, special);
            _ = await source.SaveChangesAsync();
            source.CommandAliases.AddRange(
                Alias(sourceHostId, standard.Id, AppCommandKind.Start, "start-standard"),
                Alias(sourceHostId, special.Id, AppCommandKind.Start, "start-special"),
                Alias(sourceHostId, standard.Id, AppCommandKind.Guess, "predict"),
                Alias(sourceHostId, special.Id, AppCommandKind.Guess, "predict"),
                Alias(sourceHostId, standard.Id, AppCommandKind.Guesses, "choices"),
                Alias(sourceHostId, special.Id, AppCommandKind.Stop, "halt-special")
            );
            _ = await source.SaveChangesAsync();
            exported = await ConfigurationExportMappers.GuessingAsync(
                source,
                sourceHostId,
                CancellationToken.None
            );
        }

        await using (var destination = await database.CreateDbContextAsync())
        await using (var transaction = await destination.Database.BeginTransactionAsync())
        {
            var issues = await GuessingConfigurationTransferAdapter.StageAsync(
                destination,
                destinationHostId,
                exported,
                new(ConfigurationSectionId.Guessing, ImportConflictStrategy.Merge, []),
                CancellationToken.None
            );
            issues.ShouldBeEmpty();
            _ = await destination.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verify = await database.CreateDbContextAsync();
        var profiles = await verify
            .Profiles.Where(profile => profile.HostId == destinationHostId)
            .OrderBy(profile => profile.Slug)
            .ToArrayAsync();
        profiles.Select(profile => profile.Slug).ShouldBe(["special", "standard"]);
        var aliases = await verify
            .CommandAliases.Where(alias => alias.HostId == destinationHostId)
            .OrderBy(alias => alias.Alias)
            .ThenBy(alias => alias.GuessRoundProfileId)
            .ToArrayAsync();
        aliases.Where(alias => alias.Alias == "predict").Count().ShouldBe(2);
        aliases
            .Where(alias => alias.Alias == "predict")
            .ShouldAllBe(alias => alias.Kind == AppCommandKind.Guess);
        aliases.ShouldContain(alias => alias.Alias == "choices");
        aliases.ShouldContain(alias => alias.Alias == "halt-special");
        aliases.Count(alias => alias.Kind == AppCommandKind.Start).ShouldBe(2);
    }

    [Test]
    public async Task FormatOneStaging_StartAndCrossKindOverlaps_RejectBeforeMutation()
    {
        var cases = new[]
        {
            (First: AppCommandKind.Start, Second: AppCommandKind.Start),
            (First: AppCommandKind.Guess, Second: AppCommandKind.Win),
        };
        foreach (var (first, second) in cases)
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
            }
            var section = new GuessingSectionV1([
                ImportedProfile("first", "First", "first", true) with
                {
                    CommandAliases = [new(first, ["shared"])],
                },
                ImportedProfile("second", "Second", "second", false) with
                {
                    CommandAliases = [new(second, ["shared"])],
                },
            ]);
            await using var db = await database.CreateDbContextAsync();

            var issues = await GuessingConfigurationTransferAdapter.StageAsync(
                db,
                hostId,
                section,
                new(ConfigurationSectionId.Guessing, ImportConflictStrategy.Merge, []),
                CancellationToken.None
            );

            issues
                .ShouldHaveSingleItem()
                .Message.ShouldBe("!shared is already used by another bot command.");
            db.ChangeTracker.HasChanges().ShouldBeFalse();
        }
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

    private static CommandAlias Alias(
        int hostId,
        int profileId,
        AppCommandKind kind,
        string alias
    ) =>
        new()
        {
            HostId = hostId,
            GuessRoundProfileId = profileId,
            Kind = kind,
            Alias = alias,
        };

    private static ConfigurationDocumentV1 Document(GuessingSectionV1 guessing) =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            DateTimeOffset.UtcNow,
            new("source", "0.12.0"),
            new(Guessing: guessing)
        );
}
