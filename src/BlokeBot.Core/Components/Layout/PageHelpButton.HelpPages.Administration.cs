namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
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

    private static readonly HelpPage _configurationTransferHelp = new(
        "Configuration transfer",
        [
            new(
                "Move configuration safely",
                "Choose individual sections to export or review an imported file before saving anything.",
                [
                    "The selected channel is always the import destination.",
                    "Each section has its own add, merge or replace policy.",
                    "Runtime history, balances and credentials are never transferred.",
                ]
            ),
            new(
                "Chat Tools enablement",
                "Enablement changes are selected separately from feature configuration.",
                [
                    "Importing configuration never turns that feature on implicitly.",
                    "Disabled configuration remains saved and disabled.",
                    "Enabling a feature does not replay work suppressed while it was disabled.",
                ]
            ),
        ]
    );
}
