namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateCommunityCompetitionPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/competitions",
            Eyebrow = "Community progression · Competitions",
            Title = "Run tournaments and leagues",
            Summary =
                "Register viewers or teams. Create a reproducible schedule. Confirm results and publish bounded standings and archives.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/figures/phone-dark-competition-result.png",
                LightPhoneSource: "media/community/figures/phone-light-competition-result.png",
                DarkLaptopSource: "media/community/figures/laptop-dark-competition-result.png",
                LightLaptopSource: "media/community/figures/laptop-light-competition-result.png",
                PhoneAlt: "The Sample Channel Tournaments and leagues workspace that shows the active Summer Community Circuit, lifecycle actions, and standings.",
                LaptopAlt: "The Sample Channel Tournaments and leagues workspace that shows the active Summer Community Circuit, lifecycle actions, and standings.",
                "Staff control the authoritative lifecycle and results. Viewers receive the current public bracket, schedule, or standings."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable the feature and assign authority",
                    Steps =
                    [
                        "As the channel owner or permitted moderator, choose the channel. Open Channel setup. Turn on Tournaments & leagues under Chat tools.",
                        "Expect the feature card to save the change immediately. Expect each channel to start with this switch off.",
                        "As an owner or permitted moderator, control competitions, entrants, lifecycle, results, permitted reminders, and archives.",
                        "As a moderator, act only for the selected channel.",
                        "As a viewer, use !competitions for the current public competition. Use !competitionjoin for individual registration.",
                        "As authorized staff, manage teams and private contact details in the dashboard.",
                    ],
                    Code = "!competitions\n!competitionjoin",
                },
                new SiteGuideSection
                {
                    Heading = "Create a competition contract",
                    Steps =
                    [
                        "Create a Draft. Choose Tournament bracket, Round robin, or Prediction league. Choose Individuals or Teams.",
                        "Before you open registration, set capacity, team size, optional minimum-points eligibility, schedule order, points, tiebreak rules, and reminder lead time.",
                        "Configure the confirmed-win milestone, final-placement points, or declared Community progression achievement keys only for intended rewards.",
                        "Review the public name and rules. Then open registration. Do not change the format after results exist.",
                    ],
                    Bullets =
                    [
                        "Prediction leagues treat entered fixture scores as correct-prediction totals. They apply the configured points and tiebreaks.",
                        "A random schedule records its seed and BlokeBot algorithm version. A moderator-ranked schedule records the supplied ranks. Both preserve entrant order.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Register entrants and start",
                    Bullets =
                    [
                        "Registration enforces entry kind, capacity, team size, and eligibility. A chat join requires an authenticated Twitch user ID and selects the first open individual competition.",
                        "Private contact, lobby information, Twitch user IDs, and moderator notes stay in the dashboard. Only the configured entrant name becomes public.",
                        "Select Generate & start to close registration and save the bracket or round schedule.",
                        "If registration closes or reaches capacity, correct or reload the visible state. Do the same if eligibility blocks entry or the competition changed.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Confirm and correct results",
                    Bullets =
                    [
                        "Choose a scheduled match. Enter both scores and confirm them.",
                        "BlokeBot recalculates advancement and standings from confirmed results.",
                        "A result correction retains the previous scores and private audit reason. A tournament correction clears outcomes that no longer follow from the corrected winner.",
                        "Corrections, recalculation, and retries do not duplicate confirmed-win or final-placement rewards. BlokeBot does not grant the same milestone twice.",
                        "A stale revision or status returns a conflict.",
                        "Reload the competition. Verify the match and its current downstream effects. Then apply the intended correction.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Complete, archive, and publish",
                    Bullets =
                    [
                        "Select Complete competition to evaluate final placements and rewards from the authoritative confirmed state.",
                        "Select Archive to retain the format, schedule, standings, results, and audit history as completed history.",
                        "The public route /competitions/{channel} shows entrants, schedule or bracket, standings, confirmed scores, and archives. Lifecycle events use the same bounded public state.",
                        "The public page and lifecycle payloads exclude private contact, lobby information, moderator notes, audit reasons, internal IDs, and provider details.",
                        "Match reminders use permitted private delivery. An unavailable provider does not expose the match or report a successful delivery.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pause and restore the feature",
                    Bullets =
                    [
                        "Turn off Tournaments & leagues to remove discovery and public data.",
                        "BlokeBot then blocks registration, starts, results, advancement, reminders, rewards, events, commands, and provider work before changes.",
                        "The signed-in direct route links to Channel setup. BlokeBot retains formats, entrants, schedules, results, audit history, and archives.",
                        "Re-enable the feature to resume the current lifecycle.",
                        "BlokeBot does not replay suppressed commands, reminders, rewards, events, subscriptions, or provider work.",
                        "If a page or command is unavailable, verify the channel, switch, lifecycle, and entry kind. If an action is stale, reload before another attempt.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink(
                    "Attach approved Moments",
                    "community/moments#attach-approved-moments-to-progression"
                ),
                new SiteLink("Coordinate a Collective", "community/collectives"),
            ],
        };
    }
}
