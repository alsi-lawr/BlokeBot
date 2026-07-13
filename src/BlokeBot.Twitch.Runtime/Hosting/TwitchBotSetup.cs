namespace BlokeBot.Twitch.Runtime;

internal static class TwitchBotSetup
{
    public const string MissingRefreshTokenMessage =
        TwitchAccessTokenUnavailableException.MissingRefreshTokenMessage;

    public static string CreateOAuthStartUri(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return "the bot OAuth start endpoint";
        }

        var path = uri.AbsolutePath;
        var startPath = path.EndsWith("/callback", StringComparison.OrdinalIgnoreCase)
            ? path[..^"/callback".Length] + "/start"
            : "/oauth/start";

        return new UriBuilder(uri)
        {
            Path = startPath,
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri.ToString();
    }
}
