namespace BlokeBot.Site.Content;

internal static class SiteRoutes
{
    internal static IReadOnlyList<string> All { get; } =
    [
        "/",
        "/how-it-works",
        "/install",
        "/privacy",
        "/guide",
        "/guide/getting-started",
        "/dashboard",
        "/channels",
        "/connect",
        "/tools",
        "/configuration-transfer",
        "/overlays",
        "/overlays/cues",
        "/overlays/media",
        "/community/request-boards",
        "/community/play-with-viewers",
        "/community/moments",
        "/community/passports",
        "/community/bounties",
        "/community/progression",
        "/community/competitions",
        "/community/raid-collaboration",
        "/community/blokeraid",
        "/community/collectives",
        "/community/bingo",
        "/twitch-operations",
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
        "/automations",
        "/automations/events",
        "/automations/actions",
        "/troubleshooting",
        "/moderators",
        "/server-owners",
    ];

    internal static IReadOnlyList<string> GuideTopics { get; } = All.Skip(5).ToArray();
}
