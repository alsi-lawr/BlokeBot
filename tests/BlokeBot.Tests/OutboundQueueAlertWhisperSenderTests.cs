using System.Net;
using System.Text;
using BlokeBot.Eventing;
using BlokeBot.Features.Alerts;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Whispers;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class OutboundQueueAlertWhisperSenderTests
{
    [Test]
    public async Task MissingCustomBotToken_SendingQueueAlert_SkipsWhisperAndLookup()
    {
        await using var fixture = await SenderFixture.CreateAsync(seedCustomBot: false);

        await fixture.SendAsync();

        fixture.Http.WhisperRequestCount.ShouldBe(0);
        fixture.Http.UserLookupRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task CustomBotWithoutWhisperScope_SendingQueueAlert_SkipsWhisper()
    {
        await using var fixture = await SenderFixture.CreateAsync(
            validationScopes: ["chat:edit"]
        );

        await fixture.SendAsync();

        fixture.Http.ValidationRequestCount.ShouldBe(1);
        fixture.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task HostWithoutTwitchId_SendingQueueAlert_ResolvesIdAndWhispers()
    {
        await using var fixture = await SenderFixture.CreateAsync(
            hostTwitchUserId: null,
            resolvedStreamerUserId: "resolved-streamer-id"
        );

        await fixture.SendAsync();

        fixture.Http.UserLookupRequestCount.ShouldBe(1);
        fixture.Http.WhisperRequestCount.ShouldBe(1);
        fixture.Http.LastWhisperUri.ShouldContain("to_user_id=resolved-streamer-id");
    }

    [Test]
    public async Task HostLookupWithoutUser_SendingQueueAlert_SkipsWhisper()
    {
        await using var fixture = await SenderFixture.CreateAsync(
            hostTwitchUserId: null,
            resolvedStreamerUserId: null
        );

        await fixture.SendAsync();

        fixture.Http.UserLookupRequestCount.ShouldBe(1);
        fixture.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task CustomBotMatchesStreamer_SendingQueueAlert_SkipsWhisperBeforeQuota()
    {
        await using var fixture = await SenderFixture.CreateAsync(
            hostTwitchUserId: "custom-id"
        );

        await fixture.SendAsync();

        fixture.Http.WhisperRequestCount.ShouldBe(0);
        var quota = await fixture.Quota.GetStatusAsync(
            fixture.HostId,
            "custom-id",
            CancellationToken.None
        );
        quota.RecipientCount.ShouldBe(0);
    }

    [Test]
    public async Task ExhaustedRecipientQuota_SendingQueueAlert_SkipsWhisper()
    {
        await using var fixture = await SenderFixture.CreateAsync();
        await fixture.ExhaustQuotaAsync();

        await fixture.SendAsync();

        fixture.Http.WhisperRequestCount.ShouldBe(0);
        var quota = await fixture.Quota.GetStatusAsync(
            fixture.HostId,
            "custom-id",
            CancellationToken.None
        );
        quota.Exhausted.ShouldBeTrue();
        quota.RecipientCount.ShouldBe(HostWhisperQuotaService.UniqueRecipientLimit);
    }

    [Test]
    [Arguments(HttpStatusCode.NoContent, false)]
    [Arguments(HttpStatusCode.BadRequest, false)]
    [Arguments(HttpStatusCode.TooManyRequests, true)]
    public async Task TwitchDeliveryOutcome_SendingQueueAlert_UpdatesQuotaExhaustion(
        HttpStatusCode status,
        bool expectedExhausted
    )
    {
        await using var fixture = await SenderFixture.CreateAsync(whisperStatus: status);

        await fixture.SendAsync();

        fixture.Http.WhisperRequestCount.ShouldBe(1);
        var quota = await fixture.Quota.GetStatusAsync(
            fixture.HostId,
            "custom-id",
            CancellationToken.None
        );
        quota.RecipientCount.ShouldBe(1);
        quota.Exhausted.ShouldBe(expectedExhausted);
    }

    [Test]
    public async Task WhisperTransportFailure_SendingQueueAlert_ContainsExceptionAfterReservation()
    {
        await using var fixture = await SenderFixture.CreateAsync(throwOnWhisper: true);

        await Should.NotThrowAsync(() => fixture.SendAsync());

        fixture.Http.WhisperRequestCount.ShouldBe(1);
        var quota = await fixture.Quota.GetStatusAsync(
            fixture.HostId,
            "custom-id",
            CancellationToken.None
        );
        quota.RecipientCount.ShouldBe(1);
        quota.Exhausted.ShouldBeFalse();
    }

    private sealed class SenderFixture : IAsyncDisposable
    {
        private static readonly DateTimeOffset Now = new(
            2026,
            7,
            10,
            12,
            0,
            0,
            TimeSpan.Zero
        );

        private readonly SqliteBlokeBotDbFactory dbFactory;
        private readonly OutboundQueueAlertWhisperSender sender;
        private readonly string? hostTwitchUserId;

        private SenderFixture(
            SqliteBlokeBotDbFactory dbFactory,
            int hostId,
            string? hostTwitchUserId,
            ScriptedWhisperHttpClientFactory http,
            HostWhisperQuotaService quota,
            OutboundQueueAlertWhisperSender sender
        )
        {
            this.dbFactory = dbFactory;
            HostId = hostId;
            this.hostTwitchUserId = hostTwitchUserId;
            Http = http;
            Quota = quota;
            this.sender = sender;
        }

        public int HostId { get; }

        public ScriptedWhisperHttpClientFactory Http { get; }

        public HostWhisperQuotaService Quota { get; }

        public static async Task<SenderFixture> CreateAsync(
            bool seedCustomBot = true,
            string? hostTwitchUserId = "streamer-id",
            string? resolvedStreamerUserId = "streamer-id",
            IReadOnlyList<string>? validationScopes = null,
            HttpStatusCode whisperStatus = HttpStatusCode.NoContent,
            bool throwOnWhisper = false
        )
        {
            var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
            await using var db = await dbFactory.CreateDbContextAsync();
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                TwitchUserId = hostTwitchUserId,
                CreatedAtUtc = Now.UtcDateTime,
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
            if (seedCustomBot)
            {
                db.HostBotAccountSettings.Add(
                    new HostBotAccountSettings
                    {
                        HostId = host.Id,
                        OverrideEnabled = true,
                        TwitchUserId = "custom-id",
                        Login = "custombot",
                        AccessToken = "custom-token",
                        RefreshToken = "custom-refresh",
                        ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                        AuthorizedScopes = TwitchScopeSet.Format(
                            [TwitchScopes.UserManageWhispers]
                        ),
                        UpdatedAtUtc = Now.UtcDateTime,
                    }
                );
                await db.SaveChangesAsync();
            }

            var http = new ScriptedWhisperHttpClientFactory(
                validationScopes ?? [TwitchScopes.UserManageWhispers],
                resolvedStreamerUserId,
                whisperStatus,
                throwOnWhisper
            );
            var options = BotOptions();
            var oauth = new TwitchOAuthApiClient(http);
            var users = new TwitchHelixApiClient(http);
            var botAccounts = new HostBotAccountAuthorizationService(
                dbFactory,
                new HostBotAccountOAuthService(options, oauth, users),
                oauth,
                users,
                new TwitchTokenStatusService(
                    new ServiceCollection().BuildServiceProvider(),
                    oauth
                ),
                new HostedChannelChangeNotifier(new EventBus<AppEventKind>()),
                options
            );
            var quota = new HostWhisperQuotaService(dbFactory, new FixedTimeProvider(Now));
            var sender = new OutboundQueueAlertWhisperSender(
                botAccounts,
                quota,
                users,
                new TwitchHelixChatClient(
                    http,
                    options.Identity,
                    users
                ),
                options.Identity,
                NullLogger<OutboundQueueAlertWhisperSender>.Instance
            );
            return new SenderFixture(
                dbFactory,
                host.Id,
                hostTwitchUserId,
                http,
                quota,
                sender
            );
        }

        public Task SendAsync() =>
            sender.AlertCreatedAsync(
                new OutboundQueueAlertNotification(
                    123,
                    HostId,
                    "streamer",
                    hostTwitchUserId,
                    3,
                    TimeSpan.FromSeconds(31)
                ),
                CancellationToken.None
            );

        public async Task ExhaustQuotaAsync()
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            db.WhisperQuotaBuckets.Add(
                new WhisperQuotaBucket
                {
                    HostId = HostId,
                    BotTwitchUserId = "custom-id",
                    DayUtc = Now.UtcDateTime.Date,
                    CreatedAtUtc = Now.UtcDateTime,
                    UpdatedAtUtc = Now.UtcDateTime,
                    Recipients = Enumerable
                        .Range(0, HostWhisperQuotaService.UniqueRecipientLimit)
                        .Select(index => new WhisperQuotaRecipient
                        {
                            RecipientTwitchUserId = $"recipient-{index}",
                            RecipientLogin = $"recipient-{index}",
                            FirstSentAtUtc = Now.UtcDateTime,
                        })
                        .ToList(),
                }
            );
            await db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => dbFactory.DisposeAsync();

        private static TwitchBotSettings BotOptions() =>
            TwitchBotSettings.FromOptions(
                new TwitchBotOptions
                {
                    Identity = new TwitchBotIdentityOptions
                    {
                        ClientId = "client",
                        ClientSecret = "secret",
                        RedirectUri = "https://localhost/oauth/callback",
                    },
                }
            );
    }

    private sealed class ScriptedWhisperHttpClientFactory(
        IReadOnlyList<string> validationScopes,
        string? resolvedStreamerUserId,
        HttpStatusCode whisperStatus,
        bool throwOnWhisper
    ) : IHttpClientFactory
    {
        private readonly Handler handler = new(
            validationScopes,
            resolvedStreamerUserId,
            whisperStatus,
            throwOnWhisper
        );

        public int ValidationRequestCount => handler.ValidationRequestCount;

        public int UserLookupRequestCount => handler.UserLookupRequestCount;

        public int WhisperRequestCount => handler.WhisperRequestCount;

        public string LastWhisperUri => handler.LastWhisperUri;

        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);

        private sealed class Handler(
            IReadOnlyList<string> validationScopes,
            string? resolvedStreamerUserId,
            HttpStatusCode whisperStatus,
            bool throwOnWhisper
        ) : HttpMessageHandler
        {
            public int ValidationRequestCount { get; private set; }

            public int UserLookupRequestCount { get; private set; }

            public int WhisperRequestCount { get; private set; }

            public string LastWhisperUri { get; private set; } = string.Empty;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return request.RequestUri?.AbsolutePath switch
                {
                    "/oauth2/validate" => Task.FromResult(ValidationResponse()),
                    "/helix/users" => Task.FromResult(UserLookupResponse()),
                    "/helix/whispers" => WhisperResponseAsync(request),
                    _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
                };
            }

            private HttpResponseMessage ValidationResponse()
            {
                ValidationRequestCount++;
                var scopes = string.Join(",", validationScopes.Select(scope => $"\"{scope}\""));
                return JsonResponse(
                    $"{{\"user_id\":\"custom-id\",\"login\":\"custombot\",\"scopes\":[{scopes}]}}"
                );
            }

            private HttpResponseMessage UserLookupResponse()
            {
                UserLookupRequestCount++;
                return resolvedStreamerUserId is null
                    ? JsonResponse("{\"data\":[]}")
                    : JsonResponse(
                        $"{{\"data\":[{{\"id\":\"{resolvedStreamerUserId}\",\"login\":\"streamer\",\"display_name\":\"Streamer\",\"profile_image_url\":\"\"}}]}}"
                    );
            }

            private Task<HttpResponseMessage> WhisperResponseAsync(HttpRequestMessage request)
            {
                WhisperRequestCount++;
                LastWhisperUri = request.RequestUri?.ToString() ?? string.Empty;
                if (throwOnWhisper)
                    throw new HttpRequestException("Whisper transport failed.");

                var response = new HttpResponseMessage(whisperStatus);
                if (whisperStatus != HttpStatusCode.NoContent)
                    response.Content = new StringContent(
                        "{\"message\":\"rejected\"}",
                        Encoding.UTF8,
                        "application/json"
                    );
                return Task.FromResult(response);
            }

            private static HttpResponseMessage JsonResponse(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
