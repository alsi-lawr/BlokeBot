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
            Summary =
                "Issue deterministic shared, viewer or team cards, mark supported stream moments and keep public evidence and rewards reviewable.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/progression/phone-dark-bingo-public-card.png",
                LightPhoneSource: "media/community/progression/phone-light-bingo-public-card.png",
                DarkLaptopSource: "media/community/progression/laptop-dark-bingo-setup.png",
                LightLaptopSource: "media/community/progression/laptop-light-bingo-setup.png",
                PhoneAlt: "The Sample Channel public Bingo page that shows a team card, participant Twitch logins and a narrow-screen horizontal-scroll affordance.",
                LaptopAlt: "The Sample Channel Bingo management page with a template revision, Shared board mode, seed, participant cap and Open viewer action.",
                "Hosts open a game from a saved template revision. Participants see the frozen card assignment and public identity boundary."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable the required tools",
                    Steps =
                    [
                        "Choose the channel, open Channel setup and turn on Bingo under Chat tools. The feature card persists the change immediately.",
                        "A channel owner or permitted moderator manages templates, games, rosters, cards, manual marks and archives. Viewers join or leave before issue and follow cards in chat or on the public page.",
                        "Turn on Points before you open a game whose template awards points. Turn on Community progression and predeclare a viewer achievement that accepts external grants. If the Bingo win must unlock a title, attach a title reward to that achievement.",
                    ],
                    Note =
                        "A Stream category changed square also needs the selected channel's Twitch integration. Bingo owns the host-scoped channel.update subscription and keeps it absent while Bingo is off.",
                },
                new SiteGuideSection
                {
                    Heading = "Build a deterministic template",
                    Steps =
                    [
                        "Choose a 3 × 3, 4 × 4 or 5 × 5 grid and provide enough squares to fill it.",
                        "Give every square a public title and stable key. Choose Manual confirmation, Incoming raid, Bounty completed or Guessing result. You can also choose Giveaway started, Stream category changed or Counter reached.",
                        "Set only the supported threshold, counter or filter for that typed source. Put subjective staff guidance in Private moderator note.",
                        "Configure the line reward used by row, column and diagonal wins and, when wanted, enable a full-card win and reward. Save the revision.",
                    ],
                    Paragraphs =
                    [
                        "The issued card keeps the saved dimension, template revision, recorded seed and assignment identity. Later template edits do not alter frozen cards, square positions or win lines.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Open entry and freeze the roster",
                    Bullets =
                    [
                        "Choose Shared board, Unique per viewer or Teams, enter a recorded seed and optionally set a host participant cap. Team games can also set a host team cap and team names.",
                        "There is no product-wide participant cap. With no host cap, every joined viewer receives the applicable card and supported events update all cards synchronously. There is no hidden batch queue or rate-limit machinery.",
                        "Viewers use !bingojoin and can add a team name for team games. They use !bingoleave while entry is open. Owners and moderators can move or remove participants and keep private roster notes.",
                        "Select Issue and freeze cards only after you check the roster and teams. The issue action closes entry and permanently freezes participant, team and card assignments for that game.",
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
                        PhoneAlt: "The Sample Channel public Team Aurora Bingo card with marks, normalized evidence, a reversal and a retained rewarded win.",
                        LaptopAlt: "The Sample Channel Bingo moderator page with a frozen 4 by 4 team card, automatic marks and manual Confirm controls.",
                        "Typed automatic evidence and manual correction stay visible on the public card, while moderator notes remain in the authorized dashboard."
                    ),
                    Bullets =
                    [
                        "Automatic squares mark once from a host-scoped event that matches. Retries, restarts and provider replay do not mark the same source event twice.",
                        "Manual squares change only when an owner or moderator selects Confirm. Use Reverse to correct a mistaken manual mark. Both confirmation and reversal remain visible as public normalized evidence.",
                        "A card completes rows, columns, diagonals and the configured full-card rule from its persisted grid. Points and Community achievement or title rewards grant once per completed win rule.",
                        "If a reversed mark completed a rewarded win, BlokeBot corrects the live square. The completed win and reward remain immutable. A second mark cannot grant it again. Points and persistent unlocks are not clawed back.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Know what the public can see",
                    Bullets =
                    [
                        "The public route /bingo/{channel} shows participant Twitch names or team names. It also shows assigned cards, marks, wins and archived games.",
                        "Normalized evidence can show the event kind, time, matched square and relevant public participant name and login. Manual confirmation and reversal are public evidence too.",
                        "Raw provider payloads, provider credentials, internal identifiers, private moderator notes and internal audit reasons are never public.",
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
                        PhoneAlt: "The Sample Channel Bingo direct route that shows retained templates, cards, evidence, wins, rewards and archives while Bingo is off.",
                        LaptopAlt: "The Sample Channel public Bingo archive that shows a completed five by five Shared card at desktop width.",
                        "Archives retain the dealt grid and public evidence. A disabled signed-in route keeps the saved game intact and points back to Channel setup."
                    ),
                    Bullets =
                    [
                        "If the stream must show Bingo summaries, enable Overlays. Add an Event feed Browser Source. Keep the private Browser Source URL out of chat and screenshots.",
                        "Archive a finished game to move its frozen cards, evidence and wins into Completed history on the public page.",
                        "If entry is closed, a cap is reached or a team name is invalid, correct that condition. Then try again. Once issued, roster and assignment changes are intentionally unavailable.",
                        "If an automatic square does not mark, check its type, filter and source. Check the channel and applicable Twitch connection. Do not replace a subjective moment with an invented automatic source.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover after you turn Bingo off",
                    Bullets =
                    [
                        "If Bingo is off, BlokeBot hides navigation and public data. It stops commands, joins, marks, rewards, overlay events and channel.update reconciliation.",
                        "Templates, rosters, issued cards, normalized evidence, wins, rewards and archives remain saved. A retained signed-in direct route links to Channel setup.",
                        "Re-enable to continue from retained current state. Events, commands, subscriptions and other work suppressed while Bingo was off are not replayed.",
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
