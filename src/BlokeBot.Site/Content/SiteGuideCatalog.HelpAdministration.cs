namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateHelpAdministrationPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/troubleshooting",
            Eyebrow = "Help and recovery",
            Title = "Understand a warning or offline bot",
            Summary =
                "Start with the message on the page. BlokeBot normally identifies the absent channel, permission, connection or tool.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Quick checks",
                    Steps =
                    [
                        "Confirm the selected channel.",
                        "Open Channel setup and check the tool switch and bot status.",
                        "Complete the Twitch action offered by the page.",
                        "Open Alerts and read the newest active alert.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Common Twitch failures",
                    Bullets =
                    [
                        "Wrong account: sign out of Twitch in the pop-up. Repeat the specific Channel or Bot connection action.",
                        "Moderator-only action unavailable: confirm the bot is still a moderator, then reconnect if its grant predates the required scope.",
                        "Follower-only rejection: make the bot a moderator or manually follow the channel while signed in as the bot account.",
                        "Announcement rejected: confirm that the bot is still a moderator. Reconnect the bot account with the action in Channel setup.",
                        "Dashboard script or stylesheet is absent: ask the server owner to verify the reverse proxy path and static assets. Do not reconnect Twitch repeatedly.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Ask for useful help",
                    Paragraphs =
                    [
                        "If the problem remains, send the page name, channel, time, alert text and support reference to the server owner. Do not send Twitch secrets or tokens.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Plugin failures",
                    Bullets =
                    [
                        "Marketplace unavailable: wait for one successful catalogue refresh. If Admin shows an older snapshot, search can still use it.",
                        "Install or Update unavailable: confirm GitHub access and the tag. An absent or moved tag needs a corrected catalogue entry or package release.",
                        "Validation fault: correct the manifest, package declarations, target support, paths, links, collisions, or size limits.",
                        "Migration or activation fault: read the latest operation message. The old code cannot resume after durable migration starts.",
                        "Worker fault: BlokeBot tries one automatic restart. Correct the cause and select Restart after the next fault.",
                        "Needs attention: complete the required Twitch scope or EventSub action. Independent work can remain available.",
                        "Command absent: check built-in and custom commands first. Those routes shadow a plugin command.",
                        "Feature enablement rejected: correct a plugin command collision, invalid automatic flow, or flow name that already exists.",
                        "Automatic flow deleted: disable and enable the feature to create the flow again.",
                    ],
                    Links =
                    [
                        new SiteLink("Recover a plugin installation", "server-owners/plugins"),
                        new SiteLink("Recover a channel plugin feature", "plugins"),
                    ],
                    Note =
                        "Remove is permanent. After Remove, no plugin settings, private data, page, flow, run history, receipt, context, or latest outcome remains.",
                },
                new SiteGuideSection
                {
                    Heading = "Privacy, saved preferences and data requests",
                    Paragraphs =
                    [
                        "The privacy notice is the authoritative description of stored data. It covers Twitch data, cookies, browser storage and retention for both origins.",
                    ],
                    Bullets =
                    [
                        "The control for this help site's preferences is on the privacy notice itself.",
                        "The dashboard's control is in its account menu: Stop saving view preferences. Each origin's control governs only that origin's storage.",
                        "Send private data requests to the privacy contact in the notice. Do not send them to chat or a public board.",
                    ],
                    Links = [new SiteLink("Read the privacy notice", "privacy")],
                },
            ],
            Next =
            [
                new SiteLink("Check channel connections", "connect"),
                new SiteLink("Open the server owner guide", "server-owners"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/moderators",
            Eyebrow = "Moderator access",
            Title = "Let moderators help safely",
            Summary =
                "Channel owners control moderator access to BlokeBot. They can allow all current Twitch moderators or use access lists.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/dashboard/phone-dark-channel-setup.png",
                LightPhoneSource: "media/dashboard/phone-light-channel-setup.png",
                DarkLaptopSource: "media/dashboard/laptop-dark-channel-setup.png",
                LightLaptopSource: "media/dashboard/laptop-light-channel-setup.png",
                PhoneAlt: "Channel setup with readiness and access controls for the selected channel.",
                LaptopAlt: "Channel setup with readiness and access controls for the selected channel.",
                "Manage moderator access from Channel setup for the selected channel."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose an access mode",
                    Steps =
                    [
                        "Open Channel setup and expand Moderator help.",
                        "Turn on Let moderators help with this channel.",
                        "Choose All mods or Allowed list only, then maintain allowed and blocked names as needed.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Know the boundary",
                    Bullets =
                    [
                        "Moderator access applies only to the selected channel.",
                        "An allowed current Twitch moderator can operate tools and change the selected channel's configuration.",
                        "BlokeBot rechecks Twitch moderator authority at sensitive changes and does not trust the role for the whole login session.",
                        "If you turn moderator help off, BlokeBot keeps the saved lists for later.",
                    ],
                    Note =
                        "If Twitch removes your moderator role, BlokeBot can refuse a later change while the page remains open. Refresh the page or choose another channel. Do not ask the server owner to bypass Twitch authority.",
                },
            ],
            Next = [new SiteLink("Manage channels", "channels")],
        };

        yield return new SiteGuidePage
        {
            Route = "/server-owners",
            Eyebrow = "Technical operations",
            Title = "Run a BlokeBot server",
            Summary =
                "Install the service, connect one Twitch application, provide trusted HTTPS and keep its private state backed up.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/dashboard/phone-dark-admin.png",
                LightPhoneSource: "media/dashboard/phone-light-admin.png",
                DarkLaptopSource: "media/dashboard/laptop-dark-admin.png",
                LightLaptopSource: "media/dashboard/laptop-light-admin.png",
                PhoneAlt: "The BlokeBot admin page with the controls that server owners can use.",
                LaptopAlt: "The BlokeBot admin page with the controls that server owners can use.",
                "The admin page configures the server. It includes channel allow lists and manual channel setup."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "1. Install and run",
                    Paragraphs =
                    [
                        "Choose Nix, Docker or a source checkout. Give BlokeBot a persistent data directory. Start the dashboard on a private address.",
                    ],
                    Links =
                    [
                        new SiteLink("Choose the main database", "server-owners/database"),
                        new SiteLink("Choose an installation route", "install"),
                        new SiteLink(
                            "Installation technical details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/Installation"
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "2. Create the Twitch application",
                    Paragraphs =
                    [
                        "Create one Website Integration application in the Twitch Developer Console. Register both public HTTPS callbacks. Provide its Client ID and Client Secret to BlokeBot. Do not put the secret in source.",
                    ],
                    Code =
                        "https://bot.example.com/auth/twitch/callback\nhttps://bot.example.com/oauth/callback",
                    Links =
                    [
                        new SiteLink(
                            "Open the Twitch Developer Console",
                            "https://dev.twitch.tv/console/apps"
                        ),
                        new SiteLink(
                            "Twitch application and callback details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/Twitch-Identity-and-OAuth"
                        ),
                    ],
                    Note = "The callback text must exactly match the scheme, host, port and path.",
                },
                new SiteGuideSection
                {
                    Heading = "3. Add HTTPS",
                    Paragraphs =
                    [
                        "Give the public dashboard a trusted HTTPS address. A typical deployment keeps BlokeBot on loopback. Caddy, nginx or another reverse proxy forwards the original scheme and host.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "HTTPS and reverse-proxy details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/HTTPS-and-Reverse-Proxy"
                        ),
                    ],
                    Note =
                        "Register the public HTTPS callbacks with Twitch. The proxy's private HTTP address is unreachable from Twitch.",
                },
                new SiteGuideSection
                {
                    Heading = "4. Keep state private and backed up",
                    Paragraphs =
                    [
                        "BlokeBot keeps local state beside its main database configuration. Restrict the state to the service account and back it up with the database.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "State locations and backup details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/State-and-Secrets#state-and-backups"
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "5. Custom-bot credentials",
                    Paragraphs =
                    [
                        "Custom-bot encryption needs no operator configuration. ASP.NET Core manages Data Protection keys automatically in private persistent application state. Windows protects those keys with DPAPI LocalMachine.",
                        "A database backup does not expose reusable custom-bot tokens. Theft of the full state directory or active host is outside that boundary.",
                        "If an upgrade finds old plaintext custom-bot credentials, it deletes them and disables that custom bot. It alerts the channel owner.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "Custom-bot security details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/State-and-Secrets#custom-bot-credentials"
                        ),
                    ],
                },
            ],
            Next = [new SiteLink("Return to the user guide", "guide")],
        };
    }
}
