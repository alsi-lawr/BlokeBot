namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateGameAndPointPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/guessing",
            Eyebrow = "Guessing games",
            Title = "Set up and run a guessing game",
            Summary = "Manage Guessing rounds.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/points-and-guessing/phone-dark-guessing-workflow.webp",
                LightPhoneSource: "media/points-and-guessing/phone-light-guessing-workflow.webp",
                DarkLaptopSource: "media/points-and-guessing/laptop-dark-guessing-workflow.webp",
                LightLaptopSource: "media/points-and-guessing/laptop-light-guessing-workflow.webp",
                PhoneAlt: "Animated BlokeBot guessing dashboard that moves through a live round workflow.",
                LaptopAlt: "Animated BlokeBot guessing dashboard that moves through a live round workflow.",
                "The live dashboard keeps round status and votes together. Answers and winner controls stay on the same page."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Create reusable round types and answers.",
                        "Collect one guess per viewer.",
                        "Then record the winning answer.",
                    ],
                    Heading = "Prepare a round type",
                    Steps =
                    [
                        "Turn on Guessing game.",
                        "Open its Settings page.",
                        "Create a round type.",
                        "Add every accepted answer.",
                        "Put comma-separated aliases after its main answer.",
                        "Select a winner point reward.",
                        "Review the chat commands and bot replies.",
                        "Save the settings.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run the round",
                    Steps =
                    [
                        "Open the Guessing game Dashboard.",
                        "Select the round type.",
                        "Start the round.",
                        "Let viewers submit guesses.",
                        "Select Stop guessing.",
                        "Enter the winning answer or one of its aliases.",
                        "Declare the winner.",
                    ],
                    Paragraphs =
                    [
                        "History and Leaderboard keep completed results. Public leaderboards can share rankings without dashboard access.",
                    ],
                },
            ],
            Next = [new SiteLink("Share a leaderboard", "leaderboards")],
        };

        yield return new SiteGuidePage
        {
            Route = "/points",
            Eyebrow = "Viewer points",
            Title = "Set up and manage points",
            Summary =
                "Give each viewer a channel balance. Manage channel points. Staff can adjust balances or award prizes.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/points-and-guessing/phone-dark-points-settings.png",
                LightPhoneSource: "media/points-and-guessing/phone-light-points-settings.png",
                DarkLaptopSource: "media/points-and-guessing/laptop-dark-points-settings.png",
                LightLaptopSource: "media/points-and-guessing/laptop-light-points-settings.png",
                PhoneAlt: "Points settings show the point label and gambling chance. The cooldown and chat command words are also visible.",
                LaptopAlt: "Points settings show the point label and gambling chance. The cooldown and chat command words are also visible.",
                "Points settings define the channel's terminology and gambling rules. They also define the command words."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Viewers can check points.",
                        "Viewers can transfer points.",
                        "Viewers can gamble points.",
                    ],
                    Heading = "Configure points",
                    Steps =
                    [
                        "Turn on Points.",
                        "Open Points Settings.",
                        "Select the point label.",
                        "Select the gambling chance.",
                        "Select the wait between gambles.",
                        "Review command words and bot replies.",
                        "Save the settings.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage balances",
                    Bullets =
                    [
                        "Search for a viewer from the Points Dashboard.",
                        "Check both names and the amount.",
                        "Then choose the point adjustment.",
                        "Adjustment: move points.",
                        "Adjustment: add points.",
                        "Adjustment: take points away.",
                        "Use Recent changes to check adjustments and prizes.",
                        "Use Delete balance only when the whole record must go.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a points giveaway", "giveaways")],
        };

        yield return new SiteGuidePage
        {
            Route = "/giveaways",
            Eyebrow = "Points giveaways",
            Title = "Run a giveaway",
            Summary = "Manage giveaways.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/points-and-guessing/phone-dark-points-settings.png",
                LightPhoneSource: "media/points-and-guessing/phone-light-points-settings.png",
                DarkLaptopSource: "media/points-and-guessing/laptop-dark-points-settings.png",
                LightLaptopSource: "media/points-and-guessing/laptop-light-points-settings.png",
                PhoneAlt: "Points settings page for the configuration of channel point commands and giveaway rules.",
                LaptopAlt: "Points settings page for the configuration of channel point commands and giveaway rules.",
                "Giveaway rules live on the Points settings page alongside the channel's points configuration."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "While the channel is live, open timed entry.",
                        "Select eligibility and the winner count.",
                        "Then award random point prizes.",
                    ],
                    Heading = "Set the rules",
                    Steps =
                    [
                        "Open Points Settings.",
                        "Expand Giveaways.",
                        "Set the entry time.",
                        "Set the prize range.",
                        "Set the winner count.",
                        "Set eligibility.",
                        "Set the wait between giveaways.",
                        "Save the settings before you go live.",
                    ],
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "If Start is unavailable, check stream status.",
                        "Check for an active giveaway.",
                        "Check the cooldown that the dashboard shows.",
                    ],
                    Heading = "Start and finish",
                    Steps =
                    [
                        "While the Twitch channel is live, open the Points Dashboard.",
                        "Select Start in Giveaway.",
                        "Tell viewers to use the configured join command.",
                        "Select End to draw winners and award prizes, or Cancel to stop without winners.",
                    ],
                    Paragraphs = ["Each eligible viewer can enter once."],
                },
            ],
            Next = [new SiteLink("Review points", "points")],
        };

        yield return new SiteGuidePage
        {
            Route = "/leaderboards",
            Eyebrow = "Public results",
            Title = "Share a public leaderboard",
            Summary =
                "Viewers can open read-only guessing or points rankings without permission to manage the channel.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/points-and-guessing/phone-dark-guessing-leaderboard.png",
                LightPhoneSource: "media/points-and-guessing/phone-light-guessing-leaderboard.png",
                DarkLaptopSource: "media/points-and-guessing/laptop-dark-guessing-leaderboard.png",
                LightLaptopSource: "media/points-and-guessing/laptop-light-guessing-leaderboard.png",
                PhoneAlt: "The public guessing leaderboard shows players and correct guesses. It also shows rounds and accuracy.",
                LaptopAlt: "The public guessing leaderboard shows players and correct guesses. It also shows rounds and accuracy.",
                "Public leaderboards turn completed channel activity into a shareable read-only ranking."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Destination: Twitch panels.",
                        "Destination: chat.",
                        "Destination: community pages.",
                    ],
                    Heading = "Open and share it",
                    Steps =
                    [
                        "From Home or the sign-in page, choose Guessing or Points under Public leaderboard.",
                        "Enter the Twitch channel name.",
                        "Open the leaderboard.",
                        "Copy the browser address to the intended destination.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Empty leaderboards",
                    LegacyAnchor = "if-it-is-empty",
                    Bullets =
                    [
                        "Points rankings need viewer balances.",
                        "Guessing rankings need completed rounds.",
                        "The related tool must be on, and the channel must exist in BlokeBot.",
                    ],
                },
            ],
            Next = [new SiteLink("Run a guessing game", "guessing")],
        };
    }
}
