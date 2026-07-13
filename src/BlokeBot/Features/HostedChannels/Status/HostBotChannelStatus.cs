namespace BlokeBot.Features.HostedChannels.Status;

public enum HostBotModeratorState
{
    Unknown,
    IsModerator,
    NotModerator,
}

[Flags]
public enum HostBotChannelStatusFlags
{
    None = 0,
    BotAccountAuthorized = 1 << 0,
    ModeratorCheckConfigured = 1 << 1,
    ModeratorCheckGranted = 1 << 2,
    FollowerReadConfigured = 1 << 3,
    FollowerReadGranted = 1 << 4,
}

public sealed record HostBotChannelStatus(
    HostBotChannelStatusFlags Flags,
    HostBotModeratorState ModeratorState,
    string ModeratorStatusMessage,
    string FollowerReadStatusMessage
)
{
    public bool CanReadFollowers =>
        HasAll(
            HostBotChannelStatusFlags.BotAccountAuthorized
                | HostBotChannelStatusFlags.FollowerReadConfigured
                | HostBotChannelStatusFlags.FollowerReadGranted
        )
        && ModeratorState == HostBotModeratorState.IsModerator;

    public static HostBotChannelStatus FromReadiness(HostBotReadinessOutcome outcome)
    {
        return outcome.Kind switch
        {
            HostBotReadinessKind.NotConfigured => NotConfigured(),
            HostBotReadinessKind.TokenUnavailable
            or HostBotReadinessKind.InvalidToken
            or HostBotReadinessKind.NeedsAuthorization
            or HostBotReadinessKind.BotAccountMismatch => NeedsAuthorization(outcome.Flags),
            HostBotReadinessKind.MissingModeratorCheckScope
            or HostBotReadinessKind.MissingModeratorCheckPermission =>
                MissingModeratorCheckPermission(outcome.Flags),
            HostBotReadinessKind.IdentityLookupFailed or HostBotReadinessKind.Unknown => Unknown(
                outcome.Flags
            ),
            HostBotReadinessKind.NotModerator => NotModerator(outcome.Flags),
            HostBotReadinessKind.MissingFollowerReadScope => MissingFollowerReadPermission(
                outcome.Flags
            ),
            HostBotReadinessKind.Ready => Ready(),
            _ => Unknown(outcome.Flags),
        };
    }

    public static HostBotChannelStatus NotConfigured()
    {
        return new(
            HostBotChannelStatusFlags.None,
            HostBotModeratorState.Unknown,
            "BlokeBot needs bot account settings before it can check this.",
            "Follower-only giveaways are not set up for this bot account."
        );
    }

    public static HostBotChannelStatus NeedsAuthorization(HostBotChannelStatusFlags configuredFlags)
    {
        return new(
            configuredFlags
                & (
                    HostBotChannelStatusFlags.ModeratorCheckConfigured
                    | HostBotChannelStatusFlags.FollowerReadConfigured
                ),
            HostBotModeratorState.Unknown,
            "Connect the bot account before BlokeBot can check this.",
            "Connect the bot account before follower-only giveaways can work."
        );
    }

    public static HostBotChannelStatus MissingModeratorCheckPermission(
        HostBotChannelStatusFlags flags
    )
    {
        return new(
            flags,
            HostBotModeratorState.Unknown,
            "The connected bot account does not allow BlokeBot to check mod status.",
            (flags & HostBotChannelStatusFlags.FollowerReadConfigured) != 0
                ? "Follower-only giveaways need the mod check to work first."
                : "Follower-only giveaways are not set up for this bot account."
        );
    }

    public static HostBotChannelStatus MissingFollowerReadPermission(
        HostBotChannelStatusFlags flags
    )
    {
        return new(
            flags,
            HostBotModeratorState.IsModerator,
            "The bot is a mod in this channel.",
            "The connected bot account does not allow BlokeBot to check followers."
        );
    }

    public static HostBotChannelStatus NotModerator(HostBotChannelStatusFlags flags)
    {
        return new(
            flags,
            HostBotModeratorState.NotModerator,
            "The bot is not a mod in this channel.",
            "Follower-only giveaways need the bot to be a channel mod."
        );
    }

    public static HostBotChannelStatus Unknown(HostBotChannelStatusFlags flags)
    {
        return new(
            flags,
            HostBotModeratorState.Unknown,
            "BlokeBot could not check whether the bot is a mod.",
            "BlokeBot could not check follower-only giveaways."
        );
    }

    public static HostBotChannelStatus Ready()
    {
        return new(
            HostBotChannelStatusFlags.BotAccountAuthorized
                | HostBotChannelStatusFlags.ModeratorCheckConfigured
                | HostBotChannelStatusFlags.ModeratorCheckGranted
                | HostBotChannelStatusFlags.FollowerReadConfigured
                | HostBotChannelStatusFlags.FollowerReadGranted,
            HostBotModeratorState.IsModerator,
            "The bot is a mod in this channel.",
            "Follower-only giveaways are ready."
        );
    }

    private bool HasAll(HostBotChannelStatusFlags flags)
    {
        return (Flags & flags) == flags;
    }
}
