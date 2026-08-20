namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
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
}
