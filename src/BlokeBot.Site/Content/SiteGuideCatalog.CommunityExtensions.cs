namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommunityExtensionPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/passports",
            Eyebrow = "Community interaction · Viewer identity",
            Title = "Choose your viewer passport",
            Summary =
                "Opt in to a host-scoped profile, choose exactly who can see it and keep control of the activity BlokeBot presents.",
            Figure = new SiteFigure(
                "media/community/v010/viewer-passport-participant-dark-phone.png",
                462,
                956,
                "The Sample Channel public NightOwl viewer passport on a narrow screen, showing selected public identity and channel activity.",
                "A viewer owns the editor and visibility choice; the public route contains only the resulting permitted projection."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable the opt-in feature",
                    Steps =
                    [
                        "A channel owner or permitted moderator chooses the channel, opens Channel setup, turns on Viewer passports under Chat tools and saves. The switch is off for every channel until this happens.",
                        "The viewer signs in with Twitch and opens /passports/{channel}/me. BlokeBot keys the passport to that Twitch user ID, so a later login or display-name change updates the same profile instead of creating another one.",
                        "A new passport starts Private with attendance hidden. Saving a different visibility is the viewer's explicit opt-in to broader presentation.",
                    ],
                    Note =
                        "Replace {channel} with the channel login. The host, moderators and other viewers cannot choose a viewer's profile line, rewards, visibility or attendance setting for them.",
                },
                new SiteGuideSection
                {
                    Heading = "Build a bounded profile",
                    Bullets =
                    [
                        "Enter a profile line of at most 160 characters. BlokeBot presents it as plain text; the channel's moderation policy still applies.",
                        "Choose only a title or badge already earned in this channel. An unearned or stale reward selection is rejected rather than substituted.",
                        "The preview combines permitted points and rank, Guessing results, achievements, games and giveaways won, supported bounties and approved Moments. Source features remain authoritative; the passport is a projection, not a second activity ledger.",
                        "Attendance counts consecutive UTC days with a chat message. It does not measure watch time, and Show attendance streak can be changed independently of profile visibility.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose who can see it",
                    Bullets =
                    [
                        "Public allows anyone with /passport/{channel}/{viewer} to see the selected public-safe profile fields and allows the chat summary and public overlay or automation projection.",
                        "Channel members allows the viewer, channel managers and signed-in people who have their own passport in this channel. Anonymous visitors and unrelated signed-in accounts receive an unavailable result.",
                        "Private allows only the viewer and that channel's owner or permitted managers. Private and Channel members profiles do not leak through public chat summaries, overlays, automations, leaderboards or other public identity projections.",
                        "The public route never exposes Twitch user IDs, private source history, hidden attendance or unselected rewards.",
                    ],
                    Code = "!passport",
                },
                new SiteGuideSection
                {
                    Heading = "Save export or reset",
                    Bullets =
                    [
                        "Select Save passport after changing the profile line, title, badge, visibility or attendance choice. This is the page's sticky Save action.",
                        "Export my channel data downloads the data this self-hosted BlokeBot associates with the viewer's Twitch identity in this channel.",
                        "Reset passport requires confirmation and removes the passport and its chat-presence days. It does not rewrite the original points, Guessing, achievement, giveaway, bounty or Moment records owned by those features.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover privacy and availability",
                    Bullets =
                    [
                        "If a public link is unavailable, confirm the channel and viewer login, the viewer's current visibility, the audience's sign-in or channel membership and whether the passport has been reset.",
                        "If activity is stale, use the current Twitch identity and wait for a supported source event. Suppressed or historical activity is not reconstructed on demand.",
                        "Turning Viewer passports off removes normal discovery and public output and blocks edits, chat updates, commands, exports, resets, overlay data and automation payloads before effects. The signed-in direct route links back to Channel setup.",
                        "Saved passports, visibility and chat-presence days remain. Re-enable resumes from retained current state; chat messages, events, timers, queued work and provider actions suppressed while off are not replayed.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Build seasons and earned rewards", "community/progression"),
                new SiteLink("Review privacy boundaries", "privacy"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/competitions",
            Eyebrow = "Community progression · Competitions",
            Title = "Run tournaments and leagues",
            Summary =
                "Register viewers or teams, generate a reproducible schedule, confirm and correct results, then publish bounded standings and archives.",
            Figure = new SiteFigure(
                "media/community/v010/competition-result-light-laptop.png",
                1308,
                840,
                "The Sample Channel Tournaments and leagues workspace showing the running Summer Community Circuit, lifecycle actions and standings.",
                "Staff manage authoritative lifecycle and results; viewers receive the current public bracket, schedule or standings."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable and assign authority",
                    Steps =
                    [
                        "The channel owner or a permitted moderator opens Channel setup, turns on Tournaments & leagues under Chat tools and saves. It is a distinct switch and starts off for every channel.",
                        "Owners and permitted moderators create competitions, register or remove entrants, move the lifecycle, enter or correct results, send permitted reminders and archive records. A moderator can act only for the selected channel.",
                        "Viewers use !competitions for the current public competition and !competitionjoin during individual registration. Team registration and private contact details are handled by authorised staff in the dashboard.",
                    ],
                    Code = "!competitions\n!competitionjoin",
                },
                new SiteGuideSection
                {
                    Heading = "Create the competition contract",
                    Steps =
                    [
                        "Create a Draft and choose Tournament bracket, Round robin or Prediction league plus Individuals or Teams.",
                        "Set capacity, team size, optional minimum-points eligibility, schedule order, standing points, tiebreak rules and reminder lead time before opening registration.",
                        "Configure confirmed-win milestone and final-placement points or predeclared Community progression achievement keys only when those rewards are intended.",
                        "Open registration after reviewing the public name and rules. A format cannot be silently changed after results exist.",
                    ],
                    Bullets =
                    [
                        "Prediction leagues treat each fixture's entered scores as correct-prediction totals, then apply the configured standing points and tiebreaks.",
                        "Random schedule generation records its seed and BlokeBot algorithm version. Moderator-ranked seeding records the supplied ranks. Both preserve a reproducible entrant order.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Register and start",
                    Bullets =
                    [
                        "Registration accepts only the configured entry kind, capacity, team size and eligibility. A viewer chat join requires an authenticated Twitch user ID and joins the first open individual competition.",
                        "Private contact, lobby information, Twitch user IDs and moderator registration notes stay in the dashboard. The configured public entrant name is the deliberate public identity boundary.",
                        "Generate & start closes registration and persists the bracket or round schedule. If registration is closed, full, ineligible or the competition changed, fix or reload that visible state instead of retrying with invented data.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Confirm and correct results",
                    Bullets =
                    [
                        "Choose a scheduled match, enter both scores and confirm. Tournament advancement and league standings recompute from confirmed results.",
                        "Correcting a confirmed result retains its previous scores and private audit reason. A tournament correction safely clears downstream outcomes that no longer follow from the corrected winner.",
                        "Confirmed-win and final-placement rewards are idempotent across corrections, recomputation and retries; an already granted milestone is not paid again.",
                        "A stale revision or changed status returns a conflict. Reload the competition, verify the selected match and current downstream effects, then apply a fresh intentional correction.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Complete archive and publish",
                    Bullets =
                    [
                        "Complete competition evaluates final placements and rewards from the authoritative confirmed state. Archive then keeps the format, schedule, standings, results and audit history as completed history.",
                        "The public route /competitions/{channel} shows entrant identities, schedule or bracket, standings, confirmed scores and archives. Lifecycle events sent to overlays or automations use the same bounded public state.",
                        "Private contact, lobby information, moderator notes, audit reasons, internal IDs and provider details never appear on the public page or lifecycle payloads.",
                        "Match reminders use private delivery only where permitted; an unavailable provider does not make the match public or fabricate successful delivery.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pause and recover",
                    Bullets =
                    [
                        "Turning Tournaments & leagues off removes normal discovery and public data and blocks registration, starts, results, advancement, reminders, rewards, events, commands and provider work before mutation. The retained signed-in direct route links to Channel setup.",
                        "Formats, entrants, schedules, results, audit history and archives remain saved. Re-enable resumes that current lifecycle without replaying suppressed commands, reminders, rewards, events, subscriptions or provider work.",
                        "If a public page or command is unavailable, confirm the selected channel, switch, current lifecycle and entry kind. If a result action is stale, reload before retrying; do not repeatedly submit an old revision.",
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
                "Welcome incoming communities once, shortlist only explicitly approved live channels and keep every outgoing Twitch raid under host confirmation.",
            Figure = new SiteFigure(
                "media/community/v010/raid-collaboration-light-phone.png",
                462,
                956,
                "The Sample Channel raid hub on a narrow screen showing approved live channels and host-controlled Shoutout and Prepare raid actions.",
                "The narrow active hub keeps approved candidates, explainable filters and host-confirmed Twitch actions readable together."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Connect and opt in",
                    Steps =
                    [
                        "Connect the selected Twitch channel, then have its owner or permitted moderator turn on Raid & collaboration in Channel setup and save. The distinct switch starts off for every channel.",
                        "The owner or a moderator permitted for the selected channel configures welcome and shortlist settings and operates the hub. Viewers do not approve channels or start provider actions.",
                        "The channel owner reconnects the Twitch integration with raid management access when Confirm and start raid reports missing authority.",
                    ],
                    Note =
                        "Raid & collaboration owns its native shoutout step. The separate Shoutouts switch is not a prerequisite, although Twitch authority, live state, cooldown and provider checks are still enforced.",
                },
                new SiteGuideSection
                {
                    Heading = "Welcome an incoming raid",
                    Bullets =
                    [
                        "Choose whether the welcome message and native shoutout steps run, write the bounded tokenised message and set the deduplication window.",
                        "Incoming Twitch EventSub supplies the source channel and one aggregate viewer count. BlokeBot does not infer, enumerate or store individual viewer attribution.",
                        "Duplicate deliveries converge into one history entry. Repeated raids inside the configured window do not repeat the welcome sequence.",
                        "The history records visible welcome and shoutout outcomes. A cooldown, offline state or provider rejection remains an explicit non-success rather than a fabricated send.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Build an approved shortlist",
                    Steps =
                    [
                        "Add Twitch channels deliberately to Approved channels. Approval is a host-owned allowlist, not a Twitch relationship, safety score or reputation claim.",
                        "Optionally restrict language and categories and set the recent outgoing-relationship gap. Save settings.",
                        "Review live candidates and the reason each other approved channel is excluded. The hub rechecks live state, language, category and recent history rather than using a stale recommendation.",
                        "An optional approved clip appears only when Twitch confirms that it belongs to that channel and is no more than 30 days old.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Confirm host-owned actions",
                    Bullets =
                    [
                        "Shoutout uses the shared native Twitch boundary and repeats authority, live-state, cooldown and deduplication checks at send time.",
                        "Prepare raid opens an explicit confirmation for the selected approved live channel. It does not call Twitch.",
                        "Only Confirm and start raid performs the outgoing provider action, after rechecking the feature and Twitch authority. The hub never raids automatically from a recommendation, incoming relationship or retry.",
                        "If the target goes offline, becomes ineligible, authorization expires or the provider declines, refresh the hub and make a new host decision. Do not assume the previous preparation remains authority.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pause retain and recover",
                    Bullets =
                    [
                        "Turning Raid & collaboration off removes discovery and blocks the hub, EventSub subscription and reconciliation, welcome sequence, history mutations, shortlist checks, shoutout handoff and outgoing provider calls before effects.",
                        "Settings, approved channels and history remain private and retained. The signed-in direct route points to Channel setup.",
                        "Re-enable establishes current subscriptions and accepts new work. Incoming events, welcome steps, shoutouts, timers, queued work and outgoing actions suppressed while off are never replayed.",
                        "If incoming raids are absent after re-enable, confirm the selected channel ID and Twitch connection rather than relying on login fallback. If native actions fail, reconnect the owning channel account with the requested permission.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Coordinate a multi-host raid relay", "community/collectives"),
                new SiteLink("Configure native shoutouts", "twitch-operations/shoutouts"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/community/blokeraid",
            Eyebrow = "Community progression · Cooperative game",
            Title = "Run a persistent BlokeRaid campaign",
            Summary =
                "Carry one host-scoped channel boss across streams while preserving resolved actions, shared ward progress, phases, standings and completion recaps.",
            Figure = new SiteFigure(
                "media/community/v010/blokeraid-completion-dark-phone.png",
                462,
                956,
                "The Sample Channel public BlokeRaid page on a narrow screen showing current contributors and the completed Static Colossus recap.",
                "The active campaign persists across streams; the public route keeps current standings and the latest completed recap readable."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable and configure the game",
                    Steps =
                    [
                        "A channel owner or permitted moderator opens Channel setup, turns on Cooperative game under Chat tools and saves. The switch is off for every channel until selected.",
                        "Open BlokeRaid at /raid and configure the boss name, health, shared ward, duration, deterministic phase thresholds and public responses.",
                        "Set outcome range, cooldown and per-stream limit for Attack, Mend and Nova; set Nova's point cost, damage per correct Guessing result and the per-contributor victory point reward.",
                        "Choose Manual or Weekly reset. Weekly reset uses the configured UTC weekday and hour. Select Save configuration, then start one campaign.",
                    ],
                    Paragraphs =
                    [
                        "Configuration changes affect the next action, but recorded outcomes and completed recaps do not reroll. An active campaign's phase can move only forward even if thresholds are edited.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Assign host moderator and viewer actions",
                    Bullets =
                    [
                        "The owner and permitted moderators use the dashboard or moderator-only !raid start, !raid end and !raid reset commands. They control lifecycle, not individual viewer outcomes.",
                        "While Twitch reports the channel live, a viewer uses attack to damage the boss, mend to restore the shared ward or nova to spend the configured BlokeBot points for a special attack.",
                        "Each authenticated chat action records its resolved outcome, stream identity, before-and-after health and ward and resulting phase. Retrying the same message does not reroll or double-spend it.",
                        "Status and standings are available in chat and at /raid/{channel}. Public standings show recorded participant logins, contribution totals and the latest completion recap.",
                    ],
                    Code =
                        "!raid status\n!raid attack\n!raid mend\n!raid nova\n!raid standings\n\nModerators: !raid start | end | reset",
                },
                new SiteGuideSection
                {
                    Heading = "Integrate correct guesses",
                    Bullets =
                    [
                        "A completed Guessing round contributes the configured damage once for each distinct recorded correct login and once for that round ID.",
                        "Guessing history remains the source of the result. BlokeRaid records the resulting cooperative action without changing the Guessing round.",
                        "If Guessing or Cooperative game is unavailable when a result occurs, that effect is suppressed. Re-enabling either feature does not replay the missed result.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Reach phases and completion",
                    Bullets =
                    [
                        "Health and ward remain within their configured bounds. Crossing a saved health threshold moves to the next deterministic phase and emits its public response once.",
                        "Victory freezes the campaign, records a recap and grants the configured host-scoped point reward once to every recorded contributor. Titles, achievements and automation-configured rewards are not part of this BlokeRaid version.",
                        "If a reward cannot fit a contributor's point balance, a moderator must review Points; repeated action retries do not create a second victory reward.",
                        "End records a non-victory completion. Reset completes the current campaign and starts a fresh recorded boss from the saved configuration.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Handle cooldowns limits and stale actions",
                    Bullets =
                    [
                        "Actions require a confirmed live stream. Offline and temporarily unknown liveness return different recovery messages; wait or retry after Twitch state is available.",
                        "A cooldown reports the remaining time. A per-stream limit remains until a new stream identity. Mend is unavailable when the ward is full, and Nova reports its cost and current balance when points are insufficient.",
                        "A changed configuration revision is not overwritten by an old save. Reload current settings, reapply the intended edit and save a fresh revision.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pause persistence and schedules",
                    Bullets =
                    [
                        "Turning Cooperative game off removes navigation and public standings and blocks chat actions, moderator controls, Guessing effects, point spending and rewards, resets, schedules and emitted events before mutation.",
                        "Configuration, active campaign, contributions, resolved outcomes, phases and recaps remain. The signed-in direct route links to Channel setup.",
                        "Re-enable resumes the same active campaign and moves its expiry past the disabled interval. Suppressed commands, Guessing results, resets, rewards, schedules and events are not replayed.",
                        "A weekly reset missed while disabled does not catch up. Review the retained campaign after re-enable and use an explicit moderator reset only when a fresh boss is intended.",
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
                "Invite explicit hosts on one BlokeBot installation, preserve each host's authority and share only bounded tournament, raid-relay or goal projections.",
            Figure = new SiteFigure(
                "media/community/v010/collectives-recovery-dark-phone.png",
                462,
                956,
                "The Sample Channel Collectives direct route on a narrow screen showing retained consent and workflow recovery while the feature is off.",
                "The disabled route preserves consent and workflows and explains recovery without replay."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable explicit host consent",
                    Steps =
                    [
                        "Every participating channel owner or permitted moderator turns on that channel's single Collectives switch in Channel setup. It starts off for every channel and has no second switch in the workspace.",
                        "One host creates a Collective and becomes its first Coordinator. A coordinator invites only known hosts on the same self-hosted BlokeBot installation.",
                        "The invited host accepts or declines only for itself. Twitch raids, follows, shared moderators and channel relationships never create membership, consent or trust.",
                    ],
                    Paragraphs =
                    [
                        "Here, a collaborator is an active member host, not a viewer-level account role. It can read the bounded shared workflow and act only for itself. A moderator's selected-host permission remains host-scoped; membership does not let a coordinator or collaborator edit another channel's Twitch connection, provider access, source mapping, lobby details, rewards or moderator notes.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage coordinator and member roles",
                    Bullets =
                    [
                        "A Coordinator can invite known hosts, withdraw a pending invitation, edit shared workflow definitions, transfer coordination and remove bounded participation.",
                        "An active member can leave only for its own host. A pending member can accept or decline only for itself.",
                        "At least one active coordinator must remain. Transfer coordination before the last coordinator leaves or is removed; the rejected action leaves membership unchanged.",
                        "Membership and authority changes are audited with the acting host and operation reference. Retrying the same accepted operation is idempotent.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Reference a host-owned tournament",
                    Bullets =
                    [
                        "Choose an active member as Owning host and reference that host's public competition ID. The owner must have Tournaments & leagues on and the competition must exist for that host.",
                        "The Collective copies no competition record. It shares a read-only name, format, status, round, entrant count, confirmed-result count and revision while the owning host remains authoritative.",
                        "Private entrant contact, lobby details, moderator notes, rewards and result audit stay with the owning host. Open tournament returns to that host-owned workflow.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Confirm each raid-relay handoff",
                    Bullets =
                    [
                        "Configure only active consenting members as current and next hosts. Each involved host needs Collectives and Raid & collaboration on.",
                        "The current host alone confirms its outgoing Twitch raid. Shared state carries current and next host, status, audited handoffs and aggregate viewer count, never viewer identities.",
                        "BlokeBot rechecks membership, selected-host authority, relay identity and revision, both feature gates and pause watermarks after provider work. A stale reconfiguration, revoke, leave, disable or disable-and-re-enable returns typed non-success and cannot overwrite newer state.",
                        "A provider rejection is retained once with a new revision and audit entry. Refresh the relay before a deliberate retry; it is never silently converted into success or replayed later.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Combine bounded public goals",
                    Steps =
                    [
                        "The coordinator creates the goal name, unit, positive target and future UTC deadline.",
                        "Each active host chooses only its own public viewer-funded bounty as its source. That host needs Bounties and Points on; another host cannot set or expose the private source mapping for it.",
                        "The shared view publishes the target, current total, per-host totals, deadline and status. Contributor identities, rewards, balances, notes and internal source mappings remain local.",
                    ],
                    Paragraphs =
                    [
                        "The public route and !collective summary include only active, enabled, explicitly participating hosts and the current allowlisted projection.",
                    ],
                    Code = "!collective",
                },
                new SiteGuideSection
                {
                    Heading = "Save only host-local settings",
                    Bullets =
                    [
                        "The workflow editor changes Collective-scoped definitions only when the selected host can coordinate.",
                        "The Details sidecar identifies settings private to the selected host, including that host's goal source and notification audience. Save local settings is the only sticky Save in this workspace and appears only for a genuine local change.",
                        "A stale local-settings revision returns a conflict. Reload the current Collective, compare the selected host and workflow and then reapply the intended host-local choice.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Disable recover and bound public output",
                    Bullets =
                    [
                        "Turning Collectives off for a host removes its normal navigation and public output and blocks its membership, workflow, runtime, shared-event, command, automation, retry, reconciliation and provider boundaries before work begins.",
                        "Collectives, consent, local settings, bounded history and audits remain saved. The signed-in direct route explains recovery and links to Channel setup.",
                        "Re-enable resumes retained current state from a new watermark. Invitations, events, timers, retries, relays, reconciliation and provider actions suppressed while off are never replayed.",
                        "If a host or workflow disappears from a public projection, check current membership and that host's switch and required owning feature. Restore consent or feature availability explicitly; private state is not projected as a fallback.",
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
