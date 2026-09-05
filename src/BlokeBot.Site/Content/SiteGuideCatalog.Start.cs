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
                "Use your normal Twitch account. Then select the channel whose tools you want to view or manage.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Before you begin",
                    Bullets =
                    [
                        "Have the BlokeBot web address that you received.",
                        "Use the Twitch account connected to your channel or moderator role.",
                        "If you need permission to change setup, ask a channel owner or BlokeBot administrator.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Sign in",
                    Steps =
                    [
                        "Open the BlokeBot address.",
                        "Select Continue with Twitch.",
                        "Sign in to Twitch.",
                        "Review the permissions that Twitch shows.",
                        "Return to BlokeBot.",
                        "Check your account name and role in the top bar.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose a channel",
                    Steps =
                    [
                        "Select My channel for the Twitch channel you own.",
                        "When you help manage another available channel, use Other channels.",
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
                PhoneAlt: "The BlokeBot dashboard shows the selected Sample Channel. It also shows channel setup and chat-tool navigation.",
                LaptopAlt: "The BlokeBot dashboard shows the selected Sample Channel. It also shows channel setup and chat-tool navigation.",
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
                        "Channel setup contains connections.",
                        "Channel setup contains moderator access.",
                        "Channel setup contains feature switches.",
                        "For this channel, Chat tools contains selected interaction tools and progression tools.",
                        "For this channel, Chat tools contains selected game tools and points tools.",
                        "For this channel, Chat tools contains selected command tools and overlay tools.",
                        "For this channel, Chat tools contains selected enabled Native Twitch tools.",
                        "Expand Native Twitch to move between its four focused task pages.",
                    ],
                    Paragraphs =
                    [
                        "Before you save, check the selected channel. A change for one channel does not change another.",
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
            Title = "Channels and access",
            Summary = "Each Twitch channel keeps its own connection and tools.",
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
                    Bullets =
                    [
                        "Each Twitch channel keeps its own games.",
                        "Each Twitch channel keeps its own points.",
                        "People who can help are specific to each Twitch channel.",
                    ],
                    Heading = "Create your channel setup",
                    Steps =
                    [
                        "Sign in with the Twitch account that owns the channel.",
                        "Select My channel.",
                        "Open Channel setup.",
                        "Select Create channel setup.",
                        "If the action is unavailable, ask a BlokeBot administrator for channel-creation access.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Switch safely",
                    Paragraphs =
                    [
                        "If you help more than one channel, use the channel selector. Your role can permit tool use but not changes to channel setup.",
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
