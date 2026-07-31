using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot.Core;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
    private bool _isOpen;

    private HelpPage? _currentHelp => HelpForPath(_currentPath);

    private string _currentPath
    {
        get
        {
            var relative = _navigation.ToBaseRelativePath(_navigation.Uri);
            var path = relative.Split('?', '#')[0].Trim('/');
            return string.IsNullOrWhiteSpace(path) ? "/" : "/" + path;
        }
    }

    protected override void OnInitialized()
    {
        _navigation.LocationChanged += OnLocationChanged;
    }

    public void Dispose()
    {
        _navigation.LocationChanged -= OnLocationChanged;
    }

    private void Close()
    {
        _isOpen = false;
    }

    private void Toggle()
    {
        _isOpen = !_isOpen;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _ = InvokeAsync(() =>
        {
            _isOpen = false;
            StateHasChanged();
        });
    }

    private static HelpPage? HelpForPath(string path)
    {
        return path switch
        {
            "/" => _homeHelp,
            "/guessing" => _guessingDashboardHelp,
            "/guessing/settings" => _guessingSettingsHelp,
            "/points" => _pointsDashboardHelp,
            "/points/settings" => _pointsSettingsHelp,
            "/custom-commands/settings" => _customCommandsHelp,
            "/host" => _hostConfigHelp,
            "/requests" => _requestBoardsHelp,
            "/queues" => _playQueuesHelp,
            "/moments" => _momentsHelp,
            "/overlays" => _overlaysHelp,
            "/twitch-operations/shoutouts" => _shoutoutsHelp,
            "/twitch-operations/polls" => _pollsHelp,
            "/twitch-operations/clips-markers" => _clipsMarkersHelp,
            "/twitch-operations/channel-points" => _channelPointsHelp,
            "/twitch-operations/predictions" => _predictionsHelp,
            _ => null,
        };
    }

    internal static bool HasUsefulHelpForPath(string path)
    {
        return HelpForPath(path) is { } help
            && !string.IsNullOrWhiteSpace(help.Title)
            && help.Sections.Count > 0
            && help.Sections.All(section =>
                !string.IsNullOrWhiteSpace(section.Title)
                && (
                    !string.IsNullOrWhiteSpace(section.Body)
                    || section.Items.Any(item => !string.IsNullOrWhiteSpace(item))
                )
            );
    }

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
            "Turning it off hides normal navigation and stops commands, automation, public output, and provider actions.",
            "Saved configuration and history are retained. Turning it back on resumes from the current state without replaying work suppressed while it was off.",
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
                    "Turning a tool off retains its setup and history while stopping its commands, public output, automation, and provider actions.",
                    "Turning a tool back on restores access to saved data without replaying work suppressed while it was off.",
                ]
            ),
            new(
                "Channel setup",
                "Create your channel setup, let the bot chat in your stream, and start or stop it when you need.",
                []
            ),
            new(
                "Moderator access",
                "You can let all of your Twitch mods help by default, limit access to named mods, or block specific mods from changing this channel.",
                []
            ),
            new(
                "Available viewer commands",
                "Choose the global chat words that open the viewer command catalog. The catalog publishes one canonical name for each command.",
                [
                    "The list shows one canonical name for each viewer-safe command and never includes moderator-only commands.",
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

    private static readonly HelpPage _playQueuesHelp = new(
        "Play with viewers",
        [
            _featureSwitchHelp,
            new(
                "Create or edit a queue",
                "Choose a saved queue to edit it, or select New queue to start a draft. A new queue is not created until you complete its details and select Save queue.",
                [
                    "The viewer-page link becomes available after the new queue has been saved.",
                    "Entry fields are private to moderators. Choose a field in the inventory to edit it.",
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
                "Live preview uses the same renderer as OBS without putting the private URL in this page.",
                [
                    "Guessing overlays show open, closed, and completed rounds from the existing Guessing game. Sample buttons preview each supported state without changing a round.",
                    "Guess count display and winner-result duration are saved per guessing overlay.",
                    "Connection status is approximate diagnostic information, not proof that an OBS scene is visible.",
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
                "Reusable media and web cues",
                "Create an enabled Cue player Browser Source, upload validated MP4 or MP3 media, then compose reusable Cue-V1 layers in the cue editor.",
                [
                    "Uploaded files remain in private channel storage. A cue cannot use another channel’s asset, and an in-use asset cannot be deleted.",
                    "Remote HTTPS media loads directly in the Browser Source. BlokeBot does not fetch or proxy it, and private-network destinations are rejected unless the server owner opts in.",
                    "External pages run in a restrictive iframe sandbox. A site may refuse framing; a bounded failure or timeout advances the queue without weakening the sandbox.",
                    "Test playback uses the same Cue player target, queue policy, live transport, and media routes used by later triggers.",
                    "Turning Overlays off blocks cue management, uploads, private media, playback, queues, and external loads. Saved cues and assets remain, but suppressed or queued runs never replay after re-enable or restart.",
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
                    "For each answer, enter its canonical name first and any accepted aliases after it, separated by commas.",
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
                []
            ),
            new(
                "Chat commands",
                "Command words are what viewers type after the exclamation mark. Separate extra command words with commas.",
                [
                    "Use <code>{user}</code> for the viewer's name and <code>{channel}</code> for the channel name.",
                    "Use <code>{args}</code> for everything typed after the command, or <code>{arg1}</code> through <code>{arg9}</code> for individual words.",
                    "Counter commands can use <code>{count}</code> for the new number.",
                    "Overlay cue commands inherit both the Custom commands and Overlays switches. If either is off, playback, testing, replies, cooldowns, use claims, and viewer-catalog disclosure are paused.",
                    "Turning either switch back on restores the saved cue setup without replaying commands that were suppressed while it was off.",
                    "Test cue uses the same host-bound playback admission as a viewer command, but does not send chat, start a cooldown, or claim a viewer use.",
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
