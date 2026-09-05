namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateHelpAdministrationPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/troubleshooting",
            Eyebrow = "Help and recovery",
            Title = "Warnings and an offline bot",
            Summary =
                "Start with the message on the page. Use the reported problem to find the recovery action.",
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "BlokeBot normally identifies an absent channel.",
                        "BlokeBot normally identifies an absent permission.",
                        "BlokeBot normally identifies an absent connection.",
                        "BlokeBot normally identifies an absent tool.",
                    ],
                    Heading = "Quick checks",
                    Steps =
                    [
                        "Check the selected channel.",
                        "Open Channel setup.",
                        "Check the tool switch and bot status.",
                        "Complete the Twitch action offered by the page.",
                        "Open Alerts.",
                        "Read the newest active alert.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Common Twitch failures",
                    Bullets =
                    [
                        "If Twitch uses the wrong account, sign out of Twitch in the pop-up. Repeat the specific Channel or Bot connection action.",
                        "If a moderator-only action is unavailable, check that the bot is still a moderator. If its grant predates the required scope, reconnect.",
                        "For a follower-only rejection, make the bot a moderator or follow the channel manually as the bot account.",
                        "If Twitch rejects an announcement, check that the bot is still a moderator. Reconnect the bot account with the action in Channel setup.",
                        "If a dashboard script or stylesheet is absent, ask the server owner to check proxy paths and static assets. Do not reconnect Twitch repeatedly.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Include the time.",
                        "Include the alert text.",
                        "Include the support reference.",
                    ],
                    Heading = "Information for support",
                    LegacyAnchor = "ask-for-useful-help",
                    Paragraphs =
                    [
                        "If the problem remains, send the page name and channel to the server owner. Do not send Twitch secrets or tokens.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Plugin failures",
                    Bullets =
                    [
                        "After Remove, no plugin settings remain.",
                        "After Remove, no private data remains.",
                        "After Remove, no page remains.",
                        "After Remove, no flow remains.",
                        "After Remove, no run history remains.",
                        "After Remove, no receipt remains.",
                        "After Remove, no context remains.",
                        "After Remove, no latest outcome remains.",
                        "If the marketplace is unavailable, wait for one successful catalog refresh. If Admin shows an older snapshot, search can still use it.",
                        "If Install or Update is unavailable, check GitHub access and the tag. An absent or moved tag needs a corrected catalog entry or package release.",
                        "For a validation fault, correct the reported cause.",
                        "The manifest is a possible cause of a validation fault.",
                        "Package declarations are a possible cause of a validation fault.",
                        "Target support is a possible cause of a validation fault.",
                        "Also check paths.",
                        "Also check links.",
                        "Also check collisions.",
                        "Also check size limits.",
                        "For a migration or activation fault, read the latest operation message. The old code cannot resume after durable migration starts.",
                        "For a worker fault, BlokeBot tries one automatic restart. After the next fault, correct the cause. Select Restart.",
                        "Needs attention: complete the required Twitch scope or EventSub action. Independent work can remain available.",
                        "If a command is absent, check built-in and custom commands first. Those routes shadow a plugin command.",
                        "If BlokeBot rejects feature enablement, correct the reported cause.",
                        "A possible cause is a plugin command collision.",
                        "A possible cause is an invalid automatic flow.",
                        "A possible cause is an existing flow name.",
                        "If an automatic flow is deleted, disable the feature. Enable it again to create the flow again.",
                    ],
                    Links =
                    [
                        new SiteLink("Recover a plugin installation", "server-owners/plugins"),
                        new SiteLink("Recover a channel plugin feature", "plugins"),
                    ],
                    Note = "Remove is permanent.",
                },
                new SiteGuideSection
                {
                    Heading = "Privacy and data requests",
                    LegacyAnchor = "privacy-saved-preferences-and-data-requests",
                    Paragraphs =
                    [
                        "The privacy notice is the authoritative description of stored data.",
                    ],
                    Bullets =
                    [
                        "The privacy notice covers Twitch data for both origins.",
                        "The privacy notice covers cookies for both origins.",
                        "The privacy notice covers browser storage for both origins.",
                        "The privacy notice covers retention for both origins.",
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
                        "Open Channel setup.",
                        "Expand Moderator help.",
                        "Turn on Let moderators help with this channel.",
                        "Select All mods or Allowed list only.",
                        "Maintain allowed and blocked names as necessary.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Moderator authority",
                    LegacyAnchor = "know-the-boundary",
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
            Summary = "Operate the BlokeBot server.",
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
                    Bullets =
                    [
                        "Choose an installation method.",
                        "Give BlokeBot a persistent data directory.",
                        "Start the dashboard on a private address.",
                        "Install the service.",
                        "Connect one Twitch application.",
                        "Provide trusted HTTPS.",
                        "Keep backups of the private state.",
                        "Installation method: Nix.",
                        "Installation method: Docker.",
                        "Installation method: a source checkout.",
                    ],
                    Heading = "Installation",
                    LegacyAnchor = "1-install-and-run",
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
                    Bullets =
                    [
                        "Create one Website Integration application in the Twitch Developer Console.",
                        "Register both public HTTPS callbacks.",
                        "Provide its Client ID and Client Secret to BlokeBot.",
                        "Do not put the secret in source.",
                        "The callback text must exactly match the scheme.",
                        "The callback text must exactly match the host.",
                        "The callback text must exactly match the port.",
                        "The callback text must exactly match the path.",
                    ],
                    Heading = "Twitch application",
                    LegacyAnchor = "2-create-the-twitch-application",
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
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Caddy can forward the original scheme and host.",
                        "nginx can forward the original scheme and host.",
                        "Another reverse proxy can forward the original scheme and host.",
                    ],
                    Heading = "Public HTTPS",
                    LegacyAnchor = "3-add-https",
                    Paragraphs =
                    [
                        "Give the public dashboard a trusted HTTPS address. A typical deployment keeps BlokeBot on loopback. Use a reverse proxy that forwards the original scheme and host.",
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
                    Heading = "Private state and backups",
                    LegacyAnchor = "4-keep-state-private-and-backed-up",
                    Paragraphs =
                    [
                        "BlokeBot keeps local state beside its main database configuration. Restrict the state to the service account. Back up the state with the database.",
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
                    Heading = "Custom-bot credentials",
                    LegacyAnchor = "5-custom-bot-credentials",
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
                .. ViewerPortalOperationSections(),
            ],
            Next = [new SiteLink("Return to the user guide", "guide")],
        };
    }
}
