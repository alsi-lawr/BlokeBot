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
                "BlokeBot identifies the required Twitch account or permission. It keeps the bot stopped until the channel is ready.",
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
                    Paragraphs =
                    [
                        "The Chat access authorization approves BlokeBot for channel chat.",
                    ],
                    Steps =
                    [
                        "Select the channel.",
                        "Open Channel setup.",
                        "Under Chat access, select Connect channel.",
                        "Complete Twitch authorization as the channel owner.",
                        "Return to the same selected channel.",
                        "Check that Chat access is connected.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Connect the Twitch integration",
                    Steps =
                    [
                        "Under Twitch integration, select Connect or Reconnect.",
                        "Complete Twitch authorization as the channel owner.",
                        "Approve every requested permission.",
                        "Return to the same selected channel.",
                        "Check that Twitch integration is connected.",
                    ],
                    Note =
                        "This is separate from Chat access. Disconnect removes BlokeBot's saved authorization for this channel. Reconnect replaces it.",
                },
                new SiteGuideSection
                {
                    Heading = "Connect the bot account",
                    Paragraphs =
                    [
                        "A bot with a moderator role is the recommended setup for announcements and follower-only chat.",
                    ],
                    Steps =
                    [
                        "If the connection pop-up uses your normal account, sign out of Twitch there.",
                        "Select Connect bot.",
                        "Sign in as the dedicated bot account named by BlokeBot.",
                        "Make the bot a moderator in your Twitch channel.",
                        "When the controls become available, select Start bot.",
                        "To remove BlokeBot from chat, use Stop bot.",
                    ],
                    Note =
                        "Twitch does not provide an API that lets BlokeBot make its bot account follow your channel. If the channel uses follower-only chat, check the bot role. If the bot is not a moderator, follow the channel as the bot. BlokeBot checks this state and alerts you when Twitch rejects follower-only delivery.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "If Twitch used the wrong account, close the result window.",
                        "Sign out of Twitch in that browser context.",
                        "Repeat the account-specific action.",
                        "A separate approval applies to Chat access.",
                        "A separate approval applies to Twitch integration.",
                        "A separate approval applies to the bot-account connection.",
                    ],
                    Heading = "Reconnect the right identity",
                    Paragraphs =
                    [
                        "Use the reconnect action beside the connection that is stale. A reconnection applies only to its specific approval. A reconnection of one approval does not repair the others.",
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
                "Each available Chat Tools feature is an independent opt-in choice. Each channel can run only the tools it needs.",
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
                        "A new channel starts with every available Chat Tools feature disabled.",
                        "Channels migrated from an earlier BlokeBot release keep their effective feature behavior. After an upgrade, review their switches. Do not assume that the upgrade applied the new-channel default.",
                    ],
                    Bullets =
                    [
                        "A new channel starts with this feature group off: Native Twitch operations.",
                        "A new channel starts with this feature group off: community interaction.",
                        "A new channel starts with this feature group off: community progression.",
                        "A new channel starts with this feature group off: games.",
                        "A new channel starts with this feature group off: Points.",
                        "A new channel starts with this feature group off: Custom commands.",
                        "A new channel starts with this feature group off: Overlays.",
                        "A disabled feature does not appear in navigation.",
                        "A disabled feature does not accept chat commands and public-page actions.",
                        "A disabled feature does not accept provider events and background work.",
                        "If you disable the feature, BlokeBot pauses it and keeps its saved configuration and data.",
                        "If you enable the feature again, it resumes from the current state.",
                        "BlokeBot does not replay commands missed while the feature was off.",
                        "BlokeBot does not replay provider events missed while the feature was off.",
                        "BlokeBot does not replay scheduled work missed while the feature was off.",
                    ],
                    Note =
                        "Channel setup uses the shared semantic-card layout. A 12px gap separates each top-level feature card. The page adds no extra space.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Satisfy each requirement that the feature page shows for the Twitch connection.",
                        "Satisfy each requirement that the feature page shows for permission.",
                        "Satisfy each requirement that the feature page shows for a live stream.",
                        "Satisfy each requirement that the feature page shows for an active game.",
                    ],
                    Heading = "Turn on only what the channel needs",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/chat-tools/phone-dark-chat-tools-enabled.png",
                        LightPhoneSource: "media/chat-tools/phone-light-chat-tools-enabled.png",
                        DarkLaptopSource: "media/chat-tools/laptop-dark-chat-tools-enabled.png",
                        LightLaptopSource: "media/chat-tools/laptop-light-chat-tools-enabled.png",
                        PhoneAlt: "BlokeBot Channel setup on a phone shows Request boards and Moments on. Points and Custom commands are also on. All other features are off.",
                        LaptopAlt: "BlokeBot Channel setup shows Request boards and Moments on. Points and Custom commands are also on. All other features are off.",
                        "Each feature is an independent opt-in choice. A channel can run four tools and leave the rest off."
                    ),
                    Paragraphs = ["Each feature card saves its on or off state immediately."],
                    Steps =
                    [
                        "Select the correct channel.",
                        "Open Channel setup.",
                        "Open Chat tools.",
                        "Turn on each feature that this channel will use.",
                        "Open the new navigation item.",
                        "Before live use, complete its settings.",
                    ],
                    Note =
                        "A feature switch controls availability, not readiness. Configure the feature.",
                },
                new SiteGuideSection
                {
                    Heading = "Available tools",
                    LegacyAnchor = "what-you-can-add",
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
