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
            Summary = "Manage competitions.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/figures/phone-dark-competition-result.png",
                LightPhoneSource: "media/community/figures/phone-light-competition-result.png",
                DarkLaptopSource: "media/community/figures/laptop-dark-competition-result.png",
                LightLaptopSource: "media/community/figures/laptop-light-competition-result.png",
                PhoneAlt: "The Sample Channel Tournaments and leagues workspace shows the active Summer Community Circuit. Lifecycle actions and standings are visible.",
                LaptopAlt: "The Sample Channel Tournaments and leagues workspace shows the active Summer Community Circuit. Lifecycle actions and standings are visible.",
                "Staff control the authoritative lifecycle and results. Viewers receive the current public bracket or schedule. The other possible view shows current public standings."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Register viewers or teams.",
                        "Create a reproducible schedule.",
                        "Confirm results.",
                        "Publish standings and archives within the public data limits.",
                        "An owner or permitted moderator controls competitions.",
                        "An owner or permitted moderator controls entrants.",
                        "An owner or permitted moderator controls lifecycle.",
                        "An owner or permitted moderator controls results.",
                        "An owner or permitted moderator controls permitted reminders.",
                        "An owner or permitted moderator controls archives.",
                    ],
                    Heading = "Enable the feature and assign authority",
                    Paragraphs =
                    [
                        "The feature card saves the change immediately. Each channel starts with this switch off.",
                    ],
                    Steps =
                    [
                        "As the channel owner or permitted moderator, select the channel.",
                        "Open Channel setup.",
                        "Turn on Tournaments & leagues under Chat tools.",
                        "As a moderator, act only for the selected channel.",
                        "As a viewer, use !competitions for the current public competition.",
                        "Use !competitionjoin for individual registration.",
                        "As authorized staff, manage teams and private contact details in the dashboard.",
                    ],
                    Code = "!competitions\n!competitionjoin",
                },
                new SiteGuideSection
                {
                    Heading = "Competition configuration",
                    LegacyAnchor = "create-a-competition-contract",
                    Steps =
                    [
                        "Create a Draft.",
                        "Select one of the competition formats below.",
                        "Choose Individuals or Teams.",
                        "Before you open registration, set capacity and team size.",
                        "Set optional minimum-points eligibility.",
                        "Set the schedule order.",
                        "Set points and tiebreak rules.",
                        "Set the reminder lead time.",
                        "Configure only the rewards that you intend to grant.",
                        "Review the public name and rules.",
                        "Then open registration.",
                        "Do not change the format after results exist.",
                    ],
                    Bullets =
                    [
                        "Reward configuration: the confirmed-win milestone.",
                        "Reward configuration: final-placement points.",
                        "Reward configuration: declared Community progression achievement keys.",
                        "Competition format: Tournament bracket.",
                        "Competition format: Round robin.",
                        "Competition format: Prediction league.",
                        "Prediction leagues treat entered fixture scores as correct-prediction totals. They apply the configured points and tiebreaks.",
                        "A random schedule records its seed and BlokeBot algorithm version. A moderator-ranked schedule records the supplied ranks. Both preserve entrant order.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Register entrants and start",
                    Bullets =
                    [
                        "Registration enforces entry kind and capacity.",
                        "Registration enforces team size and eligibility.",
                        "A chat join requires an authenticated Twitch user ID and selects the first open individual competition.",
                        "The dashboard keeps private contact and lobby information private.",
                        "The dashboard keeps Twitch user IDs and moderator notes private.",
                        "Only the configured entrant name becomes public.",
                        "Select Generate & start to close registration and save the bracket or round schedule.",
                        "If registration closes, correct or reload the visible state.",
                        "If registration reaches capacity, correct or reload the visible state.",
                        "If eligibility blocks entry, correct or reload the visible state.",
                        "If the competition changed, correct or reload the visible state.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Confirm and correct results",
                    Bullets =
                    [
                        "Select a scheduled match.",
                        "Enter both scores.",
                        "Confirm the scores.",
                        "BlokeBot recalculates advancement and standings from confirmed results.",
                        "A result correction retains the previous scores and private audit reason. A tournament correction clears outcomes that no longer follow from the corrected winner.",
                        "Corrections do not duplicate confirmed-win or final-placement rewards.",
                        "Recalculation does not duplicate confirmed-win or final-placement rewards.",
                        "Retries do not duplicate confirmed-win or final-placement rewards.",
                        "BlokeBot does not grant the same milestone twice.",
                        "A stale revision or status returns a conflict.",
                        "Reload the competition.",
                        "Check the match and its current downstream effects.",
                        "Apply the intended correction.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Completed competitions",
                    LegacyAnchor = "complete-archive-and-publish",
                    Bullets =
                    [
                        "Select Complete competition to evaluate final placements and rewards from the authoritative confirmed state.",
                        "Select Archive to retain completed competition history.",
                        "Archive retains the format as completed history.",
                        "Archive retains the schedule as completed history.",
                        "Archive retains the standings as completed history.",
                        "Archive retains the results as completed history.",
                        "Archive retains the audit history as completed history.",
                        "The public route /competitions/{channel} shows entrants.",
                        "It also shows the schedule or bracket.",
                        "The public route /competitions/{channel} shows standings and confirmed scores.",
                        "The public route /competitions/{channel} shows archives.",
                        "Lifecycle events use the same bounded public state.",
                        "The public page and lifecycle payloads exclude private contact and lobby information.",
                        "The public page and lifecycle payloads exclude moderator notes and audit reasons.",
                        "The public page and lifecycle payloads exclude internal IDs and provider details.",
                        "Match reminders use permitted private delivery. An unavailable provider does not expose the match or report a successful delivery.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Pause and restore the feature",
                    Bullets =
                    [
                        "Turn off Tournaments & leagues to remove discovery and public data.",
                        "With Tournaments & leagues off, BlokeBot blocks registration and starts before changes.",
                        "With Tournaments & leagues off, BlokeBot blocks results and advancement before changes.",
                        "With Tournaments & leagues off, BlokeBot blocks reminders and rewards before changes.",
                        "With Tournaments & leagues off, BlokeBot blocks events and commands before changes.",
                        "With Tournaments & leagues off, BlokeBot blocks provider work before changes.",
                        "The signed-in direct route links to Channel setup.",
                        "BlokeBot retains formats and entrants.",
                        "BlokeBot retains schedules and results.",
                        "BlokeBot retains audit history and archives.",
                        "Re-enable the feature to resume the current lifecycle.",
                        "BlokeBot does not replay suppressed commands and reminders.",
                        "BlokeBot does not replay suppressed rewards and events.",
                        "BlokeBot does not replay suppressed subscriptions and provider work.",
                        "If a page or command is unavailable, check the channel.",
                        "Check the switch.",
                        "Check the lifecycle.",
                        "Check the entry kind.",
                        "If an action is stale, reload before another attempt.",
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
