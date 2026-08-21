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
    }
}
