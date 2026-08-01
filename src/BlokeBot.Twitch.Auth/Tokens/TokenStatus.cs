using System.Collections.Immutable;
using System.Diagnostics;

namespace BlokeBot.Twitch.Auth;

public enum TokenStatusTransportFailureReason
{
    RequestFailed,
    ResponseInvalid,
    TimedOut,
}

public abstract record TokenStatusError
{
    private TokenStatusError() { }

    public ImmutableArray<string> RequiredScopes =>
        Match(
            acquisition => acquisition.RequiredScopesSnapshot,
            validation => validation.RequiredScopesSnapshot
        );

    public sealed record AcquisitionUnavailable(
        TokenStatusTransportFailureReason Reason,
        string FailureType,
        ImmutableArray<string> RequiredScopesSnapshot
    ) : TokenStatusError;

    public sealed record ValidationUnavailable(
        TokenStatusTransportFailureReason Reason,
        string FailureType,
        ImmutableArray<string> RequiredScopesSnapshot
    ) : TokenStatusError;

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

public abstract record TokenStatus
{
    private TokenStatus() { }

    public sealed record Unknown(TokenStatusError Error) : TokenStatus;

    public sealed record Unavailable(
        AccessTokenUnavailableReason Reason,
        ImmutableArray<string> RequiredScopes
    ) : TokenStatus;

    public sealed record Invalid(ImmutableArray<string> RequiredScopes) : TokenStatus;

    public sealed record MissingScopes(
        string AccessToken,
        TokenValidation Validation,
        ImmutableArray<string> RequiredScopes,
        ImmutableArray<string> GrantedScopes,
        ImmutableArray<string> Missing
    ) : TokenStatus
    {
        public override string ToString() =>
            $"TwitchTokenStatus.{nameof(MissingScopes)} {{ RequiredScopeCount = {RequiredScopes.Length}, GrantedScopeCount = {GrantedScopes.Length}, MissingScopeCount = {Missing.Length} }}";
    }

    public sealed record Ready(
        string AccessToken,
        TokenValidation Validation,
        ImmutableArray<string> RequiredScopes,
        ImmutableArray<string> GrantedScopes
    ) : TokenStatus
    {
        public override string ToString() =>
            $"TwitchTokenStatus.{nameof(Ready)} {{ RequiredScopeCount = {RequiredScopes.Length}, GrantedScopeCount = {GrantedScopes.Length} }}";
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
