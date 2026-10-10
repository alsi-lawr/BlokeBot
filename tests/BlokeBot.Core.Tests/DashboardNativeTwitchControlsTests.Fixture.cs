using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardNativeTwitchControlsTests
{
    private static readonly CancellationToken _ct = CancellationToken.None;

    private static T Value<T>(DashboardResult<T> result) =>
        result.ShouldBeOfType<DashboardResult<T>.Succeeded>().Value;

    private static AuthenticatedSession Session(
        DashboardHostId host,
        AuthRole role = AuthRole.Streamer
    )
    {
        var selected = new BotHostChoice(host.Value, "host", "Host", role);
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

    private sealed class Broadcaster : IHostBroadcasterTokenStatusProvider
    {
        internal bool Ready { get; set; } = true;

        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        ) =>
            Task.FromResult<TokenStatus>(
                Ready
                    ? new TokenStatus.Ready(
                        "token",
                        new TokenValidation(
                            "host-id",
                            "host",
                            OAuthScopeSet.Create(
                                HostBroadcasterAuthorizationService.MilestoneScopes
                            )
                        ),
                        [.. HostBroadcasterAuthorizationService.MilestoneScopes],
                        [.. HostBroadcasterAuthorizationService.MilestoneScopes]
                    )
                    : new TokenStatus.Unavailable(
                        AccessTokenUnavailableReason.MissingRefreshToken,
                        [.. HostBroadcasterAuthorizationService.MilestoneScopes]
                    )
            );

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        ) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    )
                )
            );
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal SqliteBlokeBotDbFactory Database { get; }
        internal QueryMeasurements Queries { get; }
        internal Moderator Moderator { get; } = new();
        internal Broadcaster Broadcaster { get; } = new();
        internal Provider Provider { get; } = new();
        internal EventBus<AppEventKind> Events { get; } = TestEventBus.Create<AppEventKind>();
        internal DashboardHostId Host { get; private set; }
        internal DashboardHostId Other { get; private set; }
        internal int PollTemplateId { get; private set; }
        internal int PredictionTemplateId { get; private set; }
        internal PollService Polls { get; }
        internal PredictionService Predictions { get; }
        internal DashboardPollControls PollControls =>
            new(new(Database, Moderator), Database, Polls);
        internal DashboardPredictionControls PredictionControls =>
            new(new(Database, Moderator), Database, Predictions);
        internal AuthenticatedSession Owner => Session(Host);
        internal DashboardWidgetConfiguration.Poll PollConfiguration =>
            new(new DashboardBinding.Configured(new DashboardTarget.Poll(PollTemplateId)));
        internal DashboardWidgetConfiguration.Prediction PredictionConfiguration =>
            new(
                new DashboardBinding.Configured(
                    new DashboardTarget.Prediction(PredictionTemplateId)
                )
            );

        private Fixture(SqliteBlokeBotDbFactory database, QueryMeasurements measurements)
        {
            Database = database;
            Queries = measurements;
            var authorization = new BroadcasterOperationAuthorization(
                Broadcaster,
                new DurableAlertService(Database, TimeProvider.System, Events)
            );
            var helix = new HelixClient(Provider, TwitchEndpointPolicy.Default);
            var settings = BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
            );
            var gate = new NativeTwitchFeatureGate(Database);
            Polls = new(Database, authorization, helix, settings, Events, gate);
            Predictions = new(
                Database,
                authorization,
                helix,
                settings,
                Events,
                NullLogger<PredictionService>.Instance,
                gate
            );
        }

        internal static async Task<Fixture> CreateAsync()
        {
            var measurements = new QueryMeasurements();
            var fixture = new Fixture(
                await SqliteBlokeBotDbFactory.CreateAsync(measurements),
                measurements
            );
            await using (var context = fixture.Database.CreateDbContext())
            {
                var host = new BotHost
                {
                    Login = "host",
                    DisplayName = "Host",
                    TwitchUserId = "host-id",
                    EnabledFeatures = HostFeatureFlags.All,
                };
                var other = new BotHost
                {
                    Login = "other",
                    DisplayName = "Other",
                    TwitchUserId = "other-id",
                    EnabledFeatures = HostFeatureFlags.All,
                };
                _ = context.Hosts.Add(host);
                _ = context.Hosts.Add(other);
                _ = await context.SaveChangesAsync();
                fixture.Host = new(host.Id);
                fixture.Other = new(other.Id);
            }
            fixture.PollTemplateId = (
                await fixture.Polls.SaveTemplateAsync(
                    fixture.Host.Value,
                    new("Question", ["Yes", "No"], 60, false, 0),
                    _ct
                )
            )
                .ShouldBeOfType<PollOperationOutcome.TemplateSaved>()
                .Template.Id;
            fixture.PredictionTemplateId = (
                await fixture.Predictions.SaveTemplateAsync(
                    fixture.Host.Value,
                    new("Prediction", ["Yes", "No"], 60),
                    _ct
                )
            )
                .ShouldBeOfType<PredictionOperationOutcome.TemplateSaved>()
                .Template.Id;
            measurements.Reset();
            return fixture;
        }

        internal async Task SeedHistoryAsync(int perHost)
        {
            await using var context = Database.CreateDbContext();
            foreach (var host in new[] { Host.Value, Other.Value })
            {
                for (var index = 0; index < perHost; index++)
                {
                    _ = context.TwitchPollTemplates.Add(
                        new TwitchPollTemplate
                        {
                            HostId = host,
                            Title = $"Other poll {index}",
                            DurationSeconds = 60,
                            CreatedAtUtc = DateTime.UtcNow,
                            Choices =
                            [
                                new() { Position = 0, Title = "A" },
                                new() { Position = 1, Title = "B" },
                            ],
                        }
                    );
                    _ = context.TwitchPredictionTemplates.Add(
                        new TwitchPredictionTemplate
                        {
                            HostId = host,
                            Title = $"Other prediction {index}",
                            PredictionWindowSeconds = 60,
                            CreatedAtUtc = DateTime.UtcNow,
                            Outcomes =
                            [
                                new() { Position = 0, Title = "A" },
                                new() { Position = 1, Title = "B" },
                            ],
                        }
                    );
                    _ = context.TwitchPolls.Add(
                        new TwitchPoll
                        {
                            HostId = host,
                            ProviderPollId = $"history-poll-{host}-{index}",
                            Title = "History",
                            Status = TwitchPollStatus.Completed,
                            ChoicesJson = "[]",
                            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                            EndedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow,
                        }
                    );
                    _ = context.TwitchPredictions.Add(
                        new TwitchPrediction
                        {
                            HostId = host,
                            ProviderPredictionId = $"history-prediction-{host}-{index}",
                            Title = "History",
                            Status = TwitchPredictionStatus.Resolved,
                            OutcomesJson = "[]",
                            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                            EndedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow,
                        }
                    );
                }
            }
            _ = await context.SaveChangesAsync();
            Queries.Reset();
        }

        internal async Task FeaturesAsync(HostFeatureFlags features)
        {
            await using var context = Database.CreateDbContext();
            var host = await context.Hosts.SingleAsync(x => x.Id == Host.Value);
            host.EnabledFeatures = features;
            _ = await context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Provider.Dispose();
            await Database.DisposeAsync();
        }
    }

    private sealed class QueryMeasurements : DbCommandInterceptor, IMaterializationInterceptor
    {
        internal List<string> Commands { get; } = [];
        internal Dictionary<Type, int> Entities { get; } = [];

        internal int Materialized<T>() => Entities.GetValueOrDefault(typeof(T));

        internal void Reset()
        {
            Commands.Clear();
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

        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            Entities[entity.GetType()] = Entities.GetValueOrDefault(entity.GetType()) + 1;
            return entity;
        }
    }

    private sealed record ProviderRequest(
        string Path,
        HttpMethod Method,
        string Body,
        double ElapsedMs
    );

    private sealed class Provider : HttpMessageHandler, IHttpClientFactory
    {
        internal string BroadcasterType { get; set; } = "affiliate";
        internal HttpStatusCode EligibilityStatus { get; set; } = HttpStatusCode.OK;
        internal HttpStatusCode MutationStatus { get; set; } = HttpStatusCode.OK;
        internal string FailureMessage { get; set; } = "Provider failed";
        internal List<ProviderRequest> Requests { get; } = [];
        private int _poll;
        private int _prediction;
        internal int Mutations =>
            Requests.Count(x => x.Method == HttpMethod.Post || x.Method == HttpMethod.Patch);

        public HttpClient CreateClient(string name) => new(this, false);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var started = Stopwatch.GetTimestamp();
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(ct);
            HttpResponseMessage response;
            if (path.EndsWith("/users", StringComparison.Ordinal))
            {
                response =
                    EligibilityStatus == HttpStatusCode.OK
                        ? Json(
                            $$"""{"data":[{"id":"host-id","broadcaster_type":"{{BroadcasterType}}"}]}"""
                        )
                        : Failure(EligibilityStatus);
            }
            else if (MutationStatus != HttpStatusCode.OK && request.Method != HttpMethod.Get)
            {
                response = Failure(MutationStatus);
            }
            else if (path.EndsWith("/polls", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Post)
                {
                    _poll++;
                }
                var status = request.Method == HttpMethod.Patch ? "TERMINATED" : "ACTIVE";
                response = Json(
                    $$"""{"data":[{"id":"poll-{{_poll}}","broadcaster_id":"host-id","title":"Question","choices":[{"id":"yes","title":"Yes","votes":1,"channel_points_votes":0},{"id":"no","title":"No","votes":0,"channel_points_votes":0}],"status":"{{status}}","started_at":"2026-10-10T10:00:00Z","ends_at":"2026-10-10T10:01:00Z"}]}"""
                );
            }
            else
            {
                if (request.Method == HttpMethod.Post)
                {
                    _prediction++;
                }
                var status =
                    request.Method == HttpMethod.Patch
                        ? JsonSerializer
                            .Deserialize<JsonElement>(body)
                            .GetProperty("status")
                            .GetString()!
                        : "ACTIVE";
                response = Json(
                    $$"""{"data":[{"id":"prediction-{{_prediction}}","broadcaster_id":"host-id","title":"Prediction","outcomes":[{"id":"yes","title":"Yes","color":"BLUE","users":1,"channel_points":100,"top_predictors":[{"user_login":"viewer","user_name":"Viewer","channel_points_used":100}]},{"id":"no","title":"No","color":"PINK","users":0,"channel_points":0,"top_predictors":[]}],"status":"{{status}}","created_at":"2026-10-10T10:00:00Z"}]}"""
                );
            }
            Requests.Add(
                new(path, request.Method, body, Stopwatch.GetElapsedTime(started).TotalMilliseconds)
            );
            return response;
        }

        private HttpResponseMessage Failure(HttpStatusCode status) =>
            new(status)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(
                        new
                        {
                            error = status.ToString(),
                            status = (int)status,
                            message = FailureMessage,
                        }
                    ),
                    Encoding.UTF8,
                    "application/json"
                ),
            };

        private static HttpResponseMessage Json(string content) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
    }
}
