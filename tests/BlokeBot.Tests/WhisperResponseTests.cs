using System.Net;
using System.Text;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Whispers;
using BlokeBot.Identity;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class WhisperResponseTests
{
    [Test]
    public async Task SameRecipientSameDay_ReservingQuota_CountsOnce()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var quota = new HostWhisperQuotaService(
            dbFactory,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero))
        );

        var first = await quota.ReserveRecipientAsync(
            hostId,
            "bot-id",
            "viewer-id",
            "viewer",
            CancellationToken.None
        );
        var second = await quota.ReserveRecipientAsync(
            hostId,
            "bot-id",
            "viewer-id",
            "Viewer",
            CancellationToken.None
        );

        first.Allowed.ShouldBeTrue();
        first.CountedNewRecipient.ShouldBeTrue();
        second.Allowed.ShouldBeTrue();
        second.CountedNewRecipient.ShouldBeFalse();
        second.Status.RecipientCount.ShouldBe(1);
    }

    [Test]
    public async Task QuotaAtLimit_ReservingExistingAndNewRecipient_AllowsExistingAndBlocksNew()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var quota = new HostWhisperQuotaService(
            dbFactory,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero))
        );

        for (var index = 0; index < HostWhisperQuotaService.UniqueRecipientLimit; index++)
        {
            var result = await quota.ReserveRecipientAsync(
                hostId,
                "bot-id",
                $"viewer-id-{index}",
                $"viewer{index}",
                CancellationToken.None
            );
            result.Allowed.ShouldBeTrue();
        }

        var blocked = await quota.ReserveRecipientAsync(
            hostId,
            "bot-id",
            "viewer-id-40",
            "viewer40",
            CancellationToken.None
        );
        var existing = await quota.ReserveRecipientAsync(
            hostId,
            "bot-id",
            "viewer-id-0",
            "viewer0",
            CancellationToken.None
        );

        blocked.Allowed.ShouldBeFalse();
        blocked.Status.RecipientCount.ShouldBe(HostWhisperQuotaService.UniqueRecipientLimit);
        blocked.Status.Exhausted.ShouldBeTrue();
        existing.Allowed.ShouldBeTrue();
        existing.Status.RecipientCount.ShouldBe(HostWhisperQuotaService.UniqueRecipientLimit);
        existing.Status.Exhausted.ShouldBeTrue();
    }

    [Test]
    public async Task TwitchRateLimit_SendingWhisper_FallsBackToChatAndExhaustsQuota()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCustomBotAsync(dbFactory, hostId);
        var httpClientFactory = new WhisperHttpClientFactory(HttpStatusCode.TooManyRequests);
        var options = BotOptions();
        var oauth = new TwitchOAuthApiClient(httpClientFactory);
        var helixUsers = new TwitchHelixApiClient(httpClientFactory);
        var hostBotAccounts = new HostBotAccountAuthorizationService(
            dbFactory,
            new HostBotAccountOAuthService(options, oauth, helixUsers),
            oauth,
            helixUsers,
            new TwitchTokenStatusService(new ServiceCollection().BuildServiceProvider(), oauth),
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            options
        );
        var chat = new RecordingChatSender();
        var quota = new HostWhisperQuotaService(
            dbFactory,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero))
        );
        var sender = new HostWhisperCommandResponseSender(
            chat,
            hostBotAccounts,
            quota,
            helixUsers,
            new TwitchHelixChatClient(
                httpClientFactory,
                options.Identity,
                helixUsers
            ),
            dbFactory,
            options.Identity,
            NullLogger<HostWhisperCommandResponseSender>.Instance
        );
        var source = new TwitchChatMessage(
            "viewer",
            "streamer",
            "!points",
            "raw",
            new Dictionary<string, string> { ["user-id"] = "viewer-id" }
        );

        await sender.SendAsync(
            source,
            TwitchCommandResponse.Whisper("your balance is 10"),
            CancellationToken.None
        );
        var status = await quota.GetStatusAsync(hostId, "custom-id", CancellationToken.None);

        chat.Messages.ShouldBe([new SentChatMessage("streamer", "your balance is 10")]);
        status.Exhausted.ShouldBeTrue();
        status.RecipientCount.ShouldBe(1);
        httpClientFactory.WhisperRequestCount.ShouldBe(1);
    }

    [Test]
    public async Task SelfWhisper_SendingResponse_FallsBackWithoutRequestOrQuota()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCustomBotAsync(dbFactory, hostId);
        var httpClientFactory = new WhisperHttpClientFactory(HttpStatusCode.NoContent);
        var options = BotOptions();
        var oauth = new TwitchOAuthApiClient(httpClientFactory);
        var helixUsers = new TwitchHelixApiClient(httpClientFactory);
        var hostBotAccounts = new HostBotAccountAuthorizationService(
            dbFactory,
            new HostBotAccountOAuthService(options, oauth, helixUsers),
            oauth,
            helixUsers,
            new TwitchTokenStatusService(new ServiceCollection().BuildServiceProvider(), oauth),
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            options
        );
        var chat = new RecordingChatSender();
        var quota = new HostWhisperQuotaService(
            dbFactory,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero))
        );
        var sender = new HostWhisperCommandResponseSender(
            chat,
            hostBotAccounts,
            quota,
            helixUsers,
            new TwitchHelixChatClient(
                httpClientFactory,
                options.Identity,
                helixUsers
            ),
            dbFactory,
            options.Identity,
            NullLogger<HostWhisperCommandResponseSender>.Instance
        );
        var source = new TwitchChatMessage(
            "custombot",
            "streamer",
            "!points",
            "raw",
            new Dictionary<string, string> { ["user-id"] = "custom-id" }
        );

        await sender.SendAsync(
            source,
            TwitchCommandResponse.Whisper("your balance is 10"),
            CancellationToken.None
        );
        var status = await quota.GetStatusAsync(hostId, "custom-id", CancellationToken.None);

        chat.Messages.ShouldBe([new SentChatMessage("streamer", "your balance is 10")]);
        status.RecipientCount.ShouldBe(0);
        httpClientFactory.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task RejectedWhisper_SendingThroughHelix_PreservesStatusAndBody()
    {
        var body = """{"status":400,"message":"cannot whisper yourself"}""";
        var httpClientFactory = new WhisperHttpClientFactory(HttpStatusCode.BadRequest, body);
        var options = BotOptions();
        var helixUsers = new TwitchHelixApiClient(httpClientFactory);
        var helix = new TwitchHelixChatClient(
            httpClientFactory,
            options.Identity,
            helixUsers
        );

        var result = await helix.SendWhisperAsync(
            "override-whisper-token",
            "custom-id",
            "custom-id",
            "message",
            CancellationToken.None
        );

        result.Status.ShouldBe(TwitchWhisperSendStatus.Rejected);
        result.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        result.ResponseBody.ShouldBe(body);
    }

    private static TwitchBotSettings BotOptions() =>
        TwitchBotSettings.FromOptions(
            new TwitchBotOptions
            {
                Identity = new TwitchBotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://localhost:7107/oauth/callback",
                    Scopes = ["chat:read", "chat:edit", TwitchScopes.UserReadModeratedChannels],
                },
            }
        );

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            CreatedAtUtc = DateTime.UtcNow,
            DisplayName = login,
            Login = login,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedCustomBotAsync(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.HostBotAccountSettings.Add(
            new HostBotAccountSettings
            {
                HostId = hostId,
                OverrideEnabled = true,
                WhisperResponsesEnabled = true,
                TwitchUserId = "custom-id",
                Login = "custombot",
                AccessToken = "override-whisper-token",
                RefreshToken = "override-refresh",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                AuthorizedScopes = TwitchScopeSet.Format([TwitchScopes.UserManageWhispers]),
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record SentChatMessage(string Channel, string Message);

    private sealed class RecordingChatSender : ITwitchChatMessageSender
    {
        public List<SentChatMessage> Messages { get; } = [];

        public Task SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Messages.Add(new SentChatMessage(channel, message));
            return Task.CompletedTask;
        }
    }

    private sealed class WhisperHttpClientFactory(
        HttpStatusCode whisperStatus,
        string? whisperBody = null
    ) : IHttpClientFactory
    {
        private readonly Handler handler = new(whisperStatus, whisperBody);

        public int WhisperRequestCount => handler.WhisperRequestCount;

        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);

        private sealed class Handler(HttpStatusCode whisperStatus, string? whisperBody)
            : HttpMessageHandler
        {
            public int WhisperRequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                var response = request.RequestUri?.AbsolutePath switch
                {
                    "/oauth2/validate" => ValidationResponse(request),
                    "/helix/users" => JsonResponse(
                        """
                        {"data":[{"id":"viewer-id","login":"viewer","display_name":"Viewer","profile_image_url":""}]}
                        """
                    ),
                    "/helix/whispers" => WhisperResponse(),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                };
                return Task.FromResult(response);
            }

            private HttpResponseMessage WhisperResponse()
            {
                WhisperRequestCount++;
                var response = new HttpResponseMessage(whisperStatus);
                if (whisperBody is not null)
                    response.Content = new StringContent(
                        whisperBody,
                        Encoding.UTF8,
                        "application/json"
                    );

                return response;
            }

            private static HttpResponseMessage ValidationResponse(HttpRequestMessage request)
            {
                return request.Headers.Authorization?.Parameter switch
                {
                    "override-whisper-token" => JsonResponse(
                        """
                        {"user_id":"custom-id","login":"custombot","scopes":["user:manage:whispers"]}
                        """
                    ),
                    _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                };
            }

            private static HttpResponseMessage JsonResponse(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }
}
