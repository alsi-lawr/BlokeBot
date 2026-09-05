namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommunityBingoPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/bingo",
            Eyebrow = "Community progression · Bingo",
            Title = "Run Stream-event Bingo",
            Summary = "Manage Bingo.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/progression/phone-dark-bingo-public-card.png",
                LightPhoneSource: "media/community/progression/phone-light-bingo-public-card.png",
                DarkLaptopSource: "media/community/progression/laptop-dark-bingo-setup.png",
                LightLaptopSource: "media/community/progression/laptop-light-bingo-setup.png",
                PhoneAlt: "The Sample Channel public Bingo page shows a team card and participant Twitch logins. The narrow screen shows a horizontal-scroll indicator.",
                LaptopAlt: "The Sample Channel Bingo management page shows a template revision and Shared board mode. The seed and participant cap are visible. Open viewer is available.",
                "Hosts open a game from a saved template revision. Participants see the frozen card assignment and public identity boundary."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Issue deterministic cards.",
                        "Mark supported stream moments.",
                        "Keep public evidence and rewards available for review.",
                        "A channel owner or permitted moderator manages templates.",
                        "A channel owner or permitted moderator manages games.",
                        "A channel owner or permitted moderator manages rosters.",
                        "A channel owner or permitted moderator manages cards.",
                        "A channel owner or permitted moderator manages manual marks.",
                        "A channel owner or permitted moderator manages archives.",
                        "Card assignment: shared.",
                        "Card assignment: viewer.",
                        "Card assignment: team.",
                    ],
                    Heading = "Enable the required tools",
                    Paragraphs =
                    [
                        "The feature card saves the change immediately.",
                        "Viewers join or leave before issue and follow cards in chat or on the public page.",
                    ],
                    Steps =
                    [
                        "Select the channel.",
                        "Open Channel setup.",
                        "Turn on Bingo under Chat tools.",
                        "Before you open a game with point rewards, turn on Points.",
                        "Turn on Community progression.",
                        "Predeclare a viewer achievement that accepts external grants.",
                        "If the Bingo win must unlock a title, attach a title reward to that achievement.",
                    ],
                    Note =
                        "A Stream category changed square also needs the selected channel's Twitch integration. Bingo owns the host-scoped channel.update subscription and keeps it absent while Bingo is off.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "The line reward applies to row wins.",
                        "The line reward applies to column wins.",
                        "The line reward applies to diagonal wins.",
                        "Supported source configuration can be a threshold.",
                        "Supported source configuration can be a counter.",
                        "Supported source configuration can be a filter.",
                        "Square source: Giveaway started.",
                        "Square source: Stream category changed.",
                        "Square source: Counter reached.",
                        "Square source: Manual confirmation.",
                        "Square source: Incoming raid.",
                        "Square source: Bounty completed.",
                        "Square source: Guessing result.",
                        "Grid size: 3 × 3.",
                        "Grid size: 4 × 4.",
                        "Grid size: 5 × 5.",
                        "Later template edits do not alter frozen cards.",
                        "Later template edits do not alter square positions.",
                        "Later template edits do not alter win lines.",
                        "The issued card keeps the saved dimension.",
                        "The issued card keeps the template revision.",
                        "The issued card keeps the recorded seed.",
                        "The issued card keeps the assignment identity.",
                    ],
                    Heading = "Build a deterministic template",
                    Steps =
                    [
                        "Select a grid size.",
                        "Provide enough squares to fill the grid.",
                        "Give every square a public title and stable key.",
                        "Choose a square source from the supported options.",
                        "Alternatively, choose one of the other square sources.",
                        "Set only the configuration that the typed source supports.",
                        "Put subjective staff guidance in Private moderator note.",
                        "Configure the line reward.",
                        "If you want a full-card win, enable it and its reward.",
                        "Save the revision.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Open entry and freeze the roster",
                    Bullets =
                    [
                        "Select a card mode.",
                        "Card mode: Shared board.",
                        "Card mode: Unique per viewer.",
                        "Card mode: Teams.",
                        "Enter a recorded seed.",
                        "If necessary, set a host participant cap. Team games also permit a host team cap and team names.",
                        "There is no product-wide participant cap. With no host cap, every joined viewer receives the applicable card and supported events update all cards synchronously. There is no hidden batch queue or rate-limit machinery.",
                        "Viewers use !bingojoin and can add a team name for team games. They use !bingoleave while entry is open.",
                        "Owners and moderators can move participants.",
                        "Owners and moderators can remove participants.",
                        "Owners and moderators can keep private roster notes.",
                        "Select Issue and freeze cards only after you check the roster and teams.",
                        "The issue action closes entry.",
                        "For that game, the issue action permanently freezes participant assignments.",
                        "For that game, the issue action permanently freezes team assignments.",
                        "For that game, the issue action permanently freezes card assignments.",
                    ],
                    Code = "!bingo\n!bingojoin [team name]\n!bingoleave",
                },
                new SiteGuideSection
                {
                    Heading = "Mark events and correct mistakes",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/progression/phone-dark-bingo-evidence.png",
                        LightPhoneSource: "media/community/progression/phone-light-bingo-evidence.png",
                        DarkLaptopSource: "media/community/progression/laptop-dark-bingo-moderation.png",
                        LightLaptopSource: "media/community/progression/laptop-light-bingo-moderation.png",
                        PhoneAlt: "The Sample Channel public Team Aurora Bingo card shows marks and normalized evidence. It also shows a reversal and a retained rewarded win.",
                        LaptopAlt: "The Sample Channel Bingo moderator page shows a frozen 4 by 4 team card. Automatic marks and manual Confirm controls are visible.",
                        "Typed automatic evidence and manual correction stay visible on the public card, while moderator notes remain in the authorized dashboard."
                    ),
                    Bullets =
                    [
                        "Automatic squares mark once from a host-scoped event that matches.",
                        "Retries do not mark the same source event twice.",
                        "Restarts do not mark the same source event twice.",
                        "Provider replay does not mark the same source event twice.",
                        "Manual squares change only when an owner or moderator selects Confirm. Use Reverse to correct a mistaken manual mark. Both confirmation and reversal remain visible as public normalized evidence.",
                        "From its persisted grid, a card completes rows.",
                        "From its persisted grid, a card completes columns.",
                        "From its persisted grid, a card completes diagonals.",
                        "From its persisted grid, a card completes the configured full-card rule.",
                        "Points rewards grant once per completed win rule.",
                        "Community achievement rewards grant once per completed win rule.",
                        "Community title rewards grant once per completed win rule.",
                        "If a reversed mark completed a rewarded win, BlokeBot corrects the live square. The completed win and reward remain immutable. A second mark cannot grant it again. BlokeBot does not take back points or persistent unlocks.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Public information",
                    LegacyAnchor = "know-what-the-public-can-see",
                    Bullets =
                    [
                        "The public route /bingo/{channel} shows participant Twitch names or team names.",
                        "It also shows assigned cards.",
                        "It also shows marks.",
                        "It also shows wins.",
                        "It also shows archived games.",
                        "Normalized evidence can show the event kind.",
                        "Normalized evidence can show the time.",
                        "Normalized evidence can show the matched square.",
                        "Normalized evidence can show the relevant public participant name.",
                        "Normalized evidence can show the relevant public participant login.",
                        "Manual confirmation and reversal are public evidence too.",
                        "Public Bingo output never includes raw provider payloads and provider credentials.",
                        "Public Bingo output never includes internal identifiers and private moderator notes.",
                        "Public Bingo output never includes internal audit reasons.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Publish overlays and archives",
                    Media = new SiteMedia(
                        DarkPhoneSource: "media/community/progression/phone-dark-bingo-disabled.png",
                        LightPhoneSource: "media/community/progression/phone-light-bingo-disabled.png",
                        DarkLaptopSource: "media/community/progression/laptop-dark-bingo-archive.png",
                        LightLaptopSource: "media/community/progression/laptop-light-bingo-archive.png",
                        PhoneAlt: "The Sample Channel Bingo direct route retains templates and cards while Bingo is off. Evidence and wins remain. Rewards and archives remain.",
                        LaptopAlt: "The Sample Channel public Bingo archive that shows a completed five by five Shared card at desktop width.",
                        "Archives retain the dealt grid and public evidence. A disabled signed-in route keeps the saved game intact and points back to Channel setup."
                    ),
                    Bullets =
                    [
                        "If the stream must show Bingo summaries, enable Overlays.",
                        "Add an Event feed Browser Source.",
                        "Keep the private Browser Source URL out of chat and screenshots.",
                        "Archive a finished game.",
                        "Archive retains the frozen cards of a finished game in Completed history on the public page.",
                        "Archive retains its evidence in the same history.",
                        "Archive retains its wins in the same history.",
                        "Correct the condition that prevents entry.",
                        "Entry can be closed.",
                        "A cap can be reached.",
                        "A team name can be invalid.",
                        "Try again. After issue, roster and assignment changes are unavailable.",
                        "If an automatic square does not mark, check its type.",
                        "If an automatic square does not mark, check its filter.",
                        "If an automatic square does not mark, check its source.",
                        "Check the channel and applicable Twitch connection. Do not replace a subjective moment with an invented automatic source.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover after you turn Bingo off",
                    Bullets =
                    [
                        "If Bingo is off, BlokeBot hides navigation and public data.",
                        "With Bingo off, BlokeBot stops commands and joins.",
                        "With Bingo off, BlokeBot stops marks and rewards.",
                        "With Bingo off, BlokeBot stops overlay events and channel.update reconciliation.",
                        "BlokeBot keeps saved templates and rosters.",
                        "BlokeBot keeps saved issued cards and normalized evidence.",
                        "BlokeBot keeps saved wins and rewards.",
                        "BlokeBot keeps saved archives.",
                        "A retained signed-in direct route links to Channel setup.",
                        "Re-enable to continue from retained current state.",
                        "BlokeBot does not replay suppressed events from the period when Bingo was off.",
                        "BlokeBot does not replay suppressed commands from the period when Bingo was off.",
                        "BlokeBot does not replay suppressed subscriptions from the period when Bingo was off.",
                        "BlokeBot does not replay suppressed other work from the period when Bingo was off.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Build Community rewards", "community/progression"),
                new SiteLink("Add an Event feed overlay", "overlays"),
            ],
        };
    }
}
