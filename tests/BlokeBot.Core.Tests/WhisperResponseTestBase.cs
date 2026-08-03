using System.Net;
using System.Text;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Core.Tests;

public abstract class WhisperResponseTestBase
{
    private protected static WhisperQuotaService CreateQuota(SqliteBlokeBotDbFactory dbFactory) =>
        new(
            dbFactory,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero))
        );

    private protected static async Task<PrivateDeliveryError> SendPrivateFailureAsync(
        WhisperHarness harness,
        ChatMessage source,
        string message = "private response"
    )
    {
        await harness.Sender.SendAsync(
            source,
            CommandResponse.Whisper(message),
            CancellationToken.None
        );
        harness.Chat.Messages.ShouldBeEmpty();
        var failure = harness.FailureHandler.Failures.ShouldHaveSingleItem();
        failure.Context.HostChannel.ShouldBe("streamer");
        return failure.Error;
    }

    private protected static BotSettings BotOptions() =>
        BotSettings.FromOptions(
            new BotOptions
            {
                Identity = new BotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://localhost:7107/oauth/callback",
                    Scopes = ["chat:read", "chat:edit", Scopes.UserReadModeratedChannels],
                },
            }
        );

    private protected static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
            DisplayName = login,
            Login = login,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private protected static async Task SeedCustomBotAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        bool whisperResponsesEnabled
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = new HostBotAccountSettings
        {
            HostId = hostId,
            OverrideEnabled = true,
            WhisperResponsesEnabled = whisperResponsesEnabled,
            TwitchUserId = "custom-id",
            Login = "custombot",
            AuthorizedScopes = ScopeSet.Format([Scopes.UserManageWhispers]),
            UpdatedAtUtc = DateTime.UtcNow,
        };
        HostBotAccountTokenProtectionTestSupport.SetProtectedPayload(
            settings,
            new HostBotAccountTokenPayload(
                "override-whisper-token",
                "override-refresh",
                DateTimeOffset.UtcNow.AddHours(1)
            )
        );
        _ = db.HostBotAccountSettings.Add(settings);
        _ = await db.SaveChangesAsync();
    }

    private protected sealed class WhisperHarness : IAsyncDisposable
    {
        private WhisperHarness(
            SqliteBlokeBotDbFactory dbFactory,
            int hostId,
            WhisperHttpClientFactory http,
            RecordingChatSender chat,
            WhisperQuotaService quota,
            RecordingPrivateDeliveryFailureHandler failureHandler,
            RecordingLogger<WhisperCommandResponseSender> publicChatLogger,
            WhisperCommandResponseSender sender
        )
        {
            _dbFactory = dbFactory;
            HostId = hostId;
            Http = http;
            Chat = chat;
            Quota = quota;
            FailureHandler = failureHandler;
            PublicChatLogger = publicChatLogger;
            Sender = sender;
        }

        private readonly SqliteBlokeBotDbFactory _dbFactory;

        internal int HostId { get; }

        internal WhisperHttpClientFactory Http { get; }

        internal RecordingChatSender Chat { get; }

        internal WhisperQuotaService Quota { get; }

        internal RecordingPrivateDeliveryFailureHandler FailureHandler { get; }

        internal RecordingLogger<WhisperCommandResponseSender> PublicChatLogger { get; }

        internal WhisperCommandResponseSender Sender { get; }

        internal static async Task<WhisperHarness> CreateAsync(
            HttpStatusCode whisperStatus,
            string? whisperBody = null,
            string usersJson =
                """{"data":[{"id":"viewer-id","login":"viewer","display_name":"Viewer","profile_image_url":""}]}""",
            Exception? usersException = null,
            Exception? whisperException = null,
            bool validationAccepted = true,
            bool whisperResponsesEnabled = true,
            CancellationTokenSource? cancelOnWhisper = null,
            Exception? handlerException = null,
            CancellationTokenSource? cancelOnHandling = null,
            PublicChatSendOutcome? publicChatOutcome = null
        )
        {
            var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
            var hostId = await SeedHostAsync(dbFactory, "streamer");
            await SeedCustomBotAsync(dbFactory, hostId, whisperResponsesEnabled);

            var http = new WhisperHttpClientFactory(
                whisperStatus,
                whisperBody,
                usersJson,
                usersException,
                whisperException,
                validationAccepted,
                cancelOnWhisper
            );
            var options = BotOptions();
            var oauth = new OAuthTransport(
                http,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            );
            var helixUsers = new HelixClient(
                http,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            );
            var hostBotAccounts = new HostBotAccountAuthorizationService(
                dbFactory,
                new HostBotAccountOAuthService(options, oauth, helixUsers),
                oauth,
                helixUsers,
                HostBotAccountTokenProtectionTestSupport.CreateProtector(),
                new UnavailableTokenStatusSource(),
                new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
                options
            );
            var chat = new RecordingChatSender(
                publicChatOutcome ?? new PublicChatSendOutcome.Accepted()
            );
            var quota = CreateQuota(dbFactory);
            var failureHandler = new RecordingPrivateDeliveryFailureHandler(
                handlerException,
                cancelOnHandling
            );
            var publicChatLogger = new RecordingLogger<WhisperCommandResponseSender>();
            var sender = new WhisperCommandResponseSender(
                chat,
                hostBotAccounts,
                quota,
                helixUsers,
                new WhisperClient(http, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
                dbFactory,
                options.Identity,
                failureHandler,
                publicChatLogger
            );
            return new(
                dbFactory,
                hostId,
                http,
                chat,
                quota,
                failureHandler,
                publicChatLogger,
                sender
            );
        }

        internal ChatMessage Source(bool includeUserId = true, string userId = "viewer-id")
        {
            IReadOnlyDictionary<string, string> tags = includeUserId
                ? new Dictionary<string, string> { ["user-id"] = userId }
                : [];
            return new("viewer", "streamer", "!points", "raw", tags);
        }

        public async ValueTask DisposeAsync() => await _dbFactory.DisposeAsync();
    }

    private protected sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private protected sealed record SentChatMessage(
        string Channel,
        string Message,
        PublicChatPinIntent? Pin = null
    );

    private protected sealed record HandledPrivateDeliveryFailure(
        PrivateDeliveryError Error,
        PrivateDeliveryFailureContext Context
    );

    private protected sealed class RecordingPrivateDeliveryFailureHandler(
        Exception? exception = null,
        CancellationTokenSource? cancelOnHandling = null
    ) : IPrivateDeliveryFailureHandler
    {
        internal List<HandledPrivateDeliveryFailure> Failures { get; } = [];

        public ValueTask HandleAsync(
            PrivateDeliveryError error,
            PrivateDeliveryFailureContext context,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Failures.Add(new(error, context));
            if (cancelOnHandling is not null)
            {
                cancelOnHandling.Cancel();
                return ValueTask.FromCanceled(cancellationToken);
            }

            return exception is null ? ValueTask.CompletedTask : ValueTask.FromException(exception);
        }
    }

    private protected sealed class RecordingChatSender(PublicChatSendOutcome outcome)
        : IPublicChatMessageSender
    {
        internal List<SentChatMessage> Messages { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Messages.Add(new SentChatMessage(channel, message));
            return ValueTask.FromResult(outcome);
        }

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            PublicChatPinIntent pinIntent,
            CancellationToken cancellationToken
        )
        {
            Messages.Add(new SentChatMessage(channel, message, pinIntent));
            return ValueTask.FromResult(outcome);
        }
    }

    private protected sealed class WhisperHttpClientFactory(
        HttpStatusCode whisperStatus,
        string? whisperBody,
        string usersJson,
        Exception? usersException,
        Exception? whisperException,
        bool validationAccepted,
        CancellationTokenSource? cancelOnWhisper
    ) : IHttpClientFactory
    {
        internal int ValidationRequestCount { get; private set; }
        internal int WhisperRequestCount { get; private set; }

        public HttpClient CreateClient(string name) =>
            new(
                new Handler(
                    this,
                    whisperStatus,
                    whisperBody,
                    usersJson,
                    usersException,
                    whisperException,
                    validationAccepted,
                    cancelOnWhisper
                )
            );

        private sealed class Handler(
            WhisperHttpClientFactory owner,
            HttpStatusCode whisperStatus,
            string? whisperBody,
            string usersJson,
            Exception? usersException,
            Exception? whisperException,
            bool validationAccepted,
            CancellationTokenSource? cancelOnWhisper
        ) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            ) =>
                request.RequestUri?.AbsolutePath switch
                {
                    "/oauth2/validate" => Task.FromResult(ValidationResponse(request)),
                    "/oauth2/token" when !validationAccepted => Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.BadRequest)
                    ),
                    "/helix/users" when usersException is not null =>
                        Task.FromException<HttpResponseMessage>(usersException),
                    "/helix/users" => Task.FromResult(JsonResponse(usersJson)),
                    "/helix/whispers" when whisperException is not null => FailedWhisper(
                        whisperException
                    ),
                    "/helix/whispers" when cancelOnWhisper is { } cancellation => CancelledWhisper(
                        cancellation,
                        cancellationToken
                    ),
                    "/helix/whispers" => Task.FromResult(WhisperResponse()),
                    _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
                };

            private HttpResponseMessage WhisperResponse()
            {
                owner.WhisperRequestCount++;
                var response = new HttpResponseMessage(whisperStatus);
                if (whisperBody is not null)
                {
                    response.Content = new StringContent(
                        whisperBody,
                        Encoding.UTF8,
                        "application/json"
                    );
                }

                return response;
            }

            private Task<HttpResponseMessage> FailedWhisper(Exception exception)
            {
                owner.WhisperRequestCount++;
                return Task.FromException<HttpResponseMessage>(exception);
            }

            private Task<HttpResponseMessage> CancelledWhisper(
                CancellationTokenSource cancellation,
                CancellationToken cancellationToken
            )
            {
                owner.WhisperRequestCount++;
                cancellation.Cancel();
                return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
            }

            private HttpResponseMessage ValidationResponse(HttpRequestMessage request)
            {
                owner.ValidationRequestCount++;
                return
                    validationAccepted
                    && request.Headers.Authorization?.Parameter == "override-whisper-token"
                    ? JsonResponse(
                        """{"user_id":"custom-id","login":"custombot","scopes":["user:manage:whispers"]}"""
                    )
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            private static HttpResponseMessage JsonResponse(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }

    private protected sealed class SensitiveWhisperException(string message) : Exception(message);

    private protected sealed class RecordingLogger<T> : ILogger<T>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(static pair => pair.Key, static pair => pair.Value)
                : [];
            Entries.Add(new(logLevel, formatter(state, exception), exception, properties));
        }
    }

    private protected sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties
    );
}
