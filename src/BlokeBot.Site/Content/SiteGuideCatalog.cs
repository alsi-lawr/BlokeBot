namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static readonly IReadOnlyDictionary<string, SiteGuidePage> _pages = CreatePages()
        .Concat(CreateCommunityExtensionPages())
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

        yield return new SiteGuidePage
        {
            Route = "/overlays",
            Eyebrow = "Stream presentation · Browser Sources",
            Title = "Create Browser Sources for OBS",
            Summary =
                "Create private Browser Sources. Preview and position their content. Keep each saved source operational in OBS.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/overlays/phone-dark-overlay-sources.png",
                LightPhoneSource: "media/overlays/phone-light-overlay-sources.png",
                DarkLaptopSource: "media/overlays/laptop-dark-overlay-sources.png",
                LightLaptopSource: "media/overlays/laptop-light-overlay-sources.png",
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
                        "Open Channel setup and turn on Overlays. The feature card persists the change immediately. Guessing, Points and Play with viewers must also be on before Browser Sources for these features can preview or display.",
                        "Use software that supports web Browser Sources, such as OBS Studio.",
                        "Open Overlays under Chat tools. Sources, Cues and Media are fragment-addressed tabs of one page at /overlays#sources, /overlays#cues and /overlays#media in BlokeBot.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Create and protect a Browser Source",
                    Steps =
                    [
                        "On Sources, select New, enter a name, choose its type and complete the type-specific settings.",
                        "Select Create overlay. New opens an unsaved editor. Creation happens only after this final action.",
                        "Copy the private Browser Source URL when it appears. BlokeBot can show it only after creation or rotation.",
                        "In OBS, add a Browser Source and paste the URL. Set Width to 1920 and Height to 1080. Place it in the scene.",
                    ],
                    Note =
                        "Treat the private URL like a password. Keep it out of chat, screenshots, stream recordings and public notes. If someone shared or possibly shared the private URL, rotate it immediately. The old URL then stops operation.",
                },
                new SiteGuideSection
                {
                    Heading = "Preview and edit appearance",
                    Bullets =
                    [
                        "Preview is above configuration. Choose Live for the current saved state or Representative to inspect a useful example before the real trigger happens.",
                        "The 1920 × 1080 canvas shows how the selected Browser Source will look in OBS. Drag anywhere on the selected body to move it. Drag an edge or corner to resize it.",
                        "Use the arrow keys on the selected body for one-pixel movement, or Shift plus an arrow for ten pixels. The keyboard-operable edges and corners resize in the same increments.",
                        "Enter X, Y, Width and Height for precise geometry. Reset restores the type's default placement.",
                        "Geometry, style and display choices update only the authenticated Preview until you save. Select Save overlay before you expect a change in OBS or another private Browser Source.",
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
                        "Use the listed selectors to adjust colors and type. These selectors do not change the dashboard or another Browser Source.",
                        "BlokeBot rejects imports, external URLs, markup, scripts, at-rules and selectors outside the selected Browser Source.",
                        "If BlokeBot rejects CSS, correct the issue in the message and save again. The invalid change is not partly applied. The last saved appearance remains live.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Guessing rounds",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/overlays/phone-dark-overlay-guessing.png",
                        LightPhoneSource: "media/overlays/phone-light-overlay-guessing.png",
                        DarkLaptopSource: "media/overlays/laptop-dark-overlay-guessing.png",
                        LightLaptopSource: "media/overlays/laptop-light-overlay-guessing.png",
                        PhoneAlt: "Guessing Browser Source on a phone that shows a representative open round in Preview.",
                        LaptopAlt: "Guessing Browser Source that shows representative round choices, draggable Preview and settings.",
                        "Representative states let you place the Guessing Browser Source before a real round begins."
                    ),
                    Bullets =
                    [
                        "Turn on Guessing game and create the Browser Source. Choose whether to show the number of guesses.",
                        "Use Representative to inspect Open, Closed and Result states. Save the appearance, then use the normal Guessing dashboard to start, stop and resolve a round.",
                        "The first configured answer is its main answer. Aliases still work for viewers but do not change the displayed setup language.",
                        "If BlokeBot pauses Preview, restore both Overlays and Guessing game in Channel setup. Saved setup remains while either feature is off.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Active Giveaways",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/overlays/phone-dark-overlay-giveaway.png",
                        LightPhoneSource: "media/overlays/phone-light-overlay-giveaway.png",
                        DarkLaptopSource: "media/overlays/laptop-dark-overlay-giveaway.png",
                        LightLaptopSource: "media/overlays/laptop-light-overlay-giveaway.png",
                        PhoneAlt: "Giveaway Browser Source on a phone that shows an active giveaway in Preview.",
                        LaptopAlt: "Giveaway Browser Source that shows an active giveaway, compact display controls and appearance options.",
                        "The active Giveaway Preview shows useful live content. Without an active giveaway the Browser Source renders nothing."
                    ),
                    Bullets =
                    [
                        "Turn on Points and choose a Giveaway title. Set the entrant count, close-time countdown and current join command.",
                        "Use Representative to inspect Open, Closing, Completed or Cancelled presentation. Save before you run the giveaway from Points.",
                        "When there is no active giveaway, the Browser Source renders nothing. There is no idle card for viewers.",
                        "If it stays blank during an active giveaway, check Overlays and Points. Check the source, private URL and giveaway state.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Show community goals and bounties",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/figures/phone-dark-progression-overlay-setup.png",
                        LightPhoneSource: "media/community/figures/phone-light-progression-overlay-setup.png",
                        DarkLaptopSource: "media/community/figures/laptop-dark-progression-overlay-setup.png",
                        LightLaptopSource: "media/community/figures/laptop-light-progression-overlay-setup.png",
                        PhoneAlt: "The Sample Channel Community milestone Browser Source editor that shows source selection, rotation and representative progress states.",
                        LaptopAlt: "The Sample Channel Community milestone Browser Source editor that shows source selection, rotation and representative progress states.",
                        "The signed-in editor selects bounded authoritative data. The private Browser Source renders current public progress and does not expose its URL."
                    ),
                    Steps =
                    [
                        "Create a Community goal or Viewer-funded bounty Browser Source. A community goal inherits Community progression and Overlays. A bounty inherits Bounties and Overlays. Bounties itself remains unavailable when its required Points switch is off.",
                        "Choose one current public item or rotate current public items at the saved interval. A bounty can also show a bounded number of recent public contributor login and amount callouts.",
                        "Use Representative to inspect Active, Progress update, Completed, Failed, Expired and Empty. This action does not change the goal or bounty. Position it, save the source and use its private URL in OBS.",
                    ],
                    Bullets =
                    [
                        "Live contributions coalesce and update current progress without refresh. A reconnection restores the latest authoritative state. It does not replay each prior contribution or completion animation.",
                        "Community goal output contains public communal definitions only. It excludes Hidden seasons, per-viewer progress, identities and private notes.",
                        "Bounty output contains public title, progress, target, percentage, expiry and lifecycle state plus only the configured public pledge callouts. It excludes private bounties, Twitch user IDs, balances, moderation reasons and internal accounting.",
                        "If either inherited parent is off, the retained editor points to Channel setup. Projection, preview, tests, publication, reconnection and animation stop. Saved source and domain history remain. An enable action restores the current state. It does not replay suppressed updates, timers, queued work or animations.",
                    ],
                    Links =
                    [
                        new SiteLink("Set up viewer-funded bounties", "community/bounties"),
                        new SiteLink("Create communal season goals", "community/progression"),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Event feed",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/overlays/phone-dark-overlay-event-feed.png",
                        LightPhoneSource: "media/overlays/phone-light-overlay-event-feed.png",
                        DarkLaptopSource: "media/overlays/laptop-dark-overlay-event-feed.png",
                        LightLaptopSource: "media/overlays/laptop-light-overlay-event-feed.png",
                        PhoneAlt: "Event feed Browser Source on a phone that shows a representative channel event and compact source controls.",
                        LaptopAlt: "Event feed Browser Source that shows its Preview, waiting-card limit and enabled event sources.",
                        "One Event feed can present point awards, Guessing winners, Giveaway winners, Bingo events and achievement completions."
                    ),
                    Bullets =
                    [
                        "Choose the maximum waiting cards and what happens when the feed is full.",
                        "Turn point awards, Guessing winners, Giveaway winners, Bingo events and achievement completions on or off independently. Settings for an off source collapse and keep their saved values.",
                        "For each enabled source, edit its message, priority and display time, then choose a Representative event to check the result.",
                        "If an expected card is absent, confirm its feature and event source are on. Re-enable the source for future events. Events missed while it was off are not replayed.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Present achievements in the Event feed",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/figures/phone-dark-achievement-feed-setup.png",
                        LightPhoneSource: "media/community/figures/phone-light-achievement-feed-setup.png",
                        DarkLaptopSource: "media/community/figures/laptop-dark-achievement-feed-setup.png",
                        LightLaptopSource: "media/community/figures/laptop-light-achievement-feed-setup.png",
                        PhoneAlt: "The Sample Channel Event feed Browser Source editor with independent event kinds and Achievement completion.",
                        LaptopAlt: "The Sample Channel Event feed Browser Source editor with independent event kinds and Achievement completion.",
                        "Achievement completion is one bounded Event feed kind with its own message, priority, duration and representative preview."
                    ),
                    Bullets =
                    [
                        "Turn on both Overlays and Community progression, select the Event feed source and enable Achievement completion. This feature does not create an additional Channel setup switch.",
                        "Set the public-safe template, priority and display time, then preview a Representative completion. Preview and test do not grant an achievement or mutate progression.",
                        "A genuine supported achievement completion enters the queue once. It can show the viewer name, achievement name and presentation-safe rewards or points. Twitch user IDs, balances, moderator notes, internal keys and reward tokens remain absent.",
                        "If either parent is off, BlokeBot immediately clears a connected achievement card. It blocks projection, the queue, preview, publication and reconnection. Other configured Event feed kinds can continue when their own requirements are met.",
                        "Saved feed configuration and history remain. Re-enable accepts only new achievement completions. Suppressed events, queued work, timers and animations do not replay, and stale pre-disable publication cannot reappear after the clear.",
                    ],
                    Links =
                    [
                        new SiteLink("Configure seasons and achievements", "community/progression"),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Viewer Queue",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/overlays/phone-dark-overlay-viewer-queue.png",
                        LightPhoneSource: "media/overlays/phone-light-overlay-viewer-queue.png",
                        DarkLaptopSource: "media/overlays/laptop-dark-overlay-viewer-queue.png",
                        LightLaptopSource: "media/overlays/laptop-light-overlay-viewer-queue.png",
                        PhoneAlt: "Viewer Queue Browser Source on a phone that shows a safe representative public queue summary.",
                        LaptopAlt: "Viewer Queue Browser Source that shows a representative party and safe public queue summary in Preview.",
                        "Viewer Queue presents current, next and waiting viewers. It does not expose private party information."
                    ),
                    Bullets =
                    [
                        "Turn on Play with viewers and create a queue first. A viewer must sign in with Twitch to use its viewer page. There is no unsigned typed-login fallback.",
                        "Choose the queue and the number of Current party and Next rows. Inspect Open, Ready check and Party selected examples.",
                        "Every configured field is optional and public on the viewer page and Viewer Queue overlay. Ask only for details that are safe to show on stream.",
                        "Configured entry answers are public on the Viewer Queue overlay. Lobby messages and moderator notes remain private, and the overlay does not show a wait estimate.",
                        "If BlokeBot pauses Preview, restore both Overlays and Play with viewers. The current queue and saved appearance remain. BlokeBot does not replay missed animations.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Cue player and live recovery",
                    Bullets =
                    [
                        "A Cue player is a Browser Source target for reusable Cues. Create and protect its private URL here, then build and test content on the Cues page.",
                        "Send test pulse checks the selected enabled source. A connected Preview or OBS source responds and does not expose its private URL.",
                        "If OBS is stale after a network loss or restart, reload that Browser Source. It reads the latest saved state and reconnects.",
                        "Rename keeps the private URL. Disable stops display and retains setup. Rotate revokes the old URL. Delete permanently removes the source.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Build and trigger reusable Cues", "overlays/cues"),
                new SiteLink("Manage media for Cues", "overlays/media"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/overlays/cues",
            Eyebrow = "Stream presentation · Cues",
            Title = "Build and trigger reusable Cues",
            Summary =
                "Combine uploaded media, online media and web pages, then play the saved Cue through a Cue player Browser Source.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/overlays/phone-dark-overlay-cues.png",
                LightPhoneSource: "media/overlays/phone-light-overlay-cues.png",
                DarkLaptopSource: "media/overlays/laptop-dark-overlay-cues.png",
                LightLaptopSource: "media/overlays/laptop-light-overlay-cues.png",
                PhoneAlt: "Cues page on a phone that shows the saved Cue list and task-focused content editor.",
                LaptopAlt: "Cues page that shows attached saved Cues and editor columns with a reusable web layer.",
                "Saved Cues and their editor stay together. Test playback targets a Cue player Browser Source."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare a Cue player",
                    Steps =
                    [
                        "Turn on Overlays in Channel setup.",
                        "On Sources, create an enabled Cue player Browser Source. Copy its private URL. Add it to OBS at 1920 × 1080.",
                        "Open Cues at /overlays#cues and choose the saved Cue player under Test playback.",
                    ],
                    Note =
                        "If Overlays is off, BlokeBot pauses Cue edits and playback. Saved Cues remain. If you enable Overlays again, BlokeBot does not play Cue requests missed while the feature was off.",
                },
                new SiteGuideSection
                {
                    Heading = "Build reusable content",
                    Steps =
                    [
                        "Select New cue and name it. Set its total duration. Choose what happens when another Cue plays.",
                        "Add uploaded media, online media or a web page. Reorder or remove content as needed. Content lower in the list appears in front when stacking values match.",
                        "For each item, set when it starts, how long it plays, stacking order, left, top, width and height.",
                        "For image, audio and video content, set the available volume. Choose Show all, Fill and crop or Stretch to fill.",
                        "Turn Cue enabled on and select Create cue or Save cue.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose overlap and test playback",
                    Bullets =
                    [
                        "Play after the current cue waits. Replace the current cue interrupts it. Skip while another cue plays drops the new request. Play at the same time overlaps them.",
                        "Choose an enabled Cue player and select Play test cue. Watch the embedded preview or OBS source for the saved result.",
                        "A test can wait briefly when the Cue player is disconnected. If the test expires or BlokeBot rejects it, reconnect the player. Try one fresh test. Do not add repeated requests.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Trigger a Cue from chat",
                    Steps =
                    [
                        "Open Custom commands and create or edit a command.",
                        "Under What happens, choose Play an overlay cue. Choose the Cue player, saved Cue and busy-player behavior. Choose whether the chat reply occurs before or after Cue acceptance.",
                        "Use the command's Test cue action, save the command, and send its main command word in chat.",
                    ],
                    Bullets =
                    [
                        "Enable the command, Cue, Cue player and Overlays feature for playback.",
                        "BlokeBot reports a replaced or deleted Cue or target as unavailable. Choose a current saved Cue and Browser Source, then save the command again.",
                        "The selected Cue can use safe chat context. It does not expose the private Browser Source URL.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover embedded content",
                    Bullets =
                    [
                        "Use complete secure addresses that begin with https:// for online media and web pages. A blocked, invalid or unreachable address must be corrected at its source.",
                        "Some sites prevent embedded use. Use an embeddable page or media address instead. Do not weaken Browser Source safety settings.",
                        "If uploaded media is unavailable or replaced, open Media and repair that asset. Return to the Cue and confirm the saved selection.",
                        "If the layer layout is wrong, correct its timing, order or percentage geometry, save, and run one new test.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Manage media for Cues", "overlays/media"),
                new SiteLink("Create Custom Commands", "commands"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/overlays/media",
            Eyebrow = "Stream presentation · Media library",
            Title = "Manage media for Cues",
            Summary =
                "Upload private channel media, preview saved files and repair the assets used by reusable Cues.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/overlays/phone-dark-overlay-media.png",
                LightPhoneSource: "media/overlays/phone-light-overlay-media.png",
                DarkLaptopSource: "media/overlays/laptop-dark-overlay-media.png",
                LightLaptopSource: "media/overlays/laptop-light-overlay-media.png",
                PhoneAlt: "Media library on a phone that shows private upload controls and the saved-media area.",
                LaptopAlt: "Media library that shows channel storage use, drag-and-drop upload and saved media management.",
                "Media stays in the selected channel's private storage and is available to its Cues."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Upload accepted browser media",
                    Steps =
                    [
                        "Turn on Overlays, choose the channel and open Media at /overlays#media.",
                        "Enter a clear Media name. Drag an image, audio or video file onto the Media file area. You can also use the file picker.",
                        "Wait for the upload result and confirm the saved file appears under Saved media.",
                        "Open Cues, add Uploaded media and choose the saved name.",
                    ],
                    Note =
                        "Uploads stay in private channel storage. The page shows current use and capacity. Another channel cannot select or serve this channel's media.",
                },
                new SiteGuideSection
                {
                    Heading = "Preview, replace or delete",
                    Bullets =
                    [
                        "Preview a saved image, audio or video before you assign it to a live Cue.",
                        "Replace file keeps the saved media item and updates its content for future playback. Test every Cue that depends on it before you go live.",
                        "Delete only after you check dependent Cues. A Cue does not silently substitute another file when its selected media is unavailable.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover an upload or playback failure",
                    Bullets =
                    [
                        "Unsupported file: choose an ordinary browser-supported image, audio or video file. Do not rename an incompatible file.",
                        "Storage full: delete unused media or replace a large file with a smaller browser-ready version. Upload once.",
                        "Upload interrupted: keep the original file and reload the page. Confirm whether a saved item exists before you retry.",
                        "Cue cannot play the file: preview the saved media. Replace a damaged or unsupported file. Save and test the dependent Cue again.",
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
                PhoneAlt: "The Sample Channel public request board on a narrow screen with open rules and the submission form.",
                LaptopAlt: "The Sample Channel Request boards moderator page with a saved Game night requests board and its configuration.",
                "Moderators configure the board at /requests. Viewers use its public channel-and-board address."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose the right view",
                    Bullets =
                    [
                        "A channel owner or permitted moderator chooses the channel. That person opens Request boards at /requests to manage boards.",
                        "Open public board copies the viewer route /requests/{channel}/{board-name}. Anyone can read a saved board. A viewer signs in with Twitch to submit, vote or withdraw.",
                        "Chat participants can discover boards with !requests. Website and chat actions use the same board, limits, votes and request states.",
                    ],
                    Note =
                        "The words in braces describe a route value. Replace them with the channel login and the board's Command and URL name. Do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Configure a board",
                    Steps =
                    [
                        "Select New, give the board a Command and URL name, title and description, then choose whether it accepts submissions.",
                        "Set the point cost, refund policy, active-submission limit, submission cooldown, voting switch and per-viewer vote limit.",
                        "Add only the fields participants need. A field can be Text, Link, Choose from a list, Number or Twitch clip link. Set its label and applicable length, choice or number limits.",
                        "Select Save board, then use Open public board and read the Board rules exactly as a participant will see them.",
                    ],
                    Paragraphs =
                    [
                        "The public queue order explains the complete stable order. Moderator priority, votes and assigned queue position refine that order. They do not hide a different participant rule.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Submit and vote",
                    Bullets =
                    [
                        "On the public page, sign in with Twitch, complete Title and the configured fields, then select Submit request. The page shows the request number and its current public state.",
                        "In chat, use !request <board> <title> | field=value | category=value | tags=a,b. Required field keys come from that board's configuration.",
                        "Use !requestvote <request-number> to vote in chat, or Vote on the public board. A repeated vote does not add another vote.",
                        "A submitter can Withdraw an active request from the public page. Private moderator text is never shown there.",
                    ],
                    Note =
                        "BlokeBot recognizes a repeated delivery of the same chat submission. It reports the original request and does not create or charge another.",
                },
                new SiteGuideSection
                {
                    Heading = "Moderate the lifecycle",
                    Steps =
                    [
                        "Review the submitted values and any possible-duplicate warning. Set public category, tags, priority and Public note when they help participants.",
                        "Move Awaiting review to Approved or Rejected. Approved requests can move to In queue or Accepted. In queue or Accepted requests can move to Completed. Submitters can select Withdraw. BlokeBot gives merged duplicates the Merged into another request state.",
                        "Use Merge with the target request number when two entries are the same request. The public board keeps the Merged into another request outcome and the target request's combined support.",
                        "When the dashboard is not convenient, use !requestapprove, !requestreject, !requestqueue, !requestaccept or !requestcomplete followed by one request number.",
                        "To merge in chat, use !requestmerge <source-number> <target-number>.",
                    ],
                    Paragraphs =
                    [
                        "Private moderator note and Private rejection reason remain moderator-only. Put public participant context in Public note instead.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Points, failure and recovery",
                    Bullets =
                    [
                        "When the board accepts the initial submission, it holds the cost from the viewer's available balance before moderator review. The reservation moves from No points charged to Points held, then finishes as Points refunded or Points charged. Never charge the viewer manually as well.",
                        "Completion charges the held points. A closure follows the selected policy: Never refund, Refund if rejected or withdrawn, or Refund if not fulfilled.",
                        "If validation, the cooldown, a limit or the balance rejects a submission, correct the message shown and submit once. If an outcome is already visible, reload before you try again.",
                        "If request state and points still disagree after reload, leave the request unchanged. Send the channel, board name, request number, approximate time and visible message to the server owner. Do not share Twitch tokens or private notes.",
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
                "Open a queue and collect optional public entry details. Run ready checks. Deliver private lobby information only to participants.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-play-with-viewers.png",
                LightPhoneSource: "media/community/phone-light-play-with-viewers.png",
                DarkLaptopSource: "media/community/laptop-dark-play-with-viewers.png",
                LightLaptopSource: "media/community/laptop-light-play-with-viewers.png",
                PhoneAlt: "The Sample Channel Community night party viewer page with its public queue rule and optional entry form.",
                LaptopAlt: "The Sample Channel Play with viewers moderator page with a saved queue, party size and fair-selection configuration.",
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
                        "Open viewer page uses /queues/{channel}/{queue-name}. A viewer must sign in with Twitch to join. There is no unsigned typed-login fallback.",
                        "Moderator controls, priorities, moderator notes and lobby messages are never shown on the public page. Configured entry fields and their answers are public.",
                    ],
                    Note =
                        "The words in braces describe a route value. Replace them with the channel login and the queue's Command and URL name. Do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Configure and open the queue",
                    Steps =
                    [
                        "Select New, set the Command and URL name, Queue name, Game or activity and Party size.",
                        "Choose First to join or Viewers who played least recently. The viewer page states the applicable fair-selection rule before anyone joins.",
                        "Set Ready expiry, History retention and Skip/no-show exclusion. Add optional public entry fields and any required roles in role=count form.",
                        "Choose whether the public page can show participant names. Turn Queue open on and save. Inspect Open viewer page at two widths.",
                    ],
                    Paragraphs =
                    [
                        "Every configured field is optional and public on the viewer page and Viewer Queue overlay. Examples include platform, region, rank and preferred role. Lobby messages and moderator notes remain private.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Join from the page or chat",
                    Bullets =
                    [
                        "On the viewer page, fill the requested fields and select Join. Check position reports the current place. Leave removes the entry. I'm ready answers an active ready check.",
                        "In chat use !queue [queue], !join [queue] key=value, !leave [queue], !position [queue] and !ready [queue]. The queue name is optional when the channel has only one queue.",
                        "A second join request keeps one entry. The signed-in Twitch identity is authoritative and blocks a duplicate entry from a second typed identity.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Select and run a party",
                    Steps =
                    [
                        "Review Waiting viewers and the visible next-candidate order. Entries move through Waiting, Awaiting response, Ready, Selected, Left queue, Skipped and Did not respond. Adjust Priority or Moderator note only when a documented channel rule requires it.",
                        "Start a Ready check for candidates. Participants must use I'm ready or !ready before Ready expiry. Then select Select next party.",
                        "Use Keep party to retain the current group. Use Replace one, Skip or No-show when someone cannot play. The configured exclusion prevents immediate re-entry after a skip or no-show.",
                        "Enter the Lobby message and select Whisper party. Confirm success before you start. Never paste a private lobby code into public chat as a fallback.",
                    ],
                    Paragraphs =
                    [
                        "Moderators can use !queueopen [queue] and !queueclose [queue]. Close the queue before you resolve a disputed selection. New joins cannot move the visible candidate order.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover safely",
                    Bullets =
                    [
                        "If a participant misses Ready expiry, run a new ready check or use Replace one. Use No-show only when the channel's exclusion rule must apply.",
                        "If a whisper fails, verify that the bot connection can whisper. Retry Whisper party only after the page reports the failure. Do not reveal the private message publicly.",
                        "If selection cannot satisfy required roles, leave the current party intact. Adjust the pool or role requirements and select again.",
                        "History retention removes old participation data after the configured period. If you shorten it, future fairness evidence changes. Record that channel decision before you save.",
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
                PhoneAlt: "The Sample Channel stream recap on a narrow screen with an approved Community clutch save and a recorded vote.",
                LaptopAlt: "The Sample Channel Moments moderator page with capture settings and an approved Community clutch save in the clip gallery.",
                "Moderators work at /moments. Approved entries appear in channel, stream and weekly recaps."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare a live channel",
                    Bullets =
                    [
                        "Choose the channel and open Moments at /moments. Captures require Twitch to report that channel live and require the selected channel's Twitch connection.",
                        "Set the Merge window from 15 to 300 seconds. 90 seconds is the default. Calls inside that window join the same stream moment and keep each contributor and suggestion.",
                        "Choose No reward, First viewer to request or All contributing viewers. Set the amount. Choose whether a confirmed clip failure can use a stream marker.",
                        "Save settings and check that the page shows Live stream with a stream identity. Then invite viewers to capture.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Capture a candidate",
                    Bullets =
                    [
                        "A viewer uses !moment <suggested title> | category=<suggested category>. !clip accepts the same form.",
                        "A moderator can use Capture now. BlokeBot first requests a Twitch clip. BlokeBot uses marker fallback only after a confirmed clip failure and only when you enable it.",
                        "Each call returns a public moment number. Repeated or concurrent calls for the same live moment converge. They do not create duplicate Twitch actions or rewards.",
                    ],
                    Note =
                        "BlokeBot links to Twitch media. It does not copy or host the clip or VOD.",
                },
                new SiteGuideSection
                {
                    Heading = "Moderate public metadata",
                    Steps =
                    [
                        "In Candidates, review Creating clip, Clip ready, Marker ready or Could not create clip. Review the contributor count and viewer suggestions.",
                        "Set Public title and Category, select Save details, then Approve. Reject keeps its reason private. Merge uses another moment number.",
                        "Use Open on Twitch to verify available media. Only approved moments appear in public recaps.",
                    ],
                    Paragraphs =
                    [
                        "Moderator note, rejection reason, audit text and Twitch failure details stay on the moderator view. Public recaps show only approved title, category, counts and the Twitch link.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Attach approved Moments to progression",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/figures/phone-dark-moment-attachment.png",
                        LightPhoneSource: "media/community/figures/phone-light-moment-attachment.png",
                        DarkLaptopSource: "media/community/figures/laptop-dark-moment-attachment.png",
                        LightLaptopSource: "media/community/figures/laptop-light-moment-attachment.png",
                        PhoneAlt: "The Sample Channel public bounty with an attached approved Moment, public-safe title and Twitch media link on a narrow screen.",
                        LaptopAlt: "The Sample Channel public bounty with an attached approved Moment, public-safe title and Twitch media link on a narrow screen.",
                        "Authorized staff attach by reference in the destination. Viewers receive only the Moment's current approved public-safe fields."
                    ),
                    Steps =
                    [
                        "Approve the Moment for the selected channel first. A channel owner or permitted moderator then opens the destination bounty, achievement or confirmed tournament match.",
                        "Open its Moments section, choose a same-host approved Moment and attach it. The destination context remains visible. It prevents confusion between a confirmed result and another match.",
                        "Use Remove in the same section to detach the reference. The Moment, Twitch clip or marker and moderation history remain owned by Moments and are not copied or deleted.",
                    ],
                    Bullets =
                    [
                        "A bounty attachment inherits Moments, Bounties and Bounties' effective Points requirement. An achievement attachment inherits Moments and Community progression. A match attachment inherits Moments and Tournaments & leagues. This feature does not add an attachment switch.",
                        "Only approved, same-host, currently public-safe Moments are discoverable. BlokeBot suppresses unavailable Moments from management, public destination pages, events and downstream presentation.",
                        "If the same source Moment returns to Approved, a retained link becomes visible again. Every parent gate must also be available. BlokeBot does not replay an attach event or suppressed work when it reappears.",
                        "Public destinations can show current title, category and Twitch media link. Moderator notes, rejection reasons, failure detail, internal IDs and audit text remain private.",
                        "If a parent is off, the embedded section shows Channel setup recovery. It blocks discovery, changes, public relationships, events, overlays and automations. Valid links remain saved and reappear from current state after re-enable without replay.",
                    ],
                    Links =
                    [
                        new SiteLink("Manage viewer-funded bounties", "community/bounties"),
                        new SiteLink("Manage achievements", "community/progression"),
                        new SiteLink("Manage confirmed matches", "community/competitions"),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Share recaps and votes",
                    Bullets =
                    [
                        "Weekly recap opens /moments/{channel} for the current ISO-UTC week. A stream recap uses /moments/{channel}/streams/{stream-id}.",
                        "A signed-in viewer votes with Twitch ID. An unsigned viewer can enter a normalized Twitch login. Each identity contributes at most one vote to a moment.",
                        "Finalize previous week records the winner for the completed week. It uses the vote count and stable order. A repeated finalization returns the same winner.",
                    ],
                    Note =
                        "Replace every value in braces with the channel login or Twitch stream identity shown by BlokeBot. Do not type the braces.",
                },
                new SiteGuideSection
                {
                    Heading = "Read Twitch states before you retry",
                    Bullets =
                    [
                        "Creating clip means that Twitch continues to prepare the clip. Reload the same candidate later. Do not capture again just to force an answer.",
                        "An ambiguous outcome means Twitch did not confirm whether its request completed. BlokeBot preserves that uncertainty and does not create a fallback marker from it.",
                        "Offline means wait for a live stream. If Twitch reports that clips or VODs are disabled, correct that setting or continue with no marker fallback. If access is unauthorized, reconnect the selected channel account.",
                        "If the failure continues, keep the candidate. Send the channel, moment number, stream identity, approximate time and Twitch message to the server owner. Never send tokens or private moderation text.",
                    ],
                },
            ],
            Next = [new SiteLink("Use Native Twitch tools", "twitch-operations")],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/bounties",
            Eyebrow = "Community progression · Bounties",
            Title = "Fund channel challenges with viewer points",
            Summary =
                "Open a clear challenge and let viewers reserve points toward it. Settle each outcome and show who contributed.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/progression/phone-dark-bounties-public-board.png",
                LightPhoneSource: "media/community/progression/phone-light-bounties-public-board.png",
                DarkLaptopSource: "media/community/progression/laptop-dark-bounties-setup.png",
                LightLaptopSource: "media/community/progression/laptop-light-bounties-setup.png",
                PhoneAlt: "The Sample Channel public bounty board that shows a funding challenge, total, deadline and recorded contributor Twitch logins.",
                LaptopAlt: "The Sample Channel Bounties management page that shows the proposed-bounty setup fields, visibility and point settlement choices.",
                "Owners and moderators configure Bounties in the dashboard. Participants fund Public challenges on the board or in chat."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn on Bounties and Points",
                    Steps =
                    [
                        "Choose the channel, open Channel setup and turn on Points and Bounties under Chat tools. Turn on both switches before Bounties appears in normal navigation or accepts work.",
                        "The feature cards persist those switch changes immediately. Open Bounties and use the Page help (?) button if you need the in-dashboard summary.",
                        "A channel owner or permitted moderator creates and settles bounties. A participant needs a Twitch chat identity and enough available BlokeBot points to pledge.",
                    ],
                    Note =
                        "Bounties use BlokeBot points. This feature cannot debit or pay out Twitch Channel Points.",
                },
                new SiteGuideSection
                {
                    Heading = "Create a proposal and open funding",
                    Steps =
                    [
                        "Enter the public title and description, funding target, UTC expiry and optional fixed completion-bonus pool.",
                        "Choose Public or Private visibility, what a Failed outcome does with pledges, and Equal or Proportional completion-bonus distribution.",
                        "Put staff-only context in Private moderator note, then select Create proposed bounty. Proposed is a draft and cannot receive pledges.",
                        "Review the selected channel and values, add a Private audit reason, then select Open funding.",
                    ],
                    Paragraphs =
                    [
                        "The lifecycle is Proposed, Funding, Accepted and one terminal outcome: Completed, Failed, Expired or Cancelled. Reject is a distinct audited action. It stores a Cancelled outcome.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pledge and follow progress",
                    Bullets =
                    [
                        "The public board is /bounties/{channel}. Replace the value in braces with the channel login. Public bounties show the title, description, state, funding total, target, deadline, bonus, terminal history and recorded contributors.",
                        "A signed-in participant enters Pledge points on the board, or uses chat. BlokeBot reserves the accepted amount from that channel's available point balance.",
                        "BlokeBot caps a pledge request above the required amount to the target remainder. It does not overfund the bounty. A repeated delivery records the pledge only once.",
                        "Owners and moderators can select Accept while the state is Funding. They can do this before or after contributions reach the target. When contributions reach the target, BlokeBot does not accept the challenge automatically.",
                    ],
                    Code = "!bounties\n!bounty <bounty-id>\n!bountypledge <bounty-id> <points>",
                    Note =
                        "The words in angle brackets describe a value. Use the public bounty reference that BlokeBot shows. Do not type the brackets.",
                },
                new SiteGuideSection
                {
                    Heading = "Moderate deadlines and outcomes",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/progression/phone-dark-bounties-disabled.png",
                        LightPhoneSource: "media/community/progression/phone-light-bounties-disabled.png",
                        DarkLaptopSource: "media/community/progression/laptop-dark-bounties-moderation.png",
                        LightLaptopSource: "media/community/progression/laptop-light-bounties-moderation.png",
                        PhoneAlt: "The Sample Channel Bounties direct route that shows retained-state recovery while the feature is off.",
                        LaptopAlt: "The Sample Channel funding bounty that shows contributor logins, pledge totals, moderator actions, a private audit reason and expiry extension.",
                        "Funding and moderation remain reviewable in the dashboard. A retained signed-in direct route points back to Channel setup while Bounties is off."
                    ),
                    Bullets =
                    [
                        "You can extend Funding and Accepted bounties before they become terminal. Either state can expire. Check the UTC expiry before you extend it.",
                        "An Accepted bounty can move to Completed, Failed or Cancelled. Every action records the authenticated actor, time, action and private audit reason.",
                        "Completed consumes all reserved pledges. BlokeBot splits its fixed bonus pool across contributor logins with the selected Equal or Proportional rule. It cannot grant twice.",
                        "Reject, Cancel and Expire refund every reserved pledge. Fail applies the bounty's chosen Refund pledges or Spend pledges policy exactly once.",
                    ],
                    Paragraphs =
                    [
                        "If another moderator changed the bounty, reload before you act. BlokeBot rejects a stale transition and keeps the newer state.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Understand identity and privacy",
                    Bullets =
                    [
                        "A Public bounty exposes each recorded normalized Twitch login and its total pledge amount. A Private bounty publishes no bounty data.",
                        "BlokeBot groups contributors by that host-scoped recorded login. Point debits, refunds and bonuses also use it. A later Twitch rename does not move the balance or combine historical logins.",
                        "The public board and chat summary never contain private moderator notes, audit reasons, raw provider data or internal identifiers.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover without double settlement",
                    Bullets =
                    [
                        "If Points is off, turn it on in Channel setup. Saved bounty work remains unchanged while the dependency is unavailable.",
                        "If BlokeBot rejects a pledge, correct the visible balance, state, expiry or validation cause. Submit once. If a pledge or transition is visible, reload. Do not repeat it.",
                        "If Bounties is off, BlokeBot hides navigation, commands and public data. It stops pledges, moderation, expiry work, ledger changes and emitted events. Saved bounties, pledges and history remain.",
                        "Re-enable Bounties and Points to continue from retained current state. Commands, expiries, events and other work suppressed while off are not replayed.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Build seasons and achievements", "community/progression"),
                new SiteLink("Review viewer points", "points"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/progression",
            Eyebrow = "Community progression · Seasons",
            Title = "Run seasons, quests and achievements",
            Summary =
                "Turn supported channel events into individual or communal progress, then preserve standings and viewer-earned rewards beyond the season.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/progression/phone-dark-community-progression-public.png",
                LightPhoneSource: "media/community/progression/phone-light-community-progression-public.png",
                DarkLaptopSource: "media/community/progression/laptop-dark-community-progression-setup.png",
                LightLaptopSource: "media/community/progression/laptop-light-community-progression-setup.png",
                PhoneAlt: "The Sample Channel public season page that shows named standings and current viewer quest progress on a narrow screen.",
                LaptopAlt: "The Sample Channel Community progression page that shows new-season dates, Public visibility and private moderator notes.",
                "The management page starts the season contract. The public page makes named standings and progress visible when the season is Public."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose authority and visibility",
                    Steps =
                    [
                        "Choose the channel, open Channel setup and turn on Community progression under Chat tools. The feature card persists the change immediately.",
                        "A channel owner or permitted moderator creates seasons, definitions and rewards and controls lifecycle and reset schedules. Participant progress comes from authenticated Twitch chat and supported Twitch or BlokeBot events.",
                        "Choose Public to publish participant Twitch identities and progression, or Hidden to publish no progression data. Private moderator notes stay on the management page in both modes.",
                    ],
                    Paragraphs =
                    [
                        "Public seasons show Twitch display names and recorded logins in standings. They show individual quest and achievement progress, communal goals and completions. They also show equipped rewards, unlock history and archived history. They never expose raw provider payloads, provider credentials, internal IDs, moderator notes or internal audit material.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Create the season contract",
                    Steps =
                    [
                        "Create a Draft season with a name, public description, UTC start and end and Public or Hidden visibility.",
                        "While it is Draft, add host-scoped Title, Badge icon or Cosmetic accent rewards. Select only supported presentation tokens. Arbitrary CSS is not accepted.",
                        "Add Quest or Achievement definitions. Choose per-viewer or channel-wide communal progress. Choose One-time or Repeatable completion, a target, optional reward keys and one supported event rule.",
                        "Open the season only after the definition and reward inventory is complete. Progress events outside the open season's start and end are not counted.",
                    ],
                    Bullets =
                    [
                        "Typed rules cover chat messages, follows, subscriptions, cheers, incoming raids, reward redemptions, completed bounties and predeclared external achievement grants.",
                        "Definitions allow only supported rule, owner, increment and filter combinations. A rejected combination remains unsaved. Choose a compatible option. Do not treat the event as generic text.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Follow individual and communal progress",
                    Bullets =
                    [
                        "A host-scoped event that matches advances each applicable active definition once. Event-value rules use the supported numeric value, while occurrence rules add one.",
                        "Per-viewer definitions update that participant's current progress, completions and standings. Communal definitions combine events that qualify into one channel-wide goal.",
                        "Participants use !progress for a short view of current Public season progress. They use /community/{channel} for standings, goals, completions and rewards.",
                        "Authorized staff can manage Hidden seasons. The !progress command reports that progression is hidden. The public route publishes no season data.",
                    ],
                    Code =
                        "!progress\n!equiptitle <reward-key>\n!equipbadge <reward-key>\n!equipaccent <reward-key>",
                },
                new SiteGuideSection
                {
                    Heading = "Schedule repeatable resets",
                    Bullets =
                    [
                        "Daily and weekly repeatable definitions use the channel timezone, the configured local time and, for weekly resets, the configured weekday. The page shows the next UTC reset.",
                        "A local time in a daylight-saving gap moves forward to the first valid instant. A local time in an overlap uses its first occurrence and does not reset again at the second.",
                        "After downtime, BlokeBot rolls at most once into the current period. It does not replay every missed daily or weekly period.",
                        "If you save a schedule change during an active period, BlokeBot closes that period. It resets active repeatable progress. Select Reset active progress now before Save schedule and roll over immediately. The confirmed change applies once across retries and multiple instances.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Grant and equip persistent rewards",
                    Bullets =
                    [
                        "A completion grants its configured points, title, badge icon and supported cosmetic accent atomically. A retry of the same completion does not grant twice.",
                        "Viewer unlocks survive season closure and archival. A viewer can equip one unlocked title, badge and accent for this host with the chat commands shown above.",
                        "An equip action checks reward ownership and host scope. It changes the current selection and does not rewrite the immutable season completion record.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Close archive and recover",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/progression/phone-dark-community-progression-disabled.png",
                        LightPhoneSource: "media/community/progression/phone-light-community-progression-disabled.png",
                        DarkLaptopSource: "media/community/progression/laptop-dark-community-progression-archive.png",
                        LightLaptopSource: "media/community/progression/laptop-light-community-progression-archive.png",
                        PhoneAlt: "The Sample Channel Community progression direct route that shows retained-state recovery while the feature is off.",
                        LaptopAlt: "The Sample Channel public progression page that shows completed achievement history, persistent reward unlocks and an archived season standings snapshot.",
                        "A close action preserves a final standings snapshot and completion history. A disable action preserves the same data and routes staff back to Channel setup."
                    ),
                    Bullets =
                    [
                        "Close and snapshot standings freezes final standings and completion history. Archive keeps that snapshot and every persistent viewer unlock and equipped selection.",
                        "If expected progress is absent, confirm the channel, dates, rule, scope, filter, period and visibility. Then send one new event.",
                        "If Community progression is off, BlokeBot stops commands, events, timers, automation, rewards and public output before mutation. Seasons, progress, schedules, rewards and history remain saved.",
                        "Re-enable to continue in the current period. BlokeBot does not replay suppressed events or every reset period missed while the feature was off.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Run Stream-event Bingo", "community/bingo"),
                new SiteLink("Use viewer-funded bounties", "community/bounties"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/bingo",
            Eyebrow = "Community progression · Bingo",
            Title = "Run Stream-event Bingo",
            Summary =
                "Issue deterministic shared, viewer or team cards, mark supported stream moments and keep public evidence and rewards reviewable.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/progression/phone-dark-bingo-public-card.png",
                LightPhoneSource: "media/community/progression/phone-light-bingo-public-card.png",
                DarkLaptopSource: "media/community/progression/laptop-dark-bingo-setup.png",
                LightLaptopSource: "media/community/progression/laptop-light-bingo-setup.png",
                PhoneAlt: "The Sample Channel public Bingo page that shows a team card, participant Twitch logins and a narrow-screen horizontal-scroll affordance.",
                LaptopAlt: "The Sample Channel Bingo management page with a template revision, Shared board mode, seed, participant cap and Open viewer action.",
                "Hosts open a game from a saved template revision. Participants see the frozen card assignment and public identity boundary."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable the required tools",
                    Steps =
                    [
                        "Choose the channel, open Channel setup and turn on Bingo under Chat tools. The feature card persists the change immediately.",
                        "A channel owner or permitted moderator manages templates, games, rosters, cards, manual marks and archives. Viewers join or leave before issue and follow cards in chat or on the public page.",
                        "Turn on Points before you open a game whose template awards points. Turn on Community progression and predeclare a viewer achievement that accepts external grants. If the Bingo win must unlock a title, attach a title reward to that achievement.",
                    ],
                    Note =
                        "A Stream category changed square also needs the selected channel's Twitch integration. Bingo owns the host-scoped channel.update subscription and keeps it absent while Bingo is off.",
                },
                new SiteGuideSection
                {
                    Heading = "Build a deterministic template",
                    Steps =
                    [
                        "Choose a 3 × 3, 4 × 4 or 5 × 5 grid and provide enough squares to fill it.",
                        "Give every square a public title and stable key. Choose Manual confirmation, Incoming raid, Bounty completed or Guessing result. You can also choose Giveaway started, Stream category changed or Counter reached.",
                        "Set only the supported threshold, counter or filter for that typed source. Put subjective staff guidance in Private moderator note.",
                        "Configure the line reward used by row, column and diagonal wins and, when wanted, enable a full-card win and reward. Save the revision.",
                    ],
                    Paragraphs =
                    [
                        "The issued card keeps the saved dimension, template revision, recorded seed and assignment identity. Later template edits do not alter frozen cards, square positions or win lines.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Open entry and freeze the roster",
                    Bullets =
                    [
                        "Choose Shared board, Unique per viewer or Teams, enter a recorded seed and optionally set a host participant cap. Team games can also set a host team cap and team names.",
                        "There is no product-wide participant cap. With no host cap, every joined viewer receives the applicable card and supported events update all cards synchronously. There is no hidden batch queue or rate-limit machinery.",
                        "Viewers use !bingojoin and can add a team name for team games. They use !bingoleave while entry is open. Owners and moderators can move or remove participants and keep private roster notes.",
                        "Select Issue and freeze cards only after you check the roster and teams. The issue action closes entry and permanently freezes participant, team and card assignments for that game.",
                    ],
                    Code = "!bingo\n!bingojoin [team name]\n!bingoleave",
                },
                new SiteGuideSection
                {
                    Heading = "Mark events and correct mistakes",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/progression/phone-dark-bingo-evidence.png",
                        LightPhoneSource: "media/community/progression/phone-light-bingo-evidence.png",
                        DarkLaptopSource: "media/community/progression/laptop-dark-bingo-moderation.png",
                        LightLaptopSource: "media/community/progression/laptop-light-bingo-moderation.png",
                        PhoneAlt: "The Sample Channel public Team Aurora Bingo card with marks, normalized evidence, a reversal and a retained rewarded win.",
                        LaptopAlt: "The Sample Channel Bingo moderator page with a frozen 4 by 4 team card, automatic marks and manual Confirm controls.",
                        "Typed automatic evidence and manual correction stay visible on the public card, while moderator notes remain in the authorized dashboard."
                    ),
                    Bullets =
                    [
                        "Automatic squares mark once from a host-scoped event that matches. Retries, restarts and provider replay do not mark the same source event twice.",
                        "Manual squares change only when an owner or moderator selects Confirm. Use Reverse to correct a mistaken manual mark. Both confirmation and reversal remain visible as public normalized evidence.",
                        "A card completes rows, columns, diagonals and the configured full-card rule from its persisted grid. Points and Community achievement or title rewards grant once per completed win rule.",
                        "If a reversed mark completed a rewarded win, BlokeBot corrects the live square. The completed win and reward remain immutable. A second mark cannot grant it again. Points and persistent unlocks are not clawed back.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Know what the public can see",
                    Bullets =
                    [
                        "The public route /bingo/{channel} shows participant Twitch names or team names. It also shows assigned cards, marks, wins and archived games.",
                        "Normalized evidence can show the event kind, time, matched square and relevant public participant name and login. Manual confirmation and reversal are public evidence too.",
                        "Raw provider payloads, provider credentials, internal identifiers, private moderator notes and internal audit reasons are never public.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Publish overlays and archives",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/progression/phone-dark-bingo-disabled.png",
                        LightPhoneSource: "media/community/progression/phone-light-bingo-disabled.png",
                        DarkLaptopSource: "media/community/progression/laptop-dark-bingo-archive.png",
                        LightLaptopSource: "media/community/progression/laptop-light-bingo-archive.png",
                        PhoneAlt: "The Sample Channel Bingo direct route that shows retained templates, cards, evidence, wins, rewards and archives while Bingo is off.",
                        LaptopAlt: "The Sample Channel public Bingo archive that shows a completed five by five Shared card at desktop width.",
                        "Archives retain the dealt grid and public evidence. A disabled signed-in route keeps the saved game intact and points back to Channel setup."
                    ),
                    Bullets =
                    [
                        "If the stream must show Bingo summaries, enable Overlays. Add an Event feed Browser Source. Keep the private Browser Source URL out of chat and screenshots.",
                        "Archive a finished game to move its frozen cards, evidence and wins into Completed history on the public page.",
                        "If entry is closed, a cap is reached or a team name is invalid, correct that condition. Then try again. Once issued, roster and assignment changes are intentionally unavailable.",
                        "If an automatic square does not mark, check its type, filter and source. Check the channel and applicable Twitch connection. Do not replace a subjective moment with an invented automatic source.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover after you turn Bingo off",
                    Bullets =
                    [
                        "If Bingo is off, BlokeBot hides navigation and public data. It stops commands, joins, marks, rewards, overlay events and channel.update reconciliation.",
                        "Templates, rosters, issued cards, normalized evidence, wins, rewards and archives remain saved. A retained signed-in direct route links to Channel setup.",
                        "Re-enable to continue from retained current state. Events, commands, subscriptions and other work suppressed while Bingo was off are not replayed.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Build Community rewards", "community/progression"),
                new SiteLink("Add an Event feed overlay", "overlays"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations",
            Eyebrow = "Native Twitch",
            Title = "Use Twitch channel tools",
            Summary =
                "Run polls, save live moments, manage rewards and settle Predictions for the selected channel.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn Native Twitch on",
                    Steps =
                    [
                        "Choose the channel in the top bar and open Channel setup.",
                        "Open Chat tools and turn on Native Twitch. The feature card persists the change immediately.",
                        "Open Native Twitch in the Chat tools navigation, then choose Polls, Clips & markers, Rewards & redemptions or Predictions.",
                    ],
                    Paragraphs =
                    [
                        "If Native Twitch is off, BlokeBot hides these pages and stops its automatic work. Saved templates, settings and history remain for the next time you turn it on.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Follow the action on the page",
                    Bullets =
                    [
                        "Polls, clips, markers, rewards, redemptions and Predictions use the selected channel's Twitch connection.",
                        "Rewards and Predictions require a Twitch Affiliate or Partner channel.",
                        "Use the ? button beside a page title for help and stay on the current task.",
                    ],
                    Note =
                        "If a page asks you to reconnect, select Reconnect to Twitch. Complete Twitch as the selected channel owner. A bot-account reconnection does not repair a channel connection. A channel reconnection does not repair the bot account.",
                },
                new SiteGuideSection
                {
                    Heading = "When a result is uncertain",
                    Steps =
                    [
                        "Read the result on the page before you repeat the action.",
                        "Reload the same page to check Twitch's current state and recent results.",
                        "Open Alerts if the page still needs attention.",
                        "Send the page name, selected channel, approximate time and alert text to the server owner. Never send Twitch tokens or secrets.",
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
                DarkPhoneSource: "media/native-twitch/phone-dark-native-polls.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-polls.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-polls.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-polls.png",
                PhoneAlt: "BlokeBot Polls page that shows a saved question, current vote totals and poll controls.",
                LaptopAlt: "BlokeBot Polls page that shows a saved question, current vote totals and poll controls.",
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
                        "Set the vote duration. Choose whether viewers can spend Channel Points on extra votes.",
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
                        "Twitch allows one active poll. A poll from another source appears here after a reload. Check its question before you end it.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "If the poll is unavailable",
                    Bullets =
                    [
                        "Use Reconnect to Twitch on this page and complete Twitch as the selected channel owner.",
                        "Finish the active poll before you start another.",
                        "If the displayed totals or result can be stale, reload before you repeat an action.",
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
                DarkPhoneSource: "media/native-twitch/phone-dark-native-clips-markers.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-clips-markers.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-clips-markers.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-clips-markers.png",
                PhoneAlt: "BlokeBot Clips and markers page that shows clip creation, stream marker and recent outcome controls.",
                LaptopAlt: "BlokeBot Clips and markers page that shows clip creation, stream marker and recent outcome controls.",
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
                        "Choose whether the clip must include the stream delay. Select Create clip once.",
                        "When Twitch completes clip preparation, open the clip from Clips and markers.",
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
                        "Markers need an active live stream with stream recordings enabled. Twitch can reject them during reruns or premieres.",
                },
                new SiteGuideSection
                {
                    Heading = "Check an unfinished attempt",
                    Bullets =
                    [
                        "If Twitch still prepares the result or the first result was uncertain, use Check status or Check outcome.",
                        "Do not make another clip or marker because Twitch takes time. A new check uses the recorded attempt.",
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
                DarkPhoneSource: "media/native-twitch/phone-dark-native-channel-points.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-channel-points.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-channel-points.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-channel-points.png",
                PhoneAlt: "BlokeBot Rewards and redemptions page that shows waiting requests, reward controls and age indicators.",
                LaptopAlt: "BlokeBot Rewards and redemptions page that shows waiting requests, reward controls and age indicators.",
                "Waiting requests appear first. Visible age cues identify requests near the stale threshold."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Answer waiting requests first",
                    Steps =
                    [
                        "Open Unfulfilled redemptions and read the reward, viewer input and waiting age.",
                        "If the request is complete, select Fulfil. If the viewer must receive points back, select Cancel & refund.",
                    ],
                    Bullets =
                    [
                        "Blue means the request is under 2 minutes old.",
                        "Amber means that the request age is from 2 minutes to under 5 minutes.",
                        "Red means that the request age is 5 minutes or more and needs attention.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage or create a reward",
                    Bullets =
                    [
                        "Here, you can edit, enable, pause or delete rewards that BlokeBot created.",
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
                        "If the Twitch result is unclear, reload before you repeat a fulfil or refund. Then check Redemption history.",
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
                DarkPhoneSource: "media/native-twitch/phone-dark-native-predictions.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-predictions.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-predictions.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-predictions.png",
                PhoneAlt: "BlokeBot Predictions page that shows a saved question, outcomes and controls for the active Prediction.",
                LaptopAlt: "BlokeBot Predictions page that shows a saved question, outcomes and controls for the active Prediction.",
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
                        "When viewers must no longer enter, select Lock.",
                        "After the real result is known, select Resolve as winner beside the correct outcome.",
                        "Select Cancel & refund only when you cannot settle the Prediction. Twitch returns the viewers' Channel Points.",
                    ],
                    Note =
                        "Resolution and refund cannot be undone. Confirm the selected channel, question and real result before you choose either action.",
                },
                new SiteGuideSection
                {
                    Heading = "If the Prediction needs attention",
                    Bullets =
                    [
                        "Predictions require a Twitch Affiliate or Partner channel.",
                        "A Prediction started elsewhere appears here after reload. Inspect it before you lock, refund or resolve it.",
                        "Use Reconnect to Twitch if this page asks for the selected channel connection.",
                        "If the Twitch state is uncertain, wait a moment and reload before you start anything new.",
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
            Media = new SiteMedia(
                DarkPhoneSource: "media/commands/phone-dark-custom-commands.png",
                LightPhoneSource: "media/commands/phone-light-custom-commands.png",
                DarkLaptopSource: "media/commands/laptop-dark-custom-commands.png",
                LightLaptopSource: "media/commands/laptop-light-custom-commands.png",
                PhoneAlt: "BlokeBot Custom commands on a phone with the saved command list and the selected command's Basics step.",
                LaptopAlt: "BlokeBot Custom commands with the saved command list beside the selected command's name, command words and chat preview.",
                "The saved command list sits beside the selected command. Its words and the viewer reply stay visible together."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Create a chat reply and command",
                    Steps =
                    [
                        "Open Custom commands, then Settings, and stay on the Commands tab.",
                        "Add a command and enter its command words without the exclamation mark. Choose who can use it.",
                        "Open Message library, add a reply with at least one message, then return to Commands.",
                        "Choose the saved reply under What happens and select Save changes.",
                    ],
                    Paragraphs =
                    [
                        "Replies can include viewer, channel and argument placeholders. The Message library keeps reusable text separate from command structure.",
                    ],
                    Note =
                        "BlokeBot cannot save a command without a message. BlokeBot opens the relevant tab or section. It focuses the field and shows the validation message. It keeps the command.",
                },
                new SiteGuideSection
                {
                    Heading = "Add random values to saved replies",
                    Bullets =
                    [
                        "{random_from|one|two} picks one value.",
                        "{random_between|1|10} picks an inclusive whole number.",
                        "Each random token occurrence makes a fresh pick.",
                        "{random_viewer} picks from Twitch chatters currently connected to chat. The active bot account must be a moderator with connected-chatter access.",
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
                        "Twitch announcement uses Twitch's coloured announcement surface. The bot must currently be a moderator and authorized for announcements.",
                        "If a scheduled send cannot happen, open its Alerts section and follow the displayed next action.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Start visual automation flows",
                    Paragraphs =
                    [
                        "Choose Run automation flows under What happens. Every enabled flow whose Custom command event selects this command starts from the same chat invocation.",
                    ],
                    Bullets =
                    [
                        "Custom commands and Automations must both be on for a command to start a flow.",
                        "Build the connection in Visual automations. The command does not keep a second flow picker.",
                        "Turning either feature off keeps the command, flow, and run history but suppresses new work without replaying it later.",
                    ],
                    Links = [new SiteLink("Build a visual automation", "automations")],
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
                "Choose one global Commands trigger. Viewers can discover a safe list of main command names for the selected channel's current state.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/commands/phone-dark-viewer-command-catalog.png",
                LightPhoneSource: "media/commands/phone-light-viewer-command-catalog.png",
                DarkLaptopSource: "media/commands/laptop-dark-viewer-command-catalog.png",
                LightLaptopSource: "media/commands/laptop-light-viewer-command-catalog.png",
                PhoneAlt: "Channel setup on a phone that shows the global Commands trigger and expanded Available viewer commands list.",
                LaptopAlt: "Channel setup that shows the global Commands trigger, expanded Available viewer commands list and a command-name conflict.",
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
                        "Enter the command words that viewers can use. Separate them with commas and omit the exclamation mark. The default is commands.",
                        "Select Save Commands. The setting applies to the whole selected channel, not to one Custom Command.",
                        "Leave the field blank and save only when you intend to disable the viewer command catalog.",
                    ],
                    Paragraphs =
                    [
                        "If a word is already owned by another command, Channel setup names the conflict. Choose another word and save. BlokeBot does not silently replace the existing command.",
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
                        "The disclosure requests a fresh snapshot whenever it opens. Supported state changes also refresh an open list. They do not replace an unsaved trigger draft.",
                        "The list includes its own saved trigger and only commands an ordinary viewer can use.",
                        "Moderator-only commands and private administration actions are never disclosed.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Understand main names",
                    Paragraphs =
                    [
                        "Each Custom Command contributes only the first command word in its saved alias list. That main-name rule keeps the catalog short and predictable. Secondary aliases still work in chat but are not advertised.",
                    ],
                    Bullets =
                    [
                        "Built-in commands use their supported public main names.",
                        "The catalog omits a moderator-only Custom Command even when its main name works for moderators.",
                        "If two routes claim the same word, the catalog reports the shadowed entry. It does not report both as available.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Why commands appear or disappear",
                    Bullets =
                    [
                        "Guess and round-summary commands appear only while the guessing game has the applicable active round state.",
                        "Giveaway entry appears only while a giveaway accepts entries.",
                        "Request-board and play-queue commands follow the channel's saved, enabled boards and queues.",
                        "Moment and clip commands depend on live-stream identity. They disappear when the channel is offline or Twitch stream identity is unavailable.",
                        "Feature commands disappear when that feature is off for the selected channel.",
                    ],
                    Paragraphs =
                    [
                        "If BlokeBot identifies the cause, it explains the unavailable feature beside the list. If no viewer commands are available, the disclosure reports this fact. It does not publish an incorrect list.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Long lists and live changes",
                    Paragraphs =
                    [
                        "BlokeBot keeps the command order stable. If the chat response exceeds the Twitch limit, BlokeBot splits the list across ordinary replies. It does not omit or duplicate names.",
                        "A game, giveaway, board, queue, feature switch or stream-liveness change can alter membership. Before you prepare an announcement or stream instructions, reopen Available viewer commands for a new check.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Fix common catalog problems",
                    Bullets =
                    [
                        "The chat trigger does nothing: confirm that at least one Commands word is saved. Resolve each conflict in Channel setup.",
                        "A Custom Command alias is absent: only its first saved word is advertised.",
                        "A moderator command is absent: the public catalog deliberately shows viewer-safe commands only.",
                        "A game or Moment command is absent: check the feature and active round or giveaway. Check the named live-stream state.",
                        "The list is empty: enable or configure a viewer feature, board, queue or Custom Command. Reopen the disclosure.",
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
                DarkPhoneSource: "media/points-and-guessing/phone-dark-guessing-workflow.webp",
                LightPhoneSource: "media/points-and-guessing/phone-light-guessing-workflow.webp",
                DarkLaptopSource: "media/points-and-guessing/laptop-dark-guessing-workflow.webp",
                LightLaptopSource: "media/points-and-guessing/laptop-light-guessing-workflow.webp",
                PhoneAlt: "Animated BlokeBot guessing dashboard that moves through a live round workflow.",
                LaptopAlt: "Animated BlokeBot guessing dashboard that moves through a live round workflow.",
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
                        "Create a round type and add every accepted answer. Put comma-separated aliases after its main answer. Choose a winner point reward.",
                        "Review the chat commands and bot replies, then save.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run the round",
                    Steps =
                    [
                        "Open the Guessing game Dashboard and choose the round type.",
                        "Start the round, let viewers submit guesses, then select Stop guessing.",
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
                "Give each viewer a channel balance. Viewers can check, transfer or gamble points. Staff can adjust balances or award prizes.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/points-and-guessing/phone-dark-points-settings.png",
                LightPhoneSource: "media/points-and-guessing/phone-light-points-settings.png",
                DarkLaptopSource: "media/points-and-guessing/laptop-dark-points-settings.png",
                LightLaptopSource: "media/points-and-guessing/laptop-light-points-settings.png",
                PhoneAlt: "Points settings with the point label, gambling chance, cooldown and chat command words.",
                LaptopAlt: "Points settings with the point label, gambling chance, cooldown and chat command words.",
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
                        "Check both names and the amount. Then move points, add points or take points away.",
                        "Use Recent changes to confirm adjustments and prizes. Delete balance only when the whole record must go.",
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
                DarkPhoneSource: "media/points-and-guessing/phone-dark-points-settings.png",
                LightPhoneSource: "media/points-and-guessing/phone-light-points-settings.png",
                DarkLaptopSource: "media/points-and-guessing/laptop-dark-points-settings.png",
                LightLaptopSource: "media/points-and-guessing/laptop-light-points-settings.png",
                PhoneAlt: "Points settings page for the configuration of channel point commands and giveaway rules.",
                LaptopAlt: "Points settings page for the configuration of channel point commands and giveaway rules.",
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
                        "Save the settings before you go live.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Start and finish",
                    Steps =
                    [
                        "While the Twitch channel is live, open the Points Dashboard and select Start in Giveaway.",
                        "Tell viewers to use the configured join command. Each eligible viewer can enter once.",
                        "Select End to draw winners and award prizes, or Cancel to stop without winners.",
                    ],
                    Paragraphs =
                    [
                        "If Start is unavailable, check stream status, an active giveaway and the cooldown shown by the dashboard.",
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
                DarkPhoneSource: "media/points-and-guessing/phone-dark-guessing-leaderboard.png",
                LightPhoneSource: "media/points-and-guessing/phone-light-guessing-leaderboard.png",
                DarkLaptopSource: "media/points-and-guessing/laptop-dark-guessing-leaderboard.png",
                LightLaptopSource: "media/points-and-guessing/laptop-light-guessing-leaderboard.png",
                PhoneAlt: "Public guessing leaderboard that shows players, correct guesses, rounds and accuracy.",
                LaptopAlt: "Public guessing leaderboard that shows players, correct guesses, rounds and accuracy.",
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
                "Build channel flows on a snapped grid. Connect typed triggers to conditions and actions. Then, validate and test the flow before you enable it.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/automations/phone-dark-grid-visual-automations.png",
                LightPhoneSource: "media/automations/phone-light-grid-visual-automations.png",
                DarkLaptopSource: "media/automations/desktop-dark-grid-visual-automations.png",
                LightLaptopSource: "media/automations/desktop-light-grid-visual-automations.png",
                PhoneAlt: "The Visual automations editor at 390 pixels. It shows snapped nodes and the validation state.",
                LaptopAlt: "The Visual automations editor. It shows the flow library, typed nodes, connections, and the node inspector.",
                "Use Grid view to arrange nodes. Use List view to inspect the same flow in its run order."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn Automations on",
                    Steps =
                    [
                        "Choose the channel in the top bar and open Channel setup.",
                        "Open Chat tools and turn on Automations. BlokeBot saves the change at once.",
                        "Open Automations. Create a flow and choose a trigger from the Node library.",
                    ],
                    Paragraphs =
                    [
                        "Automations is off by default for each channel. The channel owner or a permitted moderator manages it for the selected channel.",
                    ],
                    Note =
                        "If Automations is off, saved flows and run history remain. Events do not start flows.",
                },
                new SiteGuideSection
                {
                    Heading = "Build on the snapped grid",
                    Steps =
                    [
                        "Search the Node library. Add one or more triggers, then add controls and actions.",
                        "Select a node to open its inspector from the right. Drag an output port to a compatible input port or node.",
                        "Drag nodes on the 24-pixel grid, or move them with the keyboard. Use the canvas controls to set the flow direction and connection style.",
                        "Use Ctrl and the mouse wheel to zoom. Drag the background to move the canvas. Hold Alt and drag to select nodes.",
                        "Save the draft. Validate it, and fix each disconnected node, invalid input, cycle, missing reference, or unavailable channel tool.",
                    ],
                    Note =
                        "Each trigger starts a separate flow run. If triggers connect to the same node, each run continues through that node. A flow cannot contain a cycle.",
                },
                new SiteGuideSection
                {
                    Heading = "Test and enable safely",
                    Bullets =
                    [
                        "Test flow runs a sample event through the graph and reports each node result. It does not send chat, change points, play an overlay, or call Twitch.",
                        "Invalid graphs cannot be enabled. A flow that can send public messages, change points, play overlays, or call Twitch shows an explicit warning before enablement.",
                        "The run drawer shows the latest sample and recent live results. It identifies the node that failed, even if the flow continued.",
                        "Duplicate copies the graph and node positions as a disabled draft without copying run history.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Understand a flow",
                    Paragraphs =
                    [
                        "A flow connects triggers to actions through typed connections. Each step receives only a compatible value.",
                    ],
                    Bullets =
                    [
                        "A selected custom command, Twitch event or Channel Points redemption can start a flow.",
                        "A Condition checks a CEL expression against the trigger data. The flow continues through Matches or Does not match.",
                        "A Delay waits the configured time before the flow continues. Delayed flows do not block chat or other automations.",
                        "Actions send chat messages, play overlay cues, complete Channel Points redemptions and run native Twitch operations.",
                    ],
                    Note =
                        "Grid and List views edit the same saved graph. BlokeBot saves each node configuration and snapped position.",
                },
                new SiteGuideSection
                {
                    Heading = "Use event data safely",
                    Bullets =
                    [
                        "Each source publishes typed values for the flow. Values can include the viewer, command text, channel, event time and live stream identity.",
                        "Chat messages, marker descriptions, poll and prediction questions and expressions can include automation variables that carry those values.",
                        "BlokeBot treats viewer identities and typed text as sensitive. By default, it keeps these values out of overlays and logs.",
                    ],
                    Paragraphs =
                    [
                        "The canonical privacy notice covers automation run records and the source event context.",
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
                        "Actions inherit their feature switches. Overlay cues need Overlays. Native Twitch operations need their Native Twitch feature. Command starts need Custom commands.",
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
                "The Twitch events page lists each automation source for the selected channel. It shows the required Twitch approval and current use.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Read the source list",
                    Bullets =
                    [
                        "Open Automations, then Twitch events. Each source shows its Twitch subscription, the required approval and whether an enabled flow uses it today.",
                        "Ready means the source can start flows now. Reconnect needed and Twitch connection needed mean that the source stays inactive. BlokeBot creates no Twitch subscription and starts no flow.",
                        "Use Reconnect to Twitch on this page and complete Twitch as the selected channel owner to approve the required permissions.",
                        "A source's Twitch subscription follows the bot runtime and exists only while an enabled flow uses that source.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Stream, community and Hype Train events",
                    Bullets =
                    [
                        "Stream went live, Stream went offline, New follower and Incoming raid need no approval beyond the channel's bot connection.",
                        "New subscription and Gifted subscriptions need the channel's subscription-reading permission. Cheer needs Bits reading. The three Hype Train events need Hype Train reading. The page names each required approval exactly.",
                        "The Gifted subscriptions source uses a minimum gift count. Cheer uses a minimum Bits amount. Incoming raid uses a minimum viewer count. Smaller events do not start a flow.",
                        "Chat notification starts flows from typed Twitch notices such as announcements, resubs, gift upgrades and charity donations. You choose the notification type. Ordinary chat messages never start automations.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Channel Points redemptions",
                    Bullets =
                    [
                        "The Channel Points redemption source starts a flow when a viewer redeems a Custom Reward. It needs the channel's redemption permissions and a Twitch Affiliate or Partner channel.",
                        "A reward filter limits the source to one Custom Reward. Without it, every redemption starts the flow.",
                        "The completion policy controls the redemption status after the flow. Keep it manual, fulfil it after success or cancel it after failure. Cancellation refunds the viewer.",
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
                        "Shoutout sent and Shoutout received follow the bot account's moderator approvals and appear only while Raid & collaboration is on.",
                        "Poll started, Poll progressed and Poll ended need the channel's poll-reading permission and appear only while Polls is on.",
                        "Prediction events need the channel's Prediction-reading permission. They appear only while Predictions is on.",
                        "These sources report all polls and Predictions. This includes operations from outside BlokeBot.",
                    ],
                    Links = [new SiteLink("Set up Native Twitch", "twitch-operations")],
                },
                new SiteGuideSection
                {
                    Heading = "Start flows from a custom command",
                    Paragraphs =
                    [
                        "The Custom command source starts a flow after chat uses a selected custom command. It provides the viewer and command text. Create the command and choose Run automation flow under What happens. Custom commands and Automations must both be on.",
                    ],
                    Links = [new SiteLink("Create Custom Commands", "commands")],
                },
                new SiteGuideSection
                {
                    Heading = "When events do not arrive",
                    Bullets =
                    [
                        "Check the source badge first. For an inactive source, the badge names the required approval or connection.",
                        "BlokeBot does not replay events from an inactive source, a disabled flow or a period when Automations was off.",
                        "BlokeBot recognizes a repeated Twitch delivery inside ten minutes and starts nothing extra. This is not a lost event.",
                        "If the page cannot load, retry from the message shown. Your saved automations remain unchanged.",
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
                "Actions send chat, play overlay cues, complete Channel Points redemptions and run Native Twitch operations. Each action obeys its feature switches and Twitch limits.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Send chat and play overlay cues",
                    Bullets =
                    [
                        "Send chat message sends up to 500 characters in the channel. The message can include automation variables from the source event.",
                        "Play overlay cue plays a saved Cue through a chosen Cue player Browser Source. Enable the cue, Cue player and Overlays feature for playback.",
                        "A replaced or deleted cue or Cue player makes the action fail. BlokeBot does not play a substitute.",
                    ],
                    Links = [new SiteLink("Build reusable Cues", "overlays/cues")],
                },
                new SiteGuideSection
                {
                    Heading = "Complete Channel Points redemptions",
                    Bullets =
                    [
                        "Fulfil redemption marks the source Channel Points redemption as fulfilled. Cancel redemption cancels it so Twitch refunds the viewer's points.",
                        "Both actions apply only to the redemption that started the flow. The redemption must have the Unfulfilled state and use a reward that BlokeBot manages.",
                        "Prefer the redemption source's completion policy for whole-flow outcomes. Use these actions when a flow must settle the redemption at a specific step.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run native Twitch operations",
                    Paragraphs =
                    [
                        "Nine actions run the same operations as the Native Twitch pages. They use the channel connection and each operation's switch and requirements.",
                    ],
                    Bullets =
                    [
                        "Send shoutout targets the broadcaster who triggered the flow, such as a broadcaster from a raid. The source event must carry a viewer or broadcaster.",
                        "Start poll accepts a question of up to 60 characters. Add 2–5 choices of up to 25 characters. Set a 15-second to 30-minute duration and an optional Channel Points cost per extra vote. If another poll is active, it fails.",
                        "End poll finishes the channel's active poll immediately. A poll started outside BlokeBot is never ended by an automation.",
                        "Create clip captures the live stream immediately or after Twitch's broadcast delay. Create stream marker adds a marker with a description of up to 140 characters.",
                        "Start prediction accepts a question of up to 45 characters. Add 2–10 outcomes of up to 25 characters. Set a 30-second to 30-minute window. If another Prediction is active, it fails.",
                        "Lock prediction stops entries. Cancel prediction refunds all viewer Channel Points. Resolve prediction uses an outcome identifier from a variable or expression.",
                    ],
                    Note =
                        "Rewards and Predictions require a Twitch Affiliate or Partner channel. Each operation follows the prerequisites on its Native Twitch page.",
                    Links = [new SiteLink("Use Native Twitch tools", "twitch-operations")],
                },
                new SiteGuideSection
                {
                    Heading = "Check an action's outcome",
                    Bullets =
                    [
                        "A failed action follows its step's failure choice: stop the flow or continue past the failure.",
                        "BlokeBot does not retry an action with an uncertain Twitch outcome. It never duplicates an action to force an answer.",
                        "The applicable feature page shows the Twitch result. It shows shoutouts, the active poll or Prediction, clips and redemptions.",
                        "If an action continues to fail, fix the named connection, permission or feature switch. Then run the flow again. Alerts collects problems that need attention.",
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
                        "Custom-bot encryption needs no operator configuration. ASP.NET Core manages Data Protection keys automatically in private persistent application state. Windows protects those keys with DPAPI LocalMachine.",
                        "A copied SQLite database or SQL backup does not expose reusable custom-bot tokens. Theft or compromise of the full state directory or active host is outside that boundary.",
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
