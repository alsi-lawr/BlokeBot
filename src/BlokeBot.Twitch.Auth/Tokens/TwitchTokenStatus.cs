using System.Collections.Immutable;
using System.Diagnostics;

namespace BlokeBot.Twitch.Auth;

public enum TwitchTokenStatusTransportFailureReason
{
    RequestFailed,
    ResponseInvalid,
    TimedOut,
}

public abstract record TwitchTokenStatusError
{
    private TwitchTokenStatusError() { }

    public ImmutableArray<string> RequiredScopes => Match(
        acquisition => acquisition.RequiredScopesSnapshot,
        validation => validation.RequiredScopesSnapshot
    );

    public sealed record AcquisitionUnavailable(
        TwitchTokenStatusTransportFailureReason Reason,
        string FailureType,
        ImmutableArray<string> RequiredScopesSnapshot
    ) : TwitchTokenStatusError;

    public sealed record ValidationUnavailable(
        TwitchTokenStatusTransportFailureReason Reason,
        string FailureType,
        ImmutableArray<string> RequiredScopesSnapshot
    ) : TwitchTokenStatusError;

    public TResult Match<TResult>(
        Func<AcquisitionUnavailable, TResult> acquisitionUnavailable,
        Func<ValidationUnavailable, TResult> validationUnavailable
    )
    {
        ArgumentNullException.ThrowIfNull(acquisitionUnavailable);
        ArgumentNullException.ThrowIfNull(validationUnavailable);

        return this switch
        {
            AcquisitionUnavailable error => acquisitionUnavailable(error),
            ValidationUnavailable error => validationUnavailable(error),
            _ => throw new UnreachableException("Unknown Twitch token status error case."),
        };
    }
}

public abstract record TwitchTokenStatus
{
    private TwitchTokenStatus() { }

    public sealed record Unknown(TwitchTokenStatusError Error) : TwitchTokenStatus;

    public sealed record Unavailable(
        TwitchAccessTokenUnavailableReason Reason,
        ImmutableArray<string> RequiredScopes
    ) : TwitchTokenStatus;

    public sealed record Invalid(ImmutableArray<string> RequiredScopes) : TwitchTokenStatus;

    public sealed record MissingScopes(
        string AccessToken,
        TwitchTokenValidation Validation,
        ImmutableArray<string> RequiredScopes,
        ImmutableArray<string> GrantedScopes,
        ImmutableArray<string> Missing
    ) : TwitchTokenStatus
    {
        public override string ToString()
        {
            return $"{nameof(TwitchTokenStatus)}.{nameof(MissingScopes)} {{ RequiredScopeCount = {RequiredScopes.Length}, GrantedScopeCount = {GrantedScopes.Length}, MissingScopeCount = {Missing.Length} }}";
        }
    }

    public sealed record Ready(
        string AccessToken,
        TwitchTokenValidation Validation,
        ImmutableArray<string> RequiredScopes,
        ImmutableArray<string> GrantedScopes
    ) : TwitchTokenStatus
    {
        public override string ToString()
        {
            return $"{nameof(TwitchTokenStatus)}.{nameof(Ready)} {{ RequiredScopeCount = {RequiredScopes.Length}, GrantedScopeCount = {GrantedScopes.Length} }}";
        }
    }

    public TResult Match<TResult>(
        Func<Unknown, TResult> unknown,
        Func<Unavailable, TResult> unavailable,
        Func<Invalid, TResult> invalid,
        Func<MissingScopes, TResult> missingScopes,
        Func<Ready, TResult> ready
    )
    {
        ArgumentNullException.ThrowIfNull(unknown);
        ArgumentNullException.ThrowIfNull(unavailable);
        ArgumentNullException.ThrowIfNull(invalid);
        ArgumentNullException.ThrowIfNull(missingScopes);
        ArgumentNullException.ThrowIfNull(ready);

        return this switch
        {
            Unknown status => unknown(status),
            Unavailable status => unavailable(status),
            Invalid status => invalid(status),
            MissingScopes status => missingScopes(status),
            Ready status => ready(status),
            _ => throw new UnreachableException("Unknown Twitch token status case."),
        };
    }
}
