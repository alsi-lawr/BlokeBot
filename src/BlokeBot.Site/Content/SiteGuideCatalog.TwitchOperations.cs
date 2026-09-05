namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateTwitchOperationPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/twitch-operations",
            Eyebrow = "Native Twitch",
            Title = "Use Twitch channel tools",
            Summary = "Use Twitch tools for the selected channel.",
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "You can run polls.",
                        "You can save live moments.",
                        "You can manage rewards.",
                        "You can settle Predictions.",
                        "Its saved templates remain for the next time you turn it on.",
                        "Its saved settings remain for the next time you turn it on.",
                        "Its saved history remains for the next time you turn it on.",
                        "Polls.",
                        "Clips & markers.",
                        "Rewards & redemptions.",
                        "Predictions.",
                    ],
                    Heading = "Enable Twitch tools",
                    LegacyAnchor = "turn-native-twitch-on",
                    Steps =
                    [
                        "Select the channel in the top bar.",
                        "Open Channel setup.",
                        "Open Chat tools.",
                        "Turn on each required feature from the list below.",
                        "Open Native Twitch in the Chat tools navigation.",
                        "Select the tool that you need.",
                    ],
                    Paragraphs =
                    [
                        "Each Twitch tool has an independent switch. Each feature card saves its change immediately. If a tool is off, BlokeBot hides its page and stops its automatic work.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Follow the action on the page",
                    Bullets =
                    [
                        "Polls use the selected channel's Twitch connection.",
                        "Clips use the selected channel's Twitch connection.",
                        "Markers use the selected channel's Twitch connection.",
                        "Rewards use the selected channel's Twitch connection.",
                        "Redemptions use the selected channel's Twitch connection.",
                        "Predictions use the selected channel's Twitch connection.",
                        "Rewards and Predictions require a Twitch Affiliate or Partner channel.",
                        "For help, use the ? button beside a page title. The help keeps you on the current task.",
                    ],
                    Note =
                        "If a page asks you to reconnect, select Reconnect to Twitch. Complete Twitch authorization as the selected channel owner. A bot-account reconnection does not repair a channel connection. A channel reconnection does not repair the bot account.",
                },
                new SiteGuideSection
                {
                    Heading = "Uncertain results",
                    LegacyAnchor = "when-a-result-is-uncertain",
                    Steps =
                    [
                        "Read the result on the page before you repeat the action.",
                        "Reload the same page to check Twitch's current state and recent results.",
                        "Open Alerts if the page still needs attention.",
                        "Send the page name to the server owner.",
                        "Send the selected channel to the server owner.",
                        "Send the approximate time to the server owner.",
                        "Send the alert text to the server owner.",
                        "Never send Twitch tokens or secrets.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a poll", "twitch-operations/polls")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/polls",
            Eyebrow = "Native Twitch · Polls",
            Title = "Ask viewers a question",
            Summary = "Manage polls.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/native-twitch/phone-dark-native-polls.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-polls.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-polls.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-polls.png",
                PhoneAlt: "The BlokeBot Polls page shows a saved question and current vote totals. Poll controls are visible.",
                LaptopAlt: "The BlokeBot Polls page shows a saved question and current vote totals. Poll controls are visible.",
                "Saved questions and the active poll stay together. Recent results stay on the same page."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Save reusable poll questions.",
                        "When you need a poll, start it.",
                        "Watch the live vote totals.",
                    ],
                    Heading = "Save a poll question",
                    Paragraphs = ["The saved question appears in Run a poll."],
                    Steps =
                    [
                        "Open New poll template.",
                        "Enter the question and choices.",
                        "Set the vote duration.",
                        "Choose whether viewers can spend Channel Points on extra votes.",
                        "Select Save template.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run the poll",
                    Steps =
                    [
                        "Select Start poll beside the saved question you want to use.",
                        "Watch the choices and vote totals in the active poll.",
                        "Let Twitch finish it at the end of the duration, or select End poll to finish early.",
                    ],
                    Paragraphs =
                    [
                        "Twitch allows one active poll. A poll from another source appears here after a reload. Check its question before you end it.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "If the poll is unavailable",
                    Bullets =
                    [
                        "Use Reconnect to Twitch on this page.",
                        "Complete Twitch authorization as the selected channel owner.",
                        "Finish the active poll before you start another.",
                        "If the displayed totals or result can be stale, reload before you repeat an action.",
                    ],
                },
            ],
            Next = [new SiteLink("Save a clip or marker", "twitch-operations/clips-markers")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/clips-markers",
            Eyebrow = "Native Twitch · Clips & markers",
            Title = "Save a live moment",
            Summary =
                "Create a shareable clip now or leave a private marker to find in the stream recording later.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/native-twitch/phone-dark-native-clips-markers.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-clips-markers.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-clips-markers.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-clips-markers.png",
                PhoneAlt: "The BlokeBot Clips and markers page shows clip creation and stream marker controls. Recent outcomes are also visible.",
                LaptopAlt: "The BlokeBot Clips and markers page shows clip creation and stream marker controls. Recent outcomes are also visible.",
                "Create a clip immediately or add a marker for the selected live channel."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Create a clip",
                    Steps =
                    [
                        "While the selected channel is live, open Clips & markers.",
                        "Choose whether the clip must include the stream delay.",
                        "Select Create clip once.",
                        "When Twitch completes clip preparation, open the clip from Clips and markers.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Place a stream marker",
                    Steps =
                    [
                        "Open Place a stream marker.",
                        "Add a short description.",
                        "Select Create marker.",
                        "Find it later in the selected channel's stream recording.",
                    ],
                    Note =
                        "Markers need an active live stream with stream recordings enabled. Twitch can reject them during reruns or premieres.",
                },
                new SiteGuideSection
                {
                    Heading = "Check an unfinished attempt",
                    Bullets =
                    [
                        "If Twitch still prepares the result or the first result was uncertain, use Check status or Check outcome.",
                        "Do not make another clip or marker because Twitch takes time. A new check uses the recorded attempt.",
                        "If the page asks for the selected channel connection, use Reconnect to Twitch.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Manage rewards and redemptions", "twitch-operations/channel-points"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/channel-points",
            Eyebrow = "Native Twitch · Rewards & redemptions",
            Title = "Manage rewards and viewer requests",
            Summary = "Manage Channel Points rewards.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/native-twitch/phone-dark-native-channel-points.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-channel-points.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-channel-points.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-channel-points.png",
                PhoneAlt: "The BlokeBot Rewards and redemptions page shows waiting requests and reward controls. Age indicators are visible.",
                LaptopAlt: "The BlokeBot Rewards and redemptions page shows waiting requests and reward controls. Age indicators are visible.",
                "Waiting requests appear first. Visible age cues identify requests near the stale threshold."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Answer waiting requests first",
                    Steps =
                    [
                        "Open Unfulfilled redemptions.",
                        "Read the reward.",
                        "Read the viewer input.",
                        "Read the waiting age.",
                        "If the request is complete, select Fulfil.",
                        "If the viewer must receive points back, select Cancel & refund.",
                    ],
                    Bullets =
                    [
                        "Respond to redemptions that wait for completion.",
                        "Manage BlokeBot rewards.",
                        "Create the next reward for your viewers.",
                        "Blue means the request is under 2 minutes old.",
                        "Amber means that the request age is from 2 minutes to under 5 minutes.",
                        "Red means that the request age is 5 minutes or more and needs attention.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage or create a reward",
                    Bullets =
                    [
                        "Here, you can edit rewards that BlokeBot created.",
                        "Here, you can enable rewards that BlokeBot created.",
                        "Here, you can pause rewards that BlokeBot created.",
                        "Here, you can delete rewards that BlokeBot created.",
                        "BlokeBot shows rewards created elsewhere as read-only. It does not take ownership of them.",
                        "Create a reward appears after the waiting requests and current reward list. Set its cost and viewer instructions. Select Create reward.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "When rewards are unavailable",
                    Bullets =
                    [
                        "Channel Points rewards require a Twitch Affiliate or Partner channel.",
                        "If BlokeBot needs the selected channel's permission, use Reconnect to Twitch on this page.",
                        "If the Twitch result is unclear, reload before you repeat a fulfillment or refund. Then check Redemption history.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a Prediction", "twitch-operations/predictions")],
        };

        yield return new SiteGuidePage
        {
            Route = "/twitch-operations/predictions",
            Eyebrow = "Native Twitch · Predictions",
            Title = "Run and settle a Prediction",
            Summary = "Manage Predictions.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/native-twitch/phone-dark-native-predictions.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-predictions.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-predictions.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-predictions.png",
                PhoneAlt: "The BlokeBot Predictions page shows a saved question and outcomes. Controls for the active Prediction are visible.",
                LaptopAlt: "The BlokeBot Predictions page shows a saved question and outcomes. Controls for the active Prediction are visible.",
                "The active Prediction stays above reusable templates and recent settled results."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Save reusable Prediction questions.",
                        "Open Channel Points entries.",
                        "Then select the winner or refund everyone.",
                    ],
                    Heading = "Save and start a Prediction",
                    Steps =
                    [
                        "Open New Prediction template.",
                        "Enter the question.",
                        "Enter the possible outcomes.",
                        "Enter the entry time.",
                        "Select Save template.",
                        "Select Start Prediction beside the saved question.",
                        "Check the active question and outcome totals while viewers choose with Channel Points.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Before resolution or a refund, check the selected channel.",
                        "Check the question.",
                        "Check the real result.",
                    ],
                    Heading = "Close and settle it",
                    Paragraphs = ["Twitch returns the viewers' Channel Points."],
                    Steps =
                    [
                        "When viewers must no longer enter, select Lock.",
                        "After you know the real result, select Resolve as winner beside the correct outcome.",
                        "Select Cancel & refund only when you cannot settle the Prediction.",
                    ],
                    Note = "You cannot undo resolution or a refund.",
                },
                new SiteGuideSection
                {
                    Heading = "If the Prediction needs attention",
                    Bullets =
                    [
                        "Predictions require a Twitch Affiliate or Partner channel.",
                        "A Prediction started elsewhere appears here after reload.",
                        "Inspect the active Prediction before you lock it.",
                        "Inspect the active Prediction before you refund it.",
                        "Inspect the active Prediction before you resolve it.",
                        "If this page asks for the selected channel connection, use Reconnect to Twitch.",
                        "If the Twitch state is uncertain, wait a moment. Before you start anything new, reload.",
                    ],
                },
            ],
            Next = [new SiteLink("Return to Native Twitch help", "twitch-operations")],
        };
    }
}
