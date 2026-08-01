namespace BlokeBot.Core.Features.HostConfig.Page;

public sealed record FollowerOnlyChatStatusPanelLoadIdentity
{
    private FollowerOnlyChatStatusPanelLoadIdentity(string hostLogin, string reloadKey)
    {
        HostLogin = hostLogin;
        ReloadKey = reloadKey;
    }

    public string HostLogin { get; }

    public string ReloadKey { get; }

    public static FollowerOnlyChatStatusPanelLoadIdentity? From(
        string hostLogin,
        string? reloadKey
    ) =>
        string.IsNullOrWhiteSpace(hostLogin)
            ? null
            : new(hostLogin.Trim().ToLowerInvariant(), reloadKey ?? string.Empty);
}
