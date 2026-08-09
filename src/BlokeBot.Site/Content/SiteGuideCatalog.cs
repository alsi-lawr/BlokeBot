namespace BlokeBot.Site.Content;

internal static class SiteGuideCatalog
{
    private static readonly IReadOnlyDictionary<string, SiteGuidePage> _pages = CreatePages()
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
                GuideLink("Browser Sources", "overlays"),
                GuideLink("Cues", "overlays#cues"),
                GuideLink("Media library", "overlays#media"),
            ]
        ),
        new(
            "Community interaction",
            [
                GuideLink("Request boards", "community/request-boards"),
                GuideLink("Play with viewers", "community/play-with-viewers"),
                GuideLink("Moments", "community/moments"),
            ]
        ),
        new(
            "Native Twitch",
            [
                GuideLink("Overview", "twitch-operations"),
                GuideLink("Shoutouts", "twitch-operations/shoutouts"),
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
            "Help and administration",
            [
                GuideLink("Troubleshooting", "troubleshooting"),
                GuideLink("Moderator access", "moderators"),
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
                "The navigation follows the selected channel, groups its tools by task and shows only the features that channel has turned on.",
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
                        "Channel setup contains connections, moderator access and feature switches.",
                        "Chat tools contains Request boards, Play with viewers and Moments for the selected channel, plus each enabled Native Twitch, Guessing, Points, Custom commands and Overlays feature.",
                        "Expand Native Twitch to move between its five focused task pages.",
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
                PhoneAlt: "Channel setup for Sample Channel showing separate Chat access and Twitch integration readiness.",
                LaptopAlt: "Channel setup for Sample Channel showing separate Chat access and Twitch integration readiness.",
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
                PhoneAlt: "Channel setup showing separate actions for Chat access and the Twitch integration.",
                LaptopAlt: "Channel setup showing separate actions for Chat access and the Twitch integration.",
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
                        "This is separate from Chat access. Disconnect removes BlokeBot's saved authorization for this channel; Reconnect replaces it.",
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
                        "Use the reconnect action beside the connection that is stale. Chat access, Twitch integration and bot-account connections are different approvals; reconnecting one does not repair the others.",
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
                "Every available Chat Tools feature is independently opt-in, so each channel can run only the tools it needs.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Start with every tool off",
                    Paragraphs =
                    [
                        "A new channel starts with all twelve available Chat Tools features disabled: Shoutouts, Polls, Clips & markers, Rewards & redemptions, Predictions, Request boards, Play with viewers, Moments, Overlays, Guessing game, Points and Custom commands.",
                        "Channels migrated from an earlier BlokeBot release keep their effective feature behavior. Review their switches after upgrading rather than assuming the new-channel default was applied.",
                    ],
                    Bullets =
                    [
                        "A disabled feature is hidden from navigation and does not accept chat commands, public-page actions, provider events or background work.",
                        "Disabling pauses the feature without deleting its saved configuration or data.",
                        "Re-enabling resumes from current state. BlokeBot does not replay commands, provider events or scheduled work missed while the feature was off.",
                    ],
                    Note =
                        "Channel setup uses the application-wide semantic-card layout. Its shared 12px clearance keeps every top-level feature card separate without adding page-specific spacing.",
                },
                new SiteGuideSection
                {
                    Heading = "Turn on only what the channel needs",
                    Steps =
                    [
                        "Choose the correct channel and open Channel setup.",
                        "Open Chat tools and turn on each feature this channel will use.",
                        "Save the feature changes.",
                        "Open the new navigation item and finish its settings before using it live.",
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
                        new SiteLink("Commands and scheduled messages", "commands"),
                        new SiteLink("Guessing games", "guessing"),
                        new SiteLink("Points", "points"),
                        new SiteLink("Giveaways", "giveaways"),
                        new SiteLink("Public leaderboards", "leaderboards"),
                        new SiteLink("Native Twitch", "twitch-operations"),
                        new SiteLink("Overlays and Browser Sources", "overlays"),
                    ],
                },
            ],
            Next = [new SiteLink("Set up a request board", "community/request-boards")],
        };

        yield return new SiteGuidePage
        {
            Route = "/overlays",
            Eyebrow = "Stream presentation · Browser Sources",
            Title = "Create Browser Sources for OBS",
            Summary =
                "Create private Browser Sources, preview and position their content, then keep each saved source working in OBS.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-overlay-sources.png",
                LightPhoneSource: "media/phone-light-overlay-sources.png",
                DarkLaptopSource: "media/laptop-dark-overlay-sources.png",
                LightLaptopSource: "media/laptop-light-overlay-sources.png",
                PhoneAlt: "BlokeBot Browser Sources on a phone with a saved source, Preview and appearance controls.",
                LaptopAlt: "BlokeBot Browser Sources with saved sources beside a Preview-first appearance editor.",
                "Browser Sources keeps the saved-source list beside the selected source's Preview and settings."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare the channel and OBS",
                    Bullets =
                    [
                        "Choose the channel you intend to show on stream. The owner or a permitted moderator can manage its Browser Sources.",
                        "Open Channel setup, turn on Overlays and save. Guessing, Points and Play with viewers must also be on before their matching Browser Sources can preview or display.",
                        "Use broadcasting software with web Browser Sources, such as OBS Studio.",
                        "Open Overlays under Chat tools. Sources, Cues and Media are fragment-addressed tabs of one page at /overlays#sources, /overlays#cues and /overlays#media in BlokeBot.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Create and protect a Browser Source",
                    Steps =
                    [
                        "On Sources, select New, enter a name, choose its type and complete the type-specific settings.",
                        "Select Create overlay. New opens an unsaved editor; creation happens only after this final action.",
                        "Copy the private Browser Source URL when it appears. BlokeBot can show it only after creation or rotation.",
                        "In OBS, add a Browser Source, paste the URL, set Width to 1920 and Height to 1080, and place it in the scene.",
                    ],
                    Note =
                        "Treat the private URL like a password. Keep it out of chat, screenshots, stream recordings and public notes. Rotate it immediately if it may have been shared; the old URL then stops working.",
                },
                new SiteGuideSection
                {
                    Heading = "Preview and edit appearance",
                    Bullets =
                    [
                        "Preview is above configuration. Choose Live for the current saved state or Representative to inspect a useful example before the real trigger happens.",
                        "The 1920 × 1080 canvas shows how the selected Browser Source will look in OBS. Drag anywhere on the selected body to move it; drag an edge or corner to resize it.",
                        "Use the arrow keys on the selected body for one-pixel movement, or Shift plus an arrow for ten pixels. The keyboard-operable edges and corners resize in the same increments.",
                        "Enter X, Y, Width and Height for precise geometry. Reset restores the type's default placement.",
                        "Unsaved geometry, styling and display choices update only the signed-in Preview. Select Save overlay before expecting OBS or another private Browser Source to change.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Use Advanced styling safely",
                    Paragraphs =
                    [
                        "Advanced styling starts collapsed. Overlay-local CSS applies only to the selected source. Available selectors are .overlay, .card, .accent, .kicker, .title, .detail and .result.",
                    ],
                    Bullets =
                    [
                        "Use the listed selectors to adjust colours and type without changing the BlokeBot dashboard or another Browser Source.",
                        "Imports, external URLs, markup, scripts, at-rules and selectors outside the selected Browser Source are rejected.",
                        "If CSS is rejected, correct the message shown and save again. The invalid change is not partly applied; the last saved appearance remains live.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Guessing rounds",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/phone-dark-overlay-guessing.png",
                        LightPhoneSource: "media/phone-light-overlay-guessing.png",
                        DarkLaptopSource: "media/laptop-dark-overlay-guessing.png",
                        LightLaptopSource: "media/laptop-light-overlay-guessing.png",
                        PhoneAlt: "Guessing Browser Source on a phone showing a representative open round in Preview.",
                        LaptopAlt: "Guessing Browser Source showing representative round choices, draggable Preview and settings.",
                        "Representative states let you place the Guessing Browser Source before a real round begins."
                    ),
                    Bullets =
                    [
                        "Turn on Guessing game, create the Browser Source, and choose whether the number of guesses is shown.",
                        "Use Representative to inspect Open, Closed and Result states. Save the appearance, then use the normal Guessing dashboard to start, stop and resolve a round.",
                        "The first configured answer is its main answer; aliases still work for viewers but do not change the displayed setup language.",
                        "If Preview is paused, restore both Overlays and Guessing game in Channel setup. Saved setup remains in place while either feature is off.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Active Giveaways",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/phone-dark-overlay-giveaway.png",
                        LightPhoneSource: "media/phone-light-overlay-giveaway.png",
                        DarkLaptopSource: "media/laptop-dark-overlay-giveaway.png",
                        LightLaptopSource: "media/laptop-light-overlay-giveaway.png",
                        PhoneAlt: "Giveaway Browser Source on a phone showing an active giveaway in Preview.",
                        LaptopAlt: "Giveaway Browser Source showing an active giveaway, compact display controls and appearance editing.",
                        "The active Giveaway Preview shows useful live content; without an active giveaway the Browser Source renders nothing."
                    ),
                    Bullets =
                    [
                        "Turn on Points, choose a Giveaway title, and use the compact controls for entrant count, close-time countdown and current join command.",
                        "Use Representative to inspect Open, Closing, Completed or Cancelled presentation, then save before running the giveaway from Points.",
                        "When there is no active giveaway, the Browser Source renders nothing. There is no viewer-facing idle card.",
                        "If it stays blank during an active giveaway, check that both Overlays and Points are on, the source is enabled, OBS has the current private URL and the giveaway is actually running.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Event feed",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/phone-dark-overlay-event-feed.png",
                        LightPhoneSource: "media/phone-light-overlay-event-feed.png",
                        DarkLaptopSource: "media/laptop-dark-overlay-event-feed.png",
                        LightLaptopSource: "media/laptop-light-overlay-event-feed.png",
                        PhoneAlt: "Event feed Browser Source on a phone showing a representative channel event and compact source controls.",
                        LaptopAlt: "Event feed Browser Source showing its Preview, waiting-card limit and enabled event sources.",
                        "One Event feed can present point awards, Guessing winners and Giveaway winners."
                    ),
                    Bullets =
                    [
                        "Choose the maximum waiting cards and what happens when the feed is full.",
                        "Turn point awards, Guessing winners and Giveaway winners on or off independently. Settings for an off source collapse without discarding its saved values.",
                        "For each enabled source, edit its message, priority and display time, then choose a Representative event to check the result.",
                        "If an expected card is missing, confirm its feature and event source are on. Re-enable the source for future events; events missed while it was off are not replayed.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Viewer Queue",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/phone-dark-overlay-viewer-queue.png",
                        LightPhoneSource: "media/phone-light-overlay-viewer-queue.png",
                        DarkLaptopSource: "media/laptop-dark-overlay-viewer-queue.png",
                        LightLaptopSource: "media/laptop-light-overlay-viewer-queue.png",
                        PhoneAlt: "Viewer Queue Browser Source on a phone showing a safe representative public queue summary.",
                        LaptopAlt: "Viewer Queue Browser Source showing a representative party and safe public queue summary in Preview.",
                        "Viewer Queue presents current, next and waiting viewers without exposing private party information."
                    ),
                    Bullets =
                    [
                        "Turn on Play with viewers and create a queue first. Joining its viewer page requires Twitch sign-in; there is no unsigned typed-login fallback.",
                        "Choose the queue and how many Current party and Next rows to show, then inspect Open, Ready check and Party selected examples.",
                        "Every configured field is optional and public on the viewer page and Viewer Queue overlay. Ask only for details that are safe to show on stream.",
                        "Configured entry answers are public on the Viewer Queue overlay. Lobby messages and moderator notes remain private, and the overlay does not show a wait estimate.",
                        "If the Preview is paused, restore both Overlays and Play with viewers. The current queue and saved appearance remain in place and missed animations are not replayed.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Cue player and live recovery",
                    Bullets =
                    [
                        "A Cue player is a Browser Source target for reusable Cues. Create and protect its private URL here, then build and test content on the Cues page.",
                        "Send test pulse checks the selected enabled source. A connected Preview or OBS source should respond without exposing its private URL.",
                        "If OBS is stale after a network loss or restart, reload that Browser Source so it reads the latest saved state and reconnects.",
                        "Rename keeps the private URL. Disable stops display while retaining setup. Rotate revokes the old URL. Delete permanently removes the source.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Build and trigger reusable Cues",
                    Anchor = "cues",
                    Paragraphs =
                    [
                        "Combine uploaded media, online media and web pages, then play the saved Cue through a Cue player Browser Source.",
                    ],
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/phone-dark-overlay-cues.png",
                        LightPhoneSource: "media/phone-light-overlay-cues.png",
                        DarkLaptopSource: "media/laptop-dark-overlay-cues.png",
                        LightLaptopSource: "media/laptop-light-overlay-cues.png",
                        PhoneAlt: "Cues page on a phone showing the saved Cue list and task-facing content editor.",
                        LaptopAlt: "Cues page showing attached saved Cues and editor columns with a reusable web layer.",
                        "Saved Cues and their editor stay together; test playback targets a Cue player Browser Source."
                    ),
                },
                new SiteGuideSection
                {
                    Heading = "Prepare a Cue player",
                    Steps =
                    [
                        "Turn on Overlays in Channel setup.",
                        "On Sources, create an enabled Cue player Browser Source, copy its private URL and add it to OBS at 1920 × 1080.",
                        "Open Cues at /overlays#cues and choose the saved Cue player under Test playback.",
                    ],
                    Note =
                        "If Overlays is off, Cue editing and playback are paused while saved Cues remain. Re-enabling does not play Cue requests that were missed while the feature was off.",
                },
                new SiteGuideSection
                {
                    Heading = "Build reusable content",
                    Steps =
                    [
                        "Select New cue, name it, set its total duration and choose what happens when another Cue is already playing.",
                        "Add uploaded media, online media or a web page. Reorder or remove content as needed; content lower in the list appears in front when stacking values match.",
                        "For each item, set when it starts, how long it plays, stacking order, left, top, width and height.",
                        "For image, audio and video content, set volume where available and choose Show all, Fill and crop or Stretch to fill.",
                        "Turn Cue enabled on and select Create cue or Save cue.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose overlap and test playback",
                    Bullets =
                    [
                        "Play after the current cue waits; Replace the current cue interrupts it; Skip while another cue plays drops the new request; Play at the same time overlaps them.",
                        "Choose an enabled Cue player and select Play test cue. Watch the embedded preview or OBS source for the saved result.",
                        "A test may wait briefly when the Cue player is disconnected. If it expires or is rejected, reconnect the player and try one fresh test rather than repeatedly adding requests.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Trigger a Cue from chat",
                    Steps =
                    [
                        "Open Custom commands and create or edit a command.",
                        "Under What happens, choose Play an overlay cue, then choose the Cue player, saved Cue, busy-player behavior and whether the chat reply is sent before or after the Cue is accepted.",
                        "Use the command's Test cue action, save the command, and send its main command word in chat.",
                    ],
                    Bullets =
                    [
                        "The command, Cue, Cue player and Overlays feature must all be enabled for playback.",
                        "A replaced or deleted Cue or target is reported as unavailable. Choose a current saved Cue and Browser Source, then save the command again.",
                        "The selected Cue can use safe chat context without exposing the private Browser Source URL.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover embedded content",
                    Bullets =
                    [
                        "Use complete secure addresses beginning with https:// for online media and web pages. A blocked, invalid or unreachable address must be corrected at its source.",
                        "Some sites prevent embedding. Use an embeddable page or media address instead; do not weaken Browser Source safety settings.",
                        "If uploaded media is missing or was replaced, open Media, repair that asset, return to the Cue and confirm the saved selection.",
                        "If the layer layout is wrong, correct its timing, order or percentage geometry, save, and run one new test.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage media for Cues",
                    Anchor = "media",
                    Paragraphs =
                    [
                        "Upload private channel media, preview saved files and repair the assets used by reusable Cues.",
                    ],
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/phone-dark-overlay-media.png",
                        LightPhoneSource: "media/phone-light-overlay-media.png",
                        DarkLaptopSource: "media/laptop-dark-overlay-media.png",
                        LightLaptopSource: "media/laptop-light-overlay-media.png",
                        PhoneAlt: "Media library on a phone showing private upload controls and the saved-media area.",
                        LaptopAlt: "Media library showing channel storage use, drag-and-drop upload and saved media management.",
                        "Media stays in the selected channel's private storage and is available to its Cues."
                    ),
                },
                new SiteGuideSection
                {
                    Heading = "Upload accepted browser media",
                    Steps =
                    [
                        "Turn on Overlays, choose the channel and open Media at /overlays#media.",
                        "Enter a clear Media name, then drag an image, audio or video file onto the Media file area, or choose it with the standard file picker.",
                        "Wait for the upload result and confirm the saved file appears under Saved media.",
                        "Open Cues, add Uploaded media and choose the saved name.",
                    ],
                    Note =
                        "Uploads stay in private channel storage. The page shows current use and capacity; another channel cannot select or serve this channel's media.",
                },
                new SiteGuideSection
                {
                    Heading = "Preview, replace or delete",
                    Bullets =
                    [
                        "Preview a saved image, audio or video before assigning it to a live Cue.",
                        "Replace file keeps the saved media item while updating its content for future playback. Test every Cue that depends on it before going live.",
                        "Delete only after checking dependent Cues. A Cue does not silently substitute another file when its selected media is unavailable.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover an upload or playback failure",
                    Bullets =
                    [
                        "Unsupported file: choose an ordinary browser-supported image, audio or video file rather than renaming an incompatible file.",
                        "Storage full: delete media that is no longer used or replace a large file with a smaller browser-ready version, then upload once.",
                        "Upload interrupted: keep the original file, reload the page and confirm whether a saved item exists before retrying.",
                        "Cue cannot play the file: preview the saved media, replace it when damaged or unsupported, then save and test the dependent Cue again.",
                        "Media page unavailable: restore Overlays in Channel setup. Saved media remains while the feature is off.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Create Custom Commands", "commands"),
                new SiteLink("Troubleshoot the bot", "troubleshooting"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/request-boards",
            Eyebrow = "Community interaction · Requests",
            Title = "Run a structured request board",
            Summary =
                "Collect consistent viewer suggestions, moderate their lifecycle and keep point charges and public status understandable.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-request-boards.png",
                LightPhoneSource: "media/community/phone-light-request-boards.png",
                DarkLaptopSource: "media/community/laptop-dark-request-boards.png",
                LightLaptopSource: "media/community/laptop-light-request-boards.png",
                PhoneAlt: "The Sample Channel public request board on a narrow screen, showing open rules and the start of the submission form.",
                LaptopAlt: "The Sample Channel Request boards moderator page, showing a saved Game night requests board and its configuration.",
                "Moderators configure the board at /requests; viewers use its public channel-and-board address."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose the right view",
                    Bullets =
                    [
                        "A channel owner or permitted moderator chooses the channel and opens Request boards at /requests to create, configure and moderate boards.",
                        "Open public board copies the viewer route /requests/{channel}/{board-name}. Anyone can read an existing board; a viewer signs in with Twitch to submit, vote or withdraw.",
                        "Chat participants can discover boards with !requests. Website and chat actions use the same board, limits, votes and request states.",
                    ],
                    Note =
                        "The words in braces describe a route value. Replace them with the channel login and the board's Command and URL name; do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Configure a board",
                    Steps =
                    [
                        "Select New, give the board a Command and URL name, title and description, then choose whether it accepts submissions.",
                        "Set the point cost, refund policy, active-submission limit, submission cooldown, voting switch and per-viewer vote limit.",
                        "Add only the fields participants need. A field can be Text, Link, Choose from a list, Number or Twitch clip link; set its label and applicable length, choice or number limits.",
                        "Select Save board, then use Open public board and read the Board rules exactly as a participant will see them.",
                    ],
                    Paragraphs =
                    [
                        "The public queue order explains the complete stable ordering. Moderator priority, votes and assigned queue position refine that order without hiding a different participant rule.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Submit and vote",
                    Bullets =
                    [
                        "On the public page, sign in with Twitch, complete Title and the configured fields, then select Submit request. The page shows the request number and its current public state.",
                        "In chat, use !request <board> <title> | field=value | category=value | tags=a,b. Required field keys come from that board's configuration.",
                        "Use !requestvote <request-number> to vote in chat, or Vote on the public board. Repeating the same vote does not add another vote.",
                        "A submitter can Withdraw an active request from the public page. Private moderator text is never shown there.",
                    ],
                    Note =
                        "A repeated delivery of the same chat submission is recognised and reports the existing request instead of creating or charging a second one.",
                },
                new SiteGuideSection
                {
                    Heading = "Moderate the lifecycle",
                    Steps =
                    [
                        "Review the submitted values and any possible-duplicate warning. Set public category, tags, priority and Public note when they help participants.",
                        "Move Awaiting review to Approved or Rejected. Approved requests can move to In queue or Accepted; In queue or Accepted requests can move to Completed. Submitters may Withdraw, and merged duplicates become Merged into another request.",
                        "Use Merge with the target request number when two entries are the same request. The public board keeps the Merged into another request outcome and the surviving request's combined support.",
                        "When the dashboard is not convenient, use !requestapprove, !requestreject, !requestqueue, !requestaccept or !requestcomplete followed by one request number.",
                        "To merge in chat, use !requestmerge <source-number> <target-number>.",
                    ],
                    Paragraphs =
                    [
                        "Private moderator note and Private rejection reason remain moderator-only. Put participant-facing context in Public note instead.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Points, failure and recovery",
                    Bullets =
                    [
                        "A non-zero cost is held from the viewer's available balance when the board accepts the initial submission, before moderator review. The reservation moves from No points charged to Points held, then finishes as Points refunded or Points charged. Never charge the viewer manually as well.",
                        "Completion charges the held points. A closure follows the selected policy: Never refund, Refund if rejected or withdrawn, or Refund if not fulfilled.",
                        "If validation, the cooldown, a limit or the balance rejects a submission, correct the message shown and submit once. If an outcome is already visible, reload before trying again.",
                        "If request state and points still disagree after reload, leave the request unchanged and send the channel, board name, request number, approximate time and visible message to the server owner. Do not share Twitch tokens or private notes.",
                    ],
                },
            ],
            Next = [new SiteLink("Build a play-with-viewers queue", "community/play-with-viewers")],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/play-with-viewers",
            Eyebrow = "Community interaction · Queues",
            Title = "Build fair play-with-viewers parties",
            Summary =
                "Open a queue, collect optional public entry details, run ready checks and deliver private lobby information without posting it publicly.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-play-with-viewers.png",
                LightPhoneSource: "media/community/phone-light-play-with-viewers.png",
                DarkLaptopSource: "media/community/laptop-dark-play-with-viewers.png",
                LightLaptopSource: "media/community/laptop-light-play-with-viewers.png",
                PhoneAlt: "The Sample Channel Community night party viewer page on a narrow screen, showing the public queue rule and optional entry form.",
                LaptopAlt: "The Sample Channel Play with viewers moderator page, showing a saved queue, party size and fair-selection configuration.",
                "The moderator route /queues and viewer route /queues/{channel}/{queue-name} share one live queue."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose identities and permissions",
                    Bullets =
                    [
                        "A channel owner or permitted moderator chooses the channel and opens Play with viewers at /queues.",
                        "Open viewer page uses /queues/{channel}/{queue-name}. Joining requires Twitch sign-in; there is no unsigned typed-login fallback.",
                        "Moderator controls, priorities, moderator notes and lobby messages are never shown on the public page. Configured entry fields and their answers are public.",
                    ],
                    Note =
                        "The words in braces describe a route value. Replace them with the channel login and the queue's Command and URL name; do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Configure and open the queue",
                    Steps =
                    [
                        "Select New, set the Command and URL name, Queue name, Game or activity and Party size.",
                        "Choose First to join or Viewers who played least recently. The viewer page states the resulting fair-selection rule before anyone joins.",
                        "Set Ready expiry, History retention and Skip/no-show exclusion. Add optional public entry fields and any required roles in role=count form.",
                        "Decide whether participant names may be shown publicly, turn Queue open on, save, then inspect Open viewer page at both wide and narrow widths.",
                    ],
                    Paragraphs =
                    [
                        "Every configured field is optional and public on the viewer page and Viewer Queue overlay, including fields such as platform, region, rank and preferred role. Lobby messages and moderator notes remain private.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Join from the page or chat",
                    Bullets =
                    [
                        "On the viewer page, fill the requested fields and select Join. Check position reports the current place; Leave removes the entry; I'm ready answers an active ready check.",
                        "In chat use !queue [queue], !join [queue] key=value, !leave [queue], !position [queue] and !ready [queue]. The queue name is optional when the channel has only one queue.",
                        "Joining twice keeps one entry. The signed-in Twitch identity is authoritative and prevents a second typed identity from creating a duplicate entry.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Select and run a party",
                    Steps =
                    [
                        "Review Waiting viewers and the visible next-candidate order. Entries move through Waiting, Awaiting response, Ready, Selected, Left queue, Skipped and Did not respond. Adjust Priority or Moderator note only when a documented channel rule requires it.",
                        "Start a Ready check for candidates. Participants must use I'm ready or !ready before Ready expiry; then select Select next party.",
                        "Use Keep party to retain the current group, Replace one for a single change, or Skip and No-show when someone cannot play. The configured exclusion prevents immediate re-entry after a skip or no-show.",
                        "Enter the Lobby message and select Whisper party. Confirm success before starting; never paste a private lobby code into public chat as a fallback.",
                    ],
                    Paragraphs =
                    [
                        "Moderators can use !queueopen [queue] and !queueclose [queue]. Close the queue while resolving a disputed selection so new joins do not move the visible candidate order.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover safely",
                    Bullets =
                    [
                        "If a participant misses Ready expiry, run a new ready check or use Replace one. Use No-show only when the channel's exclusion rule should apply.",
                        "If a whisper fails, verify that the bot connection can whisper and retry Whisper party only after the page reports the failure. Do not reveal the private message publicly.",
                        "If selection cannot satisfy required roles, leave the current party intact, adjust the waiting pool or role requirements and select again.",
                        "History retention removes old participation data after the configured period. Shortening it changes future fairness evidence, so record that channel decision before saving.",
                    ],
                },
            ],
            Next = [new SiteLink("Capture and recap community moments", "community/moments")],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/moments",
            Eyebrow = "Community interaction · Moments",
            Title = "Capture, moderate and recap moments",
            Summary =
                "Turn live viewer calls into one moderated Twitch clip or marker, then publish safe stream and weekly recaps.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-moments.png",
                LightPhoneSource: "media/community/phone-light-moments.png",
                DarkLaptopSource: "media/community/laptop-dark-moments.png",
                LightLaptopSource: "media/community/laptop-light-moments.png",
                PhoneAlt: "The Sample Channel stream recap on a narrow screen, showing an approved Community clutch save and a recorded vote.",
                LaptopAlt: "The Sample Channel Moments moderator page, showing live capture settings and an approved Community clutch save.",
                "Moderators work at /moments; approved entries appear in channel, stream and weekly recaps."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare a live channel",
                    Bullets =
                    [
                        "Choose the channel and open Moments at /moments. Captures require Twitch to report that channel live and require the selected channel's Twitch connection.",
                        "Set the Merge window from 15 to 300 seconds; 90 seconds is the default. Calls inside that window join the same stream moment and keep each contributor and suggestion.",
                        "Choose No reward, First viewer to request or All contributing viewers, set the amount, and decide whether a confirmed clip failure may fall back to a stream marker.",
                        "Save settings and check that the page shows Live stream with a stream identity before inviting viewers to capture.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Capture a candidate",
                    Bullets =
                    [
                        "A viewer uses !moment <suggested title> | category=<suggested category>. !clip accepts the same form.",
                        "A moderator can use Capture now. BlokeBot first requests a Twitch clip; marker fallback is used only after a confirmed clip failure and only when enabled.",
                        "Each call returns a public moment number. Repeated or concurrent calls for the same live moment converge instead of creating duplicate Twitch actions or duplicate rewards.",
                    ],
                    Note =
                        "BlokeBot links to Twitch media; it does not copy or host the clip or VOD.",
                },
                new SiteGuideSection
                {
                    Heading = "Moderate public metadata",
                    Steps =
                    [
                        "Review Creating clip, Clip ready, Marker ready or Could not create clip, together with the contributor count and viewer suggestions in Candidates.",
                        "Set Public title and Category, select Save details, then Approve. Reject keeps its reason private; Merge uses another moment number.",
                        "Use Open on Twitch to verify available media. Only approved moments appear in public recaps.",
                    ],
                    Paragraphs =
                    [
                        "Moderator note, rejection reason, audit text and Twitch failure details stay on the moderator view. Public recaps show only approved title, category, counts and the Twitch link.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Share recaps and votes",
                    Bullets =
                    [
                        "Weekly recap opens /moments/{channel} for the current ISO-UTC week. A stream recap uses /moments/{channel}/streams/{stream-id}.",
                        "A signed-in viewer votes with Twitch ID. An unsigned viewer may enter a normalized Twitch login. Each identity contributes at most one vote to a moment.",
                        "Finalize previous week records the winner for the completed week using vote count and stable ordering. Repeating finalization returns the same winner.",
                    ],
                    Note =
                        "Replace every value in braces with the channel login or Twitch stream identity shown by BlokeBot; do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Read Twitch states before retrying",
                    Bullets =
                    [
                        "Creating clip means Twitch has not finished the clip. Reload the same candidate later; do not capture again just to force an answer.",
                        "An ambiguous outcome means Twitch did not confirm whether its request completed. BlokeBot preserves that uncertainty and does not create a fallback marker from it.",
                        "Offline means wait for a live stream. If Twitch reports clips or VODs disabled, correct that Twitch setting or continue without marker fallback. If access is unauthorized, reconnect the selected channel account.",
                        "For a continuing failure, keep the candidate and send the selected channel, moment number, stream identity, approximate time and visible Twitch message to the server owner. Never send tokens or private moderation text.",
                    ],
                },
            ],
            Next = [new SiteLink("Use Native Twitch tools", "twitch-operations")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations",
            Eyebrow = "Native Twitch",
            Title = "Use Twitch channel tools",
            Summary =
                "Send shoutouts, run polls, save live moments, manage rewards and settle Predictions for the selected channel.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn Native Twitch on",
                    Steps =
                    [
                        "Choose the channel in the top bar and open Channel setup.",
                        "Open Chat tools, turn on Native Twitch and save the change.",
                        "Open Native Twitch in the Chat tools navigation, then choose Shoutouts, Polls, Clips & markers, Rewards & redemptions or Predictions.",
                    ],
                    Paragraphs =
                    [
                        "Turning Native Twitch off hides these pages and stops its automatic work. Saved templates, settings and history remain for the next time you turn it on.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Follow the action on the page",
                    Bullets =
                    [
                        "Shoutouts use the active bot account for Twitch's shoutout action. Automatic chat-message shoutouts use the public chat connection.",
                        "Polls, clips, markers, rewards, redemptions and Predictions use the selected channel's Twitch connection.",
                        "Rewards and Predictions require a Twitch Affiliate or Partner channel.",
                        "Use the ? button beside a page title for help without leaving the task you are doing.",
                    ],
                    Note =
                        "If a page asks you to reconnect, use its Reconnect to Twitch action and complete Twitch as the selected channel owner. Reconnecting the bot account does not repair a channel connection, and reconnecting the channel does not repair the bot account.",
                },
                new SiteGuideSection
                {
                    Heading = "When a result is uncertain",
                    Steps =
                    [
                        "Read the result shown on the page before repeating the action.",
                        "Reload the same page to check Twitch's current state and recent results.",
                        "Open Alerts if the page still needs attention.",
                        "Send the page name, selected channel, approximate time and alert text to the server owner. Never send Twitch tokens or secrets.",
                    ],
                },
            ],
            Next = [new SiteLink("Set up shoutouts", "twitch-operations/shoutouts")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/shoutouts",
            Eyebrow = "Native Twitch · Shoutouts",
            Title = "Send shoutouts and welcome raids",
            Summary =
                "Recommend another live channel now, or prepare one automatic welcome for each qualifying incoming raid.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-native-shoutouts.png",
                LightPhoneSource: "media/phone-light-native-shoutouts.png",
                DarkLaptopSource: "media/laptop-dark-native-shoutouts.png",
                LightLaptopSource: "media/laptop-light-native-shoutouts.png",
                PhoneAlt: "BlokeBot Shoutouts page on a phone showing a Twitch channel name field and the Send shoutout action.",
                LaptopAlt: "BlokeBot Shoutouts page showing the manual target and automatic raid shoutout settings.",
                "Manual shoutouts, automatic incoming-raid settings and recent outcomes stay on one task page."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Send a shoutout now",
                    Steps =
                    [
                        "Open Shoutouts, enter the other channel's Twitch name and select Send shoutout.",
                        "Wait for the result before trying again. The target must be live with viewers.",
                        "Use the displayed cooldown and Recent shoutouts to decide when another send is available.",
                    ],
                    Paragraphs =
                    [
                        "If BlokeBot asks for the bot account to be reconnected, restore that account's moderator role first, then reconnect it from Channel setup.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Welcome incoming raids automatically",
                    Steps =
                    [
                        "Automatic raid shoutouts are off by default. Open the section and turn them on when you are ready.",
                        "Set the minimum viewer count, then choose either a Native Twitch shoutout or a Chat message.",
                        "For a chat message, choose Regular, Pinned or Announcement. A pinned message can use a duration from 30 to 1,800 seconds or stay pinned until stream end; an announcement colour is Default, Blue, Green, Orange or Purple.",
                        "Write the message, check its preview and readiness note, then select Save automatic shoutouts.",
                    ],
                    Bullets =
                    [
                        "Message tokens include {twitch_handle}, {display_name}, {channel_url}, {viewer_count}, {last_game|fallback} and {stream_title|fallback}.",
                        "Last game and stream title need an inline fallback because Twitch may not provide them.",
                        "BlokeBot handles each eligible raid once. A failed native shoutout is not replaced with a chat message, and a failed announcement is not replaced with a regular message.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Check an automatic outcome",
                    Bullets =
                    [
                        "Automatic shoutout outcomes shows the newest raid results and why a send was skipped or incomplete.",
                        "Native shoutouts can be skipped while Twitch's cooldown is active.",
                        "A pinned message can be sent even when Twitch cannot pin it afterwards; the outcome states both parts.",
                        "Fix the connection or permission named by the outcome before the next raid. There is no retry or fallback action for an earlier raid.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a poll", "twitch-operations/polls")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/polls",
            Eyebrow = "Native Twitch · Polls",
            Title = "Ask viewers a question",
            Summary =
                "Save reusable poll questions, start one when you need it and watch the live vote totals.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-native-polls.png",
                LightPhoneSource: "media/phone-light-native-polls.png",
                DarkLaptopSource: "media/laptop-dark-native-polls.png",
                LightLaptopSource: "media/laptop-light-native-polls.png",
                PhoneAlt: "BlokeBot Polls page showing a saved question, current voting and poll controls.",
                LaptopAlt: "BlokeBot Polls page showing a saved question, current voting and poll controls.",
                "Saved questions, the active poll and recent results stay together."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Save a poll question",
                    Steps =
                    [
                        "Open New poll template and enter the question and choices.",
                        "Set how long voting should stay open and whether viewers may spend Channel Points on extra votes.",
                        "Select Save template. The saved question appears in Run a poll.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run the poll",
                    Steps =
                    [
                        "Select Start poll beside the saved question you want to use.",
                        "Watch the choices and vote totals in the active poll.",
                        "Let Twitch finish it at the end of the duration, or select End poll to finish early.",
                    ],
                    Paragraphs =
                    [
                        "Twitch allows one active poll. A poll started elsewhere appears here after reload, so check its question before ending it.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "If the poll is unavailable",
                    Bullets =
                    [
                        "Use Reconnect to Twitch on this page and complete Twitch as the selected channel owner.",
                        "Finish the active poll before starting another.",
                        "Reload before repeating an action when the displayed totals or result may be stale.",
                    ],
                },
            ],
            Next = [new SiteLink("Save a clip or marker", "twitch-operations/clips-markers")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/clips-markers",
            Eyebrow = "Native Twitch · Clips & markers",
            Title = "Save a live moment",
            Summary =
                "Create a shareable clip now or leave a private marker to find in the stream recording later.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-native-clips-markers.png",
                LightPhoneSource: "media/phone-light-native-clips-markers.png",
                DarkLaptopSource: "media/laptop-dark-native-clips-markers.png",
                LightLaptopSource: "media/laptop-light-native-clips-markers.png",
                PhoneAlt: "BlokeBot Clips and markers page showing clip creation, stream marker and recent outcome controls.",
                LaptopAlt: "BlokeBot Clips and markers page showing clip creation, stream marker and recent outcome controls.",
                "Create a clip immediately or add a marker for the selected live channel."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Create a clip",
                    Steps =
                    [
                        "Open Clips & markers while the selected channel is live.",
                        "Choose whether the clip should include the stream delay, then select Create clip once.",
                        "Open the completed clip from Clips and markers when Twitch finishes preparing it.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Place a stream marker",
                    Steps =
                    [
                        "Open Place a stream marker and add a short description.",
                        "Select Create marker. Find it later in the selected channel's stream recording.",
                    ],
                    Note =
                        "Markers need an active live stream with stream recordings enabled. Twitch may not allow them during reruns or premieres.",
                },
                new SiteGuideSection
                {
                    Heading = "Check an unfinished attempt",
                    Bullets =
                    [
                        "Use Check status or Check outcome when Twitch is still preparing or the first result was uncertain.",
                        "Do not make another clip or marker merely because Twitch is taking time; rechecking uses the recorded attempt.",
                        "Use Reconnect to Twitch if the page asks for the selected channel connection.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Manage rewards and redemptions", "twitch-operations/channel-points"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/channel-points",
            Eyebrow = "Native Twitch · Rewards & redemptions",
            Title = "Manage rewards and viewer requests",
            Summary =
                "Respond to waiting redemptions, manage BlokeBot rewards and create the next reward for your viewers.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-native-channel-points.png",
                LightPhoneSource: "media/phone-light-native-channel-points.png",
                DarkLaptopSource: "media/laptop-dark-native-channel-points.png",
                LightLaptopSource: "media/laptop-light-native-channel-points.png",
                PhoneAlt: "BlokeBot Rewards and redemptions page showing waiting requests, reward controls and age indicators.",
                LaptopAlt: "BlokeBot Rewards and redemptions page showing waiting requests, reward controls and age indicators.",
                "Waiting requests appear first, with visible age cues for requests that are becoming stale."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Answer waiting requests first",
                    Steps =
                    [
                        "Open Unfulfilled redemptions and read the reward, viewer input and waiting age.",
                        "Select Fulfil when the request is complete, or Cancel & refund when the viewer should receive their Channel Points back.",
                    ],
                    Bullets =
                    [
                        "Blue means the request is under 2 minutes old.",
                        "Amber means it has waited from 2 minutes to under 5 minutes.",
                        "Red means it has waited 5 minutes or longer and needs attention.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage or create a reward",
                    Bullets =
                    [
                        "Rewards created by BlokeBot can be edited, enabled, paused or deleted here.",
                        "Rewards created elsewhere are shown read-only so BlokeBot does not take ownership of them.",
                        "Create a reward appears after the waiting requests and current reward list. Set its cost and viewer instructions, then choose Create reward.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "When rewards are unavailable",
                    Bullets =
                    [
                        "Channel Points rewards require a Twitch Affiliate or Partner channel.",
                        "Use Reconnect to Twitch on this page when BlokeBot needs the selected channel's permission.",
                        "Reload before repeating a fulfil or refund when Twitch's result is unclear, then check Redemption history.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a Prediction", "twitch-operations/predictions")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/predictions",
            Eyebrow = "Native Twitch · Predictions",
            Title = "Run and settle a Prediction",
            Summary =
                "Save reusable Prediction questions, open Channel Points entries, then choose the winner or refund everyone.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-native-predictions.png",
                LightPhoneSource: "media/phone-light-native-predictions.png",
                DarkLaptopSource: "media/laptop-dark-native-predictions.png",
                LightLaptopSource: "media/laptop-light-native-predictions.png",
                PhoneAlt: "BlokeBot Predictions page showing a saved question, outcomes and controls for the active Prediction.",
                LaptopAlt: "BlokeBot Predictions page showing a saved question, outcomes and controls for the active Prediction.",
                "The active Prediction stays above reusable templates and recent settled results."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Save and start a Prediction",
                    Steps =
                    [
                        "Open New Prediction template and enter the question, possible outcomes and entry time.",
                        "Select Save template, then select Start Prediction beside the saved question.",
                        "Check the active question and outcome totals while viewers choose with Channel Points.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Close and settle it",
                    Steps =
                    [
                        "Select Lock when viewers should no longer enter.",
                        "After the real result is known, select Resolve as winner beside the correct outcome.",
                        "Select Cancel & refund only when the Prediction cannot be settled; Twitch returns the viewers' Channel Points.",
                    ],
                    Note =
                        "Resolution and refund cannot be undone. Confirm the selected channel, question and real result before choosing either action.",
                },
                new SiteGuideSection
                {
                    Heading = "If the Prediction needs attention",
                    Bullets =
                    [
                        "Predictions require a Twitch Affiliate or Partner channel.",
                        "A Prediction started elsewhere appears here after reload; inspect it before locking, refunding or resolving it.",
                        "Use Reconnect to Twitch if this page asks for the selected channel connection.",
                        "When Twitch's state is uncertain, wait a moment and reload before starting anything new.",
                    ],
                },
            ],
            Next = [new SiteLink("Return to Native Twitch help", "twitch-operations")],
        };

        yield return new SiteGuidePage
        {
            Route = "/commands",
            Eyebrow = "Custom commands",
            Title = "Create commands and scheduled messages",
            Summary =
                "Save reusable bot replies, connect them to chat words, keep counters and schedule reminders.",
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
                    Heading = "Add random values to saved replies",
                    Bullets =
                    [
                        "{random_from|one|two} picks one value.",
                        "{random_between|1|10} picks an inclusive whole number.",
                        "Each random token occurrence makes a fresh pick.",
                        "{random_viewer} picks a connected Twitch chatter, not a verified viewer. The active bot account must be a moderator with connected-chatter access.",
                        "If Twitch cannot return the complete chatter list, {random_viewer} becomes empty text.",
                    ],
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
            Next =
            [
                new SiteLink("Publish the available viewer commands", "commands/catalog"),
                new SiteLink("Choose another tool", "tools"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/commands/catalog",
            Eyebrow = "Chat commands · Viewer discovery",
            Title = "Publish the commands viewers can use now",
            Summary =
                "Choose one global Commands trigger and let viewers discover a viewer-safe list of main command names that follows the selected channel's current state.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-viewer-command-catalog.png",
                LightPhoneSource: "media/phone-light-viewer-command-catalog.png",
                DarkLaptopSource: "media/laptop-dark-viewer-command-catalog.png",
                LightLaptopSource: "media/laptop-light-viewer-command-catalog.png",
                PhoneAlt: "Channel setup on a phone showing the global Commands trigger and expanded Available viewer commands list.",
                LaptopAlt: "Channel setup showing the global Commands trigger, expanded Available viewer commands list and a command-name conflict.",
                "Channel setup shows the same viewer-safe list of main command names that the global chat trigger publishes."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose the global trigger",
                    Steps =
                    [
                        "Choose the channel, open Channel setup and expand Commands.",
                        "Enter the command words viewers may use, separated by commas and without the exclamation mark. The default is commands.",
                        "Select Save Commands. The setting applies to the whole selected channel, not to one Custom Command.",
                        "Leave the field blank and save only when you intend to disable the viewer command catalog.",
                    ],
                    Paragraphs =
                    [
                        "If a word is already owned by another command, Channel setup names the conflict. Choose another word and save; BlokeBot does not silently replace the existing command.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Check what viewers will see",
                    Steps =
                    [
                        "Open Available viewer commands inside the Commands section. It starts collapsed so the setup page stays compact.",
                        "Review the current main command names and any conflict or availability explanation.",
                        "In chat, send the saved trigger such as !commands to publish the same ordered list.",
                    ],
                    Bullets =
                    [
                        "The disclosure requests a fresh snapshot whenever it opens. Supported state changes also refresh an open list without replacing an unsaved trigger draft.",
                        "The list includes its own saved trigger and only commands an ordinary viewer can use.",
                        "Moderator-only commands and private administration actions are never disclosed.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Understand main names",
                    Paragraphs =
                    [
                        "Each Custom Command contributes only the first command word in its saved alias list. That main-name rule keeps the catalog short and predictable; secondary aliases still work in chat but are not advertised.",
                    ],
                    Bullets =
                    [
                        "Built-in commands use their supported public main names.",
                        "A Custom Command that is moderator-only is omitted even when its main name works for moderators.",
                        "When two routes claim the same word, the catalog reports which entry is shadowed instead of pretending both are available.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Why commands appear or disappear",
                    Bullets =
                    [
                        "Guess and round-summary commands appear only while the guessing game has the matching active round state.",
                        "Giveaway entry appears only while a giveaway is accepting entries.",
                        "Request-board and play-queue commands follow the channel's saved, enabled boards and queues.",
                        "Moment and clip commands depend on live-stream identity and disappear while the channel is offline or Twitch stream identity is unavailable.",
                        "Feature commands disappear when that feature is off for the selected channel.",
                    ],
                    Paragraphs =
                    [
                        "An unavailable feature is explained beside the list when BlokeBot can identify the cause. If no viewer commands are currently available, the disclosure says so rather than publishing a misleading list.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Long lists and live changes",
                    Paragraphs =
                    [
                        "BlokeBot keeps the command ordering stable. When the chat response is longer than Twitch permits in one message, it splits the list across multiple ordinary replies without dropping or duplicating command names.",
                        "A game opening, giveaway ending, board or queue changing, feature switch, or stream-liveness change can alter membership. Reopen Available viewer commands for a fresh check when you are preparing an announcement or stream instructions.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Fix common catalog problems",
                    Bullets =
                    [
                        "The chat trigger does nothing: confirm at least one Commands word is saved and resolve any conflict shown in Channel setup.",
                        "A Custom Command alias is missing: only its first saved word is advertised.",
                        "A moderator command is missing: the public catalog deliberately shows viewer-safe commands only.",
                        "A game or Moment command is missing: check the feature, active round or giveaway, and live-stream availability named by the disclosure.",
                        "The list is empty: enable or configure at least one viewer-facing feature, board, queue or Custom Command, then reopen the disclosure.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Create Custom Commands", "commands"),
                new SiteLink("Choose another channel tool", "tools"),
            ],
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
                        "Create a round type, add every accepted answer, put comma-separated aliases after its main answer, and choose any winner point reward.",
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
                        "Enter the winning answer or one of its aliases and declare the winner.",
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
            Route = "/automations",
            Eyebrow = "Automations",
            Title = "Connect channel events to automatic actions",
            Summary =
                "Automations run saved flows: a source such as a Twitch event or custom command starts the flow, conditions and delays shape it, and actions do the work. This release ships the running foundation; visual flow building arrives in a later release.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn Automations on",
                    Steps =
                    [
                        "Choose the channel in the top bar and open Channel setup.",
                        "Open Chat tools, turn on Automations and save the change.",
                        "Open Automations in the navigation, then Twitch events, to see the event sources and the Twitch approval each one needs.",
                    ],
                    Paragraphs =
                    [
                        "Automations is opt-in per channel like every Chat Tools feature. The channel owner or a permitted moderator manages it for the selected channel.",
                    ],
                    Note =
                        "Turning Automations off keeps saved automations and their run history, but events stop starting flows and no Twitch event subscriptions are kept for automations. Turning it back on resumes from the current state without replaying events that were suppressed while it was off.",
                },
                new SiteGuideSection
                {
                    Heading = "Understand a flow",
                    Paragraphs =
                    [
                        "A flow connects sources to actions through typed connections, so each step only receives the kind of value it understands.",
                    ],
                    Bullets =
                    [
                        "Sources start a flow: a selected custom command, a Twitch event such as a follow or raid, or a Channel Points redemption.",
                        "A Condition evaluates a CEL expression against the triggering event's data and continues along its Matches or Does not match branch.",
                        "A Delay waits the configured time before the flow continues. Waiting flows do not block chat or other automations.",
                        "Actions send chat messages, play overlay cues, complete Channel Points redemptions and run native Twitch operations.",
                    ],
                    Note =
                        "Tools for building and editing flows arrive in a later release. In this release the dashboard shows the runtime surfaces: the Twitch events page and the Run automation flow command action.",
                },
                new SiteGuideSection
                {
                    Heading = "Use event data safely",
                    Bullets =
                    [
                        "Each source publishes typed values for the flow, such as the viewer involved, the words entered after a command, the channel, the event time and the live stream identity.",
                        "Chat messages, marker descriptions, poll and prediction questions and expressions can include automation variables that carry those values.",
                        "Values that identify a viewer or carry typed text are treated as sensitive and are kept out of overlays and logs by default.",
                    ],
                    Paragraphs =
                    [
                        "Automation run records, including the triggering event context, are covered by the canonical privacy notice.",
                    ],
                    Links = [new SiteLink("Read the privacy notice", "privacy")],
                },
                new SiteGuideSection
                {
                    Heading = "Know what happens on failure",
                    Bullets =
                    [
                        "Every step has a failure choice: stop the flow or continue past the failure. A stopped flow records the step that failed and later steps do not run.",
                        "BlokeBot never repeats an action because its outcome was uncertain. A chat message, clip or Twitch operation is not sent twice to force an answer.",
                        "Twitch can deliver the same event more than once. BlokeBot keeps a short-lived receipt, so a repeated delivery inside ten minutes starts nothing extra.",
                        "Actions inherit their own feature switches: an overlay cue needs Overlays, each native Twitch operation needs its Native Twitch feature, and command starts need Custom commands.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Start flows from Twitch events", "automations/events"),
                new SiteLink("Choose what automations do", "automations/actions"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/automations/events",
            Eyebrow = "Automations · Twitch events",
            Title = "Start automations from Twitch activity",
            Summary =
                "The Twitch events page lists every automation event source for the selected channel, the Twitch approval each one needs and whether an enabled flow uses it today.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/phone-dark-automation-events.png",
                LightPhoneSource: "media/phone-light-automation-events.png",
                DarkLaptopSource: "media/laptop-dark-automation-events.png",
                LightLaptopSource: "media/laptop-light-automation-events.png",
                PhoneAlt: "BlokeBot Twitch events page on a phone listing ready automation event sources with their Twitch subscriptions.",
                LaptopAlt: "BlokeBot Twitch events page listing ready automation event sources, their Twitch subscriptions and required approvals.",
                "Each event source states its Twitch subscription, the approval it needs and whether an enabled flow uses it."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Read the source list",
                    Bullets =
                    [
                        "Open Automations, then Twitch events. Each source shows its Twitch subscription, the required approval and whether an enabled flow uses it today.",
                        "Ready means the source can start flows now. Reconnect needed and Twitch connection needed mean the source stays inactive: no Twitch subscription is created and no flow starts.",
                        "Use Reconnect to Twitch on this page and complete Twitch as the selected channel owner to approve the missing permissions.",
                        "A source's Twitch subscription follows the bot runtime and exists only while an enabled flow uses that source.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Stream, community and Hype Train events",
                    Bullets =
                    [
                        "Stream went live, Stream went offline, New follower and Incoming raid need no approval beyond the channel's bot connection.",
                        "New subscription and Gifted subscriptions need the channel's subscription-reading permission; Cheer needs Bits reading; the three Hype Train events need Hype Train reading. The page names each required approval exactly.",
                        "Gifted subscriptions, Cheer and Incoming raid each take a minimum — gift count, Bits amount or viewer count — and smaller events start nothing.",
                        "Chat notification starts flows from typed Twitch notices such as announcements, resubs, gift upgrades and charity donations. You choose the notification type; ordinary chat messages never start automations.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Channel Points redemptions",
                    Bullets =
                    [
                        "The Channel Points redemption source starts a flow when a viewer redeems a Custom Reward. It needs the channel's redemption permissions and a Twitch Affiliate or Partner channel.",
                        "A reward filter limits the source to one Custom Reward; without it, every redemption starts the flow.",
                        "The completion policy decides the redemption's status when the flow finishes: keep it manual, fulfil it when the flow succeeds, or cancel it — refunding the viewer — when the flow fails.",
                        "Automatic completion applies only to rewards BlokeBot can manage. Redemptions of rewards created elsewhere still start flows, but their status stays untouched.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "Manage rewards and redemptions",
                            "twitch-operations/channel-points"
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Shoutout, poll and Prediction events",
                    Bullets =
                    [
                        "Shoutout sent and Shoutout received follow the bot account's moderator approvals and appear only while Shoutouts is on.",
                        "Poll started, Poll progressed and Poll ended need the channel's poll-reading permission and appear only while Polls is on.",
                        "Prediction started, Prediction progressed, Prediction locked and Prediction ended need the channel's Prediction-reading permission and appear only while Predictions is on.",
                        "These sources report polls and Predictions however they were started, including ones run outside BlokeBot.",
                    ],
                    Links = [new SiteLink("Set up Native Twitch", "twitch-operations")],
                },
                new SiteGuideSection
                {
                    Heading = "Start flows from a custom command",
                    Paragraphs =
                    [
                        "The Custom command source starts a flow when a selected custom command is used in chat, carrying the viewer and the words entered after the command. Create the command and choose Run automation flow under What happens; Custom commands and Automations must both be on.",
                    ],
                    Links = [new SiteLink("Create Custom Commands", "commands")],
                },
                new SiteGuideSection
                {
                    Heading = "When events do not arrive",
                    Bullets =
                    [
                        "Check the source's badge first: an inactive source is explained by the approval or connection it names.",
                        "Events that happened while Automations was off, while the source was inactive or while no enabled flow used it are not replayed.",
                        "A repeated Twitch delivery inside ten minutes is recognised and starts nothing extra; this is not a lost event.",
                        "If the page cannot load, retry from the message shown. Your saved automations have not changed.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Choose what automations do", "automations/actions"),
                new SiteLink("Return to the Automations overview", "automations"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/automations/actions",
            Eyebrow = "Automations · Actions",
            Title = "Choose what an automation does",
            Summary =
                "Actions send chat, play overlay cues, complete Channel Points redemptions and run native Twitch operations, each inside its own feature's switches and Twitch's published limits.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Send chat and play overlay cues",
                    Bullets =
                    [
                        "Send chat message sends up to 500 characters in the channel, including any automation variables from the triggering event.",
                        "Play overlay cue plays a saved Cue through a chosen Cue player Browser Source. The cue, the Cue player and the Overlays feature must all be enabled for playback.",
                        "A replaced or deleted cue or Cue player makes the action fail rather than silently playing something else.",
                    ],
                    Links = [new SiteLink("Build reusable Cues", "overlays#cues")],
                },
                new SiteGuideSection
                {
                    Heading = "Complete Channel Points redemptions",
                    Bullets =
                    [
                        "Fulfil redemption marks the triggering Channel Points redemption as fulfilled; Cancel redemption cancels it so Twitch refunds the viewer's points.",
                        "Both apply only to the redemption that started the flow, only while it is still unfulfilled, and only for rewards BlokeBot can manage.",
                        "Prefer the redemption source's completion policy for whole-flow outcomes; use these actions when a flow should settle the redemption at a specific step.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run native Twitch operations",
                    Paragraphs =
                    [
                        "Nine actions run the same native operations as the Native Twitch pages, using the selected channel's Twitch connection and each operation's own feature switch and requirements.",
                    ],
                    Bullets =
                    [
                        "Send shoutout targets the broadcaster who triggered the flow, such as an incoming raider; the triggering event must carry a viewer or broadcaster.",
                        "Start poll takes a question of up to 60 characters, 2–5 choices of up to 25 characters, a duration from 15 seconds to 30 minutes and an optional Channel Points cost per extra vote. It fails while another poll is running.",
                        "End poll finishes the channel's active poll immediately; a poll started outside BlokeBot is never ended by an automation.",
                        "Create clip captures the live stream immediately or after Twitch's broadcast delay. Create stream marker adds a marker with a description of up to 140 characters.",
                        "Start prediction takes a question of up to 45 characters, 2–10 outcomes of up to 25 characters and a window from 30 seconds to 30 minutes. It fails while another Prediction is running.",
                        "Lock prediction stops further entries, Cancel prediction refunds every viewer's Channel Points, and Resolve prediction settles the winner from an outcome identifier, usually supplied by an automation variable or expression.",
                    ],
                    Note =
                        "Rewards and Predictions require a Twitch Affiliate or Partner channel, and each operation follows the prerequisites shown on its Native Twitch page.",
                    Links = [new SiteLink("Use Native Twitch tools", "twitch-operations")],
                },
                new SiteGuideSection
                {
                    Heading = "Check an action's outcome",
                    Bullets =
                    [
                        "A failed action follows its step's failure choice: stop the flow or continue past the failure.",
                        "BlokeBot does not retry an action whose Twitch outcome is uncertain, so a shoutout, poll, clip or chat message is never duplicated to force an answer.",
                        "The matching feature page shows the Twitch-side result — recent shoutout outcomes, the active poll or Prediction, finished clips and waiting redemptions.",
                        "If an action keeps failing, fix the connection, permission or feature switch it names before running the flow again; Alerts collects problems that need attention.",
                    ],
                    Links = [new SiteLink("Troubleshoot the bot", "troubleshooting")],
                },
            ],
            Next =
            [
                new SiteLink("Start flows from Twitch events", "automations/events"),
                new SiteLink("Trigger flows from chat commands", "commands"),
            ],
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
                        "Announcement rejected: confirm the bot is still a moderator, then reconnect the bot account using the action shown by Channel setup.",
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
                new SiteGuideSection
                {
                    Heading = "Privacy, saved preferences and data requests",
                    Paragraphs =
                    [
                        "The privacy notice is the single authoritative description of what this help site and the dashboard store, the Twitch data BlokeBot handles, the cookies and browser storage both origins use, and how long everything is kept.",
                    ],
                    Bullets =
                    [
                        "This help site's preference-saving control is on the privacy notice itself.",
                        "The dashboard's control is in its account menu: Stop saving view preferences. Each origin's control governs only that origin's storage.",
                        "Private data requests — access, export, correction or erasure — go to the privacy contact named on the notice, not to chat or a public board.",
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
            Title = "Run a BlokeBot server",
            Summary =
                "Install the service, connect one Twitch application, provide trusted HTTPS and keep its private state backed up.",
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
                    Heading = "1. Install and run",
                    Paragraphs =
                    [
                        "Choose Nix, Docker or a source checkout, give BlokeBot a persistent data directory, and start the dashboard on a private address while you finish setup.",
                    ],
                    Links =
                    [
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
                        "Create one Website Integration application in the Twitch Developer Console. Register both public HTTPS callbacks, then provide its Client ID and Client Secret to BlokeBot without checking the secret into source.",
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
                    Note =
                        "The callback text must match exactly, including scheme, host, port and path.",
                },
                new SiteGuideSection
                {
                    Heading = "3. Add HTTPS",
                    Paragraphs =
                    [
                        "Give the public dashboard a trusted HTTPS address. A typical deployment keeps BlokeBot on loopback behind Caddy, nginx or another reverse proxy that forwards the original scheme and host.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "HTTPS and reverse-proxy details on the wiki",
                            "https://github.com/alsi-lawr/BlokeBot/wiki/HTTPS-and-Reverse-Proxy"
                        ),
                    ],
                    Note =
                        "Register the public HTTPS callbacks with Twitch, not the proxy's private HTTP address.",
                },
                new SiteGuideSection
                {
                    Heading = "4. Keep state private and backed up",
                    Paragraphs =
                    [
                        "BlokeBot keeps its SQLite database, OAuth token cache and automatically managed Data Protection keys in private persistent application state. Restrict that state to the service account and back it up from a stopped service or one consistent snapshot.",
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
                        "Custom-bot encryption needs no operator configuration. ASP.NET Core manages Data Protection keys automatically in private persistent application state; Windows protects those keys with DPAPI LocalMachine.",
                        "A copied SQLite database or SQL backup does not expose reusable custom-bot tokens. Theft or compromise of the full state directory or the running host is outside that boundary.",
                        "When an upgrade finds old plaintext custom-bot credentials, it deletes them, disables that custom bot and alerts the channel owner to reconnect it.",
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
