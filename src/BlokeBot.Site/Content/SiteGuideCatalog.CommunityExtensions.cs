namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommunityExtensionPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/passports",
            Eyebrow = "Community interaction · Viewer identity",
            Title = "Choose a viewer passport",
            Summary =
                "Create a host-scoped profile. Choose its audience and the activity that BlokeBot presents.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/figures/phone-dark-viewer-passport-participant.png",
                LightPhoneSource: "media/community/figures/phone-light-viewer-passport-participant.png",
                DarkLaptopSource: "media/community/figures/laptop-dark-viewer-passport-participant.png",
                LightLaptopSource: "media/community/figures/laptop-light-viewer-passport-participant.png",
                PhoneAlt: "The Sample Channel public NightOwl viewer passport on a narrow screen that shows selected public identity and channel activity.",
                LaptopAlt: "The Sample Channel public NightOwl viewer passport on a narrow screen that shows selected public identity and channel activity.",
                "A viewer controls the editor and visibility. The public route contains only the permitted projection."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable Viewer passports",
                    Steps =
                    [
                        "As the channel owner or permitted moderator, choose the channel. Open Channel setup. Turn on Viewer passports under Chat tools.",
                        "Expect the feature card to save the change immediately. Expect each channel to start with this switch off.",
                        "As the viewer, sign in with Twitch. Open /passports/{channel}/me. Expect BlokeBot to link the passport to that Twitch user ID.",
                        "Expect a later login or display-name change to update the same profile. Expect a new passport to start Private and hide attendance.",
                        "Save a different visibility to approve a broader audience.",
                    ],
                    Note =
                        "Replace {channel} with the channel login. Only the viewer can choose the profile line, rewards, visibility, and attendance choice.",
                },
                new SiteGuideSection
                {
                    Heading = "Create a bounded profile",
                    Bullets =
                    [
                        "Enter a profile line of 160 characters or fewer.",
                        "BlokeBot presents the profile line as plain text. The channel moderation policy still applies.",
                        "Choose only a title or badge that the viewer earned in this channel.",
                        "BlokeBot rejects an unearned or stale reward selection.",
                        "The preview combines permitted points, rank, Guessing results, achievements, game and giveaway wins, supported bounties, and approved Moments. Each source feature remains authoritative. The passport summarizes its current records.",
                        "Attendance counts consecutive recorded streams with a chat message. It does not measure watch time or every broadcast.",
                        "Change Show attendance streak independently of profile visibility.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose the audience",
                    Bullets =
                    [
                        "Public lets anyone with /passport/{channel}/{viewer} see the selected public-safe fields. It also permits chat, overlay, and automation projections.",
                        "Channel members permits the viewer, channel managers, and signed-in people with a passport in this channel. Other accounts receive an unavailable result.",
                        "Private permits only the viewer, channel owner, and permitted managers. Private and Channel members profiles stay out of all public identity projections.",
                        "The public route excludes Twitch user IDs, private source history, hidden attendance, and unselected rewards.",
                    ],
                    Code = "!passport",
                },
                new SiteGuideSection
                {
                    Heading = "Save, export, or reset the passport",
                    Bullets =
                    [
                        "Select Save passport after a profile change.",
                        "This control is the sticky Save action for the page.",
                        "Export my channel data downloads data that this BlokeBot installation associates with the viewer's Twitch identity in this channel.",
                        "Confirm Reset passport to remove the passport and its stream attendance.",
                        "The reset does not change original points, Guessing, achievement, giveaway, bounty, or Moment records.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Restore privacy and availability",
                    Bullets =
                    [
                        "If a public link is unavailable, verify the channel, viewer login, visibility, audience access, and passport state.",
                        "If activity is stale, use the current Twitch identity and wait for a supported source event.",
                        "BlokeBot does not reconstruct suppressed or historical activity on demand.",
                        "Turn off Viewer passports to remove discovery and public output.",
                        "BlokeBot then blocks edits, chat updates, commands, exports, resets, overlay data, and automation payloads before effects.",
                        "The signed-in direct route links to Channel setup. BlokeBot keeps passports, visibility, and stream attendance.",
                        "The next new stream attendance starts a new streak after you re-enable the feature.",
                        "BlokeBot does not replay suppressed chat messages, events, timers, queued work, or provider actions.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Create seasons and earned rewards", "community/progression"),
                new SiteLink("Review privacy boundaries", "privacy"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/competitions",
            Eyebrow = "Community progression · Competitions",
            Title = "Run tournaments and leagues",
            Summary =
                "Register viewers or teams. Create a reproducible schedule. Confirm results and publish bounded standings and archives.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/figures/phone-dark-competition-result.png",
                LightPhoneSource: "media/community/figures/phone-light-competition-result.png",
                DarkLaptopSource: "media/community/figures/laptop-dark-competition-result.png",
                LightLaptopSource: "media/community/figures/laptop-light-competition-result.png",
                PhoneAlt: "The Sample Channel Tournaments and leagues workspace that shows the active Summer Community Circuit, lifecycle actions, and standings.",
                LaptopAlt: "The Sample Channel Tournaments and leagues workspace that shows the active Summer Community Circuit, lifecycle actions, and standings.",
                "Staff control the authoritative lifecycle and results. Viewers receive the current public bracket, schedule, or standings."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable the feature and assign authority",
                    Steps =
                    [
                        "As the channel owner or permitted moderator, choose the channel. Open Channel setup. Turn on Tournaments & leagues under Chat tools.",
                        "Expect the feature card to save the change immediately. Expect each channel to start with this switch off.",
                        "As an owner or permitted moderator, control competitions, entrants, lifecycle, results, permitted reminders, and archives.",
                        "As a moderator, act only for the selected channel.",
                        "As a viewer, use !competitions for the current public competition. Use !competitionjoin for individual registration.",
                        "As authorized staff, manage teams and private contact details in the dashboard.",
                    ],
                    Code = "!competitions\n!competitionjoin",
                },
                new SiteGuideSection
                {
                    Heading = "Create a competition contract",
                    Steps =
                    [
                        "Create a Draft. Choose Tournament bracket, Round robin, or Prediction league. Choose Individuals or Teams.",
                        "Before you open registration, set capacity, team size, optional minimum-points eligibility, schedule order, points, tiebreak rules, and reminder lead time.",
                        "Configure the confirmed-win milestone, final-placement points, or declared Community progression achievement keys only for intended rewards.",
                        "Review the public name and rules. Then open registration. Do not change the format after results exist.",
                    ],
                    Bullets =
                    [
                        "Prediction leagues treat entered fixture scores as correct-prediction totals. They apply the configured points and tiebreaks.",
                        "A random schedule records its seed and BlokeBot algorithm version. A moderator-ranked schedule records the supplied ranks. Both preserve entrant order.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Register entrants and start",
                    Bullets =
                    [
                        "Registration enforces entry kind, capacity, team size, and eligibility. A chat join requires an authenticated Twitch user ID and selects the first open individual competition.",
                        "Private contact, lobby information, Twitch user IDs, and moderator notes stay in the dashboard. Only the configured entrant name becomes public.",
                        "Select Generate & start to close registration and save the bracket or round schedule.",
                        "If registration closes or reaches capacity, correct or reload the visible state. Do the same if eligibility blocks entry or the competition changed.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Confirm and correct results",
                    Bullets =
                    [
                        "Choose a scheduled match. Enter both scores and confirm them.",
                        "BlokeBot recalculates advancement and standings from confirmed results.",
                        "A result correction retains the previous scores and private audit reason. A tournament correction clears outcomes that no longer follow from the corrected winner.",
                        "Corrections, recalculation, and retries do not duplicate confirmed-win or final-placement rewards. BlokeBot does not grant the same milestone twice.",
                        "A stale revision or status returns a conflict.",
                        "Reload the competition. Verify the match and its current downstream effects. Then apply the intended correction.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Complete, archive, and publish",
                    Bullets =
                    [
                        "Select Complete competition to evaluate final placements and rewards from the authoritative confirmed state.",
                        "Select Archive to retain the format, schedule, standings, results, and audit history as completed history.",
                        "The public route /competitions/{channel} shows entrants, schedule or bracket, standings, confirmed scores, and archives. Lifecycle events use the same bounded public state.",
                        "The public page and lifecycle payloads exclude private contact, lobby information, moderator notes, audit reasons, internal IDs, and provider details.",
                        "Match reminders use permitted private delivery. An unavailable provider does not expose the match or report a successful delivery.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pause and restore the feature",
                    Bullets =
                    [
                        "Turn off Tournaments & leagues to remove discovery and public data.",
                        "BlokeBot then blocks registration, starts, results, advancement, reminders, rewards, events, commands, and provider work before changes.",
                        "The signed-in direct route links to Channel setup. BlokeBot retains formats, entrants, schedules, results, audit history, and archives.",
                        "Re-enable the feature to resume the current lifecycle.",
                        "BlokeBot does not replay suppressed commands, reminders, rewards, events, subscriptions, or provider work.",
                        "If a page or command is unavailable, verify the channel, switch, lifecycle, and entry kind. If an action is stale, reload before another attempt.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink(
                    "Attach approved Moments",
                    "community/moments#attach-approved-moments-to-progression"
                ),
                new SiteLink("Coordinate a Collective", "community/collectives"),
            ],
        };

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

        yield return new SiteGuidePage
        {
            Route = "/community/collectives",
            Eyebrow = "Community progression · Multi-host",
            Title = "Coordinate a multi-host Collective",
            Summary =
                "Invite explicit hosts from one BlokeBot installation. Preserve each host's authority. Share only bounded tournament, raid-relay, or goal projections.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/figures/phone-dark-collectives-recovery.png",
                LightPhoneSource: "media/community/figures/phone-light-collectives-recovery.png",
                DarkLaptopSource: "media/community/figures/laptop-dark-collectives-recovery.png",
                LightLaptopSource: "media/community/figures/laptop-light-collectives-recovery.png",
                PhoneAlt: "The Sample Channel Collectives direct route on a narrow screen that shows retained consent and recovery while the feature is off.",
                LaptopAlt: "The Sample Channel Collectives direct route on a narrow screen that shows retained consent and recovery while the feature is off.",
                "The disabled route preserves consent and workflows. It explains recovery without replay."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable explicit consent for each host",
                    Steps =
                    [
                        "Ask each channel owner or permitted moderator to turn on Collectives in Channel setup. Expect each channel to start with this switch off.",
                        "Do not look for a second switch in the workspace because it has none.",
                        "Create a Collective from one host. Use that host as the first Coordinator. Invite only known hosts from the same BlokeBot installation.",
                        "Let only the invited host accept or decline for itself.",
                        "Do not treat Twitch raids, follows, shared moderators, or channel relationships as membership, consent, or trust.",
                    ],
                    Paragraphs =
                    [
                        "A collaborator is an active member host in this Collective. It can read the bounded workflow and act only for itself.",
                        "A moderator permission remains host-scoped. Membership grants no authority over another host's Twitch connection, provider access, source mapping, lobby details, rewards, or moderator notes.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage Coordinator and member roles",
                    Bullets =
                    [
                        "A Coordinator can invite known hosts, withdraw a pending invitation, edit shared workflow definitions, transfer coordination, and remove bounded participation.",
                        "An active member can leave only for its own host. A pending member can accept or decline only for itself.",
                        "At least one active Coordinator must remain.",
                        "Transfer coordination before the last Coordinator leaves or another user removes that Coordinator.",
                        "BlokeBot rejects the action without a membership change.",
                        "The audit records each membership or authority change, the actor host, and the operation reference. A repeated accepted operation makes no additional change.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Reference a tournament from one host",
                    Bullets =
                    [
                        "Choose an active member as Owning host. Enter that host's public competition ID.",
                        "Before the choice, verify that the host enabled Tournaments & leagues and owns the competition.",
                        "The Collective does not copy the competition. It shares the read-only name, format, status, round, entrant count, confirmed-result count, and revision.",
                        "The Owning host remains authoritative.",
                        "Private entrant contact, lobby details, moderator notes, rewards, and result audit stay with the Owning host. Open tournament returns to that workflow.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Confirm each raid relay handoff",
                    Bullets =
                    [
                        "Choose only active members with consent as current and next hosts. Each host must enable Collectives and Raid & collaboration.",
                        "Only the current host confirms its outgoing Twitch raid. Shared state contains current and next host, status, audited handoffs, and total viewer count.",
                        "Shared state excludes viewer identities.",
                        "After provider work, BlokeBot checks membership, selected-host authority, relay identity, revision, both feature gates, and pause watermarks.",
                        "A stale reconfiguration, revoke, leave, disable, or disable-and-re-enable sequence returns a typed non-success. It cannot overwrite newer state.",
                        "BlokeBot records one provider rejection with a new revision and audit entry.",
                        "Before a deliberate retry, refresh the relay.",
                        "BlokeBot never reports the rejection as success or replays it later.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Combine bounded public goals",
                    Steps =
                    [
                        "As the Coordinator, create the goal name, unit, positive target, and future UTC deadline.",
                        "Ask each active host to choose only its own public viewer-funded bounty.",
                        "As that host, enable Bounties and Points before the choice.",
                        "Do not let another host set or expose the private source mapping.",
                    ],
                    Paragraphs =
                    [
                        "The shared view publishes the target, current total, per-host totals, deadline, and status. Contributor identities, rewards, balances, notes, and source mappings remain local.",
                        "The public route and !collective summary include only active hosts with explicit participation, enabled features, and the current allowlisted projection.",
                    ],
                    Code = "!collective",
                },
                new SiteGuideSection
                {
                    Heading = "Save only settings for one host",
                    Bullets =
                    [
                        "The workflow editor changes Collective definitions only if the selected host can coordinate.",
                        "The Details sidecar identifies private settings for the selected host. These settings include its goal source and notification audience.",
                        "Save local settings is the only sticky Save in this workspace. It appears only after a genuine local change.",
                        "A stale revision returns a conflict.",
                        "Reload the Collective. Compare the selected host and workflow. Then reapply the intended local choice.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Disable the feature and restore public output",
                    Bullets =
                    [
                        "Turn off Collectives for a host to remove its navigation and public output.",
                        "BlokeBot then blocks membership, workflow, runtime, shared-event, command, automation, retry, reconciliation, and provider work before it starts.",
                        "BlokeBot retains Collectives, consent, local settings, bounded history, and audits. The signed-in direct route explains recovery and links to Channel setup.",
                        "Re-enable the feature to resume retained state from a new watermark.",
                        "BlokeBot does not replay suppressed invitations, events, timers, retries, relays, reconciliation, or provider actions.",
                        "If public output disappears, verify membership, the host switch, and its required feature. Restore consent or feature availability.",
                        "BlokeBot never uses private state as a fallback projection.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Run tournaments and leagues", "community/competitions"),
                new SiteLink("Use the raid and collaboration hub", "community/raid-collaboration"),
                new SiteLink("Run viewer-funded bounties", "community/bounties"),
            ],
        };
    }
}
