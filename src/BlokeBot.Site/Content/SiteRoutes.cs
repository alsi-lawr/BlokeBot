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
        "/commands",
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
