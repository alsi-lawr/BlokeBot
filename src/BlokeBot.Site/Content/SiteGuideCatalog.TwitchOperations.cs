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
            Summary =
                "Run polls, save live moments, manage rewards and settle Predictions for the selected channel.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Turn Native Twitch on",
                    Steps =
                    [
                        "Choose the channel in the top bar and open Channel setup.",
                        "Open Chat tools and turn on Native Twitch. The feature card persists the change immediately.",
                        "Open Native Twitch in the Chat tools navigation, then choose Polls, Clips & markers, Rewards & redemptions or Predictions.",
                    ],
                    Paragraphs =
                    [
                        "If Native Twitch is off, BlokeBot hides these pages and stops its automatic work. Saved templates, settings and history remain for the next time you turn it on.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Follow the action on the page",
                    Bullets =
                    [
                        "Polls, clips, markers, rewards, redemptions and Predictions use the selected channel's Twitch connection.",
                        "Rewards and Predictions require a Twitch Affiliate or Partner channel.",
                        "Use the ? button beside a page title for help and stay on the current task.",
                    ],
                    Note =
                        "If a page asks you to reconnect, select Reconnect to Twitch. Complete Twitch as the selected channel owner. A bot-account reconnection does not repair a channel connection. A channel reconnection does not repair the bot account.",
                },
                new SiteGuideSection
                {
                    Heading = "When a result is uncertain",
                    Steps =
                    [
                        "Read the result on the page before you repeat the action.",
                        "Reload the same page to check Twitch's current state and recent results.",
                        "Open Alerts if the page still needs attention.",
                        "Send the page name, selected channel, approximate time and alert text to the server owner. Never send Twitch tokens or secrets.",
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
            Summary =
                "Save reusable poll questions, start one when you need it and watch the live vote totals.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/native-twitch/phone-dark-native-polls.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-polls.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-polls.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-polls.png",
                PhoneAlt: "BlokeBot Polls page that shows a saved question, current vote totals and poll controls.",
                LaptopAlt: "BlokeBot Polls page that shows a saved question, current vote totals and poll controls.",
                "Saved questions, the active poll and recent results stay together."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Save a poll question",
                    Steps =
                    [
                        "Open New poll template and enter the question and choices.",
                        "Set the vote duration. Choose whether viewers can spend Channel Points on extra votes.",
                        "Select Save template. The saved question appears in Run a poll.",
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
                        "Use Reconnect to Twitch on this page and complete Twitch as the selected channel owner.",
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
                PhoneAlt: "BlokeBot Clips and markers page that shows clip creation, stream marker and recent outcome controls.",
                LaptopAlt: "BlokeBot Clips and markers page that shows clip creation, stream marker and recent outcome controls.",
                "Create a clip immediately or add a marker for the selected live channel."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Create a clip",
                    Steps =
                    [
                        "Open Clips & markers while the selected channel is live.",
                        "Choose whether the clip must include the stream delay. Select Create clip once.",
                        "When Twitch completes clip preparation, open the clip from Clips and markers.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Place a stream marker",
                    Steps =
                    [
                        "Open Place a stream marker and add a short description.",
                        "Select Create marker. Find it later in the selected channel's stream recording.",
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
                        "Use Reconnect to Twitch if the page asks for the selected channel connection.",
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
            Summary =
                "Respond to waiting redemptions, manage BlokeBot rewards and create the next reward for your viewers.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/native-twitch/phone-dark-native-channel-points.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-channel-points.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-channel-points.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-channel-points.png",
                PhoneAlt: "BlokeBot Rewards and redemptions page that shows waiting requests, reward controls and age indicators.",
                LaptopAlt: "BlokeBot Rewards and redemptions page that shows waiting requests, reward controls and age indicators.",
                "Waiting requests appear first. Visible age cues identify requests near the stale threshold."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Answer waiting requests first",
                    Steps =
                    [
                        "Open Unfulfilled redemptions and read the reward, viewer input and waiting age.",
                        "If the request is complete, select Fulfil. If the viewer must receive points back, select Cancel & refund.",
                    ],
                    Bullets =
                    [
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
                        "Here, you can edit, enable, pause or delete rewards that BlokeBot created.",
                        "Rewards created elsewhere are shown read-only so BlokeBot does not take ownership of them.",
                        "Create a reward appears after the waiting requests and current reward list. Set its cost and viewer instructions, then choose Create reward.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "When rewards are unavailable",
                    Bullets =
                    [
                        "Channel Points rewards require a Twitch Affiliate or Partner channel.",
                        "Use Reconnect to Twitch on this page when BlokeBot needs the selected channel's permission.",
                        "If the Twitch result is unclear, reload before you repeat a fulfil or refund. Then check Redemption history.",
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
            Summary =
                "Save reusable Prediction questions, open Channel Points entries, then choose the winner or refund everyone.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/native-twitch/phone-dark-native-predictions.png",
                LightPhoneSource: "media/native-twitch/phone-light-native-predictions.png",
                DarkLaptopSource: "media/native-twitch/laptop-dark-native-predictions.png",
                LightLaptopSource: "media/native-twitch/laptop-light-native-predictions.png",
                PhoneAlt: "BlokeBot Predictions page that shows a saved question, outcomes and controls for the active Prediction.",
                LaptopAlt: "BlokeBot Predictions page that shows a saved question, outcomes and controls for the active Prediction.",
                "The active Prediction stays above reusable templates and recent settled results."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Save and start a Prediction",
                    Steps =
                    [
                        "Open New Prediction template and enter the question, possible outcomes and entry time.",
                        "Select Save template, then select Start Prediction beside the saved question.",
                        "Check the active question and outcome totals while viewers choose with Channel Points.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Close and settle it",
                    Steps =
                    [
                        "When viewers must no longer enter, select Lock.",
                        "After the real result is known, select Resolve as winner beside the correct outcome.",
                        "Select Cancel & refund only when you cannot settle the Prediction. Twitch returns the viewers' Channel Points.",
                    ],
                    Note =
                        "Resolution and refund cannot be undone. Confirm the selected channel, question and real result before you choose either action.",
                },
                new SiteGuideSection
                {
                    Heading = "If the Prediction needs attention",
                    Bullets =
                    [
                        "Predictions require a Twitch Affiliate or Partner channel.",
                        "A Prediction started elsewhere appears here after reload. Inspect it before you lock, refund or resolve it.",
                        "Use Reconnect to Twitch if this page asks for the selected channel connection.",
                        "If the Twitch state is uncertain, wait a moment and reload before you start anything new.",
                    ],
                },
            ],
            Next = [new SiteLink("Return to Native Twitch help", "twitch-operations")],
        };
    }
}
