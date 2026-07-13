using System.Net;
using System.Text;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Auth.Tests;

public sealed class TwitchTokenStatusServiceTests
{
    [Test]
    public async Task UnavailableStatusSource_ExecutingStatus_ReturnsUnavailableWithRequiredScopes()
    {
        var source = new UnavailableTwitchTokenStatusSource();

        var result = await source
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TwitchTokenStatus.Unavailable>();
        status.Reason.ShouldBe(TwitchAccessTokenUnavailableReason.MissingRefreshToken);
        status.RequiredScopes.ShouldBe(["chat:read"]);
    }

    [Test]
    public async Task StatusInspection_BeforeExecution_DoesNotAcquireToken()
    {
        var provider = new RecordingTokenProvider("saved-token");
        var service = Service(
            provider,
            OAuthClient("""{"user_id":"123","login":"bot","scopes":["chat:read"]}""")
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
            OAuthClient("""{"user_id":"123","login":"bot","scopes":["chat:read"]}""")
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TwitchTokenStatus.Unavailable>();
        status.Reason.ShouldBe(TwitchAccessTokenUnavailableReason.MissingRefreshToken);
        status.RequiredScopes.ShouldBe(["chat:read"]);
    }

    [Test]
    public async Task RejectedAccessToken_InspectingStatus_ReturnsInvalidWithoutTokenPayload()
    {
        var service = Service(new RecordingTokenProvider("saved-token"), OAuthClient(null));

        var result = await service
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TwitchTokenStatus.Invalid>();
        status.RequiredScopes.ShouldBe(["chat:read"]);
        status.ToString().ShouldNotContain("saved-token");
    }

    [Test]
    public async Task ValidTokenWithRequiredScopes_InspectingStatus_ReturnsReady()
    {
        var service = Service(
            new RecordingTokenProvider("saved-token"),
            OAuthClient(
                """{"user_id":"123","login":"BotAccount","scopes":["chat:edit","chat:read"]}"""
            )
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read", "chat:edit"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TwitchTokenStatus.Ready>();
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
            OAuthClient("""{"user_id":"123","login":"bot","scopes":["chat:read"]}""")
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read", "chat:edit"])
            .ExecuteAsync(CancellationToken.None);

        var status = Success(result).ShouldBeOfType<TwitchTokenStatus.MissingScopes>();
        status.AccessToken.ShouldBe("saved-token");
        status.GrantedScopes.ShouldBe(["chat:read"]);
        status.Missing.ShouldBe(["chat:edit"]);
        status.ToString().ShouldNotContain("saved-token");
    }

    [Test]
    public async Task AcquisitionTransportFailure_InspectingStatus_ReturnsTypedError()
    {
        var service = Service(
            new ThrowingTokenProvider(new HttpRequestException("sensitive provider detail")),
            OAuthClient(null)
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var error = Error(result)
            .ShouldBeOfType<TwitchTokenStatusError.AcquisitionUnavailable>();
        error.Reason.ShouldBe(TwitchTokenStatusTransportFailureReason.RequestFailed);
        error.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
        error.RequiredScopesSnapshot.ShouldBe(["chat:read"]);
        error.ToString().ShouldNotContain("sensitive provider detail");
    }

    [Test]
    public async Task InvalidValidationPayload_InspectingStatus_ReturnsTypedError()
    {
        var service = Service(
            new RecordingTokenProvider("saved-token"),
            OAuthClient("not-json")
        );

        var result = await service
            .GetUserAccessTokenStatus(["chat:read"])
            .ExecuteAsync(CancellationToken.None);

        var error = Error(result)
            .ShouldBeOfType<TwitchTokenStatusError.ValidationUnavailable>();
        error.Reason.ShouldBe(TwitchTokenStatusTransportFailureReason.ResponseInvalid);
        error.FailureType.ShouldBe("System.Text.Json.JsonException");
        error.RequiredScopesSnapshot.ShouldBe(["chat:read"]);
        error.ToString().ShouldNotContain("saved-token");
        error.ToString().ShouldNotContain("not-json");
    }

    [Test]
    public async Task RequestedCancellation_InspectingStatus_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var service = Service(
            new CancellingTokenProvider(cancellation),
            OAuthClient(null)
        );

        var thrown = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await service
                .GetUserAccessTokenStatus(["chat:read"])
                .ExecuteAsync(cancellation.Token)
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
            new TwitchOAuthApiClient(
                new CancellingValidationHttpClientFactory(cancellation)
            )
        );

        var thrown = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await service
                .GetUserAccessTokenStatus(["chat:read"])
                .ExecuteAsync(cancellation.Token)
        );

        provider.CallCount.ShouldBe(1);
        thrown.CancellationToken.ShouldBe(cancellation.Token);
    }

