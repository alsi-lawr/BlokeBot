namespace BlokeBot.Twitch;

public abstract record ModeratedChannelStatus
{
    private ModeratedChannelStatus() { }

    public sealed record Unknown : ModeratedChannelStatus;

    public sealed record NeedsAuthorization : ModeratedChannelStatus;

    public sealed record MissingPermission : ModeratedChannelStatus;

    public sealed record IsModerator : ModeratedChannelStatus;

    public sealed record NotModerator : ModeratedChannelStatus;
}
