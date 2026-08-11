using Microsoft.AspNetCore.Components.Routing;

namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
    private bool _isOpen;

    private HelpPage? _currentHelp => HelpForLocation(_currentPath, _currentFragment);

    private string _currentPath
    {
        get
        {
            var relative = _navigation.ToBaseRelativePath(_navigation.Uri);
            var path = relative.Split('?', '#')[0].Trim('/');
            return string.IsNullOrWhiteSpace(path) ? "/" : "/" + path;
        }
    }

    // Same-page fragment pushes never raise LocationChanged on the server, so the
    // fragment-owned tab state is the authority whenever it describes the current path.
    private string _currentFragment =>
        string.Equals(_fragments.Path, _currentAbsolutePath, StringComparison.Ordinal)
        && _fragments.Fragment is { } fragment
            ? fragment
            : _navigation.ToAbsoluteUri(_navigation.Uri).Fragment.TrimStart('#');

    private string _currentAbsolutePath => _navigation.ToAbsoluteUri(_navigation.Uri).AbsolutePath;

    protected override void OnInitialized()
    {
        _navigation.LocationChanged += OnLocationChanged;
        _fragments.Changed += OnFragmentChanged;
    }

    public void Dispose()
    {
        _navigation.LocationChanged -= OnLocationChanged;
        _fragments.Changed -= OnFragmentChanged;
    }

    private void OnFragmentChanged() =>
        _ = InvokeAsync(() =>
        {
            _isOpen = false;
            StateHasChanged();
        });

    private void Close() => _isOpen = false;

    private void Toggle() => _isOpen = !_isOpen;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args) =>
        _ = InvokeAsync(() =>
        {
            _isOpen = false;
            StateHasChanged();
        });

    private static HelpPage? HelpForLocation(string path, string fragment) =>
        path switch
        {
            "/" => _homeHelp,
            "/guessing" => _guessingDashboardHelp,
            "/guessing/settings" => _guessingSettingsHelp,
            "/points" => _pointsDashboardHelp,
            "/points/settings" => _pointsSettingsHelp,
            "/custom-commands/settings" => _customCommandsHelp,
            "/host" => _hostConfigHelp,
            "/requests" => _requestBoardsHelp,
            "/bounties" => _bountiesHelp,
            "/community" => _communityProgressionHelp,
            "/raid" => _blokeRaidHelp,
            "/passports" => _viewerPassportsHelp,
            _ when path.StartsWith("/passports/", StringComparison.Ordinal)
                    && path.EndsWith("/me", StringComparison.Ordinal) => _viewerPassportsHelp,
            "/bingo" => _bingoHelp,
            "/competitions" => _competitionsHelp,
            "/raid-collaboration" => _raidCollaborationHelp,
            "/queues" => _playQueuesHelp,
            "/moments" => _momentsHelp,
            "/overlays" => fragment switch
            {
                "cues" => _cuesHelp,
                "media" => _mediaLibraryHelp,
                _ => _overlaysHelp,
            },
            "/twitch-operations/shoutouts" => _shoutoutsHelp,
            "/twitch-operations/polls" => _pollsHelp,
            "/twitch-operations/clips-markers" => _clipsMarkersHelp,
            "/twitch-operations/channel-points" => _channelPointsHelp,
            "/twitch-operations/predictions" => _predictionsHelp,
            _ => null,
        };

    internal static bool HasUsefulHelpForPath(string path) =>
        HelpForLocation(path, string.Empty) is { } help
        && !string.IsNullOrWhiteSpace(help.Title)
        && help.Sections.Count > 0
        && help.Sections.All(static section =>
            !string.IsNullOrWhiteSpace(section.Title)
            && (
                !string.IsNullOrWhiteSpace(section.Body)
                || section.Items.Any(static item => !string.IsNullOrWhiteSpace(item))
            )
        );

    private static readonly string[] _templateVariableItems =
    [
        "<strong>Start reply</strong>: <code>{round}</code>, <code>{options}</code>",
        "<strong>Guess option reply</strong>: <code>{name}</code>, <code>{login}</code>",
        "<strong>Invalid guess reply</strong>: <code>{name}</code>, <code>{login}</code>",
        "<strong>How to guess reply</strong>: <code>{command}</code>",
        "<strong>Available guesses reply</strong>: <code>{round}</code>, <code>{options}</code>",
        "<strong>How to choose a winner reply</strong>: <code>{command}</code>",
        "<strong>Winner and no-winners replies</strong>: <code>{name}</code>, <code>{winners}</code>, <code>{count}</code>",
        "<strong>Stop, closed, no-round, already-running, and moderator-only replies</strong>: no live details",
    ];

    private static readonly HelpSection _featureSwitchHelp = new(
        "Turning this tool on or off",
        "Chat tools are opt-in per channel. Use Channel setup to turn this tool on or off.",
        [
            "Turning it off hides normal navigation and stops commands, automation, public output, and actions on connected services.",
            "Saved configuration and history are retained. Turning it back on resumes from the current state without replaying work suppressed while it was off.",
        ]
    );

    private static readonly HelpSection _momentAttachmentHelp = new(
        "Moment attachments",
        "Attach approved Moments from this channel inside a bounty, achievement, or confirmed result. Moment titles, categories, clips, and moderation state remain owned by Moments and are read by reference.",
        [
            "Attachments inherit Moments plus this destination feature and add no Channel setup switch. If either parent is off, discovery, changes, public output, events, and downstream presentation stop before effects.",
            "The embedded section uses the normal Channel setup recovery. Saved valid links are retained while disabled; re-enabling exposes them without replaying suppressed activity.",
            "Hidden, rejected, merged, deleted, or otherwise unavailable Moments are absent everywhere. Public views receive only current approved public-safe fields, never moderator notes or rejection reasons.",
        ]
    );

    private static readonly HelpPage _homeHelp = new(
        "Home",
        [
            new(
                "What this page is for",
                "Home gives you the short version of what BlokeBot can do for your Twitch channel.",
                []
            ),
            new(
                "Where to go next",
                "Use the menu to set up your channel, start chat games, manage points, or change bot settings.",
                []
            ),
        ]
    );

    private static readonly HelpPage _hostConfigHelp = new(
        "Channel setup",
        [
            new(
                "Choose your Chat tools",
                "Every Chat tools feature has its own switch and starts off for a new channel.",
                [
                    "Turning a tool off retains its setup and history while stopping its commands, public output, automation, and Twitch actions.",
                    "Turning a tool back on restores access to saved data without replaying work suppressed while it was off.",
                ]
            ),
            new(
                "Channel setup",
                "Create your channel setup, let the bot chat in your stream, and start or stop it when you need.",
                [
                    "Chat access is the channel owner's approval for BlokeBot to operate in channel chat.",
                    "Twitch integration is a separate owner connection. Reconnect it to replace the saved connection, or disconnect it to remove BlokeBot's stored authorization.",
                    "The bot account is the Twitch identity that sends messages. Connecting one does not connect the other two.",
                ]
            ),
            new(
                "Moderator access",
                "You can let all of your Twitch mods help by default, limit access to named mods, or block specific mods from changing this channel.",
                []
            ),
            new(
                "Available viewer commands",
                "Choose the global chat words that open the viewer command catalog. The catalog publishes the main command name for each command.",
                [
                    "The list shows the first command name for each viewer-safe command and never includes moderator-only commands.",
                    "Commands appear or disappear when games, giveaways, boards, queues, and live-stream availability change.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _requestBoardsHelp = new(
        "Request boards",
        [
            _featureSwitchHelp,
            new(
                "Create or edit a board",
                "Choose a saved board to edit it, or select New board to start a draft. A new board is not created until you complete its details and select Save board.",
                [
                    "The public board link becomes available after the new board has been saved.",
                    "Add up to 12 submission fields. Choose a field in the inventory to edit it.",
                ]
            ),
            new(
                "Moderate requests",
                "Open a saved board to approve, queue, accept, complete, reject, or merge viewer submissions.",
                [
                    "Public notes are visible to viewers. Private moderator notes and rejection reasons stay private.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _bountiesHelp = new(
        "Bounties",
        [
            _featureSwitchHelp,
            _momentAttachmentHelp,
            new(
                "Fund and settle challenges",
                "Create a draft, open funding, then accept and resolve the challenge. Bounties require Points to be on.",
                [
                    "Pledges are held from each viewer's host-scoped login balance and requests above the target are capped to the remaining amount.",
                    "Accepting below target is allowed. Funding and accepted bounties can be extended or expire.",
                    "Cancellation and expiry refund pledges. Each bounty chooses whether failure refunds or spends them.",
                    "Completion spends pledges and can distribute a fixed bonus equally or proportionally.",
                ]
            ),
            new(
                "Identity, visibility, and recovery",
                "Point settlement and contributor grouping use the recorded normalized Twitch login, not Twitch user ID.",
                [
                    "A later Twitch rename does not move a balance or combine historical logins.",
                    "Public bounties show recorded contributor logins and pledge amounts. Private bounties show no public data. Moderator reasons remain private.",
                    "Turning Bounties off retains all configuration and history. Direct links recover through Channel setup, and turning it back on does not replay suppressed commands, events, or expiries.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _communityProgressionHelp = new(
        "Community progression",
        [
            _featureSwitchHelp,
            _momentAttachmentHelp,
            new(
                "Seasons, progress, and privacy",
                "Create a draft season, add typed quests, achievements, and persistent rewards, then open it for supported BlokeBot and Twitch events.",
                [
                    "Public mode shows participant Twitch identities, live individual progress, standings, completed achievements, reward unlocks, equipped rewards, and archived history. Hidden mode publishes nothing.",
                    "Private moderator notes and internal audit records never appear on the public page.",
                    "Closing freezes a standings snapshot and immutable completion history. Archiving keeps viewer unlocks and equipped selections.",
                ]
            ),
            new(
                "Resets and schedule edits",
                "Daily and weekly repeatable quests use the channel time zone and the local boundary shown beside each definition.",
                [
                    "A daylight-saving gap moves forward to the first valid instant. An overlap uses the first occurrence and never resets again at the second.",
                    "After downtime BlokeBot performs at most one rollover into the current period; it never replays every missed period.",
                    "Saving an active schedule edit immediately closes the current period and resets active repeatable progress. You must confirm that consequence before saving; retries and multiple instances apply it once.",
                ]
            ),
            new(
                "Persistent rewards and recovery",
                "Titles, supported badge icons, cosmetic accents, and points are granted atomically when progress completes.",
                [
                    "Viewers equip one unlocked title, badge, and accent for this host with !equiptitle, !equipbadge, and !equipaccent.",
                    "Turning Community progression off blocks commands, events, timers, automation, rewards, and public output while preserving saved data. Re-enable resumes from the current period without replaying suppressed work.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _blokeRaidHelp = new(
        "BlokeRaid",
        [
            _featureSwitchHelp,
            new(
                "Run one persistent channel boss",
                "Start one active boss campaign, then let viewers attack, mend the shared ward, or spend BlokeBot points on Nova across multiple streams.",
                [
                    "Each action has its own configurable outcome range, cooldown, and per-stream limit. The resolved outcome is recorded so restarts never reroll it.",
                    "Correct Guessing results use the configured damage per distinct correct recorded login and are applied once by round ID.",
                    "Victory rewards every recorded contributor through the host-scoped point ledger. Titles, achievements, and automation-triggered effects are not part of this version.",
                ]
            ),
            new(
                "Phases, resets, and recovery",
                "Health and ward stay within their configured bounds. Crossing a health threshold selects its saved deterministic phase response.",
                [
                    "Manual reset ends the current campaign and starts a fresh recorded boss. Weekly reset runs at the chosen UTC day and hour; missed disabled periods are never replayed.",
                    "Turning Cooperative game off hides navigation and public standings and blocks commands, guessing effects, spending, rewards, resets, schedules, and emitted events before mutation.",
                    "Saved configuration, active state, contributions, outcomes, and recaps are retained. Re-enable resumes the retained campaign with its expiry moved past the disabled interval.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _viewerPassportsHelp = new(
        "Viewer passports",
        [
            _featureSwitchHelp,
            new(
                "Opt in and choose what is public",
                "A passport starts private. Choose Public or Channel members only when you want selected profile fields and activity to be visible.",
                [
                    "Profile lines are limited to 160 characters, displayed as plain text, and remain subject to the channel's moderation policy.",
                    "Attendance is based only on days when you chatted. It does not measure or claim exact watch time, and you can hide it independently.",
                    "Only titles and badges already earned in this channel can be selected.",
                ]
            ),
            new(
                "Privacy and recovery",
                "Export downloads the data this self-hosted BlokeBot deployment associates with your Twitch identity in this channel. Reset removes the passport and its chat-presence days without changing source feature history.",
                [
                    "Turning Viewer passports off blocks profile pages, commands, runtime updates, overlay data, automation payloads, export, and reset access.",
                    "Saved passports and attendance remain stored while the feature is off. Turning it back on restores them without replaying suppressed chat or other work.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _bingoHelp = new(
        "Stream-event Bingo",
        [
            _featureSwitchHelp,
            new(
                "Templates, cards, and joining",
                "Build reusable 3×3, 4×4, or 5×5 templates from typed automatic squares and moderator-confirmed moments.",
                [
                    "A saved revision, dimension, seed, and assignment identity reproduce each shared, viewer, or team card after later template edits.",
                    "Viewers join before issue. Owners and moderators can move or remove them until issuing cards freezes the roster and assignments.",
                    "Automatic choices are limited to connected Twitch and BlokeBot sources. Subjective moments stay manual; Bingo does not run scripts, image recognition, or speech recognition.",
                ]
            ),
            new(
                "Public evidence, corrections, and rewards",
                "Public cards show participant Twitch identities or team names, typed evidence, wins, and archives.",
                [
                    "Raw provider payloads, credentials, internal identifiers, private moderator notes, and internal audit reasons are never public.",
                    "Reversing a manual mark corrects the live card and evidence. Any completed win and its point, achievement, or title reward remain permanent and cannot grant again.",
                    "Achievements and titles come from predeclared Community progression achievements. Turning Bingo off retains all data while stopping commands, EventSub reconciliation, marking, rewards, public output, overlay events, and queued work; re-enable never replays suppressed events.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _competitionsHelp = new(
        "Tournaments & leagues",
        [
            _featureSwitchHelp,
            _momentAttachmentHelp,
            new(
                "Run a competition",
                "Create a draft with a tournament, round-robin, or prediction-league format, then open registration and generate the recorded schedule.",
                [
                    "Individuals and teams use the configured capacity, team size, minimum-points eligibility, seeding, standing points, and tiebreak rules.",
                    "Prediction leagues use each fixture's entered scores as correct-prediction totals; configured standing points and tiebreaks rank the league.",
                    "Random generation records its seed and BlokeBot algorithm version so the same entrant order reproduces the bracket or schedule.",
                    "Confirmed result corrections retain the previous scores in private audit history and safely clear downstream tournament outcomes that no longer apply.",
                    "Confirmed-win milestone and final-placement points or configured Community progression achievements are idempotent across retries and final-state evaluation.",
                ]
            ),
            new(
                "Privacy, reminders, and recovery",
                "Public pages show only entrant identities, schedules, standings, confirmed scores, and archived results.",
                [
                    "Private member contact, lobby information, moderator notes, and audit reasons are never published or emitted in lifecycle payloads.",
                    "Match reminders use permitted private delivery. Turning Tournaments & leagues off blocks pages, commands, registration, starts, results, advancement, reminders, rewards, lifecycle effects, and connected-provider work.",
                    "Saved configuration and history remain. Re-enable resumes retained current state without replaying commands, events, timers, reminders, subscriptions, rewards, or provider actions suppressed while off.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _raidCollaborationHelp = new(
        "Raid & collaboration",
        [
            _featureSwitchHelp,
            new(
                "Opt in and pause safely",
                "Raid & collaboration has its own Channel setup switch and starts off for every channel.",
                [
                    "While off, the hub, raid subscriptions, history changes, welcome sequence, shortlist checks, native shoutout handoff, and outgoing raid provider calls are blocked.",
                    "Saved settings, approved channels, and history remain private to this channel. Turning the feature back on restores them without replaying suppressed events, timers, welcome steps, shoutouts, queued work, or provider actions.",
                ]
            ),
            new(
                "Welcome an incoming community",
                "Choose a welcome message, optional native shoutout, and deduplication window. Duplicate EventSub deliveries appear once, and repeated raids inside the window do not repeat the welcome sequence.",
                [
                    "Twitch supplies an aggregate viewer count for a raid. BlokeBot does not infer or store individual viewer attribution.",
                    "Native shoutouts reuse Twitch authority, live-state, cooldown, and provider checks. They are controlled by Raid & collaboration, not the separate Shoutouts switch.",
                ]
            ),
            new(
                "Choose an outgoing raid",
                "Only channels you approve can appear. Live state, language, selected categories, and recent outgoing history are checked each time and every exclusion is explained.",
                [
                    "Approval is your allowlist, not a safety or reputation score.",
                    "Prepare raid opens an explicit confirmation. BlokeBot never starts a raid directly from a recommendation.",
                    "An optional approved clip is fetched through Twitch only when it belongs to that channel and is recent.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _playQueuesHelp = new(
        "Play with viewers",
        [
            _featureSwitchHelp,
            new(
                "Create or edit a queue",
                "Choose a saved queue to edit it, or select New queue to start a draft. A new queue is not created until you complete its details and select Save queue.",
                [
                    "The viewer-page link becomes available after the new queue has been saved.",
                    "Every configured entry field is optional and appears on the viewer page and Viewer Queue overlay. Choose a field in the inventory to edit it.",
                    "Lobby messages and moderator notes stay private.",
                ]
            ),
            new(
                "Run the queue",
                "Use fair selection and ready checks to form a party, then send lobby details privately to the selected viewers.",
                [
                    "Queue settings control capacity, readiness expiry, history, and skip or no-show exclusions.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _momentsHelp = new(
        "Moments",
        [
            _featureSwitchHelp,
            new(
                "Capture and moderate moments",
                "Choose how nearby captures are merged, whether a stream marker is used as a fallback, and how point rewards work.",
                [
                    "Capture now saves the current live moment for moderation.",
                    "Public titles and categories appear in the recap. Private moderator text never does.",
                ]
            ),
            new(
                "Preview the weekly recap",
                "Open weekly recap launches the existing public, shareable recap in a new tab so this moderator workspace and any unsaved inputs remain available.",
                ["Finalize previous week when the winning moment is ready to be recorded."]
            ),
        ]
    );

    private static readonly HelpPage _overlaysHelp = new(
        "Overlays",
        [
            _featureSwitchHelp,
            new(
                "Set up a Browser Source",
                "Create an overlay, copy its private Browser Source URL, then add it to OBS at 1920 by 1080.",
                [
                    "The private URL is shown only when an overlay is created or its URL is rotated.",
                    "Rotating the URL stops every OBS source that still uses the previous URL.",
                ]
            ),
            new(
                "Preview and test",
                "Live preview shows how the selected Browser Source will look in OBS without revealing its private URL.",
                [
                    "Guessing overlays show open, closed, and completed rounds from the existing Guessing game. Sample buttons preview each supported state without changing a round.",
                    "Guess count display and winner-result duration are saved per guessing overlay.",
                    "Connection status is approximate and is not proof that an OBS scene is visible.",
                    "Send test pulse publishes temporary presentation data only to the selected overlay.",
                ]
            ),
            new(
                "Guessing overlay availability",
                "Guessing overlays require both Overlays and Guessing game to be on in Channel setup.",
                [
                    "Turning either tool off blocks preview, Browser Source data, live delivery, and changes while retaining saved overlay setup and round history.",
                    "Turning both tools back on resumes from the stable current round without replaying suppressed updates or winner animations.",
                ]
            ),
            new(
                "Giveaway overlay availability",
                "Giveaway overlays require both Overlays and Points to be on in Channel setup.",
                [
                    "The overlay presents authoritative Points giveaway state, entrant count, countdown, configured winners and prizes, and the current canonical join command. It never exposes entrant identities or private eligibility details.",
                    "Turning either tool off blocks preview, Browser Source data, publication, tests, and winner animation while retaining saved setup and giveaway history.",
                    "Turning both tools back on resumes from stable current state without replaying suppressed updates, queued work, or winner animations.",
                ]
            ),
            new(
                "Unified event feed",
                "One Event Feed Browser Source presents point awards, Guessing winners, Giveaway winners, Bingo events, and achievement completions.",
                [
                    "Choose which events appear, what each item says, its importance, and how long it stays visible.",
                    "Achievement events inherit both Community progression and Overlays without adding a Channel Setup switch. Turning either parent off blocks achievement projection, queueing, preview, publication, reconnect state, and rendering while retaining saved feed settings and history.",
                    "Turning both parents back on accepts only new achievement completions. Suppressed events, animations, queued work, and timers never replay.",
                    "Achievement cards expose only a public viewer name, achievement name, and presentation-safe reward names or points. Twitch user IDs, balances, moderator notes, internal keys, and reward tokens remain private.",
                ]
            ),
            new(
                "Viewer Queue overlay",
                "Choose a saved Play with viewers queue, then choose how many current and next players appear.",
                [
                    "Viewer Queue requires both Overlays and Play with viewers to be on in Channel setup. Turning either off blocks preview and display while keeping the saved setup and queue.",
                    "Turning both tools back on shows only the queue as it is now. Party, ready, and selected-next animations missed while either tool was off do not play later.",
                    "Names follow the queue's public-name setting. Every optional entry field is public. Moderator notes, lobby details, Twitch account details, temporary skips, and queue history never appear.",
                ]
            ),
            new(
                "Community goals and viewer-funded bounties",
                "Choose one public item or rotate current public items. Community goal sources require Community progression and Overlays; viewer-funded bounty sources require Bounties and Overlays. These inherited requirements add no new Channel Setup switch.",
                [
                    "Turning either required parent off blocks projection, preview, Browser Source rendering, live publication, reconnect state, tests, and animations. The retained direct editor links to Channel setup.",
                    "Saved source configuration and domain history remain intact. Turning both parents back on restores current authoritative progress without replaying suppressed updates, timers, queued work, or completion animations.",
                    "Community sources expose only public communal goals. Bounty sources expose only public bounty state and the configured number of public pledge login/amount callouts. Hidden seasons, viewer progress, private bounties, Twitch user IDs, balances, moderator notes, reasons, and internal accounting remain private.",
                    "Representative samples cover active progress, a progress change, completion, failure, expiry, and an empty public state without changing a goal or bounty.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _cuesHelp = new(
        "Cues",
        [
            _featureSwitchHelp,
            new(
                "Compose and test cues",
                "Build reusable layers, choose a Cue player Browser Source, and try the cue exactly as it will appear in OBS.",
                [
                    "Use Media library to upload or replace cue media.",
                    "A web page may refuse framing; BlokeBot keeps the Browser Source sandbox in place and moves on after a bounded failure.",
                    "Turning Overlays off pauses editing and playback. Saved cues remain and paused cues do not play later.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _mediaLibraryHelp = new(
        "Media library",
        [
            _featureSwitchHelp,
            new(
                "Manage cue media",
                "Upload, preview, replace, and delete media used by cues.",
                [
                    "Files stay in private channel storage and cannot be used by another channel.",
                    "Media that is still used by a cue cannot be deleted; edit the cue first.",
                    "Turning Overlays off pauses file access while retaining saved media.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _shoutoutsHelp = new(
        "Shoutouts",
        [
            _featureSwitchHelp,
            new(
                "Recommend a live channel",
                "Enter the Twitch name of another channel that is live with viewers, then send the shoutout.",
                ["If Twitch asks you to wait, the page shows when you can send again."]
            ),
            new(
                "Welcome incoming raids",
                "Turn on Automatic raid shoutouts and choose the smallest raid that should receive one. New raids at or above that viewer count can be welcomed for up to two minutes after they arrive.",
                [
                    "Choose either a native Twitch shoutout or one chat message.",
                    "When chat delivery is selected, choose one presentation: regular, pinned, or announcement.",
                    "If the chosen shoutout fails, BlokeBot does not switch modes or send it again.",
                    "A pinned shoutout replaces the current pin. The previous pin is not restored afterwards.",
                ]
            ),
            new(
                "Personalise the chat message",
                "Use these details in the message. Add fallback text for the last game and stream title in case Twitch has no value.",
                [
                    "<code>{twitch_handle}</code>, <code>{display_name}</code>, <code>{channel_url}</code>",
                    "<code>{last_game|fallback}</code>, <code>{stream_title|fallback}</code>, <code>{viewer_count}</code>",
                ]
            ),
        ]
    );

    private static readonly HelpPage _pollsHelp = new(
        "Polls",
        [
            _featureSwitchHelp,
            new(
                "Run a poll",
                "Save a question and its choices, then start it whenever you want viewers to vote.",
                [
                    "The active poll shows votes while it runs. End it here when voting should stop.",
                    "If someone started the poll in Twitch, BlokeBot asks before ending it.",
                    "Finished polls remain under Recent results.",
                ]
            ),
            new(
                "If you cannot run a poll",
                "Use Reconnect to Twitch when the page asks you to connect this channel again.",
                ["If the page cannot load, use Retry before starting another poll."]
            ),
        ]
    );

    private static readonly HelpPage _clipsMarkersHelp = new(
        "Clips & markers",
        [
            _featureSwitchHelp,
            new(
                "Capture a live moment",
                "Create a shareable clip of the current stream, or add a private marker to find the moment in the recording later.",
                [
                    "Go live and turn on stream recordings and clips before using these actions.",
                    "A clip can remain pending while Twitch prepares it.",
                ]
            ),
            new(
                "If the result is not clear",
                "Use Check status or Check outcome on the existing attempt instead of creating the same clip or marker again.",
                [
                    "Use Reconnect to Twitch when the page asks you to connect this channel again.",
                    "Use Retry if the page itself could not load.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _channelPointsHelp = new(
        "Rewards & redemptions",
        [
            _featureSwitchHelp,
            new(
                "Manage rewards",
                "Create a Channel Points reward, set its cost and instructions, then enable or pause it when needed.",
                [
                    "You can edit, enable, pause, resume, or delete rewards created in BlokeBot.",
                    "Rewards created somewhere else remain visible but cannot be changed here.",
                ]
            ),
            new(
                "Complete viewer requests",
                "Fulfil a request when it is complete, or cancel it to return the viewer’s Channel Points.",
                ["Recent completed and refunded requests appear in Redemption history."]
            ),
            new(
                "If rewards are unavailable",
                "Channel Points rewards require a Twitch Affiliate or Partner channel.",
                [
                    "Use Reconnect to Twitch when the page asks you to connect this channel again.",
                    "If the page cannot load, use Retry before changing a reward or redemption.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _predictionsHelp = new(
        "Predictions",
        [
            _featureSwitchHelp,
            new(
                "Run a Prediction",
                "Save a question and possible outcomes, then start it whenever viewers should back an answer with Channel Points.",
                [
                    "Lock an active Prediction to stop new entries.",
                    "Resolve a locked Prediction by choosing the winning outcome, or cancel it to refund viewers.",
                    "BlokeBot asks before locking, resolving, or cancelling.",
                    "Finished Predictions remain under Recent results.",
                ]
            ),
            new(
                "If Predictions are unavailable",
                "Predictions require a Twitch Affiliate or Partner channel.",
                [
                    "Use Reconnect to Twitch when the page asks you to connect this channel again.",
                    "If the page cannot load, use Retry before starting another Prediction.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _guessingDashboardHelp = new(
        "Guessing game dashboard",
        [
            _featureSwitchHelp,
            new(
                "Live rounds",
                "Start a round, close it when guesses are done, then pick the winning answer. Each viewer keeps their first valid guess for that round.",
                [
                    "<code>!guess &lt;name&gt;</code> records a player guess.",
                    "<code>!guesses</code> lists the answers viewers can choose from.",
                    "<code>!win &lt;name&gt;</code> closes the round and announces winners.",
                    "You can change the chat command words in Settings.",
                ]
            ),
            new(
                "History",
                "Use History to look back at past rounds by date, round type, or viewer name.",
                []
            ),
        ]
    );

    private static readonly HelpPage _guessingSettingsHelp = new(
        "Guessing game settings",
        [
            _featureSwitchHelp,
            new(
                "Round types",
                "Create one or more kinds of guessing game. Each one can have its own answer list and chat replies.",
                [
                    "Enter the main answer first, then any accepted alternatives, separated by commas.",
                ]
            ),
            new(
                "Commands",
                "Command names are what viewers and mods type in chat. Start, stop, and win are mod-only commands.",
                []
            ),
            new(
                "Add live details to replies",
                "Words in braces are replaced with details from the current round or viewer.",
                _templateVariableItems
            ),
        ]
    );

    private static readonly HelpPage _pointsDashboardHelp = new(
        "Points dashboard",
        [
            _featureSwitchHelp,
            new(
                "Balances",
                "Use the leaderboard and search controls to check point balances for this channel.",
                [
                    "Give and remove accept whole numbers, percentages such as <code>50%</code>, or <code>all</code>.",
                    "Add gives points to an existing Twitch user and accepts whole numbers only.",
                ]
            ),
            new(
                "Giveaways",
                "Start, end, or cancel a giveaway. Giveaways only start while the channel is live.",
                [
                    "Each viewer can join once.",
                    "Winners are picked from eligible viewers and receive a random points prize.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _pointsSettingsHelp = new(
        "Points settings",
        [
            _featureSwitchHelp,
            new(
                "Commands",
                "Command names are the words viewers and mods type in chat. Each name must be unique for this channel.",
                [
                    "<code>points</code> shows the caller's balance. With a login, it is moderator-only.",
                    "<code>givepoints</code>, <code>gamble</code>, and <code>removepoints</code> accept whole numbers, percentages, or <code>all</code>.",
                    "<code>addpoints</code> gives points to an existing Twitch user and accepts whole numbers only.",
                ]
            ),
            new(
                "Add live details to replies",
                "Words in braces are replaced with details from the current viewer, balance, or giveaway.",
                [
                    "Balance and gambling replies: <code>{user}</code>, <code>{balance}</code>, <code>{amount}</code>, <code>{label}</code>.",
                    "Transfer replies: <code>{from}</code>, <code>{to}</code>, <code>{amount}</code>, <code>{label}</code>.",
                    "Giveaway replies: <code>{user}</code>, <code>{winners}</code>, <code>{time_left}</code>, <code>{label}</code>.",
                ]
            ),
            new(
                "Follower-only giveaways",
                "Follower-only giveaways need the bot account to be allowed to check followers, and the bot must be a moderator in the channel.",
                []
            ),
        ]
    );

    private static readonly HelpPage _customCommandsHelp = new(
        "Custom commands",
        [
            _featureSwitchHelp,
            new(
                "Replies",
                "A reply is a saved message. Add more than one message when you want the bot to rotate through them or pick one at random.",
                [
                    "<code>{random_from|one|two}</code> picks one value.",
                    "<code>{random_between|1|10}</code> picks an inclusive whole number.",
                    "Each random token occurrence makes a fresh pick.",
                    "<code>{random_viewer}</code> picks a connected Twitch chatter, not a verified viewer. The active bot account must be a moderator with connected-chatter access.",
                    "If Twitch cannot return the complete chatter list, <code>{random_viewer}</code> becomes empty text.",
                ]
            ),
            new(
                "Chat commands",
                "Command words are what viewers type after the exclamation mark. Separate extra command words with commas.",
                [
                    "Use <code>{user}</code> for the viewer's name and <code>{channel}</code> for the channel name.",
                    "Use <code>{args}</code> for everything typed after the command, or <code>{arg1}</code> through <code>{arg9}</code> for individual words.",
                    "Counter commands can use <code>{count}</code> for the new number.",
                    "Everyone makes a command public. Restricted commands can independently allow moderators and selected Twitch accounts; with neither selected, only the streamer can use the command.",
                    "Selected people are matched by their Twitch account, so a later Twitch name change does not remove access.",
                    "Overlay cue commands inherit both the Custom commands and Overlays switches. If either is off, cue playback, testing, chat replies, cooldowns, one-time viewer use, and viewer-catalog listing are paused.",
                    "Turning either switch back on restores the saved cue setup without replaying commands that were suppressed while it was off.",
                    "Test cue checks the selected cue and Browser Source without sending chat, starting a cooldown, or consuming a one-time viewer use.",
                ]
            ),
            new(
                "Scheduled messages",
                "Choose a saved reply, then decide whether it should be sent on a timer, after enough chat activity, or once a week.",
                []
            ),
        ]
    );

    private sealed record HelpPage(string Title, IReadOnlyList<HelpSection> Sections);

    private sealed record HelpSection(string Title, string Body, IReadOnlyList<string> Items);
}