    [Test]
    public async Task UnexpectedAcquisitionFailure_InspectingStatus_LogsRedactedContextAndEscalates()
    {
        const string SensitiveMessage = "token=provider-secret";
        var failure = new InvalidOperationException(SensitiveMessage);
        var logger = new RecordingLogger<TwitchTokenStatusService>();
        var service = Service(
            new ThrowingTokenProvider(failure),
            OAuthClient(null),
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
        entry.Properties["Operation"].ShouldBe("acquisition");
        entry.Properties["FailureType"].ShouldBe(typeof(InvalidOperationException).FullName);
        entry.Properties["{OriginalFormat}"].ShouldBe(
            "Unexpected Twitch token status {Operation} failure of type {FailureType} was escalated."
        );
        entry.Message.ShouldNotContain(SensitiveMessage);
    }

    [Test]
    public async Task UnexpectedValidationFailure_InspectingStatus_LogsRedactedContextAndEscalates()
    {
        const string SensitiveMessage = "raw-response=provider-secret";
        var failure = new InvalidOperationException(SensitiveMessage);
        var logger = new RecordingLogger<TwitchTokenStatusService>();
        var service = Service(
            new RecordingTokenProvider("saved-token"),
            OAuthClient(null, failure),
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

    private static TwitchTokenStatusService Service(
        ITwitchAccessTokenProvider provider,
        TwitchOAuthApiClient oauth,
        ILogger<TwitchTokenStatusService>? logger = null
    )
    {
        return new(provider, oauth, logger ?? new RecordingLogger<TwitchTokenStatusService>());
    }

    private static TwitchOAuthApiClient OAuthClient(
        string? validationJson,
        Exception? exception = null
    )
    {
        return new(new StatusHttpClientFactory(validationJson, exception));
    }

    private static TwitchTokenStatus Success(
        BlokeBot.Functional.Result<TwitchTokenStatus, TwitchTokenStatusError> result
    )
    {
        return result.Match(
            status => status,
            error => throw new InvalidOperationException(
                $"Expected token status success, received {error.GetType().Name}."
            )
        );
    }

    private static TwitchTokenStatusError Error(
        BlokeBot.Functional.Result<TwitchTokenStatus, TwitchTokenStatusError> result
    )
    {
        return result.Match(
            status => throw new InvalidOperationException(
                $"Expected token status error, received {status.GetType().Name}."
            ),
            error => error
        );
    }

    private sealed class RecordingTokenProvider(string accessToken) : ITwitchAccessTokenProvider
    {
        public int CallCount { get; private set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(accessToken);
        }
    }

    private sealed class ThrowingTokenProvider(Exception exception) : ITwitchAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromException<string>(exception);
        }
    }

    private sealed class CancellingTokenProvider(CancellationTokenSource cancellation)
        : ITwitchAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromCanceled<string>(cancellationToken);
        }
    }

    private sealed class UnavailableTokenProvider : ITwitchAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            throw new TwitchAccessTokenUnavailableException(
                TwitchAccessTokenUnavailableReason.MissingRefreshToken,
                TwitchAccessTokenUnavailableException.MissingRefreshTokenMessage
            );
        }
    }

    private sealed class StatusHttpClientFactory(
        string? validationJson,
        Exception? exception
    ) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new Handler(validationJson, exception), disposeHandler: false);
        }

        private sealed class Handler(string? validationJson, Exception? exception)
            : HttpMessageHandler
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
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
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

    private sealed class CancellingValidationHttpClientFactory(
        CancellationTokenSource cancellation
    ) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new Handler(cancellation), disposeHandler: false);
        }

        private sealed class Handler(CancellationTokenSource cancellation)
            : HttpMessageHandler
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
