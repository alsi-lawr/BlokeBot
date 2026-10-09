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
                "Watch-time points",
                "Points for people connected to chat, not measured video watch time.",
                [
                    "Choose a positive whole amount and save with watch-time points on. It starts off with no amount.",
                    "The first award waits a full five minutes after enabling or restarting the bot. Later intervals are every five minutes while the stream is live and the bot is connected.",
                    "Each interval uses the complete current chat membership, including lurkers and the broadcaster. Only the active bot account is excluded. Someone who just joined gets the full amount; absent people get none.",
                    "Offline, disconnected, or unavailable intervals are skipped without partial or catch-up points. Later fresh intervals recover automatically; the settings status shows when data is unavailable.",
                    "Points and the normal ledger update without award announcements, chat messages, or a new gain trigger.",
                    "Applying an older Points import without watch-time settings turns this off and clears its amount.",
                ]
            ),
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
                    "Nest existing tokens in random values or bounds, for example <code>{random_between|1|{arg1}}</code> or <code>{random_from|Hello {user}|{random_between|1|10}}</code>.",
                    "Command arguments are <code>{arg1}</code> through <code>{arg9}</code>; <code>{args}</code> is their joined text.",
                    "Number bounds must be ordered whole numbers from -2147483648 to 2147483647. Negative and equal bounds work.",
                    "If a resolved bound is missing, not a whole number, out of range or reversed, the bot sends an explanation instead of that reply.",
                    "Argument and chatter text stays text, even when it contains braces or pipes.",
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
                "Saved values",
                "",
                [
                    "Open Variables to declare and edit scoped Number or Text values and mixed dictionaries.",
                    "Use <code>{var_get|user|hugs}</code>, <code>{var_set|user|bio|{args}}</code> or <code>{var_inc|user|hugs|1}</code>.",
                    "Use <code>{dict_get|user|profile|{arg1}}</code> for a nested dictionary key.",
                    "Turn on Whole remaining text is one argument for a command such as !bio. Its one-argument reply gets the whole literal phrase in <code>{arg1}</code> and <code>{args}</code>. No text uses the zero-argument reply.",
                    "The token picker and sandbox preview use copies. They do not save live values, claim invocations or advance saved variants.",
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

    private static readonly HelpPage _variablesHelp = new(
        "Variables",
        [
            new(
                "Scopes",
                "",
                [
                    "User scope keeps a separate value for each stable Twitch viewer ID in this channel.",
                    "Global scope keeps one value for this channel.",
                    "Other channels have separate definitions and values. A changed viewer login does not change the saved values.",
                ]
            ),
            new(
                "Manual changes",
                "",
                [
                    "Select a variable or dictionary. For User scope, enter a viewer login and select Select viewer.",
                    "Change the saved value. Select Save changes.",
                    "For a dictionary, select Edit beside an entry. To add an entry, select Add entry. Enter its key. Select Open entry.",
                    "Select Number or Text for the entry. Save changes affects only this target.",
                    "Reset returns one variable value to its default. Delete entry removes one key. Confirm the affected target.",
                    "A stale save is rejected. Your edit stays on screen. Reload the current value before you save again.",
                    "Definitions have fixed scope and type. Save definition changes the name or default, not saved values. Delete definition removes its saved values across viewers. Commands that reference that definition then fail.",
                ]
            ),
            new(
                "Defaults and types",
                "",
                [
                    "Get uses a variable's default until you save a value. Dictionary Get returns the missing entry value. It does not create an entry.",
                    "Set keeps an existing entry's type. Set creates an absent dictionary entry as Text.",
                    "Increment requires Number. An absent dictionary entry starts at zero. Number values are signed 64-bit whole numbers.",
                    "Saved values have no automatic expiry. Explicitly reset or delete data to remove it.",
                    "Definitions, defaults and the single-argument setting transfer in configuration packages. Live values, dictionary entries and invocation results do not transfer. Import does not reset existing values by omission.",
                ]
            ),
            new(
                "Nested tokens",
                "",
                [
                    "Use <code>{dict_get|user|profile|{arg1}}</code> to get a key from the first argument.",
                    "Use <code>{dict_set|user|profile|{arg1}|{arg2}}</code> to save text. Use <code>{dict_inc|user|scores|{arg1}|{arg2}}</code> to add a number.",
                    "The inner token resolves first. Selected occurrences resolve once, from left to right. Later reads see earlier writes. Unselected random choices do not write.",
                    "Saved text and argument text remain literal. Braces and pipes in that text do not create tokens.",
                    "If a token cannot resolve, no token changes, claims or variant advances are saved. A chat delivery failure after commit keeps the changes and computed reply. A retry of the same message ID reuses that reply.",
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
                    "Right-click a node or connection, or use ⋯ for Undo, Redo and Delete selected. A selected node keeps its selected set. Context Menu or Shift+F10 opens actions; Escape closes the menu. Delete selected is undoable, not permanent flow deletion. Text fields keep browser menus.",
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
