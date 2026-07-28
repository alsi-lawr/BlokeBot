using System.Collections.Immutable;
using System.Net;
using System.Text;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Testing;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

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

        (
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
        (
            await service.ResolveAsync(
                first.Id,
                active.Outcomes[0].Id,
                false,
                CancellationToken.None
            )
        ).ShouldBeOfType<PredictionOperationOutcome.ConfirmationRequired>();
        handler.PatchBodies.ShouldBeEmpty();
        (
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
        (
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

        state.Authorization.ShouldBeOfType<PredictionAuthorizationReadiness.Disabled>();
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
        await SeedHostAsync(database, "first", "first-id");
        var handler = new PredictionHandler();
        var service = CreateService(database, handler);

        await service.PredictionReceivedAsync(Event("active"), CancellationToken.None);
        await SetNativeAsync(database, false);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        await using var verify = await database.CreateDbContextAsync();
        (await verify.TwitchPredictions.ToArrayAsync()).ShouldBeEmpty();
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

        outcome.ShouldBeOfType<PredictionOperationOutcome.Started>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.TwitchPredictions.ToArrayAsync())
            .ShouldHaveSingleItem()
            .ProviderPredictionId.ShouldBe("prediction-id");
        (
            (await verify.Hosts.SingleAsync()).EnabledFeatures & HostFeatureFlags.NativeTwitch
        ).ShouldBe(HostFeatureFlags.None);
    }

    private static PredictionTemplateDraft ValidTemplate()
    {
        return new("Question", ["Yes", "No"], 60);
    }

    private static async Task SetNativeAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        bool enabled
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync();
        host.EnabledFeatures = enabled
            ? host.EnabledFeatures | HostFeatureFlags.NativeTwitch
            : host.EnabledFeatures & ~HostFeatureFlags.NativeTwitch;
        await db.SaveChangesAsync();
    }

    private static PredictionService CreateService(
        IDbContextFactory<BlokeBotDbContext> database,
        PredictionHandler handler
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        return new PredictionService(
            database,
            new ReadyBroadcaster(),
            new HelixClient(new SingleHandlerFactory(handler)),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
            ),
            events,
            new DurableAlertService(database, TimeProvider.System, events),
            NullLogger<PredictionService>.Instance,
            new NativeTwitchFeatureGate(database)
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
            Login = login,
            DisplayName = login,
            TwitchUserId = twitchId,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host;
    }

    private static EventSubPredictionEvent Event(string status)
    {
        return new(
            "first-id",
            "first",
            "prediction-id",
            "Question",
            [new("yes", "Yes", "BLUE", 1, 100, [])],
            status,
            DateTimeOffset.Parse("2026-07-26T10:00:00Z"),
            null,
            status is "resolved" ? DateTimeOffset.UtcNow : null,
            status is "resolved" ? "yes" : null,
            Guid.NewGuid().ToString("N")
        );
    }

    private sealed class ReadyBroadcaster : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> scopes,
            CancellationToken ct
        )
        {
            return Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "token",
                    new TokenValidation(
                        hostId == 1 ? "first-id" : "second-id",
                        hostId == 1 ? "first" : "second",
                        OAuthScopeSet.Create(HostBroadcasterAuthorizationService.MilestoneScopes)
                    ),
                    ImmutableArray.CreateRange(HostBroadcasterAuthorizationService.MilestoneScopes),
                    ImmutableArray.CreateRange(HostBroadcasterAuthorizationService.MilestoneScopes)
                )
            );
        }

        public BlokeBot.Functional.IO<
            BotAccount,
            AccessTokenUnavailableReason
        > GetBroadcasterAccount(string channelLogin)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(handler, false);
        }
    }

    private sealed class PredictionHandler : HttpMessageHandler
    {
        public string BroadcasterType { get; set; } = string.Empty;
        public Func<Task>? AfterPredictionCreated { get; init; }
        public List<(HttpMethod Method, string Query)> Requests { get; } = [];
        public List<string> PatchBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Requests.Add((request.Method, request.RequestUri!.Query));
            if (request.RequestUri!.AbsolutePath.EndsWith("/users"))
            {
                return Json(
                    $"{{\"data\":[{{\"id\":\"first-id\",\"broadcaster_type\":\"{BroadcasterType}\"}}]}}"
                );
            }
            if (request.Method == HttpMethod.Patch)
            {
                PatchBodies.Add(await request.Content!.ReadAsStringAsync(ct));
                return Json(Prediction("RESOLVED"));
            }
            if (request.Method == HttpMethod.Post && AfterPredictionCreated is not null)
            {
                await AfterPredictionCreated();
            }
            return Json(Prediction("ACTIVE"));
        }

        private static HttpResponseMessage Json(string value)
        {
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(value, Encoding.UTF8, "application/json"),
            };
        }

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
