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
            Summary =
                "Create reusable round types and answers, collect one guess per viewer, then record the winning answer.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/points-and-guessing/phone-dark-guessing-workflow.webp",
                LightPhoneSource: "media/points-and-guessing/phone-light-guessing-workflow.webp",
                DarkLaptopSource: "media/points-and-guessing/laptop-dark-guessing-workflow.webp",
                LightLaptopSource: "media/points-and-guessing/laptop-light-guessing-workflow.webp",
                PhoneAlt: "Animated BlokeBot guessing dashboard that moves through a live round workflow.",
                LaptopAlt: "Animated BlokeBot guessing dashboard that moves through a live round workflow.",
                "The live dashboard keeps round status, votes, answers and winner controls together."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Prepare a round type",
                    Steps =
                    [
                        "Turn on Guessing game and open its Settings page.",
                        "Create a round type and add every accepted answer. Put comma-separated aliases after its main answer. Choose a winner point reward.",
                        "Review the chat commands and bot replies, then save.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Run the round",
                    Steps =
                    [
                        "Open the Guessing game Dashboard and choose the round type.",
                        "Start the round, let viewers submit guesses, then select Stop guessing.",
                        "Enter the winning answer or one of its aliases and declare the winner.",
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
                "Give each viewer a channel balance. Viewers can check, transfer or gamble points. Staff can adjust balances or award prizes.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/points-and-guessing/phone-dark-points-settings.png",
                LightPhoneSource: "media/points-and-guessing/phone-light-points-settings.png",
                DarkLaptopSource: "media/points-and-guessing/laptop-dark-points-settings.png",
                LightLaptopSource: "media/points-and-guessing/laptop-light-points-settings.png",
                PhoneAlt: "Points settings with the point label, gambling chance, cooldown and chat command words.",
                LaptopAlt: "Points settings with the point label, gambling chance, cooldown and chat command words.",
                "Points settings define the channel's terminology, gambling rules and command words."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Configure points",
                    Steps =
                    [
                        "Turn on Points and open Points Settings.",
                        "Choose the point label, gambling chance and wait between gambles.",
                        "Review command words and bot replies, then save.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Manage balances",
                    Bullets =
                    [
                        "Search for a viewer from the Points Dashboard.",
                        "Check both names and the amount. Then move points, add points or take points away.",
                        "Use Recent changes to confirm adjustments and prizes. Delete balance only when the whole record must go.",
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
            Summary =
                "Open timed entry while the channel is live, choose eligibility and winner count, then award random point prizes.",
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
                    Heading = "Set the rules",
                    Steps =
                    [
                        "Open Points Settings and expand Giveaways.",
                        "Set entry time, prize range, winner count, eligibility and the wait between giveaways.",
                        "Save the settings before you go live.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Start and finish",
                    Steps =
                    [
                        "While the Twitch channel is live, open the Points Dashboard and select Start in Giveaway.",
                        "Tell viewers to use the configured join command. Each eligible viewer can enter once.",
                        "Select End to draw winners and award prizes, or Cancel to stop without winners.",
                    ],
                    Paragraphs =
                    [
                        "If Start is unavailable, check stream status, an active giveaway and the cooldown shown by the dashboard.",
                    ],
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
                PhoneAlt: "Public guessing leaderboard that shows players, correct guesses, rounds and accuracy.",
                LaptopAlt: "Public guessing leaderboard that shows players, correct guesses, rounds and accuracy.",
                "Public leaderboards turn completed channel activity into a shareable read-only ranking."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Open and share it",
                    Steps =
                    [
                        "From Home or the sign-in page, choose Guessing or Points under Public leaderboard.",
                        "Enter the Twitch channel name and open the leaderboard.",
                        "Copy the browser address into Twitch panels, chat or community pages.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "If it is empty",
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
