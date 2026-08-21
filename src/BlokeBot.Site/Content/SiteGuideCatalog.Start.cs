namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateStartPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/guide/getting-started",
            Eyebrow = "Start here",
            Title = "Sign in and choose your channel",
            Summary =
                "Use your normal Twitch account, then choose the channel whose tools you want to view or manage.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Before you begin",
                    Bullets =
                    [
                        "Have the BlokeBot web address you were given.",
                        "Use the Twitch account connected to your channel or moderator role.",
                        "Ask a channel owner or BlokeBot administrator if you need permission to change setup.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Sign in",
                    Steps =
                    [
                        "Open the BlokeBot address and select Continue with Twitch.",
                        "Sign in to Twitch and review the permissions Twitch shows.",
                        "Return to BlokeBot and check your account name and role in the top bar.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose a channel",
                    Steps =
                    [
                        "Select My channel for the Twitch channel you own.",
                        "Use Other channels when you help manage another available channel.",
                        "If a newly available channel does not appear, select Find channels again.",
                    ],
                    Paragraphs =
                    [
                        "If you cannot create a channel setup, ask a BlokeBot administrator to approve you or add the channel.",
                    ],
                },
            ],
            Next = [new SiteLink("Learn the dashboard", "dashboard")],
        };

        yield return new SiteGuidePage
        {
            Route = "/dashboard",
            Eyebrow = "Everyday navigation",
            Title = "Find your way around the dashboard",
            Summary =
                "The navigation follows the selected channel. It groups tools by task and shows only the features that the channel turned on.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/dashboard/phone-dark-home.png",
                LightPhoneSource: "media/dashboard/phone-light-home.png",
                DarkLaptopSource: "media/dashboard/laptop-dark-home.png",
                LightLaptopSource: "media/dashboard/laptop-light-home.png",
                PhoneAlt: "BlokeBot dashboard that shows the selected Sample Channel, channel setup and chat-tool navigation.",
                LaptopAlt: "BlokeBot dashboard that shows the selected Sample Channel, channel setup and chat-tool navigation.",
                "The selected channel appears in the top bar. Its enabled tools appear in the menu."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Check the top bar first",
                    Bullets =
                    [
                        "Bot status shows whether the selected channel is ready or needs attention.",
                        "My channel and Other channels change the active channel.",
                        "Alerts opens current problems. The account menu shows your role and Sign out.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Use the menu",
                    Bullets =
                    [
                        "Home gives a short introduction and public leaderboard shortcut.",
                        "Channel setup contains connections, moderator access and feature switches.",
                        "Chat tools contains the interaction, progression, game, points, command, overlay and enabled Native Twitch tools selected for this channel.",
                        "Expand Native Twitch to move between its five focused task pages.",
                    ],
                    Paragraphs =
                    [
                        "Always confirm the selected channel before you save. A change for one channel does not change another.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Manage channels and access", "channels"),
                new SiteLink("Connect the bot", "connect"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/channels",
            Eyebrow = "Channels and access",
            Title = "Add, choose and manage channels",
            Summary =
                "Each Twitch channel keeps its own connection, tools, games, points and people who can help.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/dashboard/phone-dark-channel-setup.png",
                LightPhoneSource: "media/dashboard/phone-light-channel-setup.png",
                DarkLaptopSource: "media/dashboard/laptop-dark-channel-setup.png",
                LightLaptopSource: "media/dashboard/laptop-light-channel-setup.png",
                PhoneAlt: "Channel setup for Sample Channel that shows separate Chat access and Twitch integration readiness.",
                LaptopAlt: "Channel setup for Sample Channel that shows separate Chat access and Twitch integration readiness.",
                "The selected channel appears in the top bar. Its enabled tools appear in the menu."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Create your channel setup",
                    Steps =
                    [
                        "Sign in with the Twitch account that owns the channel.",
                        "Select My channel, open Channel setup and choose Create channel setup.",
                        "If the action is unavailable, ask a BlokeBot administrator for channel-creation access.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Switch safely",
                    Paragraphs =
                    [
                        "Use the channel selector whenever you help more than one channel. Your role can permit tool use but not changes to channel setup.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Connect this channel", "connect"),
                new SiteLink("Let moderators help", "moderators"),
            ],
        };
    }
}
