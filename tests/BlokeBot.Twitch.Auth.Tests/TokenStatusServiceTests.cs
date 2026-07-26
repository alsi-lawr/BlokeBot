using System.Net;
using System.Text;
using BlokeBot.Functional;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Auth.Tests;

public sealed class TokenStatusServiceTests
{
    [Test]
    public async Task UnavailableStatusSource_ExecutingStatus_ReturnsUnavailableWithRequiredScopes()
    {
        var source = new UnavailableTokenStatusSource();

        var result = await source
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TokenStatus.Unavailable>();
        status.Reason.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
        status.RequiredScopes.ShouldBe(["chat:read"]);
    }

    [Test]
    public async Task StatusInspection_BeforeExecution_DoesNotAcquireToken()
    {
        var provider = new RecordingTokenProvider("saved-token");
        var service = Service(
            provider,
            Transport("""{"user_id":"123","login":"bot","scopes":["chat:read"]}""")
        );

        var inspection = service.GetUserAccessTokenStatus(["chat:read"]);

        provider.CallCount.ShouldBe(0);
        await inspection.ExecuteAsync(CancellationToken.None);
        provider.CallCount.ShouldBe(1);
    }

    [Test]
    public async Task UnavailableAccessToken_InspectingStatus_ReturnsUnavailable()
    {
        var service = Service(
            new UnavailableTokenProvider(),
            Transport("""{"user_id":"123","login":"bot","scopes":["chat:read"]}""")
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TokenStatus.Unavailable>();
        status.Reason.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
        status.RequiredScopes.ShouldBe(["chat:read"]);
    }

    [Test]
    public async Task RejectedAccessToken_InspectingStatus_ReturnsInvalidWithoutTokenPayload()
    {
        var service = Service(new RecordingTokenProvider("saved-token"), Transport(null));

        var result = await service
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TokenStatus.Invalid>();
        status.RequiredScopes.ShouldBe(["chat:read"]);
        status.ToString().ShouldNotContain("saved-token");
    }

    [Test]
    public async Task ProviderRateLimit_InspectingStatus_ReturnsTransportError()
    {
        var service = Service(
            new RecordingTokenProvider("saved-token"),
            Transport(null, rejectionStatus: HttpStatusCode.TooManyRequests)
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var error = Error(result).ShouldBeOfType<TokenStatusError.ValidationUnavailable>();
        error.Reason.ShouldBe(TokenStatusTransportFailureReason.RequestFailed);
        error.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
    }

    [Test]
    public async Task ValidTokenWithRequiredScopes_InspectingStatus_ReturnsReady()
    {
        var service = Service(
            new RecordingTokenProvider("saved-token"),
            Transport(
                """{"user_id":"123","login":"BotAccount","scopes":["chat:edit","chat:read"]}"""
            )
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read", "chat:edit"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TokenStatus.Ready>();
        status.AccessToken.ShouldBe("saved-token");
        status.Validation.Login.ShouldBe("botaccount");
        status.RequiredScopes.ShouldBe(["chat:edit", "chat:read"]);
        status.GrantedScopes.ShouldBe(["chat:edit", "chat:read"]);
        status.ToString().ShouldNotContain("saved-token");
    }

    [Test]
    public async Task ValidTokenMissingScope_InspectingStatus_ReturnsMissingScopes()
    {
        var service = Service(
            new RecordingTokenProvider("saved-token"),
            Transport("""{"user_id":"123","login":"bot","scopes":["chat:read"]}""")
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read", "chat:edit"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TokenStatus.MissingScopes>();
        status.AccessToken.ShouldBe("saved-token");
        status.GrantedScopes.ShouldBe(["chat:read"]);
        status.Missing.ShouldBe(["chat:edit"]);
        status.ToString().ShouldNotContain("saved-token");
    }

    [Test]
    public async Task ValidTokenWithNoGrantedScopes_InspectingStatus_ReturnsMissingScopes()
    {
        var service = Service(
            new RecordingTokenProvider("saved-token"),
            Transport("""{"user_id":"123","login":"bot","scopes":[]}""")
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TokenStatus.MissingScopes>();
        status.GrantedScopes.ShouldBeEmpty();
        status.Missing.ShouldBe(["chat:read"]);
    }

    [Test]
    public async Task AcquisitionTransportFailure_InspectingStatus_ReturnsTypedError()
    {
        var service = Service(
            new ThrowingTokenProvider(new HttpRequestException("sensitive provider detail")),
            Transport(null)
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var error = Error(result).ShouldBeOfType<TokenStatusError.AcquisitionUnavailable>();
        error.Reason.ShouldBe(TokenStatusTransportFailureReason.RequestFailed);
        error.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
        error.RequiredScopesSnapshot.ShouldBe(["chat:read"]);
        error.ToString().ShouldNotContain("sensitive provider detail");
    }

    [Test]
    public async Task InvalidValidationPayload_InspectingStatus_ReturnsTypedError()
    {
        var service = Service(new RecordingTokenProvider("saved-token"), Transport("not-json"));

        var result = await service
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var error = Error(result).ShouldBeOfType<TokenStatusError.ValidationUnavailable>();
        error.Reason.ShouldBe(TokenStatusTransportFailureReason.ResponseInvalid);
        error.FailureType.ShouldBe("System.Text.Json.JsonException");
        error.RequiredScopesSnapshot.ShouldBe(["chat:read"]);
        error.ToString().ShouldNotContain("saved-token");
        error.ToString().ShouldNotContain("not-json");
    }

    [Test]
    public async Task RequestedCancellation_InspectingStatus_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var service = Service(new CancellingTokenProvider(cancellation), Transport(null));

        var thrown = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await service.GetUserAccessTokenStatus(["chat:read"]).ExecuteAsync(cancellation.Token)
        );

        thrown.CancellationToken.ShouldBe(cancellation.Token);
    }

    [Test]
    public async Task RequestedCancellationDuringValidation_InspectingStatus_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new RecordingTokenProvider("saved-token");
        var service = Service(
            provider,
            new OAuthTransport(new CancellingValidationHttpClientFactory(cancellation),
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default)
        );

        var thrown = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await service.GetUserAccessTokenStatus(["chat:read"]).ExecuteAsync(cancellation.Token)
        );

        provider.CallCount.ShouldBe(1);
        thrown.CancellationToken.ShouldBe(cancellation.Token);
    }

    [Test]
    public async Task UnexpectedAcquisitionFailure_InspectingStatus_LogsRedactedContextAndEscalates()
    {
        const string SensitiveMessage = "token=provider-secret";
        var failure = new InvalidOperationException(SensitiveMessage);
        var logger = new RecordingLogger<TokenStatusService>();
        var service = Service(new ThrowingTokenProvider(failure), Transport(null), logger);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await service
                .GetUserAccessTokenStatus(["chat:read"])
                .ExecuteAsync(CancellationToken.None)
        );

        thrown.ShouldBeSameAs(failure);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeNull();
        entry.Properties["Operation"].ShouldBe("acquisition");
        entry.Properties["FailureType"].ShouldBe(typeof(InvalidOperationException).FullName);
        entry
            .Properties["{OriginalFormat}"]
            .ShouldBe(
                "Unexpected Twitch token status {Operation} failure of type {FailureType} was escalated."
            );
        entry.Message.ShouldNotContain(SensitiveMessage);
    }

    [Test]
    public async Task UnexpectedValidationFailure_InspectingStatus_LogsRedactedContextAndEscalates()
    {
        const string SensitiveMessage = "raw-response=provider-secret";
        var failure = new InvalidOperationException(SensitiveMessage);
        var logger = new RecordingLogger<TokenStatusService>();
        var service = Service(
            new RecordingTokenProvider("saved-token"),
            Transport(null, failure),
            logger
        );

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await service
                .GetUserAccessTokenStatus(["chat:read"])
                .ExecuteAsync(CancellationToken.None)
        );

        thrown.ShouldBeSameAs(failure);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeNull();
        entry.Properties["Operation"].ShouldBe("validation");
        entry.Properties["FailureType"].ShouldBe(typeof(InvalidOperationException).FullName);
        entry.Message.ShouldNotContain("saved-token");
        entry.Message.ShouldNotContain(SensitiveMessage);
    }

