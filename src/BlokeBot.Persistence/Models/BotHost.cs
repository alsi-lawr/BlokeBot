namespace BlokeBot.Persistence.Models;

public sealed class BotHost
{
    public int Id { get; set; }

    public string? TwitchUserId { get; set; }

    public string Login { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? ProfileImageUrl { get; set; }

    public DateTime? ChannelBotAuthorizedAtUtc { get; set; }

    public string? ChannelBotAuthorizedScopes { get; set; }

    public BotChannelRuntimeState BotRuntimeState { get; set; }

    public DateTime? BotRuntimeStateChangedAtUtc { get; set; }

    public HostFeatureFlags EnabledFeatures { get; set; } = HostFeatureFlags.All;

    public string TimeZoneId { get; set; } = "UTC";

    public DateTime CreatedAtUtc { get; set; }
}
