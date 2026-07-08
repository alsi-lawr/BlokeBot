namespace BlokeBot.Features.HostedChannels.Status;

public enum HostBotReadinessKind
{
    NotConfigured,
    TokenUnavailable,
    InvalidToken,
    NeedsAuthorization,
    Unknown,
    MissingModeratorCheckScope,
    MissingModeratorCheckPermission,
    IdentityLookupFailed,
    BotAccountMismatch,
    NotModerator,
    MissingFollowerReadScope,
    Ready,
}

public sealed record HostBotReadinessOutcome(
    HostBotReadinessKind Kind,
    HostBotChannelStatusFlags Flags
)
{
    public static HostBotReadinessOutcome NotConfigured() =>
        new(HostBotReadinessKind.NotConfigured, HostBotChannelStatusFlags.None);

    public static HostBotReadinessOutcome TokenUnavailable(HostBotChannelStatusFlags flags) =>
        new(HostBotReadinessKind.TokenUnavailable, flags);

    public static HostBotReadinessOutcome InvalidToken(HostBotChannelStatusFlags flags) =>
        new(HostBotReadinessKind.InvalidToken, flags);

    public static HostBotReadinessOutcome NeedsAuthorization(HostBotChannelStatusFlags flags) =>
        new(HostBotReadinessKind.NeedsAuthorization, flags);

    public static HostBotReadinessOutcome Unknown(HostBotChannelStatusFlags flags) =>
        new(HostBotReadinessKind.Unknown, flags);

    public static HostBotReadinessOutcome MissingModeratorCheckScope(
        HostBotChannelStatusFlags flags
    ) => new(HostBotReadinessKind.MissingModeratorCheckScope, flags);

    public static HostBotReadinessOutcome MissingModeratorCheckPermission(
        HostBotChannelStatusFlags flags
    ) => new(HostBotReadinessKind.MissingModeratorCheckPermission, flags);

    public static HostBotReadinessOutcome IdentityLookupFailed(HostBotChannelStatusFlags flags) =>
        new(HostBotReadinessKind.IdentityLookupFailed, flags);

    public static HostBotReadinessOutcome BotAccountMismatch(HostBotChannelStatusFlags flags) =>
        new(HostBotReadinessKind.BotAccountMismatch, flags);

    public static HostBotReadinessOutcome NotModerator(HostBotChannelStatusFlags flags) =>
        new(HostBotReadinessKind.NotModerator, flags);

    public static HostBotReadinessOutcome MissingFollowerReadScope(
        HostBotChannelStatusFlags flags
    ) => new(HostBotReadinessKind.MissingFollowerReadScope, flags);

    public static HostBotReadinessOutcome Ready() =>
        new(
            HostBotReadinessKind.Ready,
            HostBotChannelStatusFlags.BotAccountAuthorized
                | HostBotChannelStatusFlags.ModeratorCheckConfigured
                | HostBotChannelStatusFlags.ModeratorCheckGranted
                | HostBotChannelStatusFlags.FollowerReadConfigured
                | HostBotChannelStatusFlags.FollowerReadGranted
        );
}
