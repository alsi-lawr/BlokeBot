namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateChannelSetupPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/connect",
            Eyebrow = "Twitch connection",
            Title = "Connect the bot to your channel",
            Summary =
                "BlokeBot explains which Twitch account or permission is needed and keeps the bot stopped until the channel is ready.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/dashboard/phone-dark-channel-setup.png",
                LightPhoneSource: "media/dashboard/phone-light-channel-setup.png",
                DarkLaptopSource: "media/dashboard/laptop-dark-channel-setup.png",
                LightLaptopSource: "media/dashboard/laptop-light-channel-setup.png",
                PhoneAlt: "Channel setup that shows separate actions for Chat access and the Twitch integration.",
                LaptopAlt: "Channel setup that shows separate actions for Chat access and the Twitch integration.",
                "Chat access and Twitch integration show their own connection actions and readiness."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Connect Chat access",
                    Steps =
                    [
                        "Select the channel and open Channel setup.",
                        "Under Chat access, select Connect channel.",
                        "Complete Twitch as the channel owner. This approves BlokeBot for channel chat.",
                        "Return to the same selected channel and confirm that Chat access is connected.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Connect the Twitch integration",
                    Steps =
                    [
                        "Under Twitch integration, select Connect or Reconnect.",
                        "Complete Twitch as the channel owner and approve every requested permission.",
                        "Return to the same selected channel and confirm that Twitch integration is connected.",
                    ],
                    Note =
                        "This is separate from Chat access. Disconnect removes BlokeBot's saved authorization for this channel. Reconnect replaces it.",
                },
                new SiteGuideSection
                {
                    Heading = "Connect the bot account",
                    Steps =
                    [
                        "If the connection pop-up uses your normal account, sign out of Twitch there.",
                        "Select Connect bot and sign in as the dedicated bot account named by BlokeBot.",
                        "Make the bot a moderator in your Twitch channel. This is the recommended setup for announcements and follower-only chat.",
                        "Select Start bot when the controls become available.",
                        "Use Stop bot when you intentionally want BlokeBot out of chat.",
                    ],
                    Note =
                        "Twitch does not provide an API that lets BlokeBot make its bot account follow your channel. If the channel uses follower-only chat, check the bot role. If the bot is not a moderator, follow the channel as the bot. BlokeBot checks this state and alerts you when Twitch rejects follower-only delivery.",
                },
                new SiteGuideSection
                {
                    Heading = "Reconnect the right identity",
                    Paragraphs =
                    [
                        "Use the reconnect action beside the connection that is stale. Chat access, Twitch integration and bot-account connections are different approvals. A reconnection of one approval does not repair the others.",
                        "If Twitch used the wrong account, close the result window. Sign out of Twitch in that browser context. Repeat the account-specific action.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Choose channel tools", "tools"),
                new SiteLink("Troubleshoot a connection", "troubleshooting"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/tools",
            Eyebrow = "Channel tools",
            Title = "Choose the tools your channel needs",
            Summary =
                "Every available Chat Tools feature is independently opt-in, so each channel can run only the tools it needs.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Start with every tool off",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/chat-tools/phone-dark-chat-tools-all-disabled.png",
                        LightPhoneSource: "media/chat-tools/phone-light-chat-tools-all-disabled.png",
                        DarkLaptopSource: "media/chat-tools/laptop-dark-chat-tools-all-disabled.png",
                        LightLaptopSource: "media/chat-tools/laptop-light-chat-tools-all-disabled.png",
                        PhoneAlt: "BlokeBot Channel setup on a phone with every Chat tools feature card set to off.",
                        LaptopAlt: "BlokeBot Channel setup with every Chat tools feature card set to off.",
                        "A new channel starts with every Chat Tools feature off. Each card carries its own switch."
                    ),
                    Paragraphs =
                    [
                        "A new channel starts with every available Chat Tools feature disabled. This includes Native Twitch operations, community interaction and progression, games, Points, Custom commands and Overlays.",
                        "Channels migrated from an earlier BlokeBot release keep their effective feature behavior. After an upgrade, review their switches. Do not assume that the upgrade applied the new-channel default.",
                    ],
                    Bullets =
                    [
                        "A disabled feature is hidden from navigation and does not accept chat commands, public-page actions, provider events or background work.",
                        "If you disable the feature, BlokeBot pauses it and keeps its saved configuration and data.",
                        "If you enable the feature again, it resumes from the current state. BlokeBot does not replay commands, provider events or scheduled work missed while the feature was off.",
                    ],
                    Note =
                        "Channel setup uses the application-wide semantic-card layout. Its shared 12px clearance separates every top-level feature card. It does not add page-specific space.",
                },
                new SiteGuideSection
                {
                    Heading = "Turn on only what the channel needs",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/chat-tools/phone-dark-chat-tools-enabled.png",
                        LightPhoneSource: "media/chat-tools/phone-light-chat-tools-enabled.png",
                        DarkLaptopSource: "media/chat-tools/laptop-dark-chat-tools-enabled.png",
                        LightLaptopSource: "media/chat-tools/laptop-light-chat-tools-enabled.png",
                        PhoneAlt: "BlokeBot Channel setup on a phone with Request boards, Moments, Points and Custom commands on and all other features off.",
                        LaptopAlt: "BlokeBot Channel setup with Request boards, Moments, Points and Custom commands on and all other features off.",
                        "Each feature is independently opt-in, so a channel can run four tools and leave the rest off."
                    ),
                    Steps =
                    [
                        "Choose the correct channel and open Channel setup.",
                        "Open Chat tools and turn on each feature this channel will use. Each feature card persists its on or off state immediately.",
                        "Open the new navigation item and finish its settings before you use it live.",
                    ],
                    Note =
                        "A feature switch controls availability, not readiness. Configure the feature and satisfy any Twitch connection, permission, live-stream or active-game requirement shown on its page.",
                },
                new SiteGuideSection
                {
                    Heading = "What you can add",
                    Links =
                    [
                        new SiteLink("Request boards", "community/request-boards"),
                        new SiteLink("Play with viewers", "community/play-with-viewers"),
                        new SiteLink("Moments and recaps", "community/moments"),
                        new SiteLink("Viewer-funded bounties", "community/bounties"),
                        new SiteLink("Seasons and achievements", "community/progression"),
                        new SiteLink("Stream-event Bingo", "community/bingo"),
                        new SiteLink("Commands and scheduled messages", "commands"),
                        new SiteLink("Guessing games", "guessing"),
                        new SiteLink("Points", "points"),
                        new SiteLink("Giveaways", "giveaways"),
                        new SiteLink("Public leaderboards", "leaderboards"),
                        new SiteLink("Native Twitch", "twitch-operations"),
                        new SiteLink("Overlays and Browser Sources", "overlays"),
                        new SiteLink("Visual automations", "automations"),
                    ],
                },
            ],
            Next = [new SiteLink("Set up a request board", "community/request-boards")],
        };
    }
}
