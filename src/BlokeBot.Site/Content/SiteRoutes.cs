namespace BlokeBot.Site.Content;

internal static class SiteRoutes
{
    internal static IReadOnlyList<string> All { get; } =
    [
        "/",
        "/how-it-works",
        "/install",
        "/guide",
        "/guide/getting-started",
        "/dashboard",
        "/channels",
        "/connect",
        "/tools",
        "/overlays",
        "/overlays/cues",
        "/overlays/media",
        "/community/request-boards",
        "/community/play-with-viewers",
        "/community/moments",
        "/twitch-operations",
        "/twitch-operations/shoutouts",
        "/twitch-operations/polls",
        "/twitch-operations/clips-markers",
        "/twitch-operations/channel-points",
        "/twitch-operations/predictions",
        "/commands",
        "/commands/catalog",
        "/guessing",
        "/points",
        "/giveaways",
        "/leaderboards",
        "/troubleshooting",
        "/moderators",
        "/server-owners",
    ];

    internal static IReadOnlyList<string> GuideTopics { get; } = All.Skip(4).ToArray();
}
