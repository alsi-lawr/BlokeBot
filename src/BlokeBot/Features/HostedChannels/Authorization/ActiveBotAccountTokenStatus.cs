namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed record ActiveBotAccountTokenStatus
{
    public required string BotLogin { get; init; }

    public string? ProfileImageUrl { get; init; }

    public required TwitchTokenStatus Status { get; init; }
}
