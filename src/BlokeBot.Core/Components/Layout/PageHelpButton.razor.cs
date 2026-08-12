using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
    private const string _popoverId = "page-help-popover";
    private const string _titleId = "page-help-title";

    private bool _isOpen;
    private bool _restoreFocus;
    private ElementReference _trigger;

    private HelpLocation? _currentLocation => LocationFor(_currentPath, _currentFragment);

    private HelpPage? _currentHelp => _currentLocation?.Help;

    private Uri? _guideUri =>
        HelpSiteGuide.Resolve(_options.Value.HelpSiteBaseUrl, _currentLocation?.GuidePath);

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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_restoreFocus)
        {
            _restoreFocus = false;
            await _trigger.FocusAsync();
        }
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

    private void CloseAndRestoreFocus()
    {
        _isOpen = false;
        _restoreFocus = true;
    }

    private void Toggle() => _isOpen = !_isOpen;

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (_isOpen && args.Key == "Escape")
        {
            CloseAndRestoreFocus();
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args) =>
        _ = InvokeAsync(() =>
        {
            _isOpen = false;
            StateHasChanged();
        });

    /// <summary>
    /// Every dashboard location that has page help, paired with the BlokeBot.Site guide it points
    /// at. Pairing them here keeps a route from gaining help without a guide destination.
    /// </summary>
    private static HelpLocation? LocationFor(string path, string fragment) =>
        path switch
        {
            "/" => new(_homeHelp, "/dashboard"),
            "/guessing" => new(_guessingDashboardHelp, "/guessing"),
            "/guessing/settings" => new(_guessingSettingsHelp, "/guessing"),
            "/points" => new(_pointsDashboardHelp, "/points"),
            "/points/settings" => new(_pointsSettingsHelp, "/points"),
            "/custom-commands/settings" => new(_customCommandsHelp, "/commands"),
            "/host" => new(_hostConfigHelp, "/channels"),
            "/requests" => new(_requestBoardsHelp, "/community/request-boards"),
            "/bounties" => new(_bountiesHelp, "/community/bounties"),
            "/community" => new(_communityProgressionHelp, "/community/progression"),
            "/raid" => new(_blokeRaidHelp, "/community/blokeraid"),
            "/passports" => new(_viewerPassportsHelp, "/community/passports"),
            _ when path.StartsWith("/passports/", StringComparison.Ordinal)
                    && path.EndsWith("/me", StringComparison.Ordinal) => new(
                _viewerPassportsHelp,
                "/community/passports"
            ),
            "/bingo" => new(_bingoHelp, "/community/bingo"),
            "/competitions" => new(_competitionsHelp, "/community/competitions"),
            "/raid-collaboration" => new(_raidCollaborationHelp, "/community/raid-collaboration"),
            "/collectives" => new(_collectivesHelp, "/community/collectives"),
            "/queues" => new(_playQueuesHelp, "/community/play-with-viewers"),
            "/moments" => new(_momentsHelp, "/community/moments"),
            "/overlays" => fragment switch
            {
                "cues" => new(_cuesHelp, "/overlays/cues"),
                "media" => new(_mediaLibraryHelp, "/overlays/media"),
                _ => new(_overlaysHelp, "/overlays"),
            },
            "/twitch-operations/shoutouts" => new(_shoutoutsHelp, "/twitch-operations/shoutouts"),
            "/twitch-operations/polls" => new(_pollsHelp, "/twitch-operations/polls"),
            "/twitch-operations/clips-markers" => new(
                _clipsMarkersHelp,
                "/twitch-operations/clips-markers"
            ),
            "/twitch-operations/channel-points" => new(
                _channelPointsHelp,
                "/twitch-operations/channel-points"
            ),
            "/twitch-operations/predictions" => new(
                _predictionsHelp,
                "/twitch-operations/predictions"
            ),
            _ => null,
        };

    internal static string? GuidePathForLocation(string path, string fragment) =>
        LocationFor(path, fragment)?.GuidePath;

    internal static bool HasUsefulHelpForPath(string path) =>
        LocationFor(path, string.Empty)?.Help is { } help
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
                "Connect your channel",
                "Give the bot chat access, connect Twitch, choose the bot account, then start or stop it here.",
                []
            ),
            new(
                "Choose your Chat tools",
                "Each tool has its own switch and starts off. Turning one off keeps its setup and history and stops its commands, public output, automation, and Twitch actions.",
                []
            ),
            new(
                "Decide who can help",
                "Let all of your Twitch mods in by default, limit access to named mods, or block specific mods from changing this channel.",
                []
            ),
            new(
                "Publish the command list",
                "Choose the chat words that show viewers the command list. Only viewer-safe commands are listed, never moderator-only ones.",
                []
            ),
        ]
    );

    private static readonly HelpPage _requestBoardsHelp = new(
        "Request boards",
        [
            new(
                "Create or edit a board",
                "Choose a saved board to edit it, or select New board to start a draft. The public link appears once the board is saved.",
                []
            ),
            new(
                "Moderate requests",
                "Open a board to approve, queue, accept, complete, reject, or merge viewer submissions.",
                []
            ),
            new(
                "What viewers can see",
                "Public notes appear on the board. Private moderator notes and rejection reasons stay private.",
                []
            ),
        ]
    );

    private static readonly HelpPage _bountiesHelp = new(
        "Bounties",
        [
            new(
                "Fund and settle a challenge",
                "Create a draft, open funding, then accept and resolve it. Bounties need Points to be on.",
                []
            ),
            new(
                "Where the points go",
                "Pledges are held from each viewer's balance. Cancelling or letting a bounty expire refunds them; completing spends them, and each bounty chooses whether failure refunds or spends.",
                []
            ),
            new(
                "What viewers can see",
                "Public bounties show contributor logins and pledge amounts. Private bounties show nothing publicly, and moderator reasons stay private.",
                []
            ),
        ]
    );

    private static readonly HelpPage _communityProgressionHelp = new(
        "Community progression",
        [
            new(
                "Run a season",
                "Create a draft season, add quests, achievements, and rewards, then open it. Viewers equip what they unlock with !equiptitle, !equipbadge, and !equipaccent.",
                []
            ),
            new(
                "Repeating quests",
                "Daily and weekly quests reset on the channel time zone boundary shown beside each one. Saving a schedule change closes the current period and resets active repeatable progress, so you are asked to confirm first.",
                []
            ),
            new(
                "What viewers can see",
                "Public mode shows names, progress, standings, and unlocks. Hidden mode publishes nothing, and moderator notes never appear publicly.",
                []
            ),
        ]
    );

    private static readonly HelpPage _blokeRaidHelp = new(
        "BlokeRaid",
        [
            new(
                "Run one channel boss",
                "Start a campaign, then let viewers attack, mend the ward, or spend points on Nova across several streams.",
                []
            ),
            new(
                "Tune the fight",
                "Each action has its own outcome range, cooldown, and per-stream limit. Health thresholds trigger the phase response you saved.",
                []
            ),
            new(
                "Resets",
                "Manual reset ends the current campaign and starts a fresh boss. Weekly reset runs at the UTC day and hour you choose.",
                []
            ),
        ]
    );

    private static readonly HelpPage _viewerPassportsHelp = new(
        "Viewer passports",
        [
            new(
                "Opt in and choose what is public",
                "A passport starts private. Choose Public or Channel members only to show selected profile fields and activity.",
                []
            ),
            new(
                "What is on show",
                "Profile lines are plain text, limited to 160 characters, and still follow the channel's moderation policy. Only titles and badges earned in this channel can be selected.",
                []
            ),
            new(
                "Export and reset",
                "Export downloads the data this BlokeBot associates with your Twitch identity in this channel. Reset permanently removes the passport and its chat-presence days.",
                []
            ),
        ]
    );

    private static readonly HelpPage _bingoHelp = new(
        "Stream-event Bingo",
        [
            new(
                "Build a template",
                "Make reusable 3x3, 4x4, or 5x5 templates from automatic squares and moderator-confirmed moments. Automatic squares only use connected Twitch and BlokeBot sources.",
                []
            ),
            new(
                "Join and issue cards",
                "Viewers join before you issue cards. You can move or remove them until issuing freezes the roster.",
                []
            ),
            new(
                "Marks and rewards",
                "Reversing a manual mark corrects the live card. A win that already paid out stays permanent and cannot pay again. Moderator notes and internal identifiers are never public.",
                []
            ),
        ]
    );

    private static readonly HelpPage _competitionsHelp = new(
        "Tournaments & leagues",
        [
            new(
                "Run a competition",
                "Create a draft as a tournament, round robin, or prediction league, open registration, then generate the schedule.",
                []
            ),
            new(
                "Results and corrections",
                "Correcting a confirmed result keeps the previous scores in private history and clears later outcomes that no longer apply. Milestone and placement rewards are paid only once.",
                []
            ),
            new(
                "What entrants can see",
                "Public pages show entrant names, schedules, standings, confirmed scores, and archives. Contact details, lobby information, and moderator notes are never published.",
                []
            ),
        ]
    );

    private static readonly HelpPage _raidCollaborationHelp = new(
        "Raid & collaboration",
        [
            new(
                "Choose where to raid",
                "The Hub shows approved channels that are live and match your filters. Approval is an allowlist you control, and Prepare raid always asks you to confirm.",
                []
            ),
            new(
                "Change welcome and shortlist rules",
                "Open Settings, make your changes, then save. Twitch gives a raid's total viewer count only, so no individual viewer is recorded.",
                []
            ),
        ]
    );

    private static readonly HelpPage _collectivesHelp = new(
        "Collectives",
        [
            new(
                "Invite hosts you know",
                "A collective is an allowlist you build here. Twitch raids, follows, and shared moderators never create membership.",
                []
            ),
            new(
                "Coordinate without taking over",
                "A coordinator can invite hosts, withdraw invitations, edit shared workflows, and end participation. Hosts accept, decline, and leave only for themselves, and one active coordinator is always kept.",
                []
            ),
            new(
                "What is shared",
                "Members see a shared summary: tournament references, relay totals, and goal progress. Contact details, lobby information, source mappings, moderator notes, rewards, and viewer identities stay with the owning host.",
                []
            ),
        ]
    );

    private static readonly HelpPage _playQueuesHelp = new(
        "Play with viewers",
        [
            new(
                "Create or edit a queue",
                "Choose a saved queue to edit it, or select New queue to start a draft. The viewer-page link appears once the queue is saved.",
                []
            ),
            new(
                "Run the queue",
                "Use fair selection and ready checks to form a party, then send lobby details privately to the viewers you picked.",
                []
            ),
            new(
                "What viewers can see",
                "Entry answers appear on the viewer page and the Viewer Queue overlay. Lobby messages and moderator notes stay private.",
                []
            ),
        ]
    );

    private static readonly HelpPage _momentsHelp = new(
        "Moments",
        [
            new(
                "Capture and moderate",
                "Capture now saves the current live moment for moderation. Choose how nearby captures merge, whether a stream marker is a fallback, and how point rewards work.",
                []
            ),
            new(
                "Publish the weekly recap",
                "Open weekly recap shows the public recap in a new tab, so this workspace and any unsaved inputs stay put. Finalize previous week records the winning moment.",
                []
            ),
            new(
                "What viewers can see",
                "Public titles and categories appear in the recap. Private moderator text never does.",
                []
            ),
        ]
    );

    private static readonly HelpPage _overlaysHelp = new(
        "Overlays",
        [
            new(
                "Set up a Browser Source",
                "Create an overlay, copy its private Browser Source URL, then add it to OBS at 1920 by 1080. The URL is shown only when the overlay is created or the URL is rotated.",
                []
            ),
            new(
                "Rotating a URL",
                "Rotating stops every OBS source that still uses the previous URL, so paste the new one into OBS straight away.",
                []
            ),
            new(
                "Preview and test",
                "Live preview shows how a Browser Source will look without revealing its URL. Sample buttons and Send test pulse only affect the selected overlay; they never change a round, giveaway, goal, or bounty.",
                []
            ),
            new(
                "What viewers can see",
                "Overlays show public presentation data only: names, counts, progress, and reward names. Twitch user IDs, balances, moderator notes, and private eligibility details never reach a Browser Source.",
                []
            ),
        ]
    );

    private static readonly HelpPage _cuesHelp = new(
        "Cues",
        [
            new(
                "Compose a cue",
                "Build reusable layers, choose a Cue player Browser Source, and try the cue exactly as it will appear in OBS. Use Media library to upload or replace cue media.",
                []
            ),
            new(
                "If a layer will not show",
                "A web page may refuse framing. BlokeBot keeps the Browser Source sandbox in place and moves on after a short wait.",
                []
            ),
        ]
    );

    private static readonly HelpPage _mediaLibraryHelp = new(
        "Media library",
        [
            new(
                "Manage cue media",
                "Upload, preview, replace, and delete the media used by cues. Files stay in private channel storage and cannot be used by another channel.",
                []
            ),
            new(
                "Deleting a file",
                "Media still used by a cue cannot be deleted. Edit the cue first, then delete the file.",
                []
            ),
        ]
    );

    private static readonly HelpPage _shoutoutsHelp = new(
        "Shoutouts",
        [
            new(
                "Recommend a live channel",
                "Enter the Twitch name of another channel that is live with viewers, then send the shoutout. If Twitch asks you to wait, the page shows when you can send again.",
                []
            ),
            new(
                "Welcome incoming raids",
                "Turn on Automatic raid shoutouts and choose the smallest raid that should get one. Choose either a native Twitch shoutout or one chat message; a failed shoutout is not resent another way.",
                [
                    "Chat delivery uses one presentation: regular, pinned, or announcement.",
                    "A pinned shoutout replaces the current pin, and the previous pin is not restored.",
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
            new(
                "Run a poll",
                "Save a question and its choices, then start it whenever you want viewers to vote. End it here when voting should stop, and find finished polls under Recent results.",
                []
            ),
            new(
                "If you cannot run a poll",
                "Use Reconnect to Twitch when the page asks you to connect this channel again, or Retry if the page itself could not load.",
                []
            ),
        ]
    );

    private static readonly HelpPage _clipsMarkersHelp = new(
        "Clips & markers",
        [
            new(
                "Capture a live moment",
                "Create a shareable clip of the current stream, or add a private marker to find the moment in the recording later. Go live and turn on recordings and clips first.",
                []
            ),
            new(
                "If the result is not clear",
                "A clip can stay pending while Twitch prepares it. Use Check status or Check outcome on the existing attempt rather than creating it again.",
                []
            ),
        ]
    );

    private static readonly HelpPage _channelPointsHelp = new(
        "Rewards & redemptions",
        [
            new(
                "Manage rewards",
                "Create a Channel Points reward, set its cost and instructions, then enable or pause it. Rewards created somewhere else stay visible but cannot be changed here.",
                []
            ),
            new(
                "Complete viewer requests",
                "Fulfil a request when it is done, or cancel it to return the viewer's Channel Points. Recent outcomes appear in Redemption history.",
                []
            ),
            new(
                "If rewards are unavailable",
                "Channel Points rewards need a Twitch Affiliate or Partner channel. Use Reconnect to Twitch when the page asks you to connect this channel again.",
                []
            ),
        ]
    );

    private static readonly HelpPage _predictionsHelp = new(
        "Predictions",
        [
            new(
                "Run a Prediction",
                "Save a question and possible outcomes, then start it when viewers should back an answer with Channel Points.",
                []
            ),
            new(
                "Finish a Prediction",
                "Lock it to stop new entries, then resolve it by choosing the winning outcome or cancel it to refund viewers. BlokeBot asks before each of these.",
                []
            ),
            new(
                "If Predictions are unavailable",
                "Predictions need a Twitch Affiliate or Partner channel. Use Reconnect to Twitch when the page asks you to connect this channel again.",
                []
            ),
        ]
    );

    private static readonly HelpPage _guessingDashboardHelp = new(
        "Guessing game dashboard",
        [
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
            new(
                "Replies",
                "A reply is a saved message. Add more than one message when you want the bot to rotate through them or pick one at random.",
                [
                    "<code>{random_from|one|two}</code> picks one value.",
                    "<code>{random_between|1|10}</code> picks an inclusive whole number.",
                    "Each random token occurrence makes a fresh pick.",
                    "<code>{random_viewer}</code> picks from Twitch chatters currently connected to chat. The active bot account must be a moderator with connected-chatter access.",
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

    private sealed record HelpLocation(HelpPage Help, string GuidePath);
}
