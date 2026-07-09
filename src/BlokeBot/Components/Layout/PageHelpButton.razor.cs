using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Components;
using BlokeBot.Components.Layout;
using BlokeBot.Eventing;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.History;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostConfig.Page;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Components.Layout;

public partial class PageHelpButton
{
    private bool isOpen;

    private HelpPage? CurrentHelp => HelpForPath(CurrentPath);

    private string CurrentPath
    {
        get
        {
            var relative = Navigation.ToBaseRelativePath(Navigation.Uri);
            var path = relative.Split('?', '#')[0].Trim('/');
            return string.IsNullOrWhiteSpace(path) ? "/" : "/" + path;
        }
    }

    protected override void OnInitialized()
    {
        Navigation.LocationChanged += OnLocationChanged;
    }

    public void Dispose()
    {
        Navigation.LocationChanged -= OnLocationChanged;
    }

    private void Close() => isOpen = false;

    private void Toggle() => isOpen = !isOpen;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _ = InvokeAsync(() =>
        {
            isOpen = false;
            StateHasChanged();
        });
    }

    private static HelpPage? HelpForPath(string path) =>
        path switch
        {
            "/" => HomeHelp,
            "/guessing" => GuessingDashboardHelp,
            "/guessing/settings" => GuessingSettingsHelp,
            "/points" => PointsDashboardHelp,
            "/points/settings" => PointsSettingsHelp,
            "/host" => HostConfigHelp,
            _ => null,
        };

    private static readonly string[] TemplateVariableItems =
    [
        "<strong>Start reply</strong>: <code>{round}</code>, <code>{options}</code>",
        "<strong>Guess option reply</strong>: <code>{name}</code>, <code>{login}</code>",
        "<strong>Invalid guess reply</strong>: <code>{name}</code>, <code>{login}</code>",
        "<strong>Guess usage reply</strong>: <code>{command}</code>",
        "<strong>Available guesses reply</strong>: <code>{round}</code>, <code>{options}</code>",
        "<strong>Win usage reply</strong>: <code>{command}</code>",
        "<strong>Winner and no-winners replies</strong>: <code>{name}</code>, <code>{winners}</code>, <code>{count}</code>",
        "<strong>Stop, closed, no-open-round, already-open, and moderator-only replies</strong>: no variables",
    ];

    private static readonly HelpPage HomeHelp = new(
        "Home",
        [
            new(
                "Application home",
                "Home is the top-level entry point for BlokeBot operations and links to the available application areas.",
                []
            ),
            new(
                "Channel access",
                "Application areas appear when the current authorization can access the related hosted channel or admin capability.",
                []
            ),
        ]
    );

    private static readonly HelpPage HostConfigHelp = new(
        "Host config",
        [
            new(
                "Channel setup",
                "Create your hosted channel, authorize the bot for chat, and start or stop the bot on your stream.",
                []
            ),
            new(
                "Moderator access",
                "Moderator access is enabled by default. Use the allow list to limit access to named moderators, or the block list to deny specific moderators.",
                []
            ),
        ]
    );

    private static readonly HelpPage GuessingDashboardHelp = new(
        "Guessing game dashboard",
        [
            new(
                "Live rounds",
                "Start a round type, stop new guesses, then declare the winning name. Players can only keep their first valid guess in a round.",
                [
                    "<code>!guess &lt;name&gt;</code> records a player guess.",
                    "<code>!guesses</code> lists the available guesses for the current round type.",
                    "<code>!win &lt;name&gt;</code> closes the round and announces winners.",
                    "Start, stop, win, guess, and guesses command words can be changed in Configuration.",
                ]
            ),
            new(
                "History",
                "Use the History tab to filter previous results by date range, round type, and username.",
                []
            ),
        ]
    );

    private static readonly HelpPage GuessingSettingsHelp = new(
        "Guessing game configuration",
        [
            new(
                "Round types",
                "Create one or more round types. Each round type has its own valid guesses and reply text.",
                []
            ),
            new(
                "Commands",
                "Aliases control the chat commands players and moderators type. Moderator commands are start, stop, and win.",
                []
            ),
            new(
                "Template variables",
                "Use these placeholders in reply templates.",
                TemplateVariableItems
            ),
        ]
    );

    private static readonly HelpPage PointsDashboardHelp = new(
        "Points dashboard",
        [
            new(
                "Balances",
                "Use the leaderboard and lookup controls to inspect stored BlokeBot point balances for the selected channel.",
                [
                    "Give and remove accept whole numbers, percentages such as <code>50%</code>, or <code>all</code>.",
                    "Add creates new points and accepts whole numbers only.",
                ]
            ),
            new(
                "Giveaways",
                "Start, end, or cancel the configured giveaway. Giveaways only start while the selected channel is live.",
                [
                    "Entrants can join once.",
                    "Winners are drawn from eligible entrants and receive a random multiple of 10 within the configured payout range.",
                ]
            ),
        ]
    );

    private static readonly HelpPage PointsSettingsHelp = new(
        "Points configuration",
        [
            new(
                "Commands",
                "Aliases are the command words used in chat. Each alias must be unique across bot functions for this channel.",
                [
                    "<code>points</code> shows the caller's balance. With a login, it is moderator-only.",
                    "<code>givepoints</code>, <code>gamble</code>, and <code>removepoints</code> accept whole numbers, percentages, or <code>all</code>.",
                    "<code>addpoints</code> creates points and accepts whole numbers only.",
                ]
            ),
            new(
                "Reply variables",
                "Use placeholders to include live command values in chat replies.",
                [
                    "Balance and gambling replies: <code>{user}</code>, <code>{balance}</code>, <code>{amount}</code>, <code>{label}</code>.",
                    "Transfer replies: <code>{from}</code>, <code>{to}</code>, <code>{amount}</code>, <code>{label}</code>.",
                    "Giveaway replies: <code>{user}</code>, <code>{winners}</code>, <code>{time_left}</code>, <code>{label}</code>.",
                ]
            ),
            new(
                "Follower eligibility",
                "Follower-only giveaways require the bot account to have the follower-read scope and to be a moderator for the channel.",
                []
            ),
        ]
    );

    private sealed record HelpPage(string Title, IReadOnlyList<HelpSection> Sections);

    private sealed record HelpSection(string Title, string Body, IReadOnlyList<string> Items);
}
