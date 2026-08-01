namespace BlokeBot.Core.Features.HostedChannels.Status;

public sealed record HostBotChannelStatus
{
    private HostBotChannelStatus(
        bool isModerator,
        bool moderatorCheckCompleted,
        bool canReadFollowers,
        string moderatorStatusMessage,
        string followerReadStatusMessage
    )
    {
        IsModerator = isModerator;
        ModeratorCheckCompleted = moderatorCheckCompleted;
        CanReadFollowers = canReadFollowers;
        ModeratorStatusMessage = moderatorStatusMessage;
        FollowerReadStatusMessage = followerReadStatusMessage;
    }

    public bool IsModerator { get; }

    public bool ModeratorCheckCompleted { get; }

    public bool CanReadFollowers { get; }

    public string ModeratorStatusMessage { get; }

    public string FollowerReadStatusMessage { get; }

    public static HostBotChannelStatus FromReadiness(HostBotReadinessOutcome outcome) =>
        outcome.Match(
            _ => NotConfigured(),
            _ => NeedsAuthorization(),
            _ => NeedsAuthorization(),
            _ => NeedsAuthorization(),
            _ => Unknown(),
            missing => MissingModeratorCheckPermission(missing.Capabilities),
            missing => MissingModeratorCheckPermission(missing.Capabilities),
            _ => Unknown(),
            _ => NeedsAuthorization(),
            _ => NotModerator(),
            _ => MissingFollowerReadPermission(),
            _ => Ready()
        );

    private static HostBotChannelStatus NotConfigured() =>
        new(
            false,
            false,
            false,
            "BlokeBot needs bot account settings before it can check this.",
            "Follower-only giveaways are not set up for this bot account."
        );

    private static HostBotChannelStatus NeedsAuthorization() =>
        new(
            false,
            false,
            false,
            "Connect the bot account before BlokeBot can check this.",
            "Connect the bot account before follower-only giveaways can work."
        );

    private static HostBotChannelStatus MissingModeratorCheckPermission(
        HostBotReadinessCapabilities capabilities
    ) =>
        new(
            false,
            false,
            false,
            "The connected bot account does not allow BlokeBot to check mod status.",
            capabilities.FollowerReadConfigured
                ? "Follower-only giveaways need the mod check to work first."
                : "Follower-only giveaways are not set up for this bot account."
        );

    private static HostBotChannelStatus MissingFollowerReadPermission() =>
        new(
            true,
            true,
            false,
            "The bot is a mod in this channel.",
            "The connected bot account does not allow BlokeBot to check followers."
        );

    private static HostBotChannelStatus NotModerator() =>
        new(
            false,
            true,
            false,
            "The bot is not a mod in this channel.",
            "Follower-only giveaways need the bot to be a channel mod."
        );

    private static HostBotChannelStatus Unknown() =>
        new(
            false,
            false,
            false,
            "BlokeBot could not check whether the bot is a mod.",
            "BlokeBot could not check follower-only giveaways."
        );

    private static HostBotChannelStatus Ready() =>
        new(
            true,
            true,
            true,
            "The bot is a mod in this channel.",
            "Follower-only giveaways are ready."
        );
}
