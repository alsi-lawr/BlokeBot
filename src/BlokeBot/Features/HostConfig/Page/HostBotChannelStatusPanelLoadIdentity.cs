namespace BlokeBot.Features.HostConfig.Page;

public sealed record HostBotChannelStatusPanelLoadIdentity
{
    private HostBotChannelStatusPanelLoadIdentity(string hostLogin, string reloadKey)
    {
        HostLogin = hostLogin;
        ReloadKey = reloadKey;
    }

    public string HostLogin { get; }

    public string ReloadKey { get; }

    public static HostBotChannelStatusPanelLoadIdentity? From(
        string hostLogin,
        string? reloadKey
    )
    {
        return string.IsNullOrWhiteSpace(hostLogin)
            ? null
            : new(hostLogin.Trim().ToLowerInvariant(), reloadKey ?? string.Empty);
    }
}
