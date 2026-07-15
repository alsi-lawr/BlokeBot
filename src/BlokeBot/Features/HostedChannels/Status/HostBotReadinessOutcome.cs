namespace BlokeBot.Features.HostedChannels.Status;

public sealed record HostBotReadinessCapabilities(
    bool ModeratorCheckConfigured,
    bool ModeratorCheckGranted,
    bool FollowerReadConfigured,
    bool FollowerReadGranted
);

public abstract record HostBotReadinessOutcome
{
    private HostBotReadinessOutcome() { }

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
