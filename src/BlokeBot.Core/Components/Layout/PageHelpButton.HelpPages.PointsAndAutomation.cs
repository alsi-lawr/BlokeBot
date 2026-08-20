namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
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
                    "Drag an output port to a compatible input port or node.",
                    "Select a node to open its inspector from the right.",
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
}
