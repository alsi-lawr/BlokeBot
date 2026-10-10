namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
    private static readonly HelpPage _dashboardsHelp = new(
        "Dashboards",
        [
            new(
                "Control",
                "Choose a saved dashboard to operate its widgets. Viewer selections and countdown duration changes are yours for this session.",
                []
            ),
            new(
                "Editor",
                "Choose or create a dashboard before opening its workspace. Add widgets from Toolbox and choose targets in Widget settings. Editor never runs your controls.",
                []
            ),
            new(
                "Arrange and save",
                "Drag a widget's handle to move it or its corner to resize it. Arrow keys on either handle and Position & size provide keyboard alternatives. Moving into one occupied slot swaps slots; other collisions ask before replacing widgets. Undo restores local changes. Save applies them to the shared dashboard; Cancel discards them.",
                []
            ),
            new(
                "Live state",
                "Runs show native status after admission. Countdown text reaching zero does not mean its completion action has finished. Unavailable widgets keep their configuration; open Editor to rebind missing targets.",
                []
            ),
        ]
    );
}
