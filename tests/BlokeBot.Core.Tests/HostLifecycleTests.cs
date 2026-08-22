using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class HostLifecycleTests
{
    [Test]
    public async Task RemovingHost_PreservesImmutableMediaReferencedBySiblingHost()
    {
        var stateDirectory = TemporaryDirectory();
        try
        {
            await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var removedHostId = await SeedHostedChannelGraphAsync(database, "removed-media");
            var siblingHostId = await SeedHostedChannelGraphAsync(database, "sibling-media");
            var databasePath = Path.Combine(stateDirectory, "blokebot.db");
            var documentId = Guid.NewGuid();
            var storageKey = new string('a', 32);
            var documentDirectory = OverlayMediaDirectory.DocumentDirectory(databasePath);
            _ = Directory.CreateDirectory(documentDirectory);
            var path = Path.Combine(documentDirectory, storageKey);
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            await using (var seed = await database.CreateDbContextAsync())
            {
                var document = new OverlayMediaDocument
                {
                    Id = documentId,
                    ContentType = "video/mp4",
                    ByteLength = 3,
                    StorageKey = storageKey,
                    State = OverlayMediaDocumentState.Available,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                seed.OverlayMediaAssets.AddRange(
                    Reference(removedHostId, "Removed host", document),
                    Reference(siblingHostId, "Sibling host", document)
                );
                _ = await seed.SaveChangesAsync();
            }

            var result = await Service(database, TestEventBus.Create<AppEventKind>(), databasePath)
                .RemoveAsync(removedHostId, CancellationToken.None);

            result.Removed.ShouldBeTrue();
            File.Exists(path).ShouldBeTrue();
            await using var verify = await database.CreateDbContextAsync();
            var sibling = await verify.OverlayMediaAssets.SingleAsync();
            sibling.HostId.ShouldBe(siblingHostId);
            sibling.DocumentId.ShouldBe(documentId);
            (await verify.OverlayMediaDocuments.CountAsync()).ShouldBe(1);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Test]
    public async Task HostWithOwnedGraphAndMedia_Removing_CascadesHostDataAndPreservesSiblings()
    {
        var stateDirectory = TemporaryDirectory();
        try
        {
            await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
            var hostId = await SeedHostedChannelGraphAsync(dbFactory, "streamer");
            var siblingId = await SeedHostedChannelGraphAsync(dbFactory, "sibling");
            var databasePath = Path.Combine(stateDirectory, "blokebot.db");
            var removedMedia = SeedMediaDirectory(databasePath, hostId);
            var siblingMedia = SeedMediaDirectory(databasePath, siblingId);
            var events = TestEventBus.Create<AppEventKind>();
            var eventCount = 0;
            _ = events.Subscribe(
                AppEventKind.HostedChannelsChanged,
                ObserverIdentity.Named("Test.HostRemoval"),
                (_, _) =>
                {
                    eventCount++;
                    return ValueTask.CompletedTask;
                }
            );
            var service = Service(dbFactory, events, databasePath);

            var result = await service.RemoveAsync(hostId, CancellationToken.None);

            result.Removed.ShouldBeTrue();
            _ = result.Media.ShouldBeOfType<HostMediaCleanup.Removed>();
            eventCount.ShouldBe(1);
            Directory.Exists(removedMedia).ShouldBeFalse();
            Directory.Exists(siblingMedia).ShouldBeTrue();
            File.Exists(Path.Combine(siblingMedia, "asset.bin")).ShouldBeTrue();
            await using var db = await dbFactory.CreateDbContextAsync();
            (await db.Hosts.CountAsync()).ShouldBe(1);
            (await db.Hosts.SingleAsync()).Id.ShouldBe(siblingId);
            (await db.HostModAccessEntries.CountAsync()).ShouldBe(1);
            (await db.HostModAccessEntries.SingleAsync()).HostId.ShouldBe(siblingId);
            (await db.CommandAliases.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
            (await db.Profiles.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
            (await db.Rounds.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
            (await db.Votes.CountAsync()).ShouldBe(1);
            (await db.PointBalances.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
            (await db.PointLedgerEntries.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
            (await db.PointsGiveaways.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
            (await db.PointsGiveawayEntrants.CountAsync()).ShouldBe(1);
            (await db.PointsGiveawayWinners.CountAsync()).ShouldBe(1);
            (await db.SiteAccessEntries.CountAsync()).ShouldBe(1);
        }
        finally
        {
            ResetPermissions(stateDirectory);
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Test]
    public async Task MissingHostWithLeftoverMedia_Removing_IsIdempotentAndStillCleansMedia()
    {
        var stateDirectory = TemporaryDirectory();
        try
        {
            await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
            var databasePath = Path.Combine(stateDirectory, "blokebot.db");
            var leftover = SeedMediaDirectory(databasePath, 123);
            var events = TestEventBus.Create<AppEventKind>();
            var eventCount = 0;
            _ = events.Subscribe(
                AppEventKind.HostedChannelsChanged,
                ObserverIdentity.Named("Test.MissingHostRemoval"),
                (_, _) =>
                {
                    eventCount++;
                    return ValueTask.CompletedTask;
                }
            );
            var service = Service(dbFactory, events, databasePath);

            var first = await service.RemoveAsync(123, CancellationToken.None);
            var second = await service.RemoveAsync(123, CancellationToken.None);

            first.Removed.ShouldBeFalse();
            _ = first.Media.ShouldBeOfType<HostMediaCleanup.Removed>();
            second.Removed.ShouldBeFalse();
            _ = second.Media.ShouldBeOfType<HostMediaCleanup.NotPresent>();
            eventCount.ShouldBe(0);
            Directory.Exists(leftover).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Test]
    public async Task UndeletableMedia_Removing_ReportsFailedDirectoryInsteadOfClaimingSuccess()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var stateDirectory = TemporaryDirectory();
        try
        {
            await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
            var hostId = await SeedHostedChannelGraphAsync(dbFactory, "streamer");
            var databasePath = Path.Combine(stateDirectory, "blokebot.db");
            var media = SeedMediaDirectory(databasePath, hostId);
            File.SetUnixFileMode(media, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var service = Service(dbFactory, TestEventBus.Create<AppEventKind>(), databasePath);

            var result = await service.RemoveAsync(hostId, CancellationToken.None);

            result.Removed.ShouldBeTrue();
            var failed = result.Media.ShouldBeOfType<HostMediaCleanup.Failed>();
            failed.Directory.ShouldBe(media);
            Directory.Exists(media).ShouldBeTrue();
        }
        finally
        {
            ResetPermissions(stateDirectory);
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Test]
    public async Task NewHost_Provisioning_PublishesHostedChannelChange()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var events = TestEventBus.Create<AppEventKind>();
        var eventCount = 0;
        _ = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.HostProvisioning"),
            (_, _) =>
            {
                eventCount++;
                return ValueTask.CompletedTask;
            }
        );
        var service = new BotHostProvisioningService(
            dbFactory,
            new HostedChannelChangeNotifier(events),
            [],
            TimeProvider.System
        );

        var hostId = await service.EnsureHostAsync(
            "streamer",
            twitchUserId: "123",
            displayName: "Streamer",
            profileImageUrl: null,
            CancellationToken.None
        );

        hostId.ShouldBeGreaterThan(0);
        eventCount.ShouldBe(1);
    }

    private static BotHostRemovalService Service(
        SqliteBlokeBotDbFactory dbFactory,
        EventBus<AppEventKind> events,
        string databasePath
    ) => ServiceCore(dbFactory, events, databasePath);

    private static BotHostRemovalService ServiceCore(
        SqliteBlokeBotDbFactory dbFactory,
        EventBus<AppEventKind> events,
        string databasePath
    )
    {
        var options = Options.Create(new BlokeBotOptions { DatabasePath = databasePath });
        var maintenance = new OverlayMediaMaintenanceService(
            dbFactory,
            options,
            new SystemOverlayMediaFileDeletion(),
            TimeProvider.System,
            NullLogger<OverlayMediaMaintenanceService>.Instance
        );
        return new(
            dbFactory,
            new HostedChannelChangeNotifier(events),
            options,
            maintenance,
            TimeProvider.System,
            NullLogger<BotHostRemovalService>.Instance
        );
    }

    private static string SeedMediaDirectory(string databasePath, int hostId)
    {
        var directory = OverlayMediaDirectory.HostDirectory(databasePath, hostId);
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "asset.bin"), "media");
        return directory;
    }

    private static OverlayMediaAsset Reference(
        int hostId,
        string name,
        OverlayMediaDocument document
    ) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            Name = name,
            ContentRevision = 1,
            Document = document,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static void ResetPermissions(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (
            var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
        )
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blokebot-host-removal-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<int> SeedHostedChannelGraphAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login
    )
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
        if (!await db.SiteAccessEntries.AnyAsync())
        {
            _ = db.SiteAccessEntries.Add(
                new SiteAccessEntry
                {
                    Login = "viewer",
                    Kind = AccessListEntryKind.Whitelist,
                    CreatedAtUtc = DateTime.UtcNow,
                }
            );
        }

        _ = await db.SaveChangesAsync();

        _ = db.HostModAccessSettings.Add(new HostModAccessSettings { HostId = host.Id });
        _ = db.HostModAccessEntries.Add(
            new HostModAccessEntry
            {
                HostId = host.Id,
                Login = "moderator",
                Kind = AccessListEntryKind.Whitelist,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = host.Id,
                Kind = AppCommandKind.Guess,
                Alias = "!guess",
            }
        );
        _ = db.PointsSettings.Add(new PointsSettings { HostId = host.Id });
        _ = db.PointBalances.Add(
            new PointBalance
            {
                HostId = host.Id,
                Login = "viewer",
                Amount = "10",
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = db.PointLedgerEntries.Add(
            new PointLedgerEntry
            {
                HostId = host.Id,
                CreatedAtUtc = DateTime.UtcNow,
                Kind = PointLedgerKind.Add,
                Login = "viewer",
                Delta = "10",
                BalanceAfter = "10",
            }
        );
        _ = db.PointsGiveaways.Add(
            new PointsGiveaway
            {
                HostId = host.Id,
                Status = PointsGiveawayStatus.Completed,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                EndsAtUtc = DateTime.UtcNow.AddMinutes(-1),
                CompletedAtUtc = DateTime.UtcNow,
                Entrants =
                [
                    new PointsGiveawayEntrant { Login = "viewer", JoinedAtUtc = DateTime.UtcNow },
                ],
                Winners = [new PointsGiveawayWinner { Login = "viewer", Payout = "10" }],
            }
        );

        var profile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
            ReplySettings = new BotReplySettings { AvailableGuessesReply = "Guesses: {options}" },
            Options = [new GuessOption { Name = "red", ReplyText = "Red" }],
        };
        _ = db.Profiles.Add(profile);
        _ = await db.SaveChangesAsync();

        var round = new GuessRound
        {
            HostId = host.Id,
            GuessRoundProfileId = profile.Id,
            Status = GuessRoundStatus.Open,
            StartedAtUtc = DateTime.UtcNow,
        };
        _ = db.Rounds.Add(round);
        _ = await db.SaveChangesAsync();

        _ = db.Votes.Add(
            new GuessVote
            {
                GuessRoundId = round.Id,
                Login = "viewer",
                GuessName = "red",
                GuessedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();

        return host.Id;
    }
}
