using System.Net;
using System.Text;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Whispers;
using BlokeBot.Identity;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class WhisperResponseTests
{
    [Test]
    public async Task SameRecipientSameDay_ReservingQuota_ReturnsNewThenExistingRecipient()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var quota = CreateQuota(dbFactory);

        var first = await quota
            .ReserveRecipient(hostId, "bot-id", "viewer-id", "viewer")
            .ExecuteAsync(CancellationToken.None);
        var second = await quota
            .ReserveRecipient(hostId, "bot-id", "viewer-id", "Viewer")
            .ExecuteAsync(CancellationToken.None);

        first.Match(
            success => success.ShouldBeOfType<WhisperQuotaReservation.NewRecipient>(),
            _ => throw new InvalidOperationException("Expected a successful reservation.")
        );
        var existing = second.Match(
            success => success.ShouldBeOfType<WhisperQuotaReservation.ExistingRecipient>(),
            _ => throw new InvalidOperationException("Expected a successful reservation.")
        );
        existing.Status.RecipientCount.ShouldBe(1);
    }

    [Test]
    public async Task QuotaReservation_Construction_DefersPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var quota = CreateQuota(dbFactory);

        var reservation = quota.ReserveRecipient(hostId, "bot-id", "viewer-id", "viewer");
        var beforeExecution = await quota.GetStatusAsync(
            hostId,
            "bot-id",
            CancellationToken.None
        );
        var result = await reservation.ExecuteAsync(CancellationToken.None);

        beforeExecution.RecipientCount.ShouldBe(0);
        result.Match(
            success => success.ShouldBeOfType<WhisperQuotaReservation.NewRecipient>(),
            _ => throw new InvalidOperationException("Expected a successful reservation.")
        );
    }

    [Test]
    public async Task InvalidIdentity_ReservingQuota_ReturnsTypedErrorWithoutPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var quota = CreateQuota(dbFactory);

        var result = await quota
            .ReserveRecipient(hostId, " ", "viewer-id", "viewer")
            .ExecuteAsync(CancellationToken.None);

        result.Match(
            _ => throw new InvalidOperationException("Expected an invalid identity error."),
            error => error.ShouldBeOfType<WhisperQuotaReservationError.InvalidIdentity>()
        );
        (await quota.GetStatusAsync(hostId, "bot-id", CancellationToken.None))
            .RecipientCount.ShouldBe(0);
    }

    [Test]
    public async Task QuotaAtLimit_ReservingExistingAndNewRecipient_ReturnsTypedCases()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var quota = CreateQuota(dbFactory);

        for (var index = 0; index < HostWhisperQuotaService.UniqueRecipientLimit; index++)
        {
            var result = await quota
                .ReserveRecipient(
                    hostId,
                    "bot-id",
                    $"viewer-id-{index}",
                    $"viewer{index}"
                )
                .ExecuteAsync(CancellationToken.None);
            result.Match(
                success => success.ShouldBeOfType<WhisperQuotaReservation.NewRecipient>(),
                _ => throw new InvalidOperationException("Expected a successful reservation.")
            );
        }

        var blocked = await quota
            .ReserveRecipient(hostId, "bot-id", "viewer-id-40", "viewer40")
            .ExecuteAsync(CancellationToken.None);
        var existing = await quota
            .ReserveRecipient(hostId, "bot-id", "viewer-id-0", "viewer0")
            .ExecuteAsync(CancellationToken.None);

        var limit = blocked.Match(
            _ => throw new InvalidOperationException("Expected a quota error."),
            error =>
                error.ShouldBeOfType<
                    WhisperQuotaReservationError.DailyRecipientLimitReached
                >()
        );
        limit.Status.RecipientCount.ShouldBe(HostWhisperQuotaService.UniqueRecipientLimit);
        limit.Status.Exhausted.ShouldBeTrue();
        existing
            .Match(
                success => success,
                _ => throw new InvalidOperationException("Expected an existing recipient.")
            )
            .ShouldBeOfType<WhisperQuotaReservation.ExistingRecipient>()
            .Status.Exhausted.ShouldBeTrue();
    }

    [Test]
    public async Task Delivery_Construction_DefersTokenQuotaAndHelixIo()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);

        var delivery = harness.Sender.Deliver(harness.Source(), "your balance is 10");
        var statusBeforeExecution = await harness.Quota.GetStatusAsync(
            harness.HostId,
            "custom-id",
            CancellationToken.None
        );

        harness.Http.ValidationRequestCount.ShouldBe(0);
        harness.Http.WhisperRequestCount.ShouldBe(0);
        statusBeforeExecution.RecipientCount.ShouldBe(0);

        var result = await delivery.ExecuteAsync(CancellationToken.None);

        var receipt = result.Match(
            receipt => receipt,
            _ => throw new InvalidOperationException("Expected private delivery success.")
        );
        receipt.ShouldBe(new PrivateDeliveryReceipt());
        harness.Http.ValidationRequestCount.ShouldBe(1);
        harness.Http.WhisperRequestCount.ShouldBe(1);
        harness.Logger.Entries.ShouldBeEmpty();
        harness.Chat.Messages.ShouldBeEmpty();
        (await harness.Quota.GetStatusAsync(
                harness.HostId,
                "custom-id",
                CancellationToken.None
            ))
            .RecipientCount.ShouldBe(1);
    }

    [Test]
    public async Task DisabledWhispers_Delivering_ReturnsDisabledWithoutTokenOrHelixIo()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            whisperResponsesEnabled: false
        );

        var error = await SendPrivateFailureAsync(harness, harness.Source());

        error.ShouldBeOfType<PrivateDeliveryError.Disabled>();
        harness.Http.ValidationRequestCount.ShouldBe(0);
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task InvalidBotToken_Delivering_ReturnsSenderIdentityUnavailable()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            validationAccepted: false
        );

        var error = await SendPrivateFailureAsync(harness, harness.Source());

        error.ShouldBeOfType<PrivateDeliveryError.SenderIdentityUnavailable>();
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task MissingRecipient_Delivering_ReturnsRecipientUnavailable()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            usersJson: """{"data":[]}"""
        );

        var error = await SendPrivateFailureAsync(
            harness,
            harness.Source(includeUserId: false)
        );

        error.ShouldBeOfType<PrivateDeliveryError.RecipientUnavailable>();
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task SelfRecipient_Delivering_ReturnsSelfRecipientWithoutQuotaOrHelixIo()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);

        var error = await SendPrivateFailureAsync(
            harness,
            harness.Source(userId: "custom-id")
        );

        error.ShouldBeOfType<PrivateDeliveryError.SelfRecipient>();
        harness.Http.WhisperRequestCount.ShouldBe(0);
        (await harness.Quota.GetStatusAsync(
                harness.HostId,
                "custom-id",
                CancellationToken.None
            ))
            .RecipientCount.ShouldBe(0);
    }

    [Test]
    public async Task ExhaustedQuota_Delivering_ReturnsQuotaExceededWithoutHelixIo()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);
        for (var index = 0; index < HostWhisperQuotaService.UniqueRecipientLimit; index++)
        {
            _ = await harness.Quota
                .ReserveRecipient(
                    harness.HostId,
                    "custom-id",
                    $"recipient-{index}",
                    $"viewer{index}"
                )
                .ExecuteAsync(CancellationToken.None);
        }

        var error = await SendPrivateFailureAsync(harness, harness.Source());

        var quota = error.ShouldBeOfType<PrivateDeliveryError.QuotaExceeded>();
        quota.Status.Exhausted.ShouldBeTrue();
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task TwitchRateLimit_Delivering_ReturnsRateLimitedAndExhaustsQuota()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.TooManyRequests,
            whisperBody: "sensitive provider response"
        );

        var error = await SendPrivateFailureAsync(
            harness,
            harness.Source(),
            "sensitive private message"
        );

        var rateLimited = error.ShouldBeOfType<PrivateDeliveryError.RateLimited>();
        rateLimited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rateLimited.ToString().ShouldNotContain("sensitive");
        (await harness.Quota.GetStatusAsync(
                harness.HostId,
                "custom-id",
                CancellationToken.None
            ))
            .Exhausted.ShouldBeTrue();
    }

    [Test]
    public async Task RejectedWhisper_Delivering_ReturnsRedactedStatus()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.BadRequest,
            whisperBody: "sensitive provider response"
        );

        var error = await SendPrivateFailureAsync(
            harness,
            harness.Source(),
            "sensitive private message"
        );

        var rejected = error.ShouldBeOfType<PrivateDeliveryError.Rejected>();
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        rejected.ToString().ShouldNotContain("sensitive");
    }

    [Test]
    public async Task RecipientLookupTransportFailure_Delivering_ReturnsTransientWithCause()
    {
        var cause = new HttpRequestException("sensitive lookup failure");
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            usersException: cause
        );

        var error = await SendPrivateFailureAsync(
            harness,
            harness.Source(includeUserId: false)
        );

        var transient = error.ShouldBeOfType<PrivateDeliveryError.Transient>();
        transient.Cause.ShouldBeSameAs(cause);
        transient.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
        transient.ToString().ShouldNotContain("sensitive lookup failure");
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task WhisperTransportFailure_Delivering_ReturnsAmbiguousWithCause()
    {
        var cause = new IOException("sensitive send failure");
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            whisperException: cause
        );

        var error = await SendPrivateFailureAsync(harness, harness.Source());

        var ambiguous = error.ShouldBeOfType<PrivateDeliveryError.Ambiguous>();
        ambiguous.Cause.ShouldBeSameAs(cause);
        ambiguous.FailureType.ShouldBe(typeof(IOException).FullName);
        ambiguous.ToString().ShouldNotContain("sensitive send failure");
    }

    [Test]
    public async Task UnexpectedPreparationFailure_Delivering_PreservesRedactedCause()
    {
        var cause = new SensitiveWhisperException("sensitive unexpected failure");
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            usersException: cause
        );

        var error = await SendPrivateFailureAsync(
            harness,
            harness.Source(includeUserId: false)
        );

        var unexpected = error.ShouldBeOfType<PrivateDeliveryError.Unexpected>();
        unexpected.Cause.ShouldBeSameAs(cause);
        unexpected.FailureType.ShouldBe(typeof(SensitiveWhisperException).FullName);
        unexpected.ToString().ShouldNotContain("sensitive unexpected failure");
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task CallerCancellation_Delivering_PropagatesCancellation()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = async () =>
            await harness
                .Sender.Deliver(harness.Source(), "message")
                .ExecuteAsync(cancellation.Token);

        await action.ShouldThrowAsync<OperationCanceledException>();
        harness.Http.ValidationRequestCount.ShouldBe(0);
        harness.Http.WhisperRequestCount.ShouldBe(0);
        harness.Logger.Entries.ShouldBeEmpty();
        harness.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task CallerCancellationDuringWhisper_Delivering_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.NoContent,
            cancelOnWhisper: cancellation
        );

        var action = async () =>
            await harness
                .Sender.Deliver(harness.Source(), "message")
                .ExecuteAsync(cancellation.Token);

        await action.ShouldThrowAsync<OperationCanceledException>();
        harness.Http.WhisperRequestCount.ShouldBe(1);
        harness.Logger.Entries.ShouldBeEmpty();
        harness.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task SuccessfulPrivateResponse_SendingCommandResponse_DoesNotUsePublicDelivery()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);

        await harness.Sender.SendAsync(
            harness.Source(),
            TwitchCommandResponse.Whisper("private response"),
            CancellationToken.None
        );

        harness.Http.WhisperRequestCount.ShouldBe(1);
        harness.Logger.Entries.ShouldBeEmpty();
        harness.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task PublicTarget_SendingCommandResponse_UsesExistingPublicDeliveryPath()
    {
        await using var harness = await WhisperHarness.CreateAsync(HttpStatusCode.NoContent);

        await harness.Sender.SendAsync(
            harness.Source(),
            TwitchCommandResponse.Chat("public response"),
            CancellationToken.None
        );

        harness.Chat.Messages.ShouldBe([new SentChatMessage("streamer", "public response")]);
        harness.Logger.Entries.ShouldBeEmpty();
        harness.Http.ValidationRequestCount.ShouldBe(0);
        harness.Http.WhisperRequestCount.ShouldBe(0);
    }

    [Test]
    public async Task PrivateFailure_Logging_RecordsOnlyStructuredRedactedContext()
    {
        await using var harness = await WhisperHarness.CreateAsync(
            HttpStatusCode.BadRequest,
            whisperBody: "sensitive provider response"
        );

        await harness.Sender.SendAsync(
            harness.Source(),
            TwitchCommandResponse.Whisper("sensitive private response"),
            CancellationToken.None
        );

        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain("Private command response delivery");
        entry.Message.ShouldContain("streamer");
        entry.Message.ShouldContain(nameof(PrivateDeliveryError.Rejected));
        entry.Message.ShouldNotContain("sensitive provider response");
        entry.Message.ShouldNotContain("access-token");
        entry.Message.ShouldNotContain("sensitive private response");
        entry.Message.ShouldNotContain("viewer");
        entry.Properties["HostChannel"].ShouldBe("streamer");
        entry.Properties["Classification"].ShouldBe(
            nameof(PrivateDeliveryError.Rejected)
        );
    }

    private static HostWhisperQuotaService CreateQuota(SqliteBlokeBotDbFactory dbFactory)
    {
        return new(
            dbFactory,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero))
        );
    }

    private static async Task<PrivateDeliveryError> SendPrivateFailureAsync(
        WhisperHarness harness,
        TwitchChatMessage source,
        string message = "private response"
    )
    {
        var result = await harness
            .Sender.Deliver(source, message)
            .ExecuteAsync(CancellationToken.None);
        return result.Match(
            _ => throw new InvalidOperationException("Expected private delivery failure."),
            error => error
        );
    }

    private static TwitchBotSettings BotOptions()
    {
        return TwitchBotSettings.FromOptions(
            new TwitchBotOptions
            {
                Identity = new TwitchBotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://localhost:7107/oauth/callback",
                    Scopes = [
                        "chat:read",
                        "chat:edit",
                        TwitchScopes.UserReadModeratedChannels,
                    ],
                },
            }
        );
    }

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

    private static async Task SeedCustomBotAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        bool whisperResponsesEnabled
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.HostBotAccountSettings.Add(
            new HostBotAccountSettings
            {
                HostId = hostId,
                OverrideEnabled = true,
                WhisperResponsesEnabled = whisperResponsesEnabled,
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

    private sealed class WhisperHarness : IAsyncDisposable
    {
        private WhisperHarness(
            SqliteBlokeBotDbFactory dbFactory,
            int hostId,
            WhisperHttpClientFactory http,
            RecordingChatSender chat,
            HostWhisperQuotaService quota,
            RecordingLogger<HostWhisperCommandResponseSender> logger,
            HostWhisperCommandResponseSender sender
        )
        {
            _dbFactory = dbFactory;
            HostId = hostId;
            Http = http;
            Chat = chat;
            Quota = quota;
            Logger = logger;
            Sender = sender;
        }

        private readonly SqliteBlokeBotDbFactory _dbFactory;

        internal int HostId { get; }

        internal WhisperHttpClientFactory Http { get; }

        internal RecordingChatSender Chat { get; }

        internal HostWhisperQuotaService Quota { get; }

        internal RecordingLogger<HostWhisperCommandResponseSender> Logger { get; }

        internal HostWhisperCommandResponseSender Sender { get; }

        internal static async Task<WhisperHarness> CreateAsync(
            HttpStatusCode whisperStatus,
            string? whisperBody = null,
            string usersJson = """{"data":[{"id":"viewer-id","login":"viewer","display_name":"Viewer","profile_image_url":""}]}""",
            Exception? usersException = null,
            Exception? whisperException = null,
            bool validationAccepted = true,
            bool whisperResponsesEnabled = true,
            CancellationTokenSource? cancelOnWhisper = null
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
            var oauth = new TwitchOAuthApiClient(http);
            var helixUsers = new TwitchHelixApiClient(http);
            var hostBotAccounts = new HostBotAccountAuthorizationService(
                dbFactory,
                new HostBotAccountOAuthService(options, oauth, helixUsers),
                oauth,
                helixUsers,
                new TwitchTokenStatusService(
                    new ServiceCollection().BuildServiceProvider(),
                    oauth
                ),
                new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
                options
            );
            var chat = new RecordingChatSender();
            var quota = CreateQuota(dbFactory);
            var logger = new RecordingLogger<HostWhisperCommandResponseSender>();
            var sender = new HostWhisperCommandResponseSender(
                chat,
                hostBotAccounts,
                quota,
                helixUsers,
                new TwitchHelixChatClient(http, options.Identity, helixUsers),
                dbFactory,
                options.Identity,
                logger
            );
            return new(dbFactory, hostId, http, chat, quota, logger, sender);
        }

        internal TwitchChatMessage Source(
            bool includeUserId = true,
            string userId = "viewer-id"
        )
        {
            IReadOnlyDictionary<string, string> tags = includeUserId
                ? new Dictionary<string, string> { ["user-id"] = userId }
                : new Dictionary<string, string>();
            return new("viewer", "streamer", "!points", "raw", tags);
        }

        public async ValueTask DisposeAsync()
        {
            await _dbFactory.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed record SentChatMessage(string Channel, string Message);

    private sealed class RecordingChatSender : ITwitchChatMessageSender
    {
        internal List<SentChatMessage> Messages { get; } = [];

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
        string? whisperBody,
        string usersJson,
        Exception? usersException,
        Exception? whisperException,
        bool validationAccepted,
        CancellationTokenSource? cancelOnWhisper
    ) : IHttpClientFactory
    {
        private readonly Handler _handler = new(
            whisperStatus,
            whisperBody,
            usersJson,
            usersException,
            whisperException,
            validationAccepted,
            cancelOnWhisper
        );

        internal int ValidationRequestCount => _handler.ValidationRequestCount;

        internal int WhisperRequestCount => _handler.WhisperRequestCount;

        public HttpClient CreateClient(string name)
        {
            return new(_handler, disposeHandler: false);
        }

        private sealed class Handler(
            HttpStatusCode whisperStatus,
            string? whisperBody,
            string usersJson,
            Exception? usersException,
            Exception? whisperException,
            bool validationAccepted,
            CancellationTokenSource? cancelOnWhisper
        ) : HttpMessageHandler
        {
            internal int ValidationRequestCount { get; private set; }

            internal int WhisperRequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return request.RequestUri?.AbsolutePath switch
                {
                    "/oauth2/validate" => Task.FromResult(ValidationResponse(request)),
                    "/helix/users" when usersException is not null =>
                        Task.FromException<HttpResponseMessage>(usersException),
                    "/helix/users" => Task.FromResult(JsonResponse(usersJson)),
                    "/helix/whispers" when whisperException is not null =>
                        FailedWhisper(whisperException),
                    "/helix/whispers" when cancelOnWhisper is { } cancellation =>
                        CancelledWhisper(cancellation, cancellationToken),
                    "/helix/whispers" => Task.FromResult(WhisperResponse()),
                    _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
                };
            }

            private HttpResponseMessage WhisperResponse()
            {
                WhisperRequestCount++;
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
                WhisperRequestCount++;
                return Task.FromException<HttpResponseMessage>(exception);
            }

            private Task<HttpResponseMessage> CancelledWhisper(
                CancellationTokenSource cancellation,
                CancellationToken cancellationToken
            )
            {
                WhisperRequestCount++;
                cancellation.Cancel();
                return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
            }

            private HttpResponseMessage ValidationResponse(HttpRequestMessage request)
            {
                ValidationRequestCount++;
                return validationAccepted
                    && request.Headers.Authorization?.Parameter == "override-whisper-token"
                    ? JsonResponse(
                        """{"user_id":"custom-id","login":"custombot","scopes":["user:manage:whispers"]}"""
                    )
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            private static HttpResponseMessage JsonResponse(string json)
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }
        }
    }

    private sealed class SensitiveWhisperException(string message) : Exception(message);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(
                new(logLevel, formatter(state, exception), exception, properties)
            );
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties
    );
}
