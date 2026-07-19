namespace BlokeBot.Site.Content;

internal static class SiteGuideCatalog
{
    private static readonly IReadOnlyDictionary<string, SiteGuidePage> _pages = CreatePages()
        .ToDictionary(page => page.Route, StringComparer.Ordinal);

    internal static IReadOnlyList<SiteGuidePage> All { get; } =
        SiteRoutes.GuideTopics.Select(route => _pages[route]).ToArray();

    internal static SiteGuidePage Get(string route)
    {
        return _pages.TryGetValue(route, out var page)
            ? page
            : throw new InvalidOperationException($"No guide content is registered for '{route}'.");
    }

    private static IEnumerable<SiteGuidePage> CreatePages()
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
                        "Select Find channels again if a newly available channel is missing.",
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
                "The menu follows the selected channel and shows only the tools that channel has turned on.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-home.png",
                LightPhoneSource: "media/phone-light-home.png",
                DarkLaptopSource: "media/laptop-dark-home.png",
                LightLaptopSource: "media/laptop-light-home.png",
                PhoneAlt: "BlokeBot dashboard showing the selected Sample Channel, channel setup and chat-tool navigation.",
                LaptopAlt: "BlokeBot dashboard showing the selected Sample Channel, channel setup and chat-tool navigation.",
                "The selected channel appears in the top bar; its enabled tools appear in the menu."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Check the top bar first",
                    Bullets =
                    [
                        "Bot status shows whether the selected channel is ready or needs attention.",
                        "My channel and Other channels change which channel you are working on.",
                        "Alerts opens current problems; the account menu shows your role and Sign out.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Use the menu",
                    Bullets =
                    [
                        "Home gives a short introduction and public leaderboard shortcut.",
                        "Channel setup contains Twitch connections, moderator access and tool switches.",
                        "Guessing, Points and Custom commands appear only when enabled for this channel.",
                    ],
                    Paragraphs =
                    [
                        "Always confirm the selected channel before saving. A change for one channel does not change another.",
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
                DarkPhoneSource: "media/phone-dark-channel-setup.png",
                LightPhoneSource: "media/phone-light-channel-setup.png",
                DarkLaptopSource: "media/laptop-dark-channel-setup.png",
                LightLaptopSource: "media/laptop-light-channel-setup.png",
                PhoneAlt: "Channel setup for Sample Channel showing Twitch chat, readiness and bot status panels.",
                LaptopAlt: "Channel setup for Sample Channel showing Twitch chat, readiness and bot status panels.",
                "The selected channel appears in the top bar; its enabled tools appear in the menu."
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
                        "Use the channel selector whenever you help more than one channel. You may be allowed to use a channel's tools without permission to change its setup.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Connect this channel", "connect"),
                new SiteLink("Let moderators help", "moderators"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/connect",
            Eyebrow = "Twitch connection",
            Title = "Connect the bot to your channel",
            Summary =
                "BlokeBot explains which Twitch account or permission is needed and keeps the bot stopped until the channel is ready.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-channel-setup.png",
                LightPhoneSource: "media/phone-light-channel-setup.png",
                DarkLaptopSource: "media/laptop-dark-channel-setup.png",
                LightLaptopSource: "media/laptop-light-channel-setup.png",
                PhoneAlt: "Channel setup showing a Connect channel action beside Twitch chat and an offline bot status.",
                LaptopAlt: "Channel setup showing a Connect channel action beside Twitch chat and an offline bot status.",
                "Connection actions and readiness messages appear beside the channel's bot controls."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Connect the channel account",
                    Steps =
                    [
                        "Select the channel and open Channel setup.",
                        "Under Twitch chat, select Connect channel.",
                        "Complete Twitch as the channel owner. This grants the channel-level permission used by the bot.",
                        "Return to the same selected channel and confirm that the channel connection is ready.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Connect the bot account",
                    Steps =
                    [
                        "Sign out of Twitch in the connection pop-up if it is using your normal account.",
                        "Select Connect bot and sign in as the dedicated bot account named by BlokeBot.",
                        "Make the bot a moderator in your Twitch channel. This is the recommended setup for announcements and follower-only chat.",
                        "Select Start bot when the controls become available.",
                        "Use Stop bot when you intentionally want BlokeBot out of chat.",
                    ],
                    Note =
                        "Twitch does not provide an API that lets BlokeBot make its bot account follow your channel. If the channel uses follower-only chat and the bot is not a moderator, sign into Twitch as the bot and follow the channel manually. BlokeBot checks this state and alerts when follower-only delivery is rejected.",
                },
                new SiteGuideSection
                {
                    Heading = "Reconnect the right identity",
                    Paragraphs =
                    [
                        "Use the reconnect action beside the connection that is stale. Channel and bot connections are different OAuth grants; reconnecting one does not repair the other.",
                        "If Twitch used the wrong account, close the result window, sign out of Twitch in that browser context and repeat the account-specific action.",
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
                "Turn on commands, guessing or points independently. The dashboard adds the matching pages without changing other tools.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn a tool on",
                    Steps =
                    [
                        "Choose the correct channel and open Channel setup.",
                        "Turn on the tool you want under Chat tools.",
                        "Open the new navigation item and finish its settings before using it live.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "What you can add",
                    Links =
                    [
                        new SiteLink("Commands and scheduled messages", "commands"),
                        new SiteLink("Guessing games", "guessing"),
                        new SiteLink("Points", "points"),
                        new SiteLink("Giveaways", "giveaways"),
                        new SiteLink("Public leaderboards", "leaderboards"),
                    ],
                },
            ],
            Next = [new SiteLink("Create a command", "commands")],
        };

        yield return new SiteGuidePage
        {
            Route = "/commands",
            Eyebrow = "Custom commands",
            Title = "Create commands and scheduled messages",
            Summary =
                "Save reusable bot replies, connect them to chat words, keep counters and schedule reminders.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-custom-commands.png",
                LightPhoneSource: "media/phone-light-custom-commands.png",
                DarkLaptopSource: "media/laptop-dark-custom-commands.png",
                LightLaptopSource: "media/laptop-light-custom-commands.png",
                PhoneAlt: "Custom commands settings showing saved replies and a hydration reminder.",
                LaptopAlt: "Custom commands settings showing saved replies and a hydration reminder.",
                "Replies are reusable messages that commands and scheduled messages can send."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Create a chat reply and command",
                    Steps =
                    [
                        "Open Custom commands, then Settings, and stay on the Commands tab.",
                        "Add a command, enter its command words without the exclamation mark and choose who may use it.",
                        "Open Message library, add a reply with at least one message, then return to Commands.",
                        "Choose the saved reply under What happens and select Save changes.",
                    ],
                    Paragraphs =
                    [
                        "Replies can include viewer, channel and argument placeholders. The Message library keeps reusable text separate from command structure.",
                    ],
                    Note =
                        "A command without a message cannot be saved. BlokeBot opens the relevant tab or section, focuses the field and shows the validation message instead of silently discarding the command.",
                },
                new SiteGuideSection
                {
                    Heading = "Add a counter, scheduled message or Twitch announcement",
                    Bullets =
                    [
                        "Counters let a command change and report a saved number.",
                        "Scheduled chat sends a saved reply on a timer, after chat activity or once a week.",
                        "Twitch announcement uses Twitch's coloured announcement surface. The bot must currently be a moderator and authorised for announcements.",
                        "If a scheduled send cannot happen, open its Alerts section and follow the displayed next action.",
                    ],
                },
            ],
            Next = [new SiteLink("Choose another tool", "tools")],
        };

