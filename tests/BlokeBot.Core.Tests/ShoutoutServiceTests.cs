using System.Collections.Immutable;
using System.Net;
using System.Text;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class ShoutoutServiceTests
{
    [Test]
    public async Task NativeTwitchDisabled_InboundDelivery_IsIgnoredUntilReenabledWithoutDeletingHistory()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "host",
                DisplayName = "Host",
                TwitchUserId = "host-id",
                EnabledFeatures = HostFeatureFlags.All & ~HostFeatureFlags.NativeTwitch,
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
            db.ShoutoutHistory.Add(
                new ShoutoutHistoryEntry
                {
                    HostId = host.Id,
                    Direction = ShoutoutHistoryDirection.Received,
                    ProviderMessageId = "retained-delivery",
                    SourceTwitchUserId = "source-id",
                    SourceLogin = "source",
                    TargetTwitchUserId = "host-id",
                    TargetLogin = "host",
                    ViewerCount = 10,
                    OccurredAtUtc = DateTime.Parse("2026-07-25T00:00:00Z").ToUniversalTime(),
                }
            );
            await db.SaveChangesAsync();
        }
        var service = new ShoutoutService(
            dbFactory,
            null!,
            null!,
            null!,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System,
            new NativeTwitchFeatureGate(dbFactory)
        );
        var delivery = new EventSubShoutoutEvent(
            "host-id",
            "host",
            "source-id",
            "source",
            "target-id",
            "target",
            42,
            DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
            null,
            null,
            EventSubShoutoutDirection.Received,
            "new-delivery"
        );

        await service.ShoutoutReceivedAsync(delivery, CancellationToken.None);

        (await service.LoadAsync(1, null, CancellationToken.None)).History.ShouldBeEmpty();
        await using (var verifyDisabled = await dbFactory.CreateDbContextAsync())
        {
            (await verifyDisabled.ShoutoutHistory.CountAsync()).ShouldBe(1);
            var host = await verifyDisabled.Hosts.SingleAsync();
            host.EnabledFeatures |= HostFeatureFlags.NativeTwitch;
            await verifyDisabled.SaveChangesAsync();
        }

        await service.ShoutoutReceivedAsync(delivery, CancellationToken.None);

        var enabledState = await service.LoadAsync(1, null, CancellationToken.None);
        enabledState.History.Count.ShouldBe(2);
        await using var verifyEnabled = await dbFactory.CreateDbContextAsync();
        (await verifyEnabled.ShoutoutHistory.CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task DuplicateProviderDelivery_RecordingShoutout_UpdatesOnlyMatchingHostOnce()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.AddRange(
                new BotHost
                {
                    Login = "first",
                    DisplayName = "First",
                    TwitchUserId = "first-id",
                },
                new BotHost
                {
                    Login = "second",
                    DisplayName = "Second",
                    TwitchUserId = "second-id",
                }
            );
            await db.SaveChangesAsync();
        }
        var service = new ShoutoutService(
            dbFactory,
            null!,
            null!,
            null!,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System,
            new NativeTwitchFeatureGate(dbFactory)
        );
        var delivery = new EventSubShoutoutEvent(
            "first-id",
            "first",
            "source-id",
            "source",
            "target-id",
            "target",
            42,
            DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-26T01:00:00Z"),
            DateTimeOffset.Parse("2026-07-26T02:00:00Z"),
            EventSubShoutoutDirection.Sent,
            "provider-delivery"
        );

        await service.ShoutoutReceivedAsync(delivery, CancellationToken.None);
        await service.ShoutoutReceivedAsync(delivery, CancellationToken.None);

        await using var verify = await dbFactory.CreateDbContextAsync();
        var first = await verify.ShoutoutHistory.Where(x => x.HostId == 1).ToArrayAsync();
        first.Length.ShouldBe(1);
        (await verify.ShoutoutHistory.Where(x => x.HostId == 2).CountAsync()).ShouldBe(0);
        first
            .Single()
            .TargetCooldownEndsAtUtc.ShouldBe(
                DateTime.Parse("2026-07-26T02:00:00Z").ToUniversalTime()
            );
        var dashboard = await service.LoadAsync(1, "target", CancellationToken.None);
        dashboard
            .TargetCooldown.ShouldBeOfType<ShoutoutTargetCooldownReadiness.EligibleAt>()
            .Value.ShouldBe(DateTime.Parse("2026-07-26T02:00:00Z").ToUniversalTime());
    }

    [Test]
    public async Task TargetCooldown_AfterHistoryTrimming_LoadingByTarget_ReturnsDurableEligibility()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.Add(
                new BotHost
                {
                    Login = "host",
                    DisplayName = "Host",
                    TwitchUserId = "host-id",
                }
            );
            await db.SaveChangesAsync();
        }
        var service = new ShoutoutService(
            dbFactory,
            null!,
            null!,
            null!,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System,
            new NativeTwitchFeatureGate(dbFactory)
        );
        var targetEligibility = DateTimeOffset.Parse("2026-07-26T02:00:00Z");
        await service.ShoutoutReceivedAsync(
            new(
                "host-id",
                "host",
                "source-id",
                "source",
                "target-id",
                "target",
                42,
                DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
                DateTimeOffset.Parse("2026-07-26T01:00:00Z"),
                targetEligibility,
                EventSubShoutoutDirection.Sent,
                "target-delivery"
            ),
            CancellationToken.None
        );
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.ShoutoutHistory.AddRange(
                Enumerable
                    .Range(0, 100)
                    .Select(index => new ShoutoutHistoryEntry
                    {
                        HostId = 1,
                        Direction = ShoutoutHistoryDirection.Received,
                        ProviderMessageId = $"ordinary-{index}",
                        SourceTwitchUserId = "source-id",
                        SourceLogin = "source",
                        TargetTwitchUserId = $"ordinary-id-{index}",
                        TargetLogin = $"ordinary-{index}",
                        ViewerCount = 1,
                        OccurredAtUtc = DateTime
                            .Parse("2026-07-26T00:01:00Z")
                            .ToUniversalTime()
                            .AddMinutes(index),
                    })
            );
            await db.SaveChangesAsync();
        }

        await service.ShoutoutReceivedAsync(
            new(
                "host-id",
                "host",
                "source-id",
                "source",
                "ordinary-id-latest",
                "ordinary-latest",
                1,
                DateTimeOffset.Parse("2026-07-26T04:00:00Z"),
                null,
                null,
                EventSubShoutoutDirection.Received,
                "ordinary-latest-delivery"
            ),
            CancellationToken.None
        );

        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.ShoutoutHistory.AnyAsync(x => x.TargetLogin == "target")).ShouldBeFalse();
        var dashboard = await service.LoadAsync(1, "target", CancellationToken.None);
        dashboard
            .TargetCooldown.ShouldBeOfType<ShoutoutTargetCooldownReadiness.EligibleAt>()
            .Value.ShouldBe(targetEligibility.UtcDateTime);
    }

    [Test]
    [Arguments(ShoutoutScenario.Self)]
    [Arguments(ShoutoutScenario.Offline)]
    [Arguments(ShoutoutScenario.NotModerator)]
    [Arguments(ShoutoutScenario.MissingScope)]
    public async Task RequiredSendOutcome_SendingShoutout_ReturnsTypedResult(
        ShoutoutScenario scenario
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.Add(
                new BotHost
                {
                    Login = "host",
                    DisplayName = "Host",
                    TwitchUserId = "host-id",
                }
            );
            await db.SaveChangesAsync();
        }
        var scopes =
            scenario is ShoutoutScenario.MissingScope
                ? [Scopes.UserReadModeratedChannels]
                : RequiredScopes();
        var service = new ShoutoutService(
            dbFactory,
            new StaticAccountProvider(TokenStatus(scopes)),
            new HelixClient(
                new ScenarioHttpClientFactory(scenario),
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            ),
            Settings(),
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System,
            new NativeTwitchFeatureGate(dbFactory)
        );

        var outcome = await service.SendAsync(1, "target", CancellationToken.None);

        switch (scenario)
        {
            case ShoutoutScenario.Self:
                outcome.ShouldBeOfType<ShoutoutOperationOutcome.SelfTarget>();
                break;
            case ShoutoutScenario.Offline:
                outcome.ShouldBeOfType<ShoutoutOperationOutcome.TargetOffline>();
                break;
            case ShoutoutScenario.NotModerator:
            case ShoutoutScenario.MissingScope:
                outcome.ShouldBeOfType<ShoutoutOperationOutcome.NotReady>();
                break;
            default:
                throw new InvalidOperationException();
        }
    }

    private static string[] RequiredScopes()
    {
        return
        [
            Scopes.UserReadModeratedChannels,
            Scopes.ModeratorReadShoutouts,
            Scopes.ModeratorManageShoutouts,
        ];
    }

    private static ActiveBotAccountTokenStatus TokenStatus(IReadOnlyList<string> scopes)
    {
        var granted = ImmutableArray.CreateRange(scopes);
        var required = ImmutableArray.CreateRange(RequiredScopes());
        var missing = ImmutableArray.CreateRange(required.Except(granted, StringComparer.Ordinal));
        var validation = new TokenValidation("bot-id", "bot", OAuthScopeSet.Create(granted));
        return new()
        {
            BotLogin = "bot",
            Status = missing.IsEmpty
                ? new TokenStatus.Ready("token", validation, required, granted)
                : new TokenStatus.MissingScopes("token", validation, required, granted, missing),
        };
    }

    private static BotSettings Settings()
    {
        return BotSettings.FromOptions(
            new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
        );
    }

    public enum ShoutoutScenario
    {
        Self,
        Offline,
        NotModerator,
        MissingScope,
    }

    private sealed class StaticAccountProvider(ActiveBotAccountTokenStatus status)
        : IHostBotAccountTokenStatusProvider
    {
        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(status);
        }
    }

    private sealed class ScenarioHttpClientFactory(ShoutoutScenario scenario) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new Handler(scenario));
        }

        private sealed class Handler(ShoutoutScenario scenario) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return request.RequestUri!.AbsolutePath switch
                {
                    "/helix/users" => Task.FromResult(
                        Json(
                            scenario is ShoutoutScenario.Self
                                ? """{"data":[{"id":"host-id","login":"host","display_name":"Host"}]}"""
                                : """{"data":[{"id":"target-id","login":"target","display_name":"Target"}]}"""
                        )
                    ),
                    "/helix/streams" => Task.FromResult(
                        Json(
                            scenario is ShoutoutScenario.Offline
                                ? """{"data":[],"pagination":{}}"""
                                : """{"data":[{"id":"stream-id","user_id":"target-id","user_login":"target","user_name":"Target","game_id":"game","game_name":"Game","type":"live","title":"Live","tags":[],"viewer_count":1,"started_at":"2026-07-26T00:00:00Z","language":"en","thumbnail_url":"","is_mature":false}],"pagination":{}}"""
                        )
                    ),
                    "/helix/moderation/channels" => Task.FromResult(
                        Json("""{"data":[],"pagination":{}}""")
                    ),
                    _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
                };
            }

            private static HttpResponseMessage Json(string value)
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(value, Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}
