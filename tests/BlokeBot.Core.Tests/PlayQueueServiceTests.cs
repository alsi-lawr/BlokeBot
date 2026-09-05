using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PlayQueueServiceTests
{
    [Test]
    public async Task ExactSelfPosition_RenameKeepsPositionWithoutClaimingSameLoginOrAnotherHost()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(alpha, Queue("squad"), default));
        _ = Success(await service.ConfigureAsync(beta, Queue("squad"), default));
        _ = Success(await service.JoinAsync(alpha, "squad", Join("ahead", "eu", "Tank"), default));
        _ = Success(
            await service.JoinAsync(alpha, "squad", Join("oldlogin", "eu", "Healer"), default)
        );
        _ = Success(
            await service.JoinAsync(
                alpha,
                "squad",
                Join("renamed", "eu", "Healer") with
                {
                    Viewer = new("renamed", "twitch-oldlogin", "Renamed"),
                },
                default
            )
        );
        var own = Success(
            await service.GetSelfPositionAsync(alpha, "squad", "twitch-oldlogin", default)
        );
        own.Value.Position.ShouldBe(2);
        _ = (await service.GetSelfPositionAsync(alpha, "squad", "different-id", default))
            .Match(
                static _ => throw new InvalidOperationException("Unexpected self row"),
                static value => value.Reason
            )
            .ShouldBeOfType<PlayQueueRejection.NotJoined>();
        _ = (await service.GetSelfPositionAsync(beta, "squad", "twitch-oldlogin", default))
            .Match(
                static _ => throw new InvalidOperationException("Unexpected self row"),
                static value => value.Reason
            )
            .ShouldBeOfType<PlayQueueRejection.NotJoined>();
    }

    [Test]
    public async Task DisabledSwitch_RetainsQueuesBlocksEffectsAndDoesNotReplayOnReenable()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(hostId, Queue("squad"), CancellationToken.None));
        int retainedEventCount;
        await using (var disable = await database.CreateDbContextAsync())
        {
            retainedEventCount = await disable.PlayQueueEvents.CountAsync();
            var host = await disable.Hosts.SingleAsync();
            host.EnabledFeatures &= ~HostFeatureFlags.PlayWithViewers;
            _ = await disable.SaveChangesAsync();
        }

        var rejected = await service.JoinAsync(
            hostId,
            "squad",
            Join("viewer", "eu", "Tank"),
            CancellationToken.None
        );

        _ = rejected
            .Match(
                static _ => throw new InvalidOperationException("Expected rejection."),
                static value => value.Reason
            )
            .ShouldBeOfType<PlayQueueRejection.FeatureDisabled>();
        (await service.GetPublicPageAsync("alpha", "squad", CancellationToken.None)).ShouldBeNull();
        (await service.GetEventsAsync(hostId, 0, 100, CancellationToken.None)).ShouldBeEmpty();
        await using (var verifyDisabled = await database.CreateDbContextAsync())
        {
            (await verifyDisabled.PlayQueues.CountAsync()).ShouldBe(1);
            (await verifyDisabled.PlayQueueEntries.CountAsync()).ShouldBe(0);
            (await verifyDisabled.PlayQueueEvents.CountAsync()).ShouldBe(retainedEventCount);
            var host = await verifyDisabled.Hosts.SingleAsync();
            host.EnabledFeatures |= HostFeatureFlags.PlayWithViewers;
            _ = await verifyDisabled.SaveChangesAsync();
        }

        _ = (
            await service.GetPublicPageAsync("alpha", "squad", CancellationToken.None)
        ).ShouldNotBeNull();
        await using var verifyEnabled = await database.CreateDbContextAsync();
        (await verifyEnabled.PlayQueueEvents.CountAsync()).ShouldBe(retainedEventCount);
    }

    [Test]
    public async Task MultipleHostScopedQueues_JoinLeavePositionAndPublicPrivacy()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(alpha, Queue("squad"), CancellationToken.None));
        _ = Success(
            await service.ConfigureAsync(
                alpha,
                Queue("duos") with
                {
                    Capacity = 2,
                    RoleRequirements = [],
                },
                CancellationToken.None
            )
        );
        _ = Success(await service.ConfigureAsync(beta, Queue("squad"), CancellationToken.None));

        var first = Success(
            await service.JoinAsync(
                alpha,
                "squad",
                Join("viewer", "100", "Tank"),
                CancellationToken.None
            )
        );
        var retry = Success(
            await service.JoinAsync(
                alpha,
                "squad",
                Join("viewer", "100", "Tank"),
                CancellationToken.None
            )
        );
        var position = Success(
            await service.GetPositionAsync(alpha, "squad", new("viewer"), CancellationToken.None)
        );
        var page = await service.GetPublicPageAsync("alpha", "squad", CancellationToken.None);
        var otherHost = await service.GetPublicPageAsync("beta", "squad", CancellationToken.None);

        first.Value.Position.ShouldBe(1);
        retry.WasIdempotent.ShouldBeTrue();
        retry.Value.InternalEntryId.ShouldBe(first.Value.InternalEntryId);
        position.Value.Position.ShouldBe(1);
        _ = page!.Waiting.ShouldHaveSingleItem();
        page.Waiting[0].DisplayName.ShouldBeNull();
        page.Waiting[0]
            .Fields.ShouldBe([
                new("platform", "Platform", ""),
                new("region", "Region", "100"),
                new("rank", "Rank", ""),
                new("preferred-role", "Preferred role", "Tank"),
            ]);
        otherHost!.Waiting.ShouldBeEmpty();
        Success(await service.LeaveAsync(alpha, "squad", new("viewer"), CancellationToken.None))
            .Value.Status.ShouldBe(PlayQueueEntryStatus.Left);
        (
            await service.GetPublicPageAsync("alpha", "duos", CancellationToken.None)
        )!.Waiting.ShouldBeEmpty();
    }

    [Test]
    public async Task ConcurrentJoinAndSelection_DoNotDuplicateEntriesOrPartySlots()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var firstService = CreateService(database);
        var secondService = CreateService(database);
        _ = Success(
            await firstService.ConfigureAsync(host, Queue("squad"), CancellationToken.None)
        );

        var sameViewer = Join("viewer", "100", "Tank") with
        {
            Viewer = new("viewer", "twitch-1", "Viewer"),
        };
        var joins = await Task.WhenAll(
            firstService.JoinAsync(host, "squad", sameViewer, CancellationToken.None),
            secondService.JoinAsync(host, "squad", sameViewer, CancellationToken.None)
        );
        joins
            .Select(Success)
            .Select(static value => value.Value.InternalEntryId)
            .Distinct()
            .Count()
            .ShouldBe(1);
        foreach (
            var (login, role) in new[]
            {
                ("two", "Healer"),
                ("three", "Damage"),
                ("four", "Damage"),
            }
        )
        {
            _ = Success(
                await firstService.JoinAsync(
                    host,
                    "squad",
                    Join(login, "eu", role),
                    CancellationToken.None
                )
            );
        }

        var selections = await Task.WhenAll(
            firstService.SelectPartyAsync(host, "squad", false, CancellationToken.None),
            secondService.SelectPartyAsync(host, "squad", true, CancellationToken.None)
        );
        var selected = selections.Select(Success).ToArray();
        selected.ShouldAllBe(static value => value.Value.Members.Count == 4);
        selected
            .SelectMany(static value => value.Value.Members)
            .Select(static value => value.EntryId)
            .Distinct()
            .Count()
            .ShouldBe(4);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.PlayQueueEntries.CountAsync()).ShouldBe(4);
        (
            await verify.PlayQueueEntries.CountAsync(static value =>
                value.Status == PlayQueueEntryStatus.Selected
            )
        ).ShouldBe(4);
    }

    [Test]
    public async Task ReadinessExpiry_IsDeterministicAndRecordsNoShow()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)
        );
        var service = CreateService(database, clock);
        _ = Success(await service.ConfigureAsync(host, Queue("squad"), CancellationToken.None));
        var entry = Success(
            await service.JoinAsync(
                host,
                "squad",
                Join("viewer", "eu", "Tank"),
                CancellationToken.None
            )
        ).Value;
        var check = Success(
            await service.StartReadyCheckAsync(host, entry.InternalEntryId, CancellationToken.None)
        );

        check.Value.Public.Status.ShouldBe(PlayQueueEntryStatus.AwaitingReady);
        clock.Advance(TimeSpan.FromSeconds(120));
        var page = await service.GetModeratorPageAsync(host, "squad", CancellationToken.None);
        page!.Waiting.ShouldBeEmpty();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.PlayQueueEntries.SingleAsync()).Status.ShouldBe(PlayQueueEntryStatus.NoShow);
        (await verify.PlayQueueExclusions.SingleAsync()).PrivateReason.ShouldBe(
            "Ready check expired"
        );
        (await service.GetEventsAsync(host, 0, 1000, CancellationToken.None))
            .Count(static value => value.Kind == PlayQueueEventKind.NoShow)
            .ShouldBe(1);
    }

    [Test]
    public async Task ReadyAfterExpiry_RejectsAndDurablyRecordsNoShow()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)
        );
        var service = CreateService(database, clock);
        _ = Success(await service.ConfigureAsync(host, Queue("squad"), CancellationToken.None));
        var entry = Success(
            await service.JoinAsync(
                host,
                "squad",
                Join("viewer", "eu", "Tank"),
                CancellationToken.None
            )
        ).Value;
        _ = Success(
            await service.StartReadyCheckAsync(host, entry.InternalEntryId, CancellationToken.None)
        );

        clock.Advance(TimeSpan.FromSeconds(120));
        var result = await service.ReadyAsync(host, "squad", new("viewer"), CancellationToken.None);

        _ = result.ShouldBeOfType<PlayQueueResult<PublicPlayQueueEntryView>.Rejected>();
        await AssertExpiredReadyCheckPersistedAsync(database, entry.InternalEntryId);
    }

    [Test]
    [Arguments(ExpiredQueueMutation.Open)]
    [Arguments(ExpiredQueueMutation.Close)]
    [Arguments(ExpiredQueueMutation.Configure)]
    public async Task QueueMutationAfterExpiry_DurablyRecordsNoShow(ExpiredQueueMutation mutation)
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)
        );
        var service = CreateService(database, clock);
        _ = Success(await service.ConfigureAsync(host, Queue("squad"), CancellationToken.None));
        var entry = Success(
            await service.JoinAsync(
                host,
                "squad",
                Join("viewer", "eu", "Tank"),
                CancellationToken.None
            )
        ).Value;
        _ = Success(
            await service.StartReadyCheckAsync(host, entry.InternalEntryId, CancellationToken.None)
        );
        if (mutation == ExpiredQueueMutation.Open)
        {
            _ = Success(await service.SetOpenAsync(host, "squad", false, CancellationToken.None));
        }

        clock.Advance(TimeSpan.FromSeconds(120));
        _ = mutation switch
        {
            ExpiredQueueMutation.Open => Success(
                await service.SetOpenAsync(host, "squad", true, CancellationToken.None)
            ),
            ExpiredQueueMutation.Close => Success(
                await service.SetOpenAsync(host, "squad", false, CancellationToken.None)
            ),
            ExpiredQueueMutation.Configure => Success(
                await service.ConfigureAsync(
                    host,
                    Queue("squad") with
                    {
                        ActivityName = "Changed game",
                    },
                    CancellationToken.None
                )
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null),
        };

        await AssertExpiredReadyCheckPersistedAsync(database, entry.InternalEntryId);
    }

    [Test]
    public async Task ReadyBeforeExpiry_ConvergesAndCanBeSelected()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(
            await service.ConfigureAsync(
                host,
                Queue("solo") with
                {
                    Capacity = 1,
                    RoleRequirements = [],
                },
                CancellationToken.None
            )
        );
        var entry = Success(
            await service.JoinAsync(
                host,
                "solo",
                Join("viewer", "eu", "Tank"),
                CancellationToken.None
            )
        ).Value;
        _ = Success(
            await service.StartReadyCheckAsync(host, entry.InternalEntryId, CancellationToken.None)
        );
        var ready = Success(
            await service.ReadyAsync(host, "solo", new("viewer"), CancellationToken.None)
        );
        var selected = Success(
            await service.SelectPartyAsync(host, "solo", false, CancellationToken.None)
        );

        ready.Value.Status.ShouldBe(PlayQueueEntryStatus.Ready);
        selected.Value.Members.ShouldHaveSingleItem().NormalizedLogin.ShouldBe("viewer");
    }

    [Test]
    public async Task LeastRecentSelection_UsesPriorityHistoryJoinTimeAndRoleComposition()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)
        );
        var service = CreateService(database, clock);
        _ = Success(await service.ConfigureAsync(host, Queue("party"), CancellationToken.None));
        foreach (
            var (login, role) in new[]
            {
                ("tank", "Tank"),
                ("healer", "Healer"),
                ("damage1", "Damage"),
                ("damage2", "Damage"),
            }
        )
        {
            _ = Success(
                await service.JoinAsync(
                    host,
                    "party",
                    Join(login, "eu", role),
                    CancellationToken.None
                )
            );
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        var first = Success(
            await service.SelectPartyAsync(host, "party", false, CancellationToken.None)
        );
        first
            .Value.Members.Select(static value => value.NormalizedLogin)
            .ShouldBe(["tank", "healer", "damage1", "damage2"], ignoreOrder: true);

        foreach (
            var (login, role) in new[]
            {
                ("tank2", "Tank"),
                ("healer2", "Healer"),
                ("damage3", "Damage"),
                ("damage4", "Damage"),
            }
        )
        {
            _ = Success(
                await service.JoinAsync(
                    host,
                    "party",
                    Join(login, "eu", role),
                    CancellationToken.None
                )
            );
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        var second = Success(
            await service.SelectPartyAsync(host, "party", false, CancellationToken.None)
        );
        second
            .Value.Members.Select(static value => value.NormalizedLogin)
            .ShouldBe(["tank2", "healer2", "damage3", "damage4"], ignoreOrder: true);
        var replacedId = second
            .Value.Members.Single(static value => value.NormalizedLogin == "damage3")
            .EntryId;
        var replaced = Success(
            await service.ReplaceOneAsync(host, replacedId, CancellationToken.None)
        );
        replaced.Value.Members.Count.ShouldBe(4);
        replaced.Value.Members.Select(static value => value.EntryId).ShouldBeUnique();
        replaced.Value.Members.Select(static value => value.EntryId).ShouldNotContain(replacedId);
    }

    [Test]
    public async Task RoleComposition_IsBestEffortAndRolelessViewersFillRemainingSeats()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(
            await service.ConfigureAsync(
                host,
                Queue("party") with
                {
                    Capacity = 3,
                    RoleRequirements = [new("Tank", 2), new("Healer", 1)],
                },
                CancellationToken.None
            )
        );
        _ = Success(
            await service.JoinAsync(
                host,
                "party",
                Join("tank", "eu", "Tank"),
                CancellationToken.None
            )
        );
        foreach (var login in new[] { "roleless_one", "roleless_two" })
        {
            _ = Success(
                await service.JoinAsync(
                    host,
                    "party",
                    Join(login, "eu", ""),
                    CancellationToken.None
                )
            );
        }

        var selected = Success(
            await service.SelectPartyAsync(host, "party", false, CancellationToken.None)
        );

        selected
            .Value.Members.Select(static value => value.NormalizedLogin)
            .ShouldBe(["tank", "roleless_one", "roleless_two"], ignoreOrder: true);
    }

    [Test]
    public async Task JoinWithoutTwitchIdentity_IsRejectedWithoutPersistingAnEntry()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(host, Queue("squad"), CancellationToken.None));
        var rejected = await service.JoinAsync(
            host,
            "squad",
            Join("viewer", "eu", "Tank") with
            {
                Viewer = new("viewer"),
            },
            CancellationToken.None
        );

        _ = rejected
            .Match(
                static _ => throw new InvalidOperationException("Expected rejection."),
                static value => value.Reason
            )
            .ShouldBeOfType<PlayQueueRejection.Invalid>();
        rejected
            .Match(
                static _ => throw new InvalidOperationException("Expected rejection."),
                static value => value.Reason.Message
            )
            .ShouldContain("Sign in with Twitch");
        await using var verify = await database.CreateDbContextAsync();
        (await verify.PlayQueueEntries.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Events_AreVersionedBoundedPublicAndHostIsolated()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(alpha, Queue("squad"), CancellationToken.None));
        _ = Success(await service.ConfigureAsync(beta, Queue("squad"), CancellationToken.None));
        _ = Success(
            await service.JoinAsync(
                alpha,
                "squad",
                Join("private_login", "SECRET-REGION", "Tank"),
                CancellationToken.None
            )
        );

        var events = await service.GetEventsAsync(alpha, 0, 2000, CancellationToken.None);
        events.ShouldAllBe(value =>
            value.HostId == alpha && value.SchemaVersion == 1 && value.PublicPayload.Length <= 1024
        );
        events.Count.ShouldBeLessThanOrEqualTo(PlayQueueLimits.MaximumEventReadCount);
        events
            .Select(value => value.PublicPayload)
            .ShouldAllBe(value =>
                !value.Contains("private_login") && !value.Contains("SECRET-REGION")
            );
    }

    private static ConfigurePlayQueueCommand Queue(string slug) =>
        new(
            slug,
            "Community squad",
            "Example game",
            4,
            true,
            PlayQueueSelectionMode.LeastRecentParticipation,
            false,
            120,
            30,
            15,
            [
                new("platform", "Platform"),
                new("region", "Region"),
                new("rank", "Rank"),
                new("preferred-role", "Preferred role", ["Tank", "Healer", "Damage"]),
            ],
            [new("Tank", 1), new("Healer", 1), new("Damage", 2)]
        );

    private static JoinPlayQueueCommand Join(string login, string region, string role) =>
        new(
            new(login, $"twitch-{login}", login),
            0,
            new Dictionary<string, string> { ["region"] = region, ["preferred-role"] = role }
        );

    private static PlayQueueService CreateService(
        SqliteBlokeBotDbFactory database,
        TimeProvider? clock = null
    ) => new(database, TestEventBus.Create<AppEventKind>(), clock ?? TimeProvider.System);

    private static PlayQueueResult<T>.Succeeded Success<T>(PlayQueueResult<T> result) =>
        result.Match(
            static value => value,
            static rejected => throw new InvalidOperationException(rejected.Reason.Message)
        );

    private static async Task AssertExpiredReadyCheckPersistedAsync(
        SqliteBlokeBotDbFactory database,
        long entryId
    )
    {
        await using var verify = await database.CreateDbContextAsync();
        var entry = await verify.PlayQueueEntries.SingleAsync(value => value.Id == entryId);
        entry.Status.ShouldBe(PlayQueueEntryStatus.NoShow);
        (
            await verify.PlayQueueExclusions.CountAsync(value =>
                value.QueueId == entry.QueueId
                && value.IdentityKey == entry.IdentityKey
                && value.PrivateReason == "Ready check expired"
            )
        ).ShouldBe(1);
        (
            await verify.PlayQueueEvents.CountAsync(value =>
                value.EntryId == entryId && value.Kind == PlayQueueEventKind.NoShow
            )
        ).ShouldBe(1);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database, string login)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;
    }

    public enum ExpiredQueueMutation
    {
        Open,
        Close,
        Configure,
    }
}
