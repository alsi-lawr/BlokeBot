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
            "/twitch-operations/shoutouts" => _shoutoutsHelp,
            "/twitch-operations/polls" => _pollsHelp,
            "/twitch-operations/clips-markers" => _clipsMarkersHelp,
            "/twitch-operations/channel-points" => _channelPointsHelp,
            "/twitch-operations/predictions" => _predictionsHelp,
            _ => null,
        };
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
                "Channel setup",
                "Create your channel setup, let the bot chat in your stream, and start or stop it when you need.",
                []
            ),
            new(
                "Moderator access",
                "You can let all of your Twitch mods help by default, limit access to named mods, or block specific mods from changing this channel.",
                []
            ),
        ]
    );

    private static readonly HelpPage _shoutoutsHelp = new(
        "Shoutouts",
        [
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
                []
            ),
            new(
                "Chat commands",
                "Command words are what viewers type after the exclamation mark. Separate extra command words with commas.",
                [
                    "Use <code>{user}</code> for the viewer's name and <code>{channel}</code> for the channel name.",
                    "Use <code>{args}</code> for everything typed after the command, or <code>{arg1}</code> through <code>{arg9}</code> for individual words.",
                    "Counter commands can use <code>{count}</code> for the new number.",
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
