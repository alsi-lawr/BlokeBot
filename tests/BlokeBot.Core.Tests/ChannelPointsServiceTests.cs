#pragma warning disable IDE0011, IDE0022
using System.Collections.Immutable;
using System.Net;
using System.Text;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
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
            TimeProvider.System
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
        await using var verify = await dbFactory.CreateDbContextAsync();
        var redemption = (
            await verify.TwitchRewardRedemptions.ToArrayAsync()
        ).ShouldHaveSingleItem();
        redemption.Status.ShouldBe(TwitchRewardRedemptionStatus.Fulfilled);
        (await verify.TwitchCustomRewards.SingleAsync()).IsManageable.ShouldBeTrue();
    }

    private sealed class ReadyBroadcasterProvider : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        ) =>
            Task.FromResult<TokenStatus>(
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

    private sealed class ChannelPointsHttpClientFactory : IHttpClientFactory
    {
        internal int RedemptionPatches { get; private set; }

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(ChannelPointsHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                if (request.Method == HttpMethod.Post)
                    return Task.FromResult(
                        Json(
                            """{"data":[{"id":"managed","title":"Managed","prompt":"Prompt","cost":100,"is_manageable":true,"is_paused":false,"is_user_input_required":true,"max_per_stream_setting":{"is_enabled":false},"max_per_user_per_stream_setting":{"is_enabled":false},"global_cooldown_setting":{"is_enabled":false},"should_redemptions_skip_request_queue":false,"background_color":"#FFFFFF"}]}"""
                        )
                    );
                if (request.Method == HttpMethod.Patch)
                {
                    owner.RedemptionPatches++;
                    return Task.FromResult(Json("""{"data":[]}"""));
                }
                throw new InvalidOperationException(
                    $"Unexpected request {request.Method} {request.RequestUri}"
                );
            }

            private static HttpResponseMessage Json(string value) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(value, Encoding.UTF8, "application/json"),
                };
        }
    }
}
