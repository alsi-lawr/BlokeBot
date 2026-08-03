using System.Globalization;
using System.Net;
using System.Text;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PredictionServiceTests
{
    [Test]
    public async Task PredictionLifecycle_EnforcesEligibilityHostIsolationConfirmationAndTerminalNonRegression()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var handler = new PredictionHandler();
        var service = CreateService(database, handler);
        var first = await SeedHostAsync(database, "first", "first-id");
        var second = await SeedHostAsync(database, "second", "second-id");

        _ = (
            await service.LoadAsync(first.Id, CancellationToken.None)
        ).Authorization.ShouldBeOfType<PredictionAuthorizationReadiness.Ineligible>();
        handler.BroadcasterType = "affiliate";
        await service.ReconcileAsync(first.Id, CancellationToken.None);
        handler.Requests.ShouldContain(request =>
            request.Method == HttpMethod.Get
            && request.Query.Contains("first=25")
            && request.Query.Contains("broadcaster_id=first-id")
        );
        (
            await service.LoadAsync(second.Id, CancellationToken.None)
        ).ActivePrediction.ShouldBeNull();
        var active = (
            await service.LoadAsync(first.Id, CancellationToken.None)
        ).ActivePrediction.ShouldNotBeNull();
        _ = (
            await service.ResolveAsync(
                first.Id,
                active.Outcomes[0].Id,
                false,
                CancellationToken.None
            )
        ).ShouldBeOfType<PredictionOperationOutcome.ConfirmationRequired>();
        handler.PatchBodies.ShouldBeEmpty();
        _ = (
            await service.ResolveAsync(
                first.Id,
                active.Outcomes[0].Id,
                true,
                CancellationToken.None
            )
        ).ShouldBeOfType<PredictionOperationOutcome.Updated>();
        handler.PatchBodies.ShouldHaveSingleItem().ShouldContain("\"status\":\"RESOLVED\"");
        handler.PatchBodies.Single().ShouldContain("\"winning_outcome_id\":\"yes\"");

        await service.PredictionReceivedAsync(Event("active"), CancellationToken.None);
        await service.PredictionReceivedAsync(Event("locked"), CancellationToken.None);
        (await service.LoadAsync(first.Id, CancellationToken.None)).Results.ShouldContain(result =>
            result.Status == "Resolved"
        );
        handler.BroadcasterType = "partner";
        _ = (
            await service.LoadAsync(first.Id, CancellationToken.None)
        ).Authorization.ShouldBeOfType<PredictionAuthorizationReadiness.Ready>();
    }

    [Test]
    public async Task NativeGate_DisabledSuppressesLoadsMutationsReconciliationAndInboundWrites()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "first", "first-id");
        await SetNativeAsync(database, false);
        var handler = new PredictionHandler { BroadcasterType = "affiliate" };
        var service = CreateService(database, handler);

        var state = await service.LoadAsync(host.Id, CancellationToken.None);
        var mutation = await service.SaveTemplateAsync(
            host.Id,
            ValidTemplate(),
            CancellationToken.None
        );
        await service.ReconcileAsync(host.Id, CancellationToken.None);
        await service.PredictionReceivedAsync(Event("active"), CancellationToken.None);

        _ = state.Authorization.ShouldBeOfType<PredictionAuthorizationReadiness.Disabled>();
        state.ActivePrediction.ShouldBeNull();
        state.Templates.ShouldBeEmpty();
        state.Results.ShouldBeEmpty();
        mutation
            .ShouldBeOfType<PredictionOperationOutcome.NotReady>()
            .Message.ShouldBe(NativeTwitchFeatureGate.DisabledMessage);
        handler.Requests.ShouldBeEmpty();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.TwitchPredictionTemplates.ToArrayAsync()).ShouldBeEmpty();
        (await verify.TwitchPredictions.ToArrayAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task QueuedPredictionProgress_RechecksGateBeforeDelayedWrite()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "first", "first-id");
        var handler = new PredictionHandler();
        var clock = new ManualTestTimeProvider(
            new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero)
        );
        var service = CreateService(database, handler, clock);
        var flushDecision = service.ObserveNextProgressFlushAsync(host.Id);

        await service.PredictionReceivedAsync(Event("active"), CancellationToken.None);
        _ = await clock.WaitForTimerRegistrationAsync();
        await SetNativeAsync(database, false);
        clock.Advance(TimeSpan.FromSeconds(1));

        (await flushDecision.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(
            PredictionProgressFlushDecision.SkippedNativeTwitchDisabled
        );

        await using var verify = await database.CreateDbContextAsync();
        (await verify.TwitchPredictions.ToArrayAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task ProductionRegistration_UsesSystemTimeAndPreservesPublicConstruction()
    {
        var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var events = TestEventBus.Create<AppEventKind>();
        var handler = new PredictionHandler();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = services.AddSingleton<IHostBroadcasterTokenStatusProvider>(new ReadyBroadcaster());
        _ = services.AddSingleton(
            new HelixClient(
                new SingleHandlerFactory(handler),
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            )
        );
        _ = services.AddSingleton(
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
            )
        );
        _ = services.AddSingleton(events);
        _ = services.AddSingleton(new DurableAlertService(database, TimeProvider.System, events));
        _ = services.AddBlokeBotTwitchOperations();
        await using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<PredictionService>();

        service.ProgressTimeProvider.ShouldBeSameAs(TimeProvider.System);
        provider.GetRequiredService<IPredictionEventObserver>().ShouldBeSameAs(service);
        typeof(PredictionService)
            .GetConstructors()
            .ShouldHaveSingleItem()
            .GetParameters()
            .Length.ShouldBe(8);
    }

    [Test]
    public async Task ProviderAcceptedPrediction_IsPersistedWhenDisableRacesAfterProviderWork()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "first", "first-id");
        var handler = new PredictionHandler
        {
            BroadcasterType = "affiliate",
            AfterPredictionCreated = () => SetNativeAsync(database, false),
        };
        var service = CreateService(database, handler);
        var saved = await service.SaveTemplateAsync(
            host.Id,
            ValidTemplate(),
            CancellationToken.None
        );
        var templateId = saved
            .ShouldBeOfType<PredictionOperationOutcome.TemplateSaved>()
            .Template.Id;

        var outcome = await service.StartAsync(host.Id, templateId, CancellationToken.None);

        _ = outcome.ShouldBeOfType<PredictionOperationOutcome.Started>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.TwitchPredictions.ToArrayAsync())
            .ShouldHaveSingleItem()
            .ProviderPredictionId.ShouldBe("prediction-id");
        (
            (await verify.Hosts.SingleAsync()).EnabledFeatures & HostFeatureFlags.Predictions
        ).ShouldBe(HostFeatureFlags.None);
    }

    [Test]
    [Arguments(true, 102, 101)]
    [Arguments(false, 100, 99)]
    public async Task ProviderAcceptedPredictionEnd_PersistsOutcomeAndOnlyTrimsWhileEnabled(
        bool disableAfterProvider,
        int expectedTotal,
        int expectedHistory
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "first", "first-id");
        await using (var db = await database.CreateDbContextAsync())
        {
            for (var index = 0; index < 101; index++)
            {
                _ = db.TwitchPredictions.Add(
                    new TwitchPrediction
                    {
                        HostId = host.Id,
                        ProviderPredictionId = $"history-{index:D3}",
                        Title = "History",
                        OutcomesJson = "[]",
                        Status = TwitchPredictionStatus.Resolved,
                        CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
                        EndedAtUtc = DateTime
                            .Parse("2026-07-25T10:00:00Z", CultureInfo.InvariantCulture)
                            .AddMinutes(-index),
                        UpdatedAtUtc = DateTime
                            .Parse("2026-07-25T10:00:00Z", CultureInfo.InvariantCulture)
                            .AddMinutes(-index),
                    }
                );
            }
            _ = db.TwitchPredictions.Add(
                new TwitchPrediction
                {
                    HostId = host.Id,
                    ProviderPredictionId = "prediction-id",
                    Title = "Question",
                    OutcomesJson =
                        """[{"Id":"yes","Title":"Yes","Color":"BLUE","Users":1,"ChannelPoints":100,"TopPredictors":[]}]""",
                    Status = TwitchPredictionStatus.Active,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var handler = new PredictionHandler
        {
            BroadcasterType = "affiliate",
            AfterPredictionEnded = disableAfterProvider
                ? () => SetNativeAsync(database, false)
                : null,
        };
        var service = CreateService(database, handler);

        var outcome = await service.ResolveAsync(host.Id, "yes", true, CancellationToken.None);

        _ = outcome.ShouldBeOfType<PredictionOperationOutcome.Updated>();
        await using var verify = await database.CreateDbContextAsync();
        var predictions = await verify.TwitchPredictions.ToArrayAsync();
        predictions.Length.ShouldBe(expectedTotal);
        predictions
            .Single(value => value.ProviderPredictionId == "prediction-id")
            .Status.ShouldBe(TwitchPredictionStatus.Resolved);
        predictions
            .Count(value =>
                value.ProviderPredictionId.StartsWith("history-", StringComparison.Ordinal)
            )
            .ShouldBe(expectedHistory);
    }

    private static PredictionTemplateDraft ValidTemplate() => new("Question", ["Yes", "No"], 60);

    private static async Task SetNativeAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        bool enabled
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync();
        host.EnabledFeatures = enabled
            ? host.EnabledFeatures | HostFeatureFlags.Predictions
            : host.EnabledFeatures & ~HostFeatureFlags.Predictions;
        _ = await db.SaveChangesAsync();
    }

    private static PredictionService CreateService(
        IDbContextFactory<BlokeBotDbContext> database,
        PredictionHandler handler,
        TimeProvider? timeProvider = null
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        var broadcasters = new ReadyBroadcaster();
        var helix = new HelixClient(
            new SingleHandlerFactory(handler),
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );
        var settings = BotSettings.FromOptions(
            new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
        );
        var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var logger = NullLogger<PredictionService>.Instance;
        var nativeTwitch = new NativeTwitchFeatureGate(database);
        return timeProvider is null
            ? new PredictionService(
                database,
                broadcasters,
                helix,
                settings,
                events,
                alerts,
                logger,
                nativeTwitch
            )
            : new PredictionService(
                database,
                broadcasters,
                helix,
                settings,
                events,
                alerts,
                logger,
                nativeTwitch,
                timeProvider
            );
    }

    private static async Task<BotHost> SeedHostAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        string login,
        string twitchId
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            TwitchUserId = twitchId,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host;
    }

    private static EventSubPredictionEvent Event(string status) =>
        new(
            "first-id",
            "first",
            "prediction-id",
            "Question",
            [new("yes", "Yes", "BLUE", 1, 100, [])],
            status,
            DateTimeOffset.Parse("2026-07-26T10:00:00Z", CultureInfo.InvariantCulture),
            null,
            status is "resolved" ? DateTimeOffset.UtcNow : null,
            status is "resolved" ? "yes" : null,
            Guid.NewGuid().ToString("N")
        );

    private sealed class ReadyBroadcaster : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> scopes,
            CancellationToken ct
        ) =>
            Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "token",
                    new TokenValidation(
                        hostId == 1 ? "first-id" : "second-id",
                        hostId == 1 ? "first" : "second",
                        OAuthScopeSet.Create(HostBroadcasterAuthorizationService.MilestoneScopes)
                    ),
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes],
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes]
                )
            );

        public BlokeBot.Functional.IO<
            BotAccount,
            AccessTokenUnavailableReason
        > GetBroadcasterAccount(string channelLogin) => throw new NotSupportedException();
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class PredictionHandler : HttpMessageHandler
    {
        public string BroadcasterType { get; set; } = string.Empty;
        public Func<Task>? AfterPredictionCreated { get; init; }
        public Func<Task>? AfterPredictionEnded { get; init; }
        public List<(HttpMethod Method, string Query)> Requests { get; } = [];
        public List<string> PatchBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Requests.Add((request.Method, request.RequestUri!.Query));
            if (request.RequestUri!.AbsolutePath.EndsWith("/users", StringComparison.Ordinal))
            {
                return Json(
                    $"{{\"data\":[{{\"id\":\"first-id\",\"broadcaster_type\":\"{BroadcasterType}\"}}]}}"
                );
            }
            if (request.Method == HttpMethod.Patch)
            {
                PatchBodies.Add(await request.Content!.ReadAsStringAsync(ct));
                if (AfterPredictionEnded is not null)
                {
                    await AfterPredictionEnded();
                }
                return Json(Prediction("RESOLVED"));
            }
            if (request.Method == HttpMethod.Post && AfterPredictionCreated is not null)
            {
                await AfterPredictionCreated();
            }
            return Json(Prediction("ACTIVE"));
        }

        private static HttpResponseMessage Json(string value) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(value, Encoding.UTF8, "application/json"),
            };

        private static string Prediction(string status)
        {
            var end =
                status == "RESOLVED"
                    ? ",\"ended_at\":\"2026-07-26T10:01:00Z\",\"winning_outcome_id\":\"yes\""
                    : string.Empty;
            return "{\"data\":[{\"id\":\"prediction-id\",\"broadcaster_id\":\"first-id\",\"title\":\"Question\",\"outcomes\":[{\"id\":\"yes\",\"title\":\"Yes\",\"color\":\"BLUE\",\"users\":1,\"channel_points\":100,\"top_predictors\":[]}],\"status\":\""
                + status
                + "\",\"created_at\":\"2026-07-26T10:00:00Z\""
                + end
                + "}],\"pagination\":{}}";
        }
    }
}