        yield return new SiteGuidePage
        {
            Route = "/guessing",
            Eyebrow = "Guessing games",
            Title = "Set up and run a guessing game",
            Summary =
                "Create reusable round types and answers, collect one guess per viewer, then record the winning answer.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-guessing-workflow.webp",
                LightPhoneSource: "media/phone-light-guessing-workflow.webp",
                DarkLaptopSource: "media/laptop-dark-guessing-workflow.webp",
                LightLaptopSource: "media/laptop-light-guessing-workflow.webp",
                PhoneAlt: "Animated BlokeBot guessing dashboard moving through a live round workflow.",
                LaptopAlt: "Animated BlokeBot guessing dashboard moving through a live round workflow.",
                "The live dashboard keeps round status, votes, answers and winner controls together."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare a round type",
                    Steps =
                    [
                        "Turn on Guessing game and open its Settings page.",
                        "Create a round type, add every accepted answer and choose any winner point reward.",
                        "Review the chat commands and bot replies, then save.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run the round",
                    Steps =
                    [
                        "Open the Guessing game Dashboard and choose the round type.",
                        "Start the round, let viewers guess, then stop guessing.",
                        "Enter the winning answer exactly as configured and declare the winner.",
                    ],
                    Paragraphs =
                    [
                        "History and Leaderboard keep completed results. Public leaderboards can share rankings without dashboard access.",
                    ],
                },
            ],
            Next = [new SiteLink("Share a leaderboard", "leaderboards")],
        };

        yield return new SiteGuidePage
        {
            Route = "/points",
            Eyebrow = "Viewer points",
            Title = "Set up and manage points",
            Summary =
                "Give each viewer a channel balance that can be checked, transferred, adjusted, gambled or awarded as a prize.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-points-settings.png",
                LightPhoneSource: "media/phone-light-points-settings.png",
                DarkLaptopSource: "media/laptop-dark-points-settings.png",
                LightLaptopSource: "media/laptop-light-points-settings.png",
                PhoneAlt: "Points settings showing the point label, gambling chance, cooldown and chat command words.",
                LaptopAlt: "Points settings showing the point label, gambling chance, cooldown and chat command words.",
                "Points settings define the channel's terminology, gambling rules and command words."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Configure points",
                    Steps =
                    [
                        "Turn on Points and open Points Settings.",
                        "Choose the point label, gambling chance and wait between gambles.",
                        "Review command words and bot replies, then save.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage balances",
                    Bullets =
                    [
                        "Search for a viewer from the Points Dashboard.",
                        "Move points between viewers, add points or take points away after checking both names and the amount.",
                        "Use Recent changes to confirm adjustments and prizes. Delete balance only when the whole record should go.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a points giveaway", "giveaways")],
        };

        yield return new SiteGuidePage
        {
            Route = "/giveaways",
            Eyebrow = "Points giveaways",
            Title = "Run a giveaway",
            Summary =
                "Open timed entry while the channel is live, choose eligibility and winner count, then award random point prizes.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-points-settings.png",
                LightPhoneSource: "media/phone-light-points-settings.png",
                DarkLaptopSource: "media/laptop-dark-points-settings.png",
                LightLaptopSource: "media/laptop-light-points-settings.png",
                PhoneAlt: "Points settings page where channel point commands and giveaway rules are configured.",
                LaptopAlt: "Points settings page where channel point commands and giveaway rules are configured.",
                "Giveaway rules live on the Points settings page alongside the channel's points configuration."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Set the rules",
                    Steps =
                    [
                        "Open Points Settings and expand Giveaways.",
                        "Set entry time, prize range, winner count, eligibility and the wait between giveaways.",
                        "Save the settings before going live.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Start and finish",
                    Steps =
                    [
                        "While the Twitch channel is live, open the Points Dashboard and select Start in Giveaway.",
                        "Tell viewers to use the configured join command; each eligible viewer can enter once.",
                        "Select End to draw winners and award prizes, or Cancel to stop without winners.",
                    ],
                    Paragraphs =
                    [
                        "If Start is unavailable, check stream status, an existing giveaway and the cooldown shown by the dashboard.",
                    ],
                },
            ],
            Next = [new SiteLink("Review points", "points")],
        };

        yield return new SiteGuidePage
        {
            Route = "/leaderboards",
            Eyebrow = "Public results",
            Title = "Share a public leaderboard",
            Summary =
                "Viewers can open read-only guessing or points rankings without permission to manage the channel.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-guessing-leaderboard.png",
                LightPhoneSource: "media/phone-light-guessing-leaderboard.png",
                DarkLaptopSource: "media/laptop-dark-guessing-leaderboard.png",
                LightLaptopSource: "media/laptop-light-guessing-leaderboard.png",
                PhoneAlt: "Public guessing leaderboard showing players, correct guesses, rounds and accuracy.",
                LaptopAlt: "Public guessing leaderboard showing players, correct guesses, rounds and accuracy.",
                "Public leaderboards turn completed channel activity into a shareable read-only ranking."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Open and share it",
                    Steps =
                    [
                        "From Home or the sign-in page, choose Guessing or Points under Public leaderboard.",
                        "Enter the Twitch channel name and open the leaderboard.",
                        "Copy the browser address into Twitch panels, chat or community pages.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "If it is empty",
                    Bullets =
                    [
                        "Points rankings need viewer balances.",
                        "Guessing rankings need completed rounds.",
                        "The related tool must be on, and the channel must exist in BlokeBot.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a guessing game", "guessing")],
        };

        yield return new SiteGuidePage
        {
            Route = "/troubleshooting",
            Eyebrow = "Help and recovery",
            Title = "Understand a warning or offline bot",
            Summary =
                "Start with the message on the page. BlokeBot normally identifies the missing channel, permission, connection or tool.",
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
                        "Wrong account: repeat the specific Channel or Bot connection action after signing out of Twitch in the pop-up.",
                        "Moderator-only action unavailable: confirm the bot is still a moderator, then reconnect if its grant predates the required scope.",
                        "Follower-only rejection: make the bot a moderator or manually follow the channel while signed in as the bot account.",
                        "Announcement rejected: confirm the bot is a moderator and reconnect it to grant moderator:manage:announcements.",
                        "Dashboard script or stylesheet missing: ask the server owner to verify the reverse proxy path and static assets rather than repeatedly reconnecting Twitch.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Ask for useful help",
                    Paragraphs =
                    [
                        "If the problem remains, send the page name, selected channel, approximate time, alert text and any support reference to the server owner. Do not send Twitch secrets or tokens.",
                    ],
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
                "Channel owners decide whether current Twitch moderators can use BlokeBot and whether access is open to all moderators or limited by lists.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-channel-setup.png",
                LightPhoneSource: "media/phone-light-channel-setup.png",
                DarkLaptopSource: "media/laptop-dark-channel-setup.png",
                LightLaptopSource: "media/laptop-light-channel-setup.png",
                PhoneAlt: "Channel setup containing readiness and access controls for the selected channel.",
                LaptopAlt: "Channel setup containing readiness and access controls for the selected channel.",
                "Moderator access is managed from Channel setup for the channel currently selected."
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
                        "Turning moderator help off keeps the saved lists for later.",
                    ],
                    Note =
                        "If Twitch removes your moderator role, a later change can be refused even while the page is still open. Refresh the page or choose another channel; do not ask the server owner to bypass Twitch authority.",
                },
            ],
            Next = [new SiteLink("Manage channels", "channels")],
        };

        yield return new SiteGuidePage
        {
            Route = "/server-owners",
            Eyebrow = "Technical operations",
            Title = "Install and operate BlokeBot",
            Summary =
                "Create the Twitch application, provide public HTTPS, keep credentials out of source and operate the bot and help site as separate services.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-admin.png",
                LightPhoneSource: "media/phone-light-admin.png",
                DarkLaptopSource: "media/laptop-dark-admin.png",
                LightLaptopSource: "media/laptop-light-admin.png",
                PhoneAlt: "The BlokeBot admin page, showing you which controls are available to server owners.",
                LaptopAlt: "The BlokeBot admin page, showing you which controls are available to server owners.",
                "The admin page allows you to configure the server to suit your needs, including allow lists for channels and manual channel setup."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "1. Create the Twitch developer application",
                    Steps =
                    [
                        "Open the Twitch Developer Console and sign in with the account that will own the application.",
                        "Create an application with a recognisable name and choose Website Integration as its category.",
                        "Add both HTTPS OAuth redirect URLs shown below, replacing bot.example.com with the public dashboard host.",
                        "Copy the Client ID and generate a Client Secret. Treat the secret as a password and rotate it after accidental disclosure.",
                    ],
                    Code =
                        "https://bot.example.com/auth/twitch/callback\nhttps://bot.example.com/oauth/callback",
                    Links =
                    [
                        new SiteLink(
                            "Open the Twitch Developer Console",
                            "https://dev.twitch.tv/console/apps"
                        ),
                    ],
                    Note =
                        "The registered callback text must match exactly, including scheme, host, port and path. The first callback signs dashboard users in; the second connects the bot account.",
                },
                new SiteGuideSection
                {
                    Heading = "2. Configure the bot identity",
                    Paragraphs =
                    [
                        "The same Twitch application credentials are used for dashboard sign-in and bot authorization. Configure a dedicated bot account login and the public bot callback URI.",
                    ],
                    Code = """
                        TwitchBot__Identity__BotUsername=your_bot_login
                        TwitchBot__Identity__ClientId=your_application_client_id
                        TwitchBot__Identity__ClientSecret=keep_this_out_of_source
                        TwitchBot__Identity__RedirectUri=https://bot.example.com/oauth/callback
                        BlokeBot__BotAdmins__0=your_twitch_login
                        """,
                    Note =
                        "Environment variables use double underscores for configuration nesting. With NixOS, put ClientSecret in services.blokebot.environmentFile; do not put it in the ordinary environment attribute because Nix store paths are world-readable.",
                },
                new SiteGuideSection
                {
                    Heading = "3. Put trusted HTTPS in front",
                    Paragraphs =
                    [
                        "For a public service, bind BlokeBot to loopback and terminate HTTPS in Caddy, nginx or another reverse proxy. Forward the original scheme and host so OAuth builds the public callback correctly.",
                    ],
                    Code =
                        "blokebot serve --host 127.0.0.1 --port 8080 --data-dir /var/lib/blokebot",
                    Note =
                        "Twitch callbacks must use the public HTTPS origin. Do not register the reverse proxy's internal HTTP address.",
                },
                new SiteGuideSection
                {
                    Heading = "Local Kestrel HTTPS configuration",
                    Paragraphs =
                    [
                        "For local setup, export a development certificate and point a separate JSON configuration file at it. This example serves https://localhost:8080 and keeps the certificate password in local-only configuration.",
                    ],
                    Code = """
                        {
                          "Kestrel": {
                            "Endpoints": {
                              "Https": {
                                "Url": "https://localhost:8080",
                                "Certificate": {
                                  "Path": "/home/you/.aspnet/https/blokebot.pfx",
                                  "Password": "change-me"
                                }
                              }
                            }
                          }
                        }
                        """,
                    Note =
                        "Keep this file outside source control and replace /home/you with the real absolute path. The browser, Kestrel configuration and Twitch callbacks must all use localhost consistently.",
                },
                new SiteGuideSection
                {
                    Heading = "Create the local certificate and serve",
                    Paragraphs =
                    [
                        "Export and trust the standard .NET development certificate, then pass the JSON file to BlokeBot. When Kestrel endpoints are configured, do not add conflicting --host or --port flags.",
                    ],
                    Code = """
                        dotnet dev-certs https --trust
                        dotnet dev-certs https -ep "$HOME/.aspnet/https/blokebot.pfx" -p change-me
                        blokebot serve --config ./blokebot.local.json --data-dir ./.local/blokebot
                        """,
                    Note =
                        "The browser must trust the certificate. A certificate for 127.0.0.1 does not automatically cover localhost, and vice versa; open the same host name configured in the certificate and Twitch callback.",
                },
                new SiteGuideSection
                {
                    Heading = "4. Protect and back up state",
                    Bullets =
                    [
                        "Give only the service account access to the data directory.",
                        "Back up blokebot.db and twitch.tokens.json together while the service is stopped or from one consistent filesystem snapshot.",
                        "Keep the help site stateless and run it separately from the bot service.",
                        "Review active alerts and service logs after every update before promoting the new generation.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "5. Verify the service",
                    Steps =
                    [
                        "Run blokebot version and record the deployed revision.",
                        "Open the public HTTPS dashboard and confirm the Blazor client connects without script or stylesheet errors.",
                        "Sign in, select a test channel, connect the channel and bot identities, then start and stop the bot.",
                        "Open the help site independently and verify its media and internal links under the configured path base.",
                    ],
                    Links =
                    [
                        new SiteLink("Choose an installation route", "install"),
                        new SiteLink(
                            "Review the source repository",
                            "https://github.com/alsi-lawr/BlokeBot"
                        ),
                    ],
                },
            ],
            Next = [new SiteLink("Return to the user guide", "guide")],
        };
    }
}
