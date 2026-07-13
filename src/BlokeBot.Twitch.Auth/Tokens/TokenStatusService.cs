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
        string accessToken;
        try
        {
            accessToken = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AccessTokenUnavailableException exception)
        {
            return Success(new TokenStatus.Unavailable(exception.Reason, requiredScopes));
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

        try
        {
            var validation = await transport
                .ValidateTokenAsync(accessToken, cancellationToken)
                .ConfigureAwait(false);
            if (validation is null)
            {
                return Success(new TokenStatus.Invalid(requiredScopes));
            }

            var grantedScopes = ImmutableArray.CreateRange(
                ScopeSet.NormalizeMany(validation.Scopes)
            );
            var missingScopes = ImmutableArray.CreateRange(
                ScopeSet.Missing(grantedScopes, requiredScopes)
            );
            return missingScopes.IsEmpty
                ? Success(
                    new TokenStatus.Ready(accessToken, validation, requiredScopes, grantedScopes)
                )
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
    )
    {
        return Result<TokenStatus, TokenStatusError>.Error(
            new TokenStatusError.AcquisitionUnavailable(
                reason,
                FailureType(exception),
                requiredScopes
            )
        );
    }

    private static Result<TokenStatus, TokenStatusError> ValidationError(
        TokenStatusTransportFailureReason reason,
        Exception exception,
        ImmutableArray<string> requiredScopes
    )
    {
        return Result<TokenStatus, TokenStatusError>.Error(
            new TokenStatusError.ValidationUnavailable(
                reason,
                FailureType(exception),
                requiredScopes
            )
        );
    }

    private static Result<TokenStatus, TokenStatusError> Success(TokenStatus status)
    {
        return Result<TokenStatus, TokenStatusError>.Success(status);
    }

    private void LogUnexpectedFailure(string operation, Exception exception)
    {
        log.LogError(
            "Unexpected Twitch token status {Operation} failure of type {FailureType} was escalated.",
            operation,
            FailureType(exception)
        );
    }

    private static string FailureType(Exception exception)
    {
        return exception.GetType().FullName ?? exception.GetType().Name;
    }
}
