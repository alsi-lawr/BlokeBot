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
            "/admin" => new(_adminHelp, "/channels"),
            "/alerts" => new(_alertsHelp, "/troubleshooting"),
            "/guessing" => new(_guessingDashboardHelp, "/guessing"),
            "/guessing/settings" => new(_guessingSettingsHelp, "/guessing"),
            "/points" => new(_pointsDashboardHelp, "/points"),
            "/points/settings" => new(_pointsSettingsHelp, "/points"),
            "/custom-commands/settings" => new(_customCommandsHelp, "/commands"),
            "/automations" => new(_automationsHelp, "/automations"),
            "/automations/events" => new(_automationEventsHelp, "/automations/events"),
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
        "<strong>Start reply</strong> uses <code>{round}</code> and <code>{options}</code>.",
        "<strong>Guess option reply</strong> uses <code>{name}</code> and <code>{login}</code>.",
        "<strong>Invalid guess reply</strong> uses <code>{name}</code> and <code>{login}</code>.",
        "<strong>How to guess reply</strong> uses <code>{command}</code>.",
        "<strong>Available guesses reply</strong> uses <code>{round}</code> and <code>{options}</code>.",
        "<strong>How to choose a winner reply</strong> uses <code>{command}</code>.",
        "<strong>Winner and no-winners replies</strong> use <code>{name}</code>.",
        "<strong>Winner and no-winners replies</strong> use <code>{winners}</code>.",
        "<strong>Winner and no-winners replies</strong> use <code>{count}</code>.",
        "<strong>Stop reply</strong> uses no live details.",
        "<strong>Closed reply</strong> uses no live details.",
        "<strong>No-round reply</strong> uses no live details.",
        "<strong>Already-running reply</strong> uses no live details.",
        "<strong>Moderator-only reply</strong> uses no live details.",
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
                "",
                [
                    "Use the menu to set up your channel.",
                    "Use the menu to start chat games.",
                    "Use the menu to manage points.",
                    "Use the menu to change bot settings.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _adminHelp = new(
        "Admin",
        [
            new(
                "Bot account",
                "",
                [
                    "Authorize the bot account.",
                    "Select Refresh to get the bot account status.",
                    "Select Disconnect to disconnect the bot account.",
                ]
            ),
            new(
                "Who can create channels",
                "The approved list or the blocked list controls who can create channels.",
                []
            ),
            new(
                "BlokeBot channels",
                "",
                [
                    "Select Add channel to add a channel.",
                    "Select Start bot to start its bot.",
                    "Select Stop bot to stop its bot.",
                    "Select Manage channel to manage the channel.",
                    "Select Remove to remove the channel.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _alertsHelp = new(
        "Alerts",
        [
            new(
                "Active alerts",
                "Active alerts show problems that need attention.",
                [
                    "Select Open for a linked alert.",
                    "Select Mark as handled after you deal with the alert.",
                ]
            ),
            new("History", "Handled alerts show who dealt with them and when.", []),
            new("Refresh the page", "", ["Select Refresh to get the current alerts."]),
        ]
    );

    private static readonly HelpPage _hostConfigHelp = new(
        "Channel setup",
        [
            new(
                "Connect your channel",
                "",
                [
                    "Give the bot chat access.",
                    "Connect Twitch.",
                    "Choose the bot account.",
                    "Start the bot here.",
                    "Stop the bot here.",
                ]
            ),
            new(
                "Choose your Chat tools",
                "Each tool has its own switch and starts off.",
                [
                    "A switched-off tool keeps its setup and history.",
                    "A switched-off tool stops its commands.",
                    "A switched-off tool stops its public output.",
                    "A switched-off tool stops its automation.",
                    "A switched-off tool stops its Twitch actions.",
                ]
            ),
            new(
                "Decide who can help",
                "",
                [
                    "Let all of your Twitch mods access this channel by default.",
                    "Limit access to named mods.",
                    "Block specific mods from changes to this channel.",
                ]
            ),
            new(
                "Publish the command list",
                "Only viewer-safe commands appear in the command list.",
                [
                    "Moderator-only commands never appear in the command list.",
                    "Choose the chat words that show viewers the command list.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _requestBoardsHelp = new(
        "Request boards",
        [
            new(
                "Create or edit a board",
                "The public link appears after you save the board.",
                ["Choose a saved board to edit it.", "Select New board to start a draft."]
            ),
            new(
                "Moderate requests",
                "",
                [
                    "Open a board.",
                    "Approve viewer submissions.",
                    "Queue viewer submissions.",
                    "Accept viewer submissions.",
                    "Complete viewer submissions.",
                    "Reject viewer submissions.",
                    "Merge viewer submissions.",
                ]
            ),
            new(
                "What viewers can see",
                "Public notes appear on the board.",
                ["Private moderator notes stay private.", "Rejection reasons stay private."]
            ),
        ]
    );

    private static readonly HelpPage _bountiesHelp = new(
        "Bounties",
        [
            new(
                "Fund and settle a challenge",
                "Bounties need Points to be on.",
                ["Create a draft.", "Open funding.", "Accept the bounty.", "Resolve the bounty."]
            ),
            new(
                "Where the points go",
                "Pledges hold points from each viewer's balance.",
                [
                    "A canceled bounty refunds the pledges.",
                    "An expired bounty refunds the pledges.",
                    "A completed bounty spends the pledges.",
                    "Each bounty defines whether failure refunds or spends the pledges.",
                ]
            ),
            new(
                "What viewers can see",
                "Public bounties show contributor logins and pledge amounts.",
                ["Private bounties show nothing publicly.", "Moderator reasons stay private."]
            ),
        ]
    );

    private static readonly HelpPage _communityProgressionHelp = new(
        "Community progression",
        [
            new(
                "Set up a season",
                "",
                [
                    "Create a draft season.",
                    "Add quests.",
                    "Add achievements.",
                    "Add rewards.",
                    "Open the season.",
                ]
            ),
            new(
                "Use unlocked rewards",
                "Viewers equip unlocked rewards with chat commands.",
                [
                    "!equiptitle equips an unlocked title.",
                    "!equipbadge equips an unlocked badge.",
                    "!equipaccent equips an unlocked accent.",
                ]
            ),
            new(
                "Quest resets",
                "Daily and weekly quests reset at the channel time zone boundary shown beside each quest.",
                [
                    "A saved schedule change closes the current period.",
                    "A saved schedule change resets active repeatable progress.",
                    "BlokeBot asks you to confirm the schedule change first.",
                ]
            ),
            new(
                "What viewers can see",
                "",
                [
                    "Public mode shows names.",
                    "Public mode shows progress.",
                    "Public mode shows standings.",
                    "Public mode shows unlocks.",
                    "Hidden mode publishes nothing.",
                    "Moderator notes never appear publicly.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _blokeRaidHelp = new(
        "BlokeRaid",
        [
            new(
                "Run one channel boss",
                "",
                [
                    "Start a campaign.",
                    "Viewers can attack across several streams.",
                    "Viewers can mend the ward across several streams.",
                    "Viewers can spend points on Nova across several streams.",
                ]
            ),
            new(
                "Tune the fight",
                "",
                [
                    "Each action has its own outcome range.",
                    "Each action has its own cooldown.",
                    "Each action has its own per-stream limit.",
                    "Health thresholds trigger your saved phase response.",
                ]
            ),
            new(
                "Resets",
                "Manual reset ends the current campaign and starts a fresh boss.",
                ["Weekly reset runs at your selected UTC day and hour."]
            ),
        ]
    );

    private static readonly HelpPage _viewerPassportsHelp = new(
        "Viewer passports",
        [
            new(
                "Choose what is public",
                "A passport starts private.",
                [
                    "Opt in to create a passport.",
                    "Choose Public or Channel members only to show selected profile fields and activity.",
                ]
            ),
            new(
                "What is on show",
                "Profile lines use plain text and have a limit of 160 characters.",
                [
                    "Profile lines follow the channel's moderation policy.",
                    "Viewers can select only titles and badges earned in this channel.",
                ]
            ),
            new(
                "Export and reset",
                "Export downloads the data that BlokeBot associates with your Twitch identity in this channel.",
                ["Reset permanently removes the passport and its stream attendance."]
            ),
        ]
    );

    private static readonly HelpPage _bingoHelp = new(
        "Stream-event Bingo",
        [
            new(
                "Build a template",
                "Automatic squares use only connected Twitch and BlokeBot sources.",
                [
                    "A reusable template can use a 3x3 size.",
                    "A reusable template can use a 4x4 size.",
                    "A reusable template can use a 5x5 size.",
                    "Add automatic squares.",
                    "Add moderator-confirmed moments.",
                ]
            ),
            new(
                "Join and issue cards",
                "Viewers join before you issue cards.",
                [
                    "Move viewers before you issue cards.",
                    "Remove viewers before you issue cards.",
                    "Issue cards to freeze the roster.",
                ]
            ),
            new(
                "Marks and rewards",
                "A reversed manual mark corrects the live card.",
                [
                    "A paid win stays permanent.",
                    "A paid win cannot pay again.",
                    "Moderator notes never become public.",
                    "Internal identifiers never become public.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _competitionsHelp = new(
        "Tournaments & leagues",
        [
            new(
                "Run a competition",
                "",
                [
                    "A competition draft can use the tournament type.",
                    "A competition draft can use the round robin type.",
                    "A competition draft can use the prediction league type.",
                    "Open registration.",
                    "Generate the schedule.",
                ]
            ),
            new(
                "Results and corrections",
                "A corrected confirmed result keeps the previous scores in private history.",
                [
                    "The correction clears later outcomes that no longer apply.",
                    "Milestone rewards pay only once.",
                    "Placement rewards pay only once.",
                ]
            ),
            new(
                "What entrants can see",
                "",
                [
                    "Public pages show entrant names.",
                    "Public pages show schedules.",
                    "Public pages show standings.",
                    "Public pages show confirmed scores.",
                    "Public pages show archives.",
                    "Contact details never appear publicly.",
                    "Lobby information never appears publicly.",
                    "Moderator notes never appear publicly.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _raidCollaborationHelp = new(
        "Raid & collaboration",
        [
            new(
                "Choose where to raid",
                "The Hub shows live shortlist channels that match your filters.",
                [
                    "Approved channels always supply candidates.",
                    "You can also include live channels that the channel owner follows.",
                    "Reconnect Twitch to give BlokeBot permission to read followed channels.",
                    "A Twitch follow is not approval or safety evidence.",
                    "Prepare raid always asks you to confirm.",
                ]
            ),
            new(
                "Recommend a live channel now",
                "",
                [
                    "Use Send a shoutout with the Twitch name of a live channel with viewers.",
                    "If Twitch asks you to wait, read the next available time in the panel.",
                    "Use Approve channel on a history entry to add that channel to your approved list immediately.",
                ]
            ),
            new(
                "Welcome incoming raids",
                "A failed shoutout does not use the other delivery method.",
                [
                    "Turn on Automatic raid shoutouts.",
                    "Choose the smallest raid that receives a shoutout.",
                    "Choose a native Twitch shoutout.",
                    "Alternatively, choose one chat message.",
                    "Only shoutout approved channels limits automatic shoutouts to your approved list.",
                    "Chat delivery can use regular presentation.",
                    "Chat delivery can use pinned presentation.",
                    "Chat delivery can use announcement presentation.",
                    "A pinned shoutout replaces the current pin.",
                    "BlokeBot does not restore the previous pin.",
                ]
            ),
            new(
                "Customize the chat message",
                "Fallback text appears when Twitch has no value for the last game or stream title.",
                [
                    "The message can use <code>{twitch_handle}</code>.",
                    "The message can use <code>{display_name}</code>.",
                    "The message can use <code>{channel_url}</code>.",
                    "The message can use <code>{last_game|fallback}</code>.",
                    "The message can use <code>{stream_title|fallback}</code>.",
                    "The message can use <code>{viewer_count}</code>.",
                    "Add fallback text for the last game.",
                    "Add fallback text for the stream title.",
                ]
            ),
            new(
                "Change welcome and shortlist rules",
                "Twitch gives only the total viewer count for a raid.",
                [
                    "Open Settings.",
                    "Choose whether to include followed live channels.",
                    "Make your changes.",
                    "Save the changes.",
                    "BlokeBot records no individual viewer from the raid.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _collectivesHelp = new(
        "Collectives",
        [
            new(
                "Invite hosts you know",
                "A collective is an allowlist that you build here.",
                [
                    "Twitch raids never create membership.",
                    "Twitch follows never create membership.",
                    "Shared moderators never create membership.",
                ]
            ),
            new(
                "Coordinate without control of hosts",
                "",
                [
                    "A coordinator can invite hosts.",
                    "A coordinator can withdraw invitations.",
                    "A coordinator can edit shared workflows.",
                    "A coordinator can end participation.",
                    "Hosts accept only for themselves.",
                    "Hosts decline only for themselves.",
                    "Hosts leave only for themselves.",
                    "The collective always keeps one active coordinator.",
                ]
            ),
            new(
                "What is shared",
                "",
                [
                    "Members see tournament references in a shared summary.",
                    "Members see relay totals in a shared summary.",
                    "Members see goal progress in a shared summary.",
                    "Each host keeps its own contact details.",
                    "Each host keeps its own lobby information.",
                    "Each host keeps its own source mappings.",
                    "Each host keeps its own moderator notes.",
                    "Each host keeps its own rewards.",
                    "Each host keeps its own viewer identities.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _playQueuesHelp = new(
        "Play with viewers",
        [
            new(
                "Create or edit a queue",
                "The viewer-page link appears after you save the queue.",
                ["Choose a saved queue to edit it.", "Select New queue to start a draft."]
            ),
            new(
                "Run the queue",
                "",
                [
                    "Use fair selection to form a party.",
                    "Use ready checks to form a party.",
                    "Send lobby details privately to your selected viewers.",
                ]
            ),
            new(
                "What viewers can see",
                "Entry answers appear on the viewer page and the Viewer Queue overlay.",
                ["Lobby messages stay private.", "Moderator notes stay private."]
            ),
        ]
    );

    private static readonly HelpPage _momentsHelp = new(
        "Moments",
        [
            new(
                "Capture and moderate",
                "Capture now saves the current live moment for moderation.",
                [
                    "Choose how nearby captures merge.",
                    "Choose whether a stream marker is a fallback.",
                    "Choose how point rewards work.",
                ]
            ),
            new(
                "Publish the weekly recap",
                "Open weekly recap opens the public recap in a new tab.",
                [
                    "The new tab keeps this workspace in place.",
                    "The new tab keeps unsaved inputs in place.",
                    "Finalize previous week records the winning moment.",
                ]
            ),
            new(
                "What viewers can see",
                "Public titles and categories appear in the recap.",
                ["Private moderator text never appears in the recap."]
            ),
        ]
    );

    private static readonly HelpPage _overlaysHelp = new(
        "Overlays",
        [
            new(
                "Set up a Browser Source",
                "The private Browser Source URL appears only after overlay creation or URL rotation.",
                [
                    "Create an overlay.",
                    "Copy its private Browser Source URL.",
                    "Add it to OBS at 1920 by 1080.",
                ]
            ),
            new(
                "Rotate a URL",
                "URL rotation stops each OBS source that uses the previous URL.",
                ["Paste the new URL into OBS immediately."]
            ),
            new(
                "Preview and test",
                "Live preview shows the Browser Source appearance without its URL.",
                [
                    "Sample buttons affect only the selected overlay.",
                    "Send test pulse affects only the selected overlay.",
                    "These tests never change a round.",
                    "These tests never change a giveaway.",
                    "These tests never change a goal.",
                    "These tests never change a bounty.",
                ]
            ),
            new(
                "What viewers can see",
                "",
                [
                    "Overlays show only public names.",
                    "Overlays show only public counts.",
                    "Overlays show only public progress.",
                    "Overlays show only public reward names.",
                    "Twitch user IDs never reach a Browser Source.",
                    "Balances never reach a Browser Source.",
                    "Moderator notes never reach a Browser Source.",
                    "Private eligibility details never reach a Browser Source.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _cuesHelp = new(
        "Cues",
        [
            new(
                "Compose a cue",
                "",
                [
                    "Build reusable layers.",
                    "Choose a Cue player Browser Source.",
                    "Try the cue exactly as it will appear in OBS.",
                    "Use Media library to upload or replace cue media.",
                ]
            ),
            new(
                "If a layer does not show",
                "A web page can refuse display in a frame.",
                [
                    "BlokeBot keeps the Browser Source sandbox in place.",
                    "BlokeBot continues after a short wait.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _mediaLibraryHelp = new(
        "Media library",
        [
            new(
                "Manage cue media",
                "Files stay in private channel storage.",
                [
                    "Another channel cannot use the files.",
                    "Upload cue media.",
                    "Preview cue media.",
                    "Replace cue media.",
                    "Delete cue media.",
                ]
            ),
            new(
                "Delete a file",
                "You cannot delete media that a cue still uses.",
                ["Edit the cue first.", "Delete the file."]
            ),
        ]
    );

    private static readonly HelpPage _pollsHelp = new(
        "Polls",
        [
            new(
                "Run a poll",
                "Finished polls appear under Recent results.",
                [
                    "Save a question and its choices.",
                    "Start the poll when you want viewers to vote.",
                    "End the poll here when you want the vote to stop.",
                ]
            ),
            new(
                "If you cannot run a poll",
                "",
                [
                    "Use Reconnect to Twitch when the page asks you to connect this channel again.",
                    "Use Retry if the page cannot load.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _clipsMarkersHelp = new(
        "Clips & markers",
        [
            new(
                "Capture a live moment",
                "A shareable clip captures the current stream.",
                [
                    "Create a shareable clip of the current stream.",
                    "Add a private marker to find the moment in the later recording.",
                    "Go live first.",
                    "Turn on recordings first.",
                    "Turn on clips first.",
                ]
            ),
            new(
                "If the result is not clear",
                "A clip can stay pending while Twitch prepares it.",
                [
                    "Use Check status on the existing attempt.",
                    "Use Check outcome on the existing attempt.",
                    "Do not create the clip again.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _channelPointsHelp = new(
        "Rewards & redemptions",
        [
            new(
                "Manage rewards",
                "Rewards created elsewhere stay visible.",
                [
                    "You cannot change those rewards here.",
                    "Create a Channel Points reward.",
                    "Set its cost.",
                    "Set its instructions.",
                    "Enable it.",
                    "Pause it.",
                ]
            ),
            new(
                "Complete viewer requests",
                "Recent outcomes appear in Redemption history.",
                [
                    "Fulfil a completed request.",
                    "Cancel a request to return the viewer's Channel Points.",
                ]
            ),
            new(
                "If rewards are unavailable",
                "Channel Points rewards need a Twitch Affiliate or Partner channel.",
                ["Use Reconnect to Twitch when the page asks you to connect this channel again."]
            ),
        ]
    );

    private static readonly HelpPage _predictionsHelp = new(
        "Predictions",
        [
            new(
                "Run a Prediction",
                "Viewers back an answer with Channel Points.",
                [
                    "Save a question.",
                    "Save the possible outcomes.",
                    "Start the Prediction when viewers must back an answer.",
                ]
            ),
            new(
                "Finish a Prediction",
                "",
                [
                    "BlokeBot asks before a lock.",
                    "BlokeBot asks before a resolution.",
                    "BlokeBot asks before a cancellation.",
                    "Lock the Prediction to stop new entries.",
                    "Choose the winning outcome to resolve the Prediction.",
                    "Cancel the Prediction to refund viewers.",
                ]
            ),
            new(
                "If Predictions are unavailable",
                "Predictions need a Twitch Affiliate or Partner channel.",
                ["Use Reconnect to Twitch when the page asks you to connect this channel again."]
            ),
        ]
    );

    private static readonly HelpPage _guessingDashboardHelp = new(
        "Guessing game dashboard",
        [
            new(
                "Live rounds",
                "Each viewer keeps their first valid guess for the round.",
                [
                    "Start a round.",
                    "Close the round after all guesses.",
                    "Pick the winning answer.",
                    "<code>!guess &lt;name&gt;</code> records a player guess.",
                    "<code>!guesses</code> lists the available answers.",
                    "<code>!win &lt;name&gt;</code> closes the round and announces winners.",
                    "Change the chat command words in Settings.",
                ]
            ),
            new(
                "History",
                "",
                [
                    "Use History to find past rounds by date.",
                    "Use History to find past rounds by round type.",
                    "Use History to find past rounds by viewer name.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _guessingSettingsHelp = new(
        "Guessing game settings",
        [
            new(
                "Round types",
                "Each guessing game type can use its own answer list and chat replies.",
                [
                    "Create one or more guessing game types.",
                    "Enter the main answer first.",
                    "Enter accepted alternatives after the main answer.",
                    "Separate accepted alternatives with commas.",
                ]
            ),
            new(
                "Commands",
                "Command names are the words that viewers and mods type in chat.",
                [
                    "Start is a mod-only command.",
                    "Stop is a mod-only command.",
                    "Win is a mod-only command.",
                ]
            ),
            new(
                "Add live details to replies",
                "Words in braces use details from the current round or viewer.",
                _templateVariableItems
            ),
        ]
    );

    private static readonly HelpPage _pointsDashboardHelp = new(
        "Points dashboard",
        [
            new(
                "Balances",
                "",
                [
                    "Give and remove accept whole numbers.",
                    "Give and remove accept percentages such as <code>50%</code>.",
                    "Give and remove accept <code>all</code>.",
                    "Use the leaderboard to check point balances for this channel.",
                    "Use the search controls to check point balances for this channel.",
                    "Add gives points to an existing Twitch user.",
                    "Add accepts whole numbers only.",
                ]
            ),
            new(
                "Giveaways",
                "Giveaways start only while the channel is live.",
                [
                    "Start a giveaway.",
                    "End a giveaway.",
                    "Cancel a giveaway.",
                    "Each viewer can join once.",
                    "BlokeBot picks winners from eligible viewers.",
                    "Each winner receives a random points prize.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _pointsSettingsHelp = new(
        "Points settings",
        [
            new(
                "Commands",
                "Command names are the words that viewers and mods type in chat.",
                [
                    "Each command name must be unique for this channel.",
                    "<code>points</code> shows the caller's balance.",
                    "With a login, <code>points</code> is moderator-only.",
                    "<code>givepoints</code> accepts whole numbers.",
                    "<code>givepoints</code> accepts percentages.",
                    "<code>givepoints</code> accepts <code>all</code>.",
                    "<code>gamble</code> accepts whole numbers.",
                    "<code>gamble</code> accepts percentages.",
                    "<code>gamble</code> accepts <code>all</code>.",
                    "<code>removepoints</code> accepts whole numbers.",
                    "<code>removepoints</code> accepts percentages.",
                    "<code>removepoints</code> accepts <code>all</code>.",
                    "<code>addpoints</code> gives points to an existing Twitch user.",
                    "<code>addpoints</code> accepts whole numbers only.",
                ]
            ),
            new(
                "Add live details to replies",
                "",
                [
                    "Words in braces can use details from the current viewer.",
                    "Words in braces can use details from the current balance.",
                    "Words in braces can use details from the current giveaway.",
                    "Balance and gamble replies use <code>{user}</code>.",
                    "Balance and gamble replies use <code>{balance}</code>.",
                    "Balance and gamble replies use <code>{amount}</code>.",
                    "Balance and gamble replies use <code>{label}</code>.",
                    "Transfer replies use <code>{from}</code>.",
                    "Transfer replies use <code>{to}</code>.",
                    "Transfer replies use <code>{amount}</code>.",
                    "Transfer replies use <code>{label}</code>.",
                    "Giveaway replies use <code>{user}</code>.",
                    "Giveaway replies use <code>{winners}</code>.",
                    "Giveaway replies use <code>{time_left}</code>.",
                    "Giveaway replies use <code>{label}</code>.",
                ]
            ),
            new(
                "Follower-only giveaways",
                "Follower-only giveaways need permission for the bot account to check followers.",
                ["The bot must be a moderator in the channel."]
            ),
        ]
    );

    private static readonly HelpPage _customCommandsHelp = new(
        "Custom commands",
        [
            new(
                "Replies",
                "A reply is a saved message.",
                [
                    "Add multiple messages to rotate through them or select one at random.",
                    "<code>{random_from|one|two}</code> selects one value.",
                    "<code>{random_between|1|10}</code> selects an inclusive whole number.",
                    "Each random token occurrence makes a new selection.",
                    "<code>{random_viewer}</code> selects a Twitch chatter who is currently connected to chat.",
                    "The active bot account must be a moderator with connected-chatter access.",
                    "If Twitch cannot return the complete chatter list, <code>{random_viewer}</code> becomes empty text.",
                ]
            ),
            new(
                "Chat commands",
                "Command words are the words that viewers type after the exclamation mark.",
                [
                    "Separate extra command words with commas.",
                    "Use <code>{user}</code> for the viewer's name.",
                    "Use <code>{channel}</code> for the channel name.",
                    "Use <code>{args}</code> for all text after the command.",
                    "Use <code>{arg1}</code> through <code>{arg9}</code> for individual words.",
                    "Use <code>{count}</code> for the new number in counter commands.",
                    "Everyone makes a command public.",
                    "Restricted commands can allow moderators independently.",
                    "Restricted commands can allow selected Twitch accounts independently.",
                    "With neither option selected, only the streamer can use the command.",
                    "Selected people match by their Twitch account.",
                    "A later Twitch name change does not remove access.",
                    "Test cue checks the selected cue and Browser Source without a chat message.",
                    "Test cue does not start a cooldown.",
                    "Test cue does not consume a one-time viewer use.",
                    "Run automation flows starts every enabled visual flow connected to this command.",
                    "Custom commands and Automations must both be on. Disabled commands and flows keep their saved setup without replaying suppressed work.",
                ]
            ),
            new(
                "Scheduled messages",
                "",
                [
                    "Choose a saved reply.",
                    "A scheduled reply can use a timer.",
                    "A scheduled reply can run after enough chat activity.",
                    "A scheduled reply can run once a week.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _automationsHelp = new(
        "Visual automations",
        [
            new(
                "Build a flow",
                "A flow starts from one or more triggers. Each trigger starts a separate run.",
                [
                    "Select Node to open the node library.",
                    "Search for a node by its name or category.",
                    "Connect an output port to a compatible input port.",
                    "Select a node to edit its fields in the inspector.",
                    "Use List view to edit the same flow without the canvas.",
                ]
            ),
            new(
                "Use the canvas",
                "The canvas uses a 24-pixel grid. It supports horizontal and vertical flow directions.",
                [
                    "Drag the background to move the canvas.",
                    "Press Ctrl and use the mouse wheel to zoom.",
                    "Press Alt and drag to select nodes.",
                    "Press Shift and select a node to change the node selection.",
                    "Select a connection to delete it.",
                ]
            ),
            new(
                "Validate and test",
                "A sample run shows node results. It does not send actions to Twitch or the live channel.",
                [
                    "Validate the flow before you save or enable it.",
                    "Correct each red node or field.",
                    "Test the flow with a sample event.",
                    "Read the run summary to find a failed node.",
                    "Confirm the warning before you enable actions with public effects.",
                ]
            ),
            new(
                "Turn Automations on or off",
                "Use the Automations switch in Channel setup.",
                [
                    "When Automations is off, BlokeBot blocks flow edits, tests, triggers, and actions.",
                    "Saved flows and run history remain.",
                    "BlokeBot does not replay events that it blocked while Automations was off.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _automationEventsHelp = new(
        "Automation Twitch events",
        [
            new(
                "Source readiness",
                "Each source shows the Twitch subscription and broadcaster approval it needs.",
                [
                    "Reconnect the selected channel when a source reports missing approval.",
                    "Only sources used by enabled flows keep their required automation subscriptions active.",
                    "Open Visual automations to add, configure, validate, test, and enable an event flow.",
                ]
            ),
            new(
                "Disabled behavior",
                "Turning Automations off pauses event starts and removes automation-owned subscriptions.",
                [
                    "Saved flows and history remain available after re-enable.",
                    "Events suppressed while the feature is off are not replayed.",
                ]
            ),
        ]
    );

    private sealed record HelpPage(string Title, IReadOnlyList<HelpSection> Sections);

    private sealed record HelpSection(string Title, string Body, IReadOnlyList<string> Items);

    private sealed record HelpLocation(HelpPage Help, string GuidePath);
}
