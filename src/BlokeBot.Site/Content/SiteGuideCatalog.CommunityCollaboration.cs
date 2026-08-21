namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommunityCollaborationPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/raid-collaboration",
            Eyebrow = "Community interaction · Raids",
            Title = "Use the raid and collaboration hub",
            Summary =
                "Welcome each raid community once. Build a live shortlist from approved channels and optional followed channels. Confirm every outgoing Twitch raid.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/figures/phone-dark-raid-collaboration.png",
                LightPhoneSource: "media/community/figures/phone-light-raid-collaboration.png",
                DarkLaptopSource: "media/community/figures/laptop-dark-raid-collaboration.png",
                LightLaptopSource: "media/community/figures/laptop-light-raid-collaboration.png",
                PhoneAlt: "The Sample Channel raid hub on a narrow screen with live shortlist channels and host-controlled actions.",
                LaptopAlt: "The Sample Channel raid hub with live shortlist channels and host-controlled Shoutout and Prepare raid actions.",
                "The hub shows live candidates, filter reasons, source evidence, and host-confirmed Twitch actions."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Connect Twitch and enable the feature",
                    Steps =
                    [
                        "Connect the selected Twitch channel. Ask its owner or permitted moderator to turn on Raid & collaboration in Channel setup.",
                        "Expect the feature card to save the change immediately. Expect each channel to start with this switch off.",
                        "As the owner or a permitted moderator, configure the welcome settings and shortlist. Operate the hub.",
                        "As a viewer, do not approve channels or start provider actions.",
                        "If Confirm and start raid reports no authority, ask the channel owner to reconnect Twitch with raid management access.",
                    ],
                    Note =
                        "Raid & collaboration controls all manual and automatic shoutouts. BlokeBot checks Twitch authority, live state, cooldown, and the provider before each send.",
                },
                new SiteGuideSection
                {
                    Heading = "Welcome a raid",
                    Bullets =
                    [
                        "Choose whether the welcome message runs. Write the bounded tokenized message. Set the duplicate-prevention window.",
                        "Twitch EventSub supplies the source channel and one total viewer count. BlokeBot does not infer or store individual viewer attribution.",
                        "Duplicate deliveries create one history entry. Repeated raids within the configured window do not repeat the welcome sequence.",
                        "The history records welcome and shoutout outcomes. A cooldown, offline state, or provider rejection records an explicit non-success.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Configure automatic raid shoutouts",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/figures/phone-dark-shoutout-setup.png",
                        LightPhoneSource: "media/community/figures/phone-light-shoutout-setup.png",
                        DarkLaptopSource: "media/community/figures/laptop-dark-shoutout-setup.png",
                        LightLaptopSource: "media/community/figures/laptop-light-shoutout-setup.png",
                        PhoneAlt: "The Sample Channel automatic raid shoutout settings that show the pinned chat message, available tokens, and viewer preview.",
                        LaptopAlt: "The Sample Channel automatic raid shoutout settings that show the pinned chat message, available tokens, and viewer preview.",
                        "The automatic shoutout setup shows the message, its tokens, and the viewer preview before one save."
                    ),
                    Steps =
                    [
                        "Expect automatic raid shoutouts to start off. Open Settings. Turn them on and set the minimum viewer count.",
                        "Turn on Only shoutout approved channels to restrict shoutouts to channels that you approved.",
                        "Choose either a Native Twitch shoutout or a Chat message.",
                        "For a chat message, choose Regular, Pinned, or Announcement. Set a Pinned duration from 30 to 1,800 seconds, or until stream end. For Announcement, choose Default, Blue, Green, Orange, or Purple.",
                        "Write the message. Check its preview and readiness note. Then save the settings once.",
                    ],
                    Bullets =
                    [
                        "Message tokens include {twitch_handle}, {display_name}, {channel_url}, {viewer_count}, {last_game|fallback}, and {stream_title|fallback}.",
                        "Last game and stream title require an inline fallback because Twitch can omit them.",
                        "BlokeBot processes each eligible raid once. It does not replace a failed native shoutout with a chat message.",
                        "BlokeBot does not replace a failed announcement with a regular message.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Send a shoutout and check its outcome",
                    Bullets =
                    [
                        "Enter a live Twitch name with viewers in Send a shoutout. Wait for the result. Use the displayed cooldown before another send.",
                        "Select Approve channel on a history entry to add that channel to Approved channels. Expect the action to disappear after approval.",
                        "Automatic shoutout outcomes shows the newest raid results and reasons for a skipped or incomplete send. Twitch can skip native shoutouts during its cooldown.",
                        "Twitch can send a Pinned message but fail to pin it. The outcome reports both parts.",
                        "Before the next raid, correct the connection or permission in the outcome. Do not retry or use a fallback for an earlier raid.",
                        "If BlokeBot requests a bot account reconnection, restore its moderator role. Then reconnect it from Channel setup.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Create a live shortlist",
                    Steps =
                    [
                        "Add Twitch channels to Approved channels only after this channel makes a deliberate approval decision.",
                        "To add another source, turn on Include followed live channels.",
                        "Reconnect the channel owner when BlokeBot requests permission to read followed channels.",
                        "Treat a Twitch follow only as discovery evidence. It is not approval, safety, or reputation evidence.",
                        "If required, restrict the language and categories. Set the recent outgoing-relationship gap. Save the settings.",
                        "Review the live candidates and each exclusion reason. Expect the hub to check current live state, language, category, and recent history.",
                        "Use an approved clip only if Twitch confirms its channel and an age of 30 days or fewer.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Confirm host-controlled actions",
                    Bullets =
                    [
                        "Shoutout uses the shared native Twitch boundary. BlokeBot repeats authority, live-state, cooldown, and duplicate checks before the send.",
                        "Select Prepare raid to open confirmation for the live shortlist channel. This action does not call Twitch.",
                        "Only Confirm and start raid calls the outgoing provider after new feature and Twitch authority checks. The hub never starts a raid automatically.",
                        "Before each shortlist action, BlokeBot checks the stable channel ID, source, live state, filters, and relationship gap again.",
                        "If the target goes offline or becomes ineligible, refresh the hub and make a new host decision.",
                        "If authorization expires or the provider declines, use the same recovery. Do not treat the previous preparation as authority.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pause, retain, and restore",
                    Bullets =
                    [
                        "Turn off Raid & collaboration to remove discovery.",
                        "BlokeBot then blocks the hub, EventSub subscription, reconciliation, welcome sequence, history changes, shortlist checks, shoutout handoff, and provider calls before effects.",
                        "BlokeBot retains private settings, approved channels, and history. The signed-in direct route points to Channel setup.",
                        "Re-enable the feature to establish current subscriptions and accept new work.",
                        "BlokeBot does not replay suppressed events, welcome steps, shoutouts, timers, queued work, or raid actions from the host.",
                        "If raids remain absent, verify the selected channel ID and Twitch connection. Do not rely on a login fallback.",
                        "If native actions fail, reconnect the account for the owning channel with the requested permission.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Coordinate a multi-host raid relay", "community/collectives"),
                new SiteLink("Use the other Twitch channel tools", "twitch-operations"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/blokeraid",
            Eyebrow = "Community progression · Cooperative game",
            Title = "Run a persistent BlokeRaid campaign",
            Summary =
                "Keep one host-scoped channel boss across streams. Preserve resolved actions, shared ward progress, phases, standings, and completion recaps.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/figures/phone-dark-blokeraid-completion.png",
                LightPhoneSource: "media/community/figures/phone-light-blokeraid-completion.png",
                DarkLaptopSource: "media/community/figures/laptop-dark-blokeraid-completion.png",
                LightLaptopSource: "media/community/figures/laptop-light-blokeraid-completion.png",
                PhoneAlt: "The Sample Channel public BlokeRaid page on a narrow screen that shows current contributors and the completed Static Colossus recap.",
                LaptopAlt: "The Sample Channel public BlokeRaid page on a narrow screen that shows current contributors and the completed Static Colossus recap.",
                "The active campaign persists across streams. The public route shows current standings and the latest completed recap."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable and configure Cooperative game",
                    Steps =
                    [
                        "As the channel owner or permitted moderator, choose the channel. Open Channel setup. Turn on Cooperative game under Chat tools.",
                        "Expect the feature card to save the change immediately. Expect each channel to start with this switch off.",
                        "Open BlokeRaid at /raid. Configure the boss name, health, shared ward, duration, deterministic phase thresholds, and public responses.",
                        "Set the outcome range, cooldown, and stream limit for Attack, Mend, and Nova.",
                        "Set the Nova point cost and damage for each correct Guessing result. Set the point reward for each victory contributor.",
                        "Choose Manual or Weekly reset. For Weekly reset, configure the UTC weekday and hour. Select Save configuration. Then start one campaign.",
                    ],
                    Paragraphs =
                    [
                        "Configuration changes affect the next action. They do not recalculate recorded outcomes or completed recaps. An active campaign phase can only move forward.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Assign host, moderator, and viewer actions",
                    Bullets =
                    [
                        "The owner and permitted moderators use the dashboard or moderator-only commands. They control the lifecycle, not individual viewer outcomes.",
                        "When Twitch reports the channel live, use attack to damage the boss. Use mend to restore the shared ward.",
                        "Use nova to spend the configured BlokeBot points for a special attack.",
                        "Each authenticated chat action records its outcome, stream identity, health and ward before and after the action, and its phase.",
                        "A duplicate message does not recalculate the outcome or spend the cost twice.",
                        "Chat and /raid/{channel} show status and standings. Public standings include participant logins, contribution totals, and the latest completion recap.",
                    ],
                    Code =
                        "!raid status\n!raid attack\n!raid mend\n!raid nova\n!raid standings\n\nModerators: !raid start | end | reset",
                },
                new SiteGuideSection
                {
                    Heading = "Apply correct guesses",
                    Bullets =
                    [
                        "A completed Guessing round applies configured damage once per distinct recorded correct login and once per round ID.",
                        "Guessing history remains authoritative. BlokeRaid records the cooperative action but does not change the Guessing round.",
                        "If Guessing or Cooperative game is unavailable, BlokeBot suppresses the effect. A later re-enable does not replay the missed result.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Reach a phase and complete the campaign",
                    Bullets =
                    [
                        "Health and ward stay within configured limits. A saved health threshold moves the campaign to the next deterministic phase and emits one public response.",
                        "Victory freezes the campaign, records a recap, and grants each recorded contributor one configured host-scoped point reward.",
                        "This BlokeRaid version excludes titles, achievements, and rewards from automation configuration.",
                        "If a reward cannot fit a contributor balance, a moderator must review Points. Repeated action retries do not create a second victory reward.",
                        "End records a non-victory completion. Reset completes the current campaign and starts a new recorded boss from the saved configuration.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Handle cooldowns, limits, and stale actions",
                    Bullets =
                    [
                        "Use actions only during a confirmed live stream. Expect different messages for offline and temporarily unknown states. Wait until Twitch state becomes available.",
                        "A cooldown reports the time left. A per-stream limit remains until a new stream identity. Mend remains unavailable when the ward is full.",
                        "If points are insufficient, Nova reports its cost and the current balance.",
                        "An old save cannot overwrite a changed configuration revision. Reload current settings. Reapply the intended edit. Save a new revision.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pause the feature and retain its schedule",
                    Bullets =
                    [
                        "Turn off Cooperative game to remove navigation and public standings.",
                        "BlokeBot then blocks chat actions, moderator controls, Guessing effects, point spending, rewards, resets, schedules, and emitted events before changes.",
                        "BlokeBot retains configuration, the active campaign, contributions, outcomes, phases, and recaps. The signed-in direct route links to Channel setup.",
                        "Re-enable the feature to resume the active campaign. Expect BlokeBot to extend its expiry by the disabled interval.",
                        "BlokeBot does not replay suppressed commands, Guessing results, resets, rewards, schedules, or events.",
                        "BlokeBot does not run a missed Weekly reset. After re-enable, review the campaign. As a moderator, use Reset only for a new boss.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Configure Guessing rounds", "guessing"),
                new SiteLink("Review viewer points", "points"),
            ],
        };
    }
}
