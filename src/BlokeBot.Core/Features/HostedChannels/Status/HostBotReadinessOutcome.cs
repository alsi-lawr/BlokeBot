using System.Diagnostics;

namespace BlokeBot.Core.Features.HostedChannels.Status;

public sealed record HostBotReadinessCapabilities(
    bool ModeratorCheckConfigured,
    bool ModeratorCheckGranted,
    bool FollowerReadConfigured,
    bool FollowerReadGranted
);

public abstract record HostBotReadinessOutcome
{
    private HostBotReadinessOutcome() { }

    public TResult Match<TResult>(
        Func<NotConfigured, TResult> notConfigured,
        Func<TokenUnavailable, TResult> tokenUnavailable,
        Func<InvalidToken, TResult> invalidToken,
        Func<NeedsAuthorization, TResult> needsAuthorization,
        Func<Unknown, TResult> unknown,
        Func<MissingModeratorCheckScope, TResult> missingModeratorCheckScope,
        Func<MissingModeratorCheckPermission, TResult> missingModeratorCheckPermission,
        Func<IdentityLookupFailed, TResult> identityLookupFailed,
        Func<BotAccountMismatch, TResult> botAccountMismatch,
        Func<NotModerator, TResult> notModerator,
        Func<MissingFollowerReadScope, TResult> missingFollowerReadScope,
        Func<Ready, TResult> ready
    )
    {
        return this switch
        {
            NotConfigured value => notConfigured(value),
            TokenUnavailable value => tokenUnavailable(value),
            InvalidToken value => invalidToken(value),
            NeedsAuthorization value => needsAuthorization(value),
            Unknown value => unknown(value),
            MissingModeratorCheckScope value => missingModeratorCheckScope(value),
            MissingModeratorCheckPermission value => missingModeratorCheckPermission(value),
            IdentityLookupFailed value => identityLookupFailed(value),
            BotAccountMismatch value => botAccountMismatch(value),
            NotModerator value => notModerator(value),
            MissingFollowerReadScope value => missingFollowerReadScope(value),
            Ready value => ready(value),
            _ => throw new UnreachableException("Unknown host bot readiness outcome."),
        };
    }

    public sealed record NotConfigured : HostBotReadinessOutcome;

    public sealed record TokenUnavailable(HostBotReadinessCapabilities Capabilities)
        : HostBotReadinessOutcome;

    public sealed record InvalidToken(HostBotReadinessCapabilities Capabilities)
        : HostBotReadinessOutcome;

    public sealed record NeedsAuthorization(HostBotReadinessCapabilities Capabilities)
        : HostBotReadinessOutcome;

    public sealed record Unknown(HostBotReadinessCapabilities Capabilities)
        : HostBotReadinessOutcome;

    public sealed record MissingModeratorCheckScope(HostBotReadinessCapabilities Capabilities)
        : HostBotReadinessOutcome;

    public sealed record MissingModeratorCheckPermission(HostBotReadinessCapabilities Capabilities)
        : HostBotReadinessOutcome;

    public sealed record IdentityLookupFailed(HostBotReadinessCapabilities Capabilities)
        : HostBotReadinessOutcome;

    public sealed record BotAccountMismatch(HostBotReadinessCapabilities Capabilities)
        : HostBotReadinessOutcome;

    public sealed record NotModerator(HostBotReadinessCapabilities Capabilities)
        : HostBotReadinessOutcome;

    public sealed record MissingFollowerReadScope(HostBotReadinessCapabilities Capabilities)
        : HostBotReadinessOutcome;

    public sealed record Ready : HostBotReadinessOutcome;
}
