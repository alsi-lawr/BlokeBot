using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardGiveawayControlsTests : PointsGiveawaySchedulerTestBase
{
    private static readonly CancellationToken _ct = CancellationToken.None;
    private static readonly DashboardWidgetConfiguration.Giveaway _configuration = new(
        new DashboardBinding.Configured(new DashboardTarget.Giveaway())
    );

    private static T Ready<T>(DashboardResult<DashboardGiveawayResult<T>> result) =>
        result
            .ShouldBeOfType<DashboardResult<DashboardGiveawayResult<T>>.Succeeded>()
            .Value.ShouldBeOfType<DashboardGiveawayResult<T>.Ready>()
            .Value;

    private static AuthenticatedSession Session(
        DashboardHostId host,
        AuthRole role = AuthRole.Streamer
    )
    {
        var selected = new BotHostChoice(host.Value, "stale-login", "Host", role);
        return new()
        {
            IsAuthenticated = true,
            UserId = "operator-id",
            Login = "operator",
            IsBotAccount = role == AuthRole.Bot,
            State = new AuthSessionState.Selected(new BotHostSelection(selected, [selected])),
        };
    }

    private sealed class Moderator : IModeratorAuthorityService
    {
        internal ModeratorAuthorityOutcome Outcome { get; set; } =
            new ModeratorAuthorityOutcome.Granted();

        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int hostId,
            CancellationToken ct
        ) => Task.FromResult(Outcome);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal PointProviderFixture Database { get; }
        internal Measurements Queries { get; }
        internal Moderator Moderator { get; } = new();
        internal Provider Provider { get; } = new();
        internal EventBus<AppEventKind> Events { get; } = TestEventBus.Create<AppEventKind>();
        internal DashboardGiveawayChanges Changes { get; } =
            new(TestEventBus.Create<DashboardGiveawayChange>());
        internal RecordingGiveawayScheduler Scheduler { get; } = new();
        internal PointsGiveawayChangeNotifier Notifier { get; }
        internal PointsGiveawayDrawService Draws { get; }
        internal PointsGiveawayService Giveaways { get; }
        internal DashboardHostId Host { get; private set; }
        internal DashboardHostId Other { get; private set; }
        internal AuthenticatedSession Owner => Session(Host);
        internal DashboardGiveawayControls Controls =>
            new(new(Database, Moderator), Database, Giveaways);

        private Fixture(PointProviderFixture database, Measurements measurements)
        {
            Database = database;
            Queries = measurements;
            Notifier = new(new PointsChangeNotifier(Events), [Changes]);
            Draws = new(Database, new PointBalanceService(Database), new FixedPointsRandom());
            var status = new HostBotStatusService(
                new StaticHostBotAppAccessTokenSource(),
                new UnavailableHostBotAccountTokenStatusProvider(),
                new HelixClient(Provider, TwitchEndpointPolicy.Default),
                BotSettings.FromOptions(new BotOptions())
            );
            Giveaways = new(
                Database,
                Draws,
                new PointsGiveawayEligibilityPolicy(
                    status,
                    NullLogger<PointsGiveawayEligibilityPolicy>.Instance
                ),
                new PointsGiveawayMessageFormatter(),
                Scheduler,
                Notifier
            );
        }

        internal static async Task<Fixture> CreateAsync(bool postgres = false)
        {
            var measurements = new Measurements();
            var fixture = new Fixture(
                postgres
                    ? await PointProviderFixture.PostgreSqlAsync(measurements)
                    : await PointProviderFixture.SqliteFileAsync(measurements),
                measurements
            );
            fixture.Host = new(await SeedHostAsync(fixture.Database, "streamer"));
            fixture.Other = new(await SeedHostAsync(fixture.Database, "other"));
            await SeedSettingsAsync(
                fixture.Database,
                fixture.Host.Value,
                s => s.GiveawayCooldownSeconds = 0
            );
            await SeedSettingsAsync(
                fixture.Database,
                fixture.Other.Value,
                s => s.GiveawayCooldownSeconds = 0
            );
            measurements.Reset();
            return fixture;
        }

        internal async Task SettingsAsync(Action<PointsSettings> update)
        {
            await using var db = Database.CreateDbContext();
            update(await db.PointsSettings.SingleAsync(x => x.HostId == Host.Value));
            _ = await db.SaveChangesAsync();
        }

        internal async Task FeaturesAsync(HostFeatureFlags features)
        {
            await using var db = Database.CreateDbContext();
            (await db.Hosts.SingleAsync(x => x.Id == Host.Value)).EnabledFeatures = features;
            _ = await db.SaveChangesAsync();
        }

        internal async Task SeedVolumeAsync(int history, int entrants)
        {
            await using var db = Database.CreateDbContext();
            var now = DateTime.UtcNow;
            foreach (var host in new[] { Host.Value, Other.Value })
            {
                for (var index = 0; index < history; index++)
                {
                    _ = db.PointsGiveaways.Add(
                        new PointsGiveaway
                        {
                            HostId = host,
                            Status = PointsGiveawayStatus.Completed,
                            StartedAtUtc = now.AddHours(-2).AddSeconds(index),
                            EndsAtUtc = now.AddHours(-1),
                            CompletedAtUtc = now.AddHours(-1),
                        }
                    );
                }
                var current = new PointsGiveaway
                {
                    HostId = host,
                    StartedAtUtc = now,
                    EndsAtUtc = now.AddMinutes(5),
                    Entrants = Enumerable
                        .Range(0, host == Host.Value ? entrants : 7)
                        .Select(index => new PointsGiveawayEntrant
                        {
                            Login = $"viewer{host}_{index}",
                            JoinedAtUtc = now,
                        })
                        .ToList(),
                };
                _ = db.PointsGiveaways.Add(current);
                _ = db.PointBalances.Add(
                    new PointBalance
                    {
                        HostId = host,
                        Login = "unrelated",
                        Amount = "10",
                        UpdatedAtUtc = now,
                    }
                );
            }
            _ = await db.SaveChangesAsync();
            Queries.Reset();
        }

        internal PointsGiveawayScheduler Worker(TimeProvider time) =>
            new(
                new PointsGiveawaySchedulerOperations(
                    Database,
                    Draws,
                    new PointsGiveawayMessageFormatter(),
                    new PointsGiveawayChangeNotification(Notifier),
                    time
                ),
                new ReplyOnlyPointsGiveawaySchedulerNotification(),
                new PointsGiveawaySchedulerRecoveryPolicy { RetryDelay = TimeSpan.Zero },
                time,
                NullLogger<PointsGiveawayScheduler>.Instance
            );

        public async ValueTask DisposeAsync()
        {
            Provider.Dispose();
            await Database.DisposeAsync();
        }
    }

    private sealed class Measurements : DbCommandInterceptor, IMaterializationInterceptor
    {
        internal List<string> Commands { get; } = [];
        internal List<int> ReaderOperations { get; } = [];
        internal Dictionary<Type, int> Entities { get; } = [];

        internal void Reset()
        {
            Commands.Clear();
            ReaderOperations.Clear();
            Entities.Clear();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData data,
            InterceptionResult<DbDataReader> result,
            CancellationToken ct = default
        )
        {
            Commands.Add(
                command.CommandText
                    + "\n"
                    + string.Join(
                        ", ",
                        command
                            .Parameters.Cast<DbParameter>()
                            .Select(x => $"{x.ParameterName}={x.Value}")
                    )
            );
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult DataReaderDisposing(
            DbCommand command,
            DataReaderDisposingEventData data,
            InterceptionResult result
        )
        {
            ReaderOperations.Add(data.ReadCount);
            return result;
        }

        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            Entities[entity.GetType()] = Entities.GetValueOrDefault(entity.GetType()) + 1;
            return entity;
        }
    }

    private sealed class Provider : HttpMessageHandler, IHttpClientFactory
    {
        internal bool Live { get; set; } = true;
        internal bool Unavailable { get; set; }
        internal List<string> RequestedLogins { get; } = [];
        internal List<double> ElapsedMs { get; } = [];

        public HttpClient CreateClient(string name) => new(this, false);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var started = Stopwatch.GetTimestamp();
            RequestedLogins.Add(request.RequestUri!.Query);
            var response = new HttpResponseMessage(
                Unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK
            )
            {
                Content = new StringContent(
                    Live
                        ? """{"data":[{"id":"stream-id","user_id":"user-id","user_login":"streamer","user_name":"Streamer","game_id":"game-id","game_name":"Game","type":"live","title":"Live","tags":[],"viewer_count":1,"started_at":"2026-10-10T10:00:00Z","language":"en","thumbnail_url":"https://example.test/{width}x{height}.jpg","is_mature":false}],"pagination":{}}"""
                        : """{"data":[],"pagination":{}}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            ElapsedMs.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return Task.FromResult(response);
        }
    }
}