    private static TokenStatusService Service(
        IAccessTokenProvider provider,
        OAuthTransport transport,
        ILogger<TokenStatusService>? logger = null
    )
    {
        return new(provider, transport, logger ?? new RecordingLogger<TokenStatusService>());
    }

    private static OAuthTransport Transport(
        string? validationJson,
        Exception? exception = null,
        HttpStatusCode rejectionStatus = HttpStatusCode.Unauthorized
    )
    {
        return new(
            new StatusHttpClientFactory(validationJson, exception, rejectionStatus),
            TwitchEndpointPolicy.Default
        );
    }

    private static TokenStatus Success(
        BlokeBot.Functional.Result<TokenStatus, TokenStatusError> result
    )
    {
        return result.Match(
            status => status,
            error =>
                throw new InvalidOperationException(
                    $"Expected token status success, received {error.GetType().Name}."
                )
        );
    }

    private static TokenStatusError Error(
        BlokeBot.Functional.Result<TokenStatus, TokenStatusError> result
    )
    {
        return result.Match(
            status =>
                throw new InvalidOperationException(
                    $"Expected token status error, received {status.GetType().Name}."
                ),
            error => error
        );
    }

    private sealed class RecordingTokenProvider(string accessToken) : IAccessTokenProvider
    {
        public int CallCount { get; private set; }

