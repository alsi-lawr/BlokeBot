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

    public static HostBotChannelStatus FromReadiness(HostBotReadinessOutcome outcome) =>
        outcome.Kind switch
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

    public static HostBotChannelStatus NotConfigured() =>
        new(
            HostBotChannelStatusFlags.None,
            HostBotModeratorState.Unknown,
            "Moderator status cannot be checked until bot account settings are updated.",
            "Follower access is not configured for the bot account."
        );

    public static HostBotChannelStatus NeedsAuthorization(
        HostBotChannelStatusFlags configuredFlags
    ) =>
        new(
            configuredFlags
                & (
                    HostBotChannelStatusFlags.ModeratorCheckConfigured
                    | HostBotChannelStatusFlags.FollowerReadConfigured
                ),
            HostBotModeratorState.Unknown,
            "Moderator status cannot be checked until the bot account is authorized.",
            "Follower access is not available until the bot account is authorized."
        );

    public static HostBotChannelStatus MissingModeratorCheckPermission(
        HostBotChannelStatusFlags flags
    ) =>
        new(
            flags,
            HostBotModeratorState.Unknown,
            "Moderator status cannot be checked with the current bot account authorization.",
            (flags & HostBotChannelStatusFlags.FollowerReadConfigured) != 0
                ? "Follower access depends on moderator status, which could not be checked."
                : "Follower access is not configured for the bot account."
        );

    public static HostBotChannelStatus MissingFollowerReadPermission(
        HostBotChannelStatusFlags flags
    ) =>
        new(
            flags,
            HostBotModeratorState.IsModerator,
            "Bot is a moderator in this channel.",
            "Follower access is missing from the bot account authorization."
        );

    public static HostBotChannelStatus NotModerator(HostBotChannelStatusFlags flags) =>
        new(
            flags,
            HostBotModeratorState.NotModerator,
            "Bot is not currently a moderator in this channel.",
            "Follower access requires the bot to be a channel moderator."
        );

    public static HostBotChannelStatus Unknown(HostBotChannelStatusFlags flags) =>
        new(
            flags,
            HostBotModeratorState.Unknown,
            "Moderator status could not be checked.",
            "Follower access could not be checked."
        );

    public static HostBotChannelStatus Ready() =>
        new(
            HostBotChannelStatusFlags.BotAccountAuthorized
                | HostBotChannelStatusFlags.ModeratorCheckConfigured
                | HostBotChannelStatusFlags.ModeratorCheckGranted
                | HostBotChannelStatusFlags.FollowerReadConfigured
                | HostBotChannelStatusFlags.FollowerReadGranted,
            HostBotModeratorState.IsModerator,
            "Bot is a moderator in this channel.",
            "Bot can read followers for this channel."
        );

    private bool HasAll(HostBotChannelStatusFlags flags) => (Flags & flags) == flags;
}
