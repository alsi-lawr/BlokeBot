using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PointsGiveawaySchedulerTests
{
    [Test]
    public async Task Restart_rehydrates_future_active_giveaway()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddHours(1)
        );
        var scheduler = CreateScheduler(dbFactory);

        await scheduler.RehydrateAsync(CancellationToken.None);

        scheduler.IsScheduled(giveawayId).ShouldBeTrue();
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Active);

        await scheduler.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Restart_expires_overdue_active_giveaway_without_payout()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow.AddMinutes(-5),
            "entrant"
        );
        var scheduler = CreateScheduler(dbFactory);

        await scheduler.RehydrateAsync(CancellationToken.None);

        scheduler.IsScheduled(giveawayId).ShouldBeFalse();
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Expired);
        giveaway.CompletedAtUtc.ShouldNotBeNull();
        (await db.PointsGiveawayWinners.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(0);
        (await db.PointBalances.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
    }

    [Test]
    public async Task Scheduler_logs_overdue_expiration_failures()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow.AddMinutes(-5)
        );
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = new PointsGiveawayScheduler(
            dbFactory,
            new ServiceCollection().BuildServiceProvider(),
            TimeProvider.System,
            logger
        );

        await scheduler.RehydrateAsync(CancellationToken.None);

        logger
            .Entries.ShouldContain(entry =>
                entry.Level == LogLevel.Error
                && entry.Message.Contains(
                    $"Failed to expire overdue points giveaway {giveawayId}",
                    StringComparison.Ordinal
                )
                && entry.Exception is InvalidOperationException
            );
    }

    [Test]
    public async Task Manual_cancel_cancels_scheduled_giveaway()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5)
        );
        var scheduler = new RecordingGiveawayScheduler();
        var service = CreateGiveawayService(dbFactory, scheduler);

        var result = await service.CancelAsync(hostId, CancellationToken.None);

        result.Success.ShouldBeTrue();
        scheduler.Cancelled.ShouldContain(giveawayId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Cancelled);
        giveaway.CompletedAtUtc.ShouldNotBeNull();
    }

    [Test]
    public async Task Manual_end_cancels_scheduled_giveaway()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5)
        );
        var scheduler = new RecordingGiveawayScheduler();
        var service = CreateGiveawayService(dbFactory, scheduler);

        var result = await service.EndAsync(hostId, "streamer", CancellationToken.None);

        result.Success.ShouldBeTrue();
        scheduler.Cancelled.ShouldContain(giveawayId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Completed);
        giveaway.CompletedAtUtc.ShouldNotBeNull();
    }

    [Test]
    public async Task Duplicate_draw_attempts_do_not_double_pay()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            "entrant"
        );
        await SeedSettingsAsync(dbFactory, hostId);
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var first = await service.DrawAsync(giveawayId, CancellationToken.None);
        var second = await service.DrawAsync(giveawayId, CancellationToken.None);

        first.Success.ShouldBeTrue();
        second.Success.ShouldBeFalse();
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Completed);
        (await db.PointsGiveawayWinners.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(1);
        (await db.PointLedgerEntries.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(1);
        var balance = await db.PointBalances.SingleAsync(x => x.HostId == hostId);
        balance.Login.ShouldBe("entrant");
        balance.Amount.ShouldBe("10");
    }

    private static PointsGiveawayScheduler CreateScheduler(SqliteBlokeBotDbFactory dbFactory)
    {
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());
        var services = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        return new PointsGiveawayScheduler(
            dbFactory,
            services,
            TimeProvider.System,
            NullLogger<PointsGiveawayScheduler>.Instance
        );
    }

    private static PointsGiveawayService CreateGiveawayService(
        SqliteBlokeBotDbFactory dbFactory,
        IPointsGiveawayScheduler scheduler
    )
    {
        var httpClientFactory = new FakeHttpClientFactory();
        var status = new HostBotStatusService(
            new ServiceCollection().BuildServiceProvider(),
            new TwitchOAuthApiClient(httpClientFactory),
            new TwitchHelixApiClient(httpClientFactory),
            Options.Create(new TwitchBotOptions())
        );
        return new PointsGiveawayService(
            dbFactory,
            new PointBalanceService(dbFactory),
            status,
            new FixedPointsRandom(),
            scheduler,
            new PointsChangeNotifier(new EventBus<AppEventKind>())
        );
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
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

    private static async Task SeedSettingsAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.PointsSettings.Add(
            new PointsSettings
            {
                HostId = hostId,
                GiveawayMinimumPayout = "10",
                GiveawayMaximumPayout = "10",
                GiveawayWinnerCount = 1,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedGiveawayAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        DateTime startedAtUtc,
        DateTime endsAtUtc,
        string? entrant = null
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = new PointsGiveaway
        {
            HostId = hostId,
            Status = PointsGiveawayStatus.Active,
            StartedAtUtc = startedAtUtc,
            EndsAtUtc = endsAtUtc,
            MinimumPayout = "10",
            MaximumPayout = "10",
            WinnerCount = 1,
            Eligibility = PointsEligibilityMode.Everyone,
        };
        if (entrant is not null)
        {
            giveaway.Entrants.Add(
                new PointsGiveawayEntrant
                {
                    Login = entrant,
                    JoinedAtUtc = DateTime.UtcNow,
                }
            );
        }

        db.PointsGiveaways.Add(giveaway);
        await db.SaveChangesAsync();
        return giveaway.Id;
    }

    private sealed class RecordingGiveawayScheduler : IPointsGiveawayScheduler
    {
        public List<int> Cancelled { get; } = [];

        public void Schedule(PointsGiveawaySchedule schedule) { }

        public void Cancel(int giveawayId) => Cancelled.Add(giveawayId);
    }

    private sealed class FixedPointsRandom : IPointsRandom
    {
        public double NextDouble() => 0;

        public int Next(int minValue, int maxValue) => minValue;
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullLoggerScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullLoggerScope : IDisposable
    {
        public static readonly NullLoggerScope Instance = new();

        public void Dispose() { }
    }
}
