namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
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
                "Each channel controls its own media links.",
                [
                    "A shared file stays available while any channel still links to it.",
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
}
