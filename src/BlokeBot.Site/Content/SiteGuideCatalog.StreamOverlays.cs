namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateStreamOverlayPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/overlays",
            Eyebrow = "Stream presentation · Browser Sources",
            Title = "Create Browser Sources for OBS",
            Summary = "Configure Browser Sources.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/overlays/phone-dark-overlay-sources.png",
                LightPhoneSource: "media/overlays/phone-light-overlay-sources.png",
                DarkLaptopSource: "media/overlays/laptop-dark-overlay-sources.png",
                LightLaptopSource: "media/overlays/laptop-light-overlay-sources.png",
                PhoneAlt: "BlokeBot Browser Sources on a phone shows a saved source and Preview. Appearance controls are also visible.",
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
                        "Create private Browser Sources.",
                        "Preview their content.",
                        "Position the content.",
                        "Keep each saved source operational in OBS.",
                        "Choose the channel you intend to show on stream. The owner or a permitted moderator can manage its Browser Sources.",
                        "Open Channel setup.",
                        "Turn on Overlays. The feature card saves the change immediately.",
                        "For a Guessing Browser Source, turn on Guessing.",
                        "For a Points Browser Source, turn on Points.",
                        "For a Play with viewers Browser Source, turn on Play with viewers. These features require both switches for preview and display.",
                        "Use software that supports web Browser Sources, such as OBS Studio.",
                        "Open Overlays under Chat tools.",
                        "Sources is a tab on the Overlays page.",
                        "Cues is a tab on the same page.",
                        "Media is a tab on the same page.",
                        "In BlokeBot, the Sources address is /overlays#sources.",
                        "The Cues address is /overlays#cues.",
                        "The Media address is /overlays#media.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Keep the private Browser Source URL out of chat.",
                        "Keep the private Browser Source URL out of screenshots.",
                        "Keep the private Browser Source URL out of stream recordings.",
                        "Keep the private Browser Source URL out of public notes.",
                    ],
                    Heading = "Create and protect a Browser Source",
                    Paragraphs =
                    [
                        "New opens an unsaved editor. Only Create overlay creates the source.",
                        "BlokeBot can show the private Browser Source URL only after creation or rotation.",
                    ],
                    Steps =
                    [
                        "On Sources, select New.",
                        "Enter a name.",
                        "Select its type.",
                        "Complete the type-specific settings.",
                        "Select Create overlay.",
                        "Copy the private Browser Source URL when it appears.",
                        "In OBS, add a Browser Source.",
                        "Paste the URL.",
                        "Set Width to 1920.",
                        "Set Height to 1080.",
                        "Place the source in the scene.",
                    ],
                    Note =
                        "Treat the private URL like a password. If someone shared or possibly shared the private URL, rotate it immediately. The old URL then stops operation.",
                },
                new SiteGuideSection
                {
                    Heading = "Preview and edit appearance",
                    Bullets =
                    [
                        "Preview is above configuration. Choose Live for the current saved state or Representative to inspect a useful example before the real trigger happens.",
                        "The 1920 × 1080 canvas shows how the selected Browser Source will look in OBS. Drag anywhere on the selected body to move it. Drag an edge or corner to resize it.",
                        "Use the arrow keys on the selected body for one-pixel movement. Use Shift plus an arrow for ten pixels. The keyboard-operable edges and corners resize in the same increments.",
                        "For precise geometry, enter X.",
                        "For precise geometry, enter Y.",
                        "For precise geometry, enter Width.",
                        "For precise geometry, enter Height.",
                        "Reset restores the type's default placement.",
                        "Until you save, changes to geometry update only the authenticated Preview.",
                        "Until you save, changes to style update only the authenticated Preview.",
                        "Until you save, changes to display choices update only the authenticated Preview.",
                        "Select Save overlay before you expect a change in OBS or another private Browser Source.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Advanced styling",
                    LegacyAnchor = "use-advanced-styling-safely",
                    Paragraphs =
                    [
                        "Advanced styling starts collapsed. Overlay-local CSS applies only to the selected source. Available selectors appear below.",
                    ],
                    Bullets =
                    [
                        ".overlay and .card.",
                        ".accent and .kicker.",
                        ".title and .detail.",
                        ".result.",
                        "Use the listed selectors to adjust colors and type. These selectors do not change the dashboard or another Browser Source.",
                        "BlokeBot rejects imports and external URLs.",
                        "BlokeBot rejects markup and scripts.",
                        "BlokeBot rejects at-rules and selectors outside the selected Browser Source.",
                        "If BlokeBot rejects CSS, correct the issue in the message. Save again. BlokeBot does not apply part of an invalid change. The last saved appearance remains live.",
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
                        LaptopAlt: "The Guessing Browser Source shows representative round choices. It also shows draggable Preview and settings.",
                        "Representative states let you place the Guessing Browser Source before a real round begins."
                    ),
                    Bullets =
                    [
                        "Turn on Guessing game.",
                        "Create the Browser Source.",
                        "Select whether to show the number of guesses.",
                        "Use Representative to inspect the Open state.",
                        "Use Representative to inspect the Closed state.",
                        "Use Representative to inspect the Result state.",
                        "Save the appearance.",
                        "Use the normal Guessing dashboard to start a round.",
                        "Stop the round.",
                        "Resolve the round.",
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
                        LaptopAlt: "The Giveaway Browser Source shows an active giveaway. Compact display controls and appearance options are also visible.",
                        "The active Giveaway Preview shows useful live content. Without an active giveaway the Browser Source renders nothing."
                    ),
                    Bullets =
                    [
                        "Turn on Points.",
                        "Select a Giveaway title.",
                        "Set the entrant count.",
                        "Set the close-time countdown.",
                        "Set the current join command.",
                        "Use Representative to inspect Open presentation.",
                        "Use Representative to inspect Closing presentation.",
                        "Use Representative to inspect Completed presentation.",
                        "Use Representative to inspect Cancelled presentation.",
                        "Save before you run the giveaway from Points.",
                        "When there is no active giveaway, the Browser Source renders nothing. There is no idle card for viewers.",
                        "If it stays blank during an active giveaway, check Overlays and Points.",
                        "Check the source.",
                        "Check the private URL.",
                        "Check the giveaway state.",
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
                        PhoneAlt: "The Sample Channel Community milestone Browser Source editor shows source selection and rotation. It also shows representative progress states.",
                        LaptopAlt: "The Sample Channel Community milestone Browser Source editor shows source selection and rotation. It also shows representative progress states.",
                        "The signed-in editor selects bounded authoritative data. The private Browser Source renders current public progress and does not expose its URL."
                    ),
                    Paragraphs =
                    [
                        "A community goal inherits Community progression and Overlays. A bounty inherits Bounties and Overlays. Bounties itself remains unavailable when its required Points switch is off.",
                        "A bounty can also show a bounded number of recent public contributor login and amount callouts.",
                        "Representative does not change the goal or bounty.",
                    ],
                    Steps =
                    [
                        "Create a Community goal or Viewer-funded bounty Browser Source.",
                        "Choose one current public item or rotate current public items at the saved interval.",
                        "Use Representative to inspect the available states.",
                        "Position the source.",
                        "Save the source.",
                        "Use its private URL in OBS.",
                    ],
                    Bullets =
                    [
                        "Representative state: Active.",
                        "Representative state: Progress update.",
                        "Representative state: Completed.",
                        "Representative state: Failed.",
                        "Representative state: Expired.",
                        "Representative state: Empty.",
                        "Live contributions coalesce and update current progress without refresh. A reconnection restores the latest authoritative state. It does not replay each prior contribution or completion animation.",
                        "Community goal output contains public communal definitions only.",
                        "Community goal output excludes Hidden seasons and per-viewer progress.",
                        "Community goal output excludes identities and private notes.",
                        "Bounty output contains the public title and public progress.",
                        "Bounty output contains the public target and the public percentage.",
                        "Bounty output contains the public expiry and the public lifecycle state.",
                        "Bounty output contains only the configured public pledge callouts.",
                        "Bounty output excludes private bounties and Twitch user IDs.",
                        "Bounty output excludes balances and moderation reasons.",
                        "Bounty output excludes internal accounting.",
                        "If either inherited parent is off, the retained editor points to Channel setup.",
                        "If either inherited parent is off, projection stops.",
                        "If either inherited parent is off, preview stops.",
                        "If either inherited parent is off, tests stop.",
                        "If either inherited parent is off, publication stops.",
                        "If either inherited parent is off, reconnection stops.",
                        "If either inherited parent is off, animation stops.",
                        "Saved source and domain history remain. An enable action restores the current state.",
                        "BlokeBot does not replay suppressed updates.",
                        "BlokeBot does not replay suppressed timers.",
                        "BlokeBot does not replay suppressed queued work.",
                        "BlokeBot does not replay suppressed animations.",
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
                        LaptopAlt: "The Event feed Browser Source shows Preview and its waiting-card limit. It also shows enabled event sources.",
                        "One Event feed can present point awards and Guessing winners. It can also present Giveaway winners and Bingo events. Achievement completions are another supported kind."
                    ),
                    Bullets =
                    [
                        "Choose the maximum waiting cards and what happens when the feed is full.",
                        "Independently turn point awards and Guessing winners on or off.",
                        "Independently turn Giveaway winners and Bingo events on or off.",
                        "Independently turn achievement completions on or off.",
                        "Settings for an off source collapse and keep their saved values.",
                        "For each enabled source, edit its message.",
                        "Edit its priority and display time.",
                        "Select a Representative event to check the result.",
                        "If an expected card is absent, check that its feature and event source are on. Re-enable the source for future events. BlokeBot does not replay events that it missed while the source was off.",
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
                        "Achievement completion is one bounded Event feed kind. It has its own message and priority. It also has its own duration and representative preview."
                    ),
                    Bullets =
                    [
                        "Turn on Overlays and Community progression.",
                        "Select the Event feed source.",
                        "Enable Achievement completion. This feature does not create an additional Channel setup switch.",
                        "Set the public-safe template.",
                        "Set the priority and display time.",
                        "Preview a Representative completion. Preview and test do not grant an achievement or change progression.",
                        "A genuine supported achievement completion enters the queue once.",
                        "An achievement card can show the viewer name.",
                        "An achievement card can show the achievement name.",
                        "An achievement card can show presentation-safe rewards or points.",
                        "Achievement cards exclude Twitch user IDs and balances.",
                        "Achievement cards exclude moderator notes and internal keys.",
                        "Achievement cards exclude reward tokens.",
                        "If either parent is off, BlokeBot immediately clears a connected achievement card.",
                        "If either parent is off, BlokeBot blocks projection.",
                        "If either parent is off, BlokeBot blocks the queue.",
                        "If either parent is off, BlokeBot blocks preview.",
                        "If either parent is off, BlokeBot blocks publication.",
                        "If either parent is off, BlokeBot blocks reconnection.",
                        "Other configured Event feed kinds can continue when their own requirements are met.",
                        "Saved feed configuration and history remain. Re-enable accepts only new achievement completions.",
                        "BlokeBot does not replay suppressed events.",
                        "It does not replay suppressed queued work.",
                        "It does not replay suppressed timers.",
                        "It does not replay suppressed animations.",
                        "After the clear, stale pre-disable publication cannot reappear.",
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
                        "Viewer Queue presents the current and next viewers. It also presents waiting viewers. It does not expose private party information."
                    ),
                    Bullets =
                    [
                        "Turn on Play with viewers. Create a queue first. A viewer must sign in with Twitch to use its viewer page. There is no unsigned typed-login fallback.",
                        "Choose the queue.",
                        "Choose the number of Current party rows.",
                        "Choose the number of Next rows.",
                        "Inspect the Open example.",
                        "Inspect the Ready check example.",
                        "Inspect the Party selected example.",
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
                        "A Cue player is a Browser Source target for reusable Cues.",
                        "Create the Cue player’s private URL here.",
                        "Protect that URL.",
                        "On the Cues page, build content.",
                        "Test the content.",
                        "Send test pulse checks the selected enabled source. A connected Preview or OBS source responds and does not expose its private URL.",
                        "If OBS is stale after a network loss or restart, reload that Browser Source. It reads the latest saved state and reconnects.",
                        "Rename keeps the private URL.",
                        "Disable stops display and retains setup.",
                        "Rotate revokes the old URL.",
                        "Delete permanently removes the source.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Build and trigger reusable Cues", "overlays/cues"),
                new SiteLink("Manage media for Cues", "overlays/media"),
            ],
        };
    }
}
