namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateAutomationCatalogPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/automations/events",
            Eyebrow = "Automations · Twitch events",
            Title = "Start automations from Twitch activity",
            Summary =
                "The Twitch events page lists each automation source for the selected channel. It shows the required Twitch approval and current use.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Read the source list",
                    Bullets =
                    [
                        "Open Automations, then Twitch events. Each source shows its Twitch subscription, the required approval and whether an enabled flow uses it today.",
                        "Ready means the source can start flows now. Reconnect needed and Twitch connection needed mean that the source stays inactive. BlokeBot creates no Twitch subscription and starts no flow.",
                        "Use Reconnect to Twitch on this page and complete Twitch as the selected channel owner to approve the required permissions.",
                        "A source's Twitch subscription follows the bot runtime and exists only while an enabled flow uses that source.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Stream, community and Hype Train events",
                    Bullets =
                    [
                        "Stream went live, Stream went offline, New follower and Incoming raid need no approval beyond the channel's bot connection.",
                        "New subscription and Gifted subscriptions need the channel's subscription-reading permission. Cheer needs Bits reading. The three Hype Train events need Hype Train reading. The page names each required approval exactly.",
                        "The Gifted subscriptions source uses a minimum gift count. Cheer uses a minimum Bits amount. Incoming raid uses a minimum viewer count. Smaller events do not start a flow.",
                        "Chat notification starts flows from typed Twitch notices such as announcements, resubs, gift upgrades and charity donations. You choose the notification type. Ordinary chat messages never start automations.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Channel Points redemptions",
                    Bullets =
                    [
                        "The Channel Points redemption source starts a flow when a viewer redeems a Custom Reward. It needs the channel's redemption permissions and a Twitch Affiliate or Partner channel.",
                        "A reward filter limits the source to one Custom Reward. Without it, every redemption starts the flow.",
                        "The completion policy controls the redemption status after the flow. Keep it manual, fulfil it after success or cancel it after failure. Cancellation refunds the viewer.",
                        "Automatic completion applies only to rewards BlokeBot can manage. Redemptions of rewards created elsewhere still start flows, but their status stays untouched.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "Manage rewards and redemptions",
                            "twitch-operations/channel-points"
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Shoutout, poll and Prediction events",
                    Bullets =
                    [
                        "Shoutout sent and Shoutout received follow the bot account's moderator approvals and appear only while Raid & collaboration is on.",
                        "Poll started, Poll progressed and Poll ended need the channel's poll-reading permission and appear only while Polls is on.",
                        "Prediction events need the channel's Prediction-reading permission. They appear only while Predictions is on.",
                        "These sources report all polls and Predictions. This includes operations from outside BlokeBot.",
                    ],
                    Links = [new SiteLink("Set up Native Twitch", "twitch-operations")],
                },
                new SiteGuideSection
                {
                    Heading = "Start flows from a custom command",
                    Paragraphs =
                    [
                        "The Custom command source starts a flow after chat uses a selected custom command. It provides the viewer and command text. Create the command and choose Run automation flow under What happens. Custom commands and Automations must both be on.",
                    ],
                    Links = [new SiteLink("Create Custom Commands", "commands")],
                },
                new SiteGuideSection
                {
                    Heading = "When events do not arrive",
                    Bullets =
                    [
                        "Check the source badge first. For an inactive source, the badge names the required approval or connection.",
                        "BlokeBot does not replay events from an inactive source, a disabled flow or a period when Automations was off.",
                        "BlokeBot recognizes a repeated Twitch delivery inside ten minutes and starts nothing extra. This is not a lost event.",
                        "If the page cannot load, retry from the message shown. Your saved automations remain unchanged.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Choose what automations do", "automations/actions"),
                new SiteLink("Return to the Automations overview", "automations"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/automations/actions",
            Eyebrow = "Automations · Actions",
            Title = "Choose what an automation does",
            Summary =
                "Actions send chat, play overlay cues, complete Channel Points redemptions and run Native Twitch operations. Each action obeys its feature switches and Twitch limits.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Send chat and play overlay cues",
                    Bullets =
                    [
                        "Send chat message sends up to 500 characters in the channel. The message can include automation variables from the source event.",
                        "Play overlay cue plays a saved Cue through a chosen Cue player Browser Source. Enable the cue, Cue player and Overlays feature for playback.",
                        "A replaced or deleted cue or Cue player makes the action fail. BlokeBot does not play a substitute.",
                    ],
                    Links = [new SiteLink("Build reusable Cues", "overlays/cues")],
                },
                new SiteGuideSection
                {
                    Heading = "Complete Channel Points redemptions",
                    Bullets =
                    [
                        "Fulfil redemption marks the source Channel Points redemption as fulfilled. Cancel redemption cancels it so Twitch refunds the viewer's points.",
                        "Both actions apply only to the redemption that started the flow. The redemption must have the Unfulfilled state and use a reward that BlokeBot manages.",
                        "Prefer the redemption source's completion policy for whole-flow outcomes. Use these actions when a flow must settle the redemption at a specific step.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run native Twitch operations",
                    Paragraphs =
                    [
                        "Nine actions run the same operations as the Native Twitch pages. They use the channel connection and each operation's switch and requirements.",
                    ],
                    Bullets =
                    [
                        "Send shoutout targets the broadcaster who triggered the flow, such as a broadcaster from a raid. The source event must carry a viewer or broadcaster.",
                        "Start poll accepts a question of up to 60 characters. Add 2–5 choices of up to 25 characters. Set a 15-second to 30-minute duration and an optional Channel Points cost per extra vote. If another poll is active, it fails.",
                        "End poll finishes the channel's active poll immediately. A poll started outside BlokeBot is never ended by an automation.",
                        "Create clip captures the live stream immediately or after Twitch's broadcast delay. Create stream marker adds a marker with a description of up to 140 characters.",
                        "Start prediction accepts a question of up to 45 characters. Add 2–10 outcomes of up to 25 characters. Set a 30-second to 30-minute window. If another Prediction is active, it fails.",
                        "Lock prediction stops entries. Cancel prediction refunds all viewer Channel Points. Resolve prediction uses an outcome identifier from a variable or expression.",
                    ],
                    Note =
                        "Rewards and Predictions require a Twitch Affiliate or Partner channel. Each operation follows the prerequisites on its Native Twitch page.",
                    Links = [new SiteLink("Use Native Twitch tools", "twitch-operations")],
                },
                new SiteGuideSection
                {
                    Heading = "Check an action's outcome",
                    Bullets =
                    [
                        "A failed action follows its step's failure choice: stop the flow or continue past the failure.",
                        "BlokeBot does not retry an action with an uncertain Twitch outcome. It never duplicates an action to force an answer.",
                        "The applicable feature page shows the Twitch result. It shows shoutouts, the active poll or Prediction, clips and redemptions.",
                        "If an action continues to fail, fix the named connection, permission or feature switch. Then run the flow again. Alerts collects problems that need attention.",
                    ],
                    Links = [new SiteLink("Troubleshoot the bot", "troubleshooting")],
                },
            ],
            Next =
            [
                new SiteLink("Start flows from Twitch events", "automations/events"),
                new SiteLink("Trigger flows from chat commands", "commands"),
            ],
        };
    }
}
