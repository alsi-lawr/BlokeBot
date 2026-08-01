using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Functional;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Auth;

public sealed class TokenStatusService(
    IAccessTokenProvider tokens,
    OAuthTransport transport,
    ILogger<TokenStatusService> log
) : ITokenStatusSource
{
    public IO<TokenStatus, TokenStatusError> GetUserAccessTokenStatus(
        IEnumerable<string?> requiredScopes
    )
    {
        ArgumentNullException.ThrowIfNull(requiredScopes);
        var required = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(requiredScopes));
        return IO<TokenStatus, TokenStatusError>.Create(cancellationToken =>
            InspectAsync(required, cancellationToken)
        );
    }

    private async ValueTask<Result<TokenStatus, TokenStatusError>> InspectAsync(
        ImmutableArray<string> requiredScopes,
        CancellationToken cancellationToken
    )
    {
        Result<string, AccessTokenUnavailableReason> accessToken;
        try
        {
            accessToken = await tokens
                .GetAccessToken()
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return AcquisitionError(
                TokenStatusTransportFailureReason.RequestFailed,
                exception,
                requiredScopes
            );
        }
        catch (IOException exception)
        {
            return AcquisitionError(
                TokenStatusTransportFailureReason.RequestFailed,
                exception,
                requiredScopes
            );
        }
        catch (JsonException exception)
        {
            return AcquisitionError(
                TokenStatusTransportFailureReason.ResponseInvalid,
                exception,
                requiredScopes
            );
        }
        catch (TimeoutException exception)
        {
            return AcquisitionError(
                TokenStatusTransportFailureReason.TimedOut,
                exception,
                requiredScopes
            );
        }
        catch (OperationCanceledException exception)
        {
            return AcquisitionError(
                TokenStatusTransportFailureReason.TimedOut,
                exception,
                requiredScopes
            );
        }
        catch (Exception exception)
        {
            LogUnexpectedFailure("acquisition", exception);
            throw;
        }

        return await accessToken
            .Match(
                token => ValidateAsync(token, requiredScopes, cancellationToken),
                reason =>
                    ValueTask.FromResult(
                        Success(new TokenStatus.Unavailable(reason, requiredScopes))
                    )
            )
            .ConfigureAwait(false);
    }

    private async ValueTask<Result<TokenStatus, TokenStatusError>> ValidateAsync(
        string accessToken,
        ImmutableArray<string> requiredScopes,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var validation = await transport
                .ValidateTokenAsync(accessToken, cancellationToken)
                .ConfigureAwait(false);
            return validation.Match(
                validated =>
                    StatusFromValidation(accessToken, validated.Validation, requiredScopes),
                _ => Success(new TokenStatus.Invalid(requiredScopes))
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return ValidationError(
                TokenStatusTransportFailureReason.RequestFailed,
                exception,
                requiredScopes
            );
        }
        catch (IOException exception)
        {
            return ValidationError(
                TokenStatusTransportFailureReason.RequestFailed,
                exception,
                requiredScopes
            );
        }
        catch (JsonException exception)
        {
            return ValidationError(
                TokenStatusTransportFailureReason.ResponseInvalid,
                exception,
                requiredScopes
            );
        }
        catch (NotSupportedException exception)
        {
            return ValidationError(
                TokenStatusTransportFailureReason.ResponseInvalid,
                exception,
                requiredScopes
            );
        }
        catch (TimeoutException exception)
        {
            return ValidationError(
                TokenStatusTransportFailureReason.TimedOut,
                exception,
                requiredScopes
            );
        }
        catch (OperationCanceledException exception)
        {
            return ValidationError(
                TokenStatusTransportFailureReason.TimedOut,
                exception,
                requiredScopes
            );
        }
        catch (Exception exception)
        {
            LogUnexpectedFailure("validation", exception);
            throw;
        }
    }

    private static Result<TokenStatus, TokenStatusError> AcquisitionError(
        TokenStatusTransportFailureReason reason,
        Exception exception,
        ImmutableArray<string> requiredScopes
    ) =>
        Result<TokenStatus, TokenStatusError>.Error(
            new TokenStatusError.AcquisitionUnavailable(
                reason,
                FailureType(exception),
                requiredScopes
            )
        );

    private static Result<TokenStatus, TokenStatusError> StatusFromValidation(
        string accessToken,
        TokenValidation validation,
        ImmutableArray<string> requiredScopes
    )
    {
        var grantedScopes = ImmutableArray.CreateRange(validation.Scopes);
        var missingScopes = ImmutableArray.CreateRange(
            ScopeSet.Missing(grantedScopes, requiredScopes)
        );
        return missingScopes.IsEmpty
            ? Success(new TokenStatus.Ready(accessToken, validation, requiredScopes, grantedScopes))
            : Success(
                new TokenStatus.MissingScopes(
                    accessToken,
                    validation,
                    requiredScopes,
                    grantedScopes,
                    missingScopes
                )
            );
    }

    private static Result<TokenStatus, TokenStatusError> ValidationError(
        TokenStatusTransportFailureReason reason,
        Exception exception,
        ImmutableArray<string> requiredScopes
    ) =>
        Result<TokenStatus, TokenStatusError>.Error(
            new TokenStatusError.ValidationUnavailable(
                reason,
                FailureType(exception),
                requiredScopes
            )
        );

    private static Result<TokenStatus, TokenStatusError> Success(TokenStatus status) =>
        Result<TokenStatus, TokenStatusError>.Success(status);

    private void LogUnexpectedFailure(string operation, Exception exception) =>
        log.LogError(
            "Unexpected Twitch token status {Operation} failure of type {FailureType} was escalated.",
            operation,
            FailureType(exception)
        );

    private static string FailureType(Exception exception) =>
        exception.GetType().FullName ?? exception.GetType().Name;
}
