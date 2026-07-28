using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class ChannelPointsServiceTests
{
    [Test]
    public async Task RewardsAndRedemptions_RespectOwnershipDeduplicateEventsAndOnlyMutateUnfulfilledManagedItems()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.AddRange(
                new BotHost
                {
                    Login = "one",
                    DisplayName = "One",
                    TwitchUserId = "one-id",
                },
                new BotHost
                {
                    Login = "two",
                    DisplayName = "Two",
                    TwitchUserId = "two-id",
                }
            );
            await db.SaveChangesAsync();
        }
        var http = new ChannelPointsHttpClientFactory();
        var events = TestEventBus.Create<AppEventKind>();
        var service = new ChannelPointsService(
            dbFactory,
            new ReadyBroadcasterProvider(),
            new HelixClient(http),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
            ),
            events,
            new DurableAlertService(dbFactory, TimeProvider.System, events),
            TimeProvider.System,
            new NativeTwitchFeatureGate(dbFactory)
        );
        var created = await service.CreateRewardAsync(
            1,
            new(
                "Managed",
                "Prompt",
                100,
                true,
                false,
                null,
                false,
                null,
                false,
                null,
                false,
                "#FFFFFF"
            ),
            CancellationToken.None
        );
        var dashboard = await service.LoadAsync(1, CancellationToken.None);
        await service.RedemptionReceivedAsync(
            new(
                "one-id",
                "one",
                "redemption",
                "managed",
                "Managed",
                "viewer",
                "viewer",
                "hello",
                HelixRewardRedemptionStatus.Unfulfilled,
                DateTimeOffset.Parse("2026-07-26T10:00:00Z"),
                "message-1"
            ),
            CancellationToken.None
        );
        await service.RedemptionReceivedAsync(
            new(
                "one-id",
                "one",
                "redemption",
                "managed",
                "Managed",
                "viewer",
                "viewer",
                "hello",
                HelixRewardRedemptionStatus.Unfulfilled,
                DateTimeOffset.Parse("2026-07-26T10:00:00Z"),
                "message-1"
            ),
            CancellationToken.None
        );
        var fulfilled = await service.UpdateRedemptionAsync(
            1,
            "redemption",
            true,
            CancellationToken.None
        );
        var wrongHost = await service.UpdateRedemptionAsync(
            2,
            "redemption",
            true,
            CancellationToken.None
        );

        created.ShouldBeOfType<ChannelPointsOperationOutcome.RewardCreated>();
        fulfilled.ShouldBeOfType<ChannelPointsOperationOutcome.RedemptionUpdated>();
        wrongHost.ShouldBeOfType<ChannelPointsOperationOutcome.RedemptionNotActionable>();
        http.RedemptionPatches.ShouldBe(1);
        http.AllRewardsLists.ShouldBe(1);
        http.ManageableRewardsLists.ShouldBe(1);
        http.RedemptionStatusLists.ShouldBe(3);
        dashboard.Rewards.ShouldHaveSingleItem().IsManageable.ShouldBeTrue();
        await using var verify = await dbFactory.CreateDbContextAsync();
        var redemption = (
            await verify.TwitchRewardRedemptions.ToArrayAsync()
        ).ShouldHaveSingleItem();
        redemption.Status.ShouldBe(TwitchRewardRedemptionStatus.Fulfilled);
        (await verify.TwitchCustomRewards.SingleAsync()).IsManageable.ShouldBeTrue();
        http.ReturnIneligibleCustomRewards = true;
        var ineligible = await service.LoadAsync(1, CancellationToken.None);
        ineligible.Authorization.ShouldBeOfType<ChannelPointsAuthorizationReadiness.Ineligible>();
    }

    [Test]
    public async Task NativeGate_DisabledSuppressesReadsWritesAndRetention_ThenReenableReconciles()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.Add(
                new BotHost
                {
                    Login = "one",
                    DisplayName = "One",
                    TwitchUserId = "one-id",
                    EnabledFeatures = HostFeatureFlags.Guessing,
                }
            );
            db.TwitchCustomRewards.Add(
                new TwitchCustomReward
                {
                    HostId = 1,
                    ProviderRewardId = "retained",
                    Title = "Retained",
                    Cost = 100,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }
        var http = new ChannelPointsHttpClientFactory();
        var service = CreateService(dbFactory, http);

        var disabled = await service.LoadAsync(1, CancellationToken.None);
        var mutation = await service.CreateRewardAsync(1, ValidDraft(), CancellationToken.None);
        await service.ReconcileAsync(1, CancellationToken.None);
        await service.RedemptionReceivedAsync(
            Redemption("disabled-redemption"),
            CancellationToken.None
        );

        disabled.Authorization.ShouldBeOfType<ChannelPointsAuthorizationReadiness.Disabled>();
        disabled.Rewards.ShouldBeEmpty();
        mutation
            .ShouldBeOfType<ChannelPointsOperationOutcome.NotReady>()
            .Message.ShouldBe(NativeTwitchFeatureGate.DisabledMessage);
        http.Requests.ShouldBe(0);
        await using (var verify = await dbFactory.CreateDbContextAsync())
        {
            (await verify.TwitchCustomRewards.ToArrayAsync())
                .ShouldHaveSingleItem()
                .ProviderRewardId.ShouldBe("retained");
            (await verify.TwitchRewardRedemptions.ToArrayAsync()).ShouldBeEmpty();
        }

        await SetNativeAsync(dbFactory, true);
        var enabled = await service.LoadAsync(1, CancellationToken.None);

        enabled.Authorization.ShouldBeOfType<ChannelPointsAuthorizationReadiness.Ready>();
        enabled.Rewards.ShouldHaveSingleItem().ProviderRewardId.ShouldBe("managed");
        http.Requests.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task ProviderAcceptedReward_IsPersistedWhenDisableRacesAfterProviderWork()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.Add(
                new BotHost
                {
                    Login = "one",
                    DisplayName = "One",
                    TwitchUserId = "one-id",
                }
            );
            await db.SaveChangesAsync();
        }
        var http = new ChannelPointsHttpClientFactory
        {
            AfterRewardCreated = () => SetNativeAsync(dbFactory, false),
        };
        var service = CreateService(dbFactory, http);

        var outcome = await service.CreateRewardAsync(1, ValidDraft(), CancellationToken.None);

        outcome.ShouldBeOfType<ChannelPointsOperationOutcome.RewardCreated>();
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.TwitchCustomRewards.ToArrayAsync())
            .ShouldHaveSingleItem()
            .ProviderRewardId.ShouldBe("managed");
        (
            (await verify.Hosts.SingleAsync()).EnabledFeatures & HostFeatureFlags.NativeTwitch
        ).ShouldBe(HostFeatureFlags.None);
    }

    private static ChannelPointsService CreateService(
        SqliteBlokeBotDbFactory dbFactory,
        ChannelPointsHttpClientFactory http
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        return new(
            dbFactory,
            new ReadyBroadcasterProvider(),
            new HelixClient(http),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
            ),
            events,
            new DurableAlertService(dbFactory, TimeProvider.System, events),
            TimeProvider.System,
            new NativeTwitchFeatureGate(dbFactory)
        );
    }

    private static ChannelPointsRewardDraft ValidDraft()
    {
        return new(
            "Managed",
            "Prompt",
            100,
            true,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            "#FFFFFF"
        );
    }

    private static EventSubRewardRedemptionEvent Redemption(string id)
    {
        return new(
            "one-id",
            "one",
            id,
            "managed",
            "Managed",
            "viewer-id",
            "viewer",
            "input",
            HelixRewardRedemptionStatus.Unfulfilled,
            DateTimeOffset.UtcNow,
            $"message-{id}"
        );
    }

    private static async Task SetNativeAsync(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        bool enabled
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync();
        host.EnabledFeatures = enabled
            ? host.EnabledFeatures | HostFeatureFlags.NativeTwitch
            : host.EnabledFeatures & ~HostFeatureFlags.NativeTwitch;
        await db.SaveChangesAsync();
    }

    private sealed class ReadyBroadcasterProvider : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        )
        {
            return Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "token",
                    new TokenValidation(
                        hostId == 1 ? "one-id" : "two-id",
                        hostId == 1 ? "one" : "two",
                        OAuthScopeSet.Create(HostBroadcasterAuthorizationService.MilestoneScopes)
                    ),
                    ImmutableArray.CreateRange(HostBroadcasterAuthorizationService.MilestoneScopes),
                    ImmutableArray.CreateRange(HostBroadcasterAuthorizationService.MilestoneScopes)
                )
            );
        }

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        )
        {
            return IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    )
                )
            );
        }
    }

    private sealed class ChannelPointsHttpClientFactory : IHttpClientFactory
    {
        internal int Requests { get; private set; }
        internal Func<Task>? AfterRewardCreated { get; init; }
        internal int RedemptionPatches { get; private set; }
        internal bool ReturnIneligibleCustomRewards { get; set; }
        internal int AllRewardsLists { get; private set; }
        internal int ManageableRewardsLists { get; private set; }
        internal int RedemptionStatusLists { get; private set; }

        public HttpClient CreateClient(string name)
        {
            return new(new Handler(this));
        }

        private sealed class Handler(ChannelPointsHttpClientFactory owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                owner.Requests++;
                var uri = request.RequestUri?.ToString() ?? string.Empty;
                if (request.Method == HttpMethod.Post)
                {
                    uri.ShouldContain("broadcaster_id=one-id");
                    var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    body.ShouldNotContain("broadcaster_id");
                    if (owner.AfterRewardCreated is not null)
                    {
                        await owner.AfterRewardCreated();
                    }
                    return Json(RewardResponse());
                }
                if (request.Method == HttpMethod.Get && uri.Contains("custom_rewards?"))
                {
                    if (owner.ReturnIneligibleCustomRewards)
                    {
                        return new(HttpStatusCode.Forbidden)
                        {
                            Content = new StringContent(
                                "Channel Points are available only to Affiliate or Partner channels.",
                                Encoding.UTF8,
                                "text/plain"
                            ),
                        };
                    }
                    if (uri.Contains("only_manageable_rewards=true"))
                    {
                        owner.ManageableRewardsLists++;
                    }
                    else
                    {
                        owner.AllRewardsLists++;
                    }
                    return Json(RewardResponse());
                }
                if (request.Method == HttpMethod.Get && uri.Contains("redemptions?"))
                {
                    uri.ShouldContain("status=");
                    uri.ShouldContain("sort=NEWEST");
                    uri.ShouldContain("first=50");
                    owner.RedemptionStatusLists++;
                    return Json("""{"data":[],"pagination":{}}""");
                }
                if (request.Method == HttpMethod.Patch)
                {
                    owner.RedemptionPatches++;
                    uri.ShouldContain("id=redemption");
                    var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    using var document = JsonDocument.Parse(body);
                    document.RootElement.EnumerateObject().Select(x => x.Name).ShouldBe(["status"]);
                    document.RootElement.GetProperty("status").GetString().ShouldBe("FULFILLED");
                    return Json("""{"data":[]}""");
                }
                throw new InvalidOperationException(
                    $"Unexpected request {request.Method} {request.RequestUri}"
                );
            }

            private static HttpResponseMessage Json(string value)
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(value, Encoding.UTF8, "application/json"),
                };
            }

            private static string RewardResponse()
            {
                return """{"data":[{"id":"managed","title":"Managed","prompt":"Prompt","cost":100,"is_enabled":true,"is_paused":false,"is_user_input_required":true,"max_per_stream_setting":{"is_enabled":false},"max_per_user_per_stream_setting":{"is_enabled":false},"global_cooldown_setting":{"is_enabled":false},"should_redemptions_skip_request_queue":false,"background_color":"#FFFFFF"}]}""";
            }
        }
    }
}
