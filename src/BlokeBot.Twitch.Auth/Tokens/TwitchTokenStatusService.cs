using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Functional;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Auth;

public sealed class TwitchTokenStatusService(
    ITwitchAccessTokenProvider tokens,
    TwitchOAuthApiClient oauth,
    ILogger<TwitchTokenStatusService> log
) : ITwitchTokenStatusSource
{
    public IO<TwitchTokenStatus, TwitchTokenStatusError> GetUserAccessTokenStatus(
        IEnumerable<string?> requiredScopes
    )
    {
        ArgumentNullException.ThrowIfNull(requiredScopes);
        var required = ImmutableArray.CreateRange(TwitchScopeSet.NormalizeMany(requiredScopes));
        return IO<TwitchTokenStatus, TwitchTokenStatusError>.Create(cancellationToken =>
            InspectAsync(required, cancellationToken)
        );
    }

    private async ValueTask<Result<TwitchTokenStatus, TwitchTokenStatusError>> InspectAsync(
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
        catch (TwitchAccessTokenUnavailableException exception)
        {
            return Success(new TwitchTokenStatus.Unavailable(exception.Reason, requiredScopes));
        }
        catch (HttpRequestException exception)
        {
            return AcquisitionError(
                TwitchTokenStatusTransportFailureReason.RequestFailed,
                exception,
                requiredScopes
            );
        }
        catch (IOException exception)
        {
            return AcquisitionError(
                TwitchTokenStatusTransportFailureReason.RequestFailed,
                exception,
                requiredScopes
            );
        }
        catch (JsonException exception)
        {
            return AcquisitionError(
                TwitchTokenStatusTransportFailureReason.ResponseInvalid,
                exception,
                requiredScopes
            );
        }
        catch (TimeoutException exception)
        {
            return AcquisitionError(
                TwitchTokenStatusTransportFailureReason.TimedOut,
                exception,
                requiredScopes
            );
        }
        catch (OperationCanceledException exception)
        {
            return AcquisitionError(
                TwitchTokenStatusTransportFailureReason.TimedOut,
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
            var validation = await oauth
                .ValidateTokenAsync(accessToken, cancellationToken)
                .ConfigureAwait(false);
            if (validation is null)
            {
                return Success(new TwitchTokenStatus.Invalid(requiredScopes));
            }

            var grantedScopes = ImmutableArray.CreateRange(
                TwitchScopeSet.NormalizeMany(validation.Scopes)
            );
            var missingScopes = ImmutableArray.CreateRange(
                TwitchScopeSet.Missing(grantedScopes, requiredScopes)
            );
            return missingScopes.IsEmpty
                ? Success(
                    new TwitchTokenStatus.Ready(
                        accessToken,
                        validation,
                        requiredScopes,
                        grantedScopes
                    )
                )
                : Success(
                    new TwitchTokenStatus.MissingScopes(
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
                TwitchTokenStatusTransportFailureReason.RequestFailed,
                exception,
                requiredScopes
            );
        }
        catch (IOException exception)
        {
            return ValidationError(
                TwitchTokenStatusTransportFailureReason.RequestFailed,
                exception,
                requiredScopes
            );
        }
        catch (JsonException exception)
        {
            return ValidationError(
                TwitchTokenStatusTransportFailureReason.ResponseInvalid,
                exception,
                requiredScopes
            );
        }
        catch (NotSupportedException exception)
        {
            return ValidationError(
                TwitchTokenStatusTransportFailureReason.ResponseInvalid,
                exception,
                requiredScopes
            );
        }
        catch (TimeoutException exception)
        {
            return ValidationError(
                TwitchTokenStatusTransportFailureReason.TimedOut,
                exception,
                requiredScopes
            );
        }
        catch (OperationCanceledException exception)
        {
            return ValidationError(
                TwitchTokenStatusTransportFailureReason.TimedOut,
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

    private static Result<TwitchTokenStatus, TwitchTokenStatusError> AcquisitionError(
        TwitchTokenStatusTransportFailureReason reason,
        Exception exception,
        ImmutableArray<string> requiredScopes
    )
    {
        return Result<TwitchTokenStatus, TwitchTokenStatusError>.Error(
            new TwitchTokenStatusError.AcquisitionUnavailable(
                reason,
                FailureType(exception),
                requiredScopes
            )
        );
    }

    private static Result<TwitchTokenStatus, TwitchTokenStatusError> ValidationError(
        TwitchTokenStatusTransportFailureReason reason,
        Exception exception,
        ImmutableArray<string> requiredScopes
    )
    {
        return Result<TwitchTokenStatus, TwitchTokenStatusError>.Error(
            new TwitchTokenStatusError.ValidationUnavailable(
                reason,
                FailureType(exception),
                requiredScopes
            )
        );
    }

    private static Result<TwitchTokenStatus, TwitchTokenStatusError> Success(
        TwitchTokenStatus status
    )
    {
        return Result<TwitchTokenStatus, TwitchTokenStatusError>.Success(status);
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
