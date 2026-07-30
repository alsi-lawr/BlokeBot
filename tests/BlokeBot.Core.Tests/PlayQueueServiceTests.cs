using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PlayQueueServiceTests
{
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
        retry.Value.Id.ShouldBe(first.Value.Id);
        position.Value.Position.ShouldBe(1);
        page!.Waiting.ShouldHaveSingleItem();
        page.Waiting[0].DisplayName.ShouldBeNull();
        page.Waiting[0].Fields.ShouldBeEmpty();
        page.ToString().ShouldNotContain("Tank");
        page.ToString().ShouldNotContain("100");
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
        joins.Select(Success).Select(value => value.Value.Id).Distinct().Count().ShouldBe(1);
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
        selected.ShouldAllBe(value => value.Value.Members.Count == 4);
        selected
            .SelectMany(value => value.Value.Members)
            .Select(value => value.Public.Id)
            .Distinct()
            .Count()
            .ShouldBe(4);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.PlayQueueEntries.CountAsync()).ShouldBe(4);
        (
            await verify.PlayQueueEntries.CountAsync(value =>
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
            await service.StartReadyCheckAsync(host, entry.Id, CancellationToken.None)
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
            .Count(value => value.Kind == PlayQueueEventKind.NoShow)
            .ShouldBe(1);
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
        _ = Success(await service.StartReadyCheckAsync(host, entry.Id, CancellationToken.None));
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
            .Value.Members.Select(value => value.NormalizedLogin)
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
            .Value.Members.Select(value => value.NormalizedLogin)
            .ShouldBe(["tank2", "healer2", "damage3", "damage4"], ignoreOrder: true);
        var replacedId = second
            .Value.Members.Single(value => value.NormalizedLogin == "damage3")
            .Public.Id;
        var replaced = Success(
            await service.ReplaceOneAsync(host, replacedId, CancellationToken.None)
        );
        replaced.Value.Members.Count.ShouldBe(4);
        replaced.Value.Members.Select(value => value.Public.Id).ShouldBeUnique();
        replaced.Value.Members.Select(value => value.Public.Id).ShouldNotContain(replacedId);
    }

    [Test]
    public async Task TwitchIdentity_ReconcilesLoginFallbackWithoutDuplicateActiveEntry()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(host, Queue("squad"), CancellationToken.None));
        var fallback = Success(
            await service.JoinAsync(
                host,
                "squad",
                Join("viewer", "eu", "Tank"),
                CancellationToken.None
            )
        );
        var identified = Success(
            await service.JoinAsync(
                host,
                "squad",
                Join("viewer", "eu", "Tank") with
                {
                    Viewer = new("viewer", "1234", "Viewer"),
                },
                CancellationToken.None
            )
        );

        identified.WasIdempotent.ShouldBeTrue();
        identified.Value.Id.ShouldBe(fallback.Value.Id);
        await using var verify = await database.CreateDbContextAsync();
        var entry = await verify.PlayQueueEntries.SingleAsync();
        entry.IdentityKey.ShouldBe("id:1234");
        entry.TwitchUserId.ShouldBe("1234");
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

    private static ConfigurePlayQueueCommand Queue(string slug)
    {
        return new(
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
                new("platform", "Platform", false),
                new("region", "Region", true),
                new("rank", "Rank", false),
                new("preferred-role", "Preferred role", true, ["Tank", "Healer", "Damage"]),
            ],
            [new("Tank", 1), new("Healer", 1), new("Damage", 2)]
        );
    }

    private static JoinPlayQueueCommand Join(string login, string region, string role)
    {
        return new(
            new(login),
            0,
            new Dictionary<string, string> { ["region"] = region, ["preferred-role"] = role }
        );
    }

    private static PlayQueueService CreateService(
        SqliteBlokeBotDbFactory database,
        TimeProvider? clock = null
    )
    {
        return new(database, TestEventBus.Create<AppEventKind>(), clock ?? TimeProvider.System);
    }

    private static PlayQueueResult<T>.Succeeded Success<T>(PlayQueueResult<T> result)
    {
        return result.Match(
            value => value,
            rejected => throw new InvalidOperationException(rejected.Reason.Message)
        );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database, string login)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan value)
        {
            _now += value;
        }
    }
}
