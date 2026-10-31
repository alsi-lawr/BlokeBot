namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateDashboardPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/dashboards",
            Eyebrow = "Channel controls",
            Title = "Build your own control dashboard",
            Summary =
                "The dashboard builder is experimental. Arrange named dashboards of native channel controls and live values. Owners and current moderators share the saved layout; each operator keeps their own viewer selection.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose Control or Editor",
                    Paragraphs =
                    [
                        "Open Dashboards → Control to choose a saved dashboard and use its controls. Open Dashboards → Editor to choose or create a dashboard before entering its workspace. Both support the shared Focus and Full screen controls. Editor never runs live operations.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Add and configure",
                    Steps =
                    [
                        "Open Toolbox, then add an automation button, value, countdown, poll, prediction, giveaway or guessing widget.",
                        "Open a widget's settings and choose a target from this channel. Automation buttons select one Manual Run source and can override its saved text. Value widgets show one scalar or one named dictionary entry.",
                        "Set a countdown name and default duration. In Control, operators can adjust duration for Start or Reset without changing the shared default.",
                    ],
                    Paragraphs =
                    [
                        "Missing, ambiguous and disabled targets remain visible. Rebind them explicitly in Editor rather than recreating the dashboard.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Arrange and save",
                    Paragraphs =
                    [
                        "Drag the header handle to move a widget and the corner handle to resize it. Arrow keys on these handles or Position & size fields provide the same editing controls. Narrow screens show an ordered list with the same settings and actions.",
                        "Moving an existing widget onto one occupied slot swaps the two complete slots. New-widget drops, resizing and collisions covering several widgets ask before replacing covered widgets. Replacements are local and Undo restores them.",
                        "Save updates the shared live dashboard and ends session Undo. Cancel discards local edits and ends that session. Unsaved navigation asks before discarding work. If another operator saves first, your edits remain available beside an explicit discard-and-reload choice. There are no stored drafts, publications or rollback history.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Operate native controls",
                    Paragraphs =
                    [
                        "Automation admission is not completion. Only that invocation's returned run IDs are tracked. Countdown text ticks locally, but reaching zero is not proof that native completion has run.",
                        "Poll endings and prediction transitions keep their existing confirmation rules. Resolve predictions using an outcome from the current prediction, and declare guessing winners using the current round's options. Selected templates and profiles choose the next start; they do not replace an already active native round.",
                        "Guessing actions send their configured reply through the existing queue, including pin intent. A queue rejection is a separate warning: the completed round action is not undone, and queue acceptance does not guarantee delivery.",
                    ],
                },
            ],
        };
    }
}