        public IO<string, AccessTokenUnavailableReason> GetAccessToken()
        {
            return IO<string, AccessTokenUnavailableReason>.Create(_ =>
            {
                CallCount++;
                return ValueTask.FromResult(
                    Result<string, AccessTokenUnavailableReason>.Success(accessToken)
                );
            });
        }
    }

    private sealed class ThrowingTokenProvider(Exception exception) : IAccessTokenProvider
    {
        public IO<string, AccessTokenUnavailableReason> GetAccessToken()
        {
            return IO<string, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromException<Result<string, AccessTokenUnavailableReason>>(exception)
            );
        }
    }

    private sealed class CancellingTokenProvider(CancellationTokenSource cancellation)
        : IAccessTokenProvider
    {
        public IO<string, AccessTokenUnavailableReason> GetAccessToken()
        {
            return IO<string, AccessTokenUnavailableReason>.Create(cancellationToken =>
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled<Result<string, AccessTokenUnavailableReason>>(
                    cancellationToken
                );
            });
        }
    }

    private sealed class UnavailableTokenProvider : IAccessTokenProvider
    {
        public IO<string, AccessTokenUnavailableReason> GetAccessToken()
        {
            return IO<string, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<string, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.MissingRefreshToken
                    )
                )
            );
        }
    }

    private sealed class StatusHttpClientFactory(
        string? validationJson,
        Exception? exception,
        HttpStatusCode rejectionStatus
    ) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(
                new Handler(validationJson, exception, rejectionStatus),
                disposeHandler: false
            );
        }

        private sealed class Handler(
            string? validationJson,
            Exception? exception,
            HttpStatusCode rejectionStatus
        ) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                if (exception is not null)
                {
                    return Task.FromException<HttpResponseMessage>(exception);
                }

                if (validationJson is null)
                {
                    return Task.FromResult(new HttpResponseMessage(rejectionStatus));
                }

                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            validationJson,
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                );
            }
        }
    }

    private sealed class CancellingValidationHttpClientFactory(CancellationTokenSource cancellation)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new Handler(cancellation), disposeHandler: false);
        }

        private sealed class Handler(CancellationTokenSource cancellation) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                cancellation.Cancel();
                return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
            }
        }
    }

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        public List<LogEntry> Entries { get; } = [];

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
            Entries.Add(new(logLevel, formatter(state, exception), exception, properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties
    );
}
