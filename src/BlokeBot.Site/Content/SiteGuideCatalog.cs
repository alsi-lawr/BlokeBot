namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static readonly IReadOnlyDictionary<string, SiteGuidePage> _pages = CreateStartPages()
        .Concat(CreateChannelSetupPages())
        .Concat(CreateConfigurationTransferPages())
        .Concat(CreateStreamOverlayPages())
        .Concat(CreateStreamMediaPages())
        .Concat(CreateCommunityInteractionPages())
        .Concat(CreateCommunityIdentityPages())
        .Concat(CreateCommunityProgressionPages())
        .Concat(CreateCommunityCompetitionPages())
        .Concat(CreateCommunityCollaborationPages())
        .Concat(CreateCommunityCollectivePages())
        .Concat(CreateCommunityBingoPages())
        .Concat(CreateTwitchOperationPages())
        .Concat(CreateCommandPages())
        .Concat(CreateGameAndPointPages())
        .Concat(CreateChannelPluginPages())
        .Concat(CreateAutomationPages())
        .Concat(CreateAutomationCatalogPages())
        .Concat(CreatePluginAdministrationPages())
        .Concat(CreateHelpAdministrationPages())
        .ToDictionary(static page => page.Route, StringComparer.Ordinal);

    internal static IReadOnlyList<SiteGuidePage> All { get; } =
        SiteRoutes.GuideTopics.Select(static route => _pages[route]).ToArray();

    internal static IReadOnlyList<SiteGuideNavigationGroup> NavigationGroups { get; } =
    [
        new(
            "Start and setup",
            [
                GuideLink("Getting started", "guide/getting-started"),
                GuideLink("Dashboard", "dashboard"),
                GuideLink("Channels", "channels"),
                GuideLink("Twitch connections", "connect"),
                GuideLink("Channel tools", "tools"),
                GuideLink("Channel plugins", "plugins"),
                GuideLink("Configuration transfer", "configuration-transfer"),
            ]
        ),
        new(
            "Stream presentation",
            [
                GuideLink("Browser Sources", "overlays"),
                GuideLink("Goal & bounty overlays", "overlays#show-community-goals-and-bounties"),
                GuideLink(
                    "Achievement event feed",
                    "overlays#present-achievements-in-the-event-feed"
                ),
                GuideLink("Cues", "overlays/cues"),
                GuideLink("Media library", "overlays/media"),
            ]
        ),
        new(
            "Community interaction",
            [
                GuideLink("Request boards", "community/request-boards"),
                GuideLink("Play with viewers", "community/play-with-viewers"),
                GuideLink("Moments", "community/moments"),
                GuideLink("Viewer passports", "community/passports"),
                GuideLink("Raid & collaboration", "community/raid-collaboration"),
            ]
        ),
        new(
            "Community progression",
            [
                GuideLink("Viewer-funded bounties", "community/bounties"),
                GuideLink("Seasons and achievements", "community/progression"),
                GuideLink("Tournaments & leagues", "community/competitions"),
                GuideLink("BlokeRaid", "community/blokeraid"),
                GuideLink("Collectives", "community/collectives"),
                GuideLink(
                    "Approved Moment attachments",
                    "community/moments#attach-approved-moments-to-progression"
                ),
                GuideLink("Stream-event Bingo", "community/bingo"),
            ]
        ),
        new(
            "Native Twitch",
            [
                GuideLink("Overview", "twitch-operations"),
                GuideLink("Polls", "twitch-operations/polls"),
                GuideLink("Clips and markers", "twitch-operations/clips-markers"),
                GuideLink("Rewards and redemptions", "twitch-operations/channel-points"),
                GuideLink("Predictions", "twitch-operations/predictions"),
            ]
        ),
        new(
            "Chat, games and points",
            [
                GuideLink("Commands", "commands"),
                GuideLink("Available viewer commands", "commands/catalog"),
                GuideLink("Guessing games", "guessing"),
                GuideLink("Viewer points", "points"),
                GuideLink("Giveaways", "giveaways"),
                GuideLink("Leaderboards", "leaderboards"),
            ]
        ),
        new(
            "Automations",
            [
                GuideLink("Visual flow editor", "automations"),
                GuideLink("Twitch events", "automations/events"),
                GuideLink("Actions", "automations/actions"),
            ]
        ),
        new(
            "Help and administration",
            [
                GuideLink("Troubleshooting", "troubleshooting"),
                GuideLink("Moderator access", "moderators"),
                GuideLink("Plugin administration", "server-owners/plugins"),
                GuideLink("Server owners", "server-owners"),
                new SiteLink("Privacy notice", "privacy"),
            ]
        ),
    ];

    internal static SiteGuidePage Get(string route) =>
        _pages.TryGetValue(route, out var page)
            ? page
            : throw new InvalidOperationException($"No guide content is registered for '{route}'.");

    private static SiteLink GuideLink(string label, string href)
    {
        _ = Get($"/{href.Split('#')[0]}");
        return new(label, href);
    }
}
