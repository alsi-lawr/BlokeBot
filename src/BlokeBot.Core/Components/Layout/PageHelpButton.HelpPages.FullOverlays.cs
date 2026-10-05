namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
    private static readonly HelpPage _fullOverlaysHelp = new(
        "Overlay editor",
        [
            new(
                "Choose your editor",
                "Overlays → Simple keeps independent browser sources. Overlays → Editor opens full HTML/CSS documents with separate drafts and publication history.",
                ["Create a blank document or an editable starter, or open a saved document."]
            ),
            new(
                "One working document",
                "Visual and HTML/CSS edit the same unsaved document. Switch modes to see your visual edits already in the source; there is no Apply or Sync step.",
                [
                    "Select a layer to move or resize it and change its style, motion or widget settings.",
                    "Find HTML opens HTML/CSS and places the caret at the selected layer.",
                    "HTML and CSS keep separate histories. Undo and Redo follow the history named in the toolbar. Incomplete source is retained, and newer overlapping edits are not overwritten.",
                ]
            ),
            new(
                "Your workspace",
                "Focus fills the application window. Full screen uses the browser's fullscreen mode; Escape or the same control exits full screen.",
                [
                    "Zoom, pan and preview viewport only change your authoring view. Shift bypasses optional snapping.",
                    "On narrow screens use Layers, Canvas and Inspector. HTML/CSS remains a separate mode. Action tips appear on keyboard focus; Escape dismisses them.",
                    "Help stays beside the theme button in the normal app topbar. Exit Focus to return to that topbar.",
                ]
            ),
            new(
                "Preview, save and publish",
                "Unsaved is the current private preview, not the live browser source. Sample and Live preview data do not consume production cue or feed work.",
                [
                    "Save draft stores your work without changing the live publication. Ctrl+S also saves the draft.",
                    "Save & publish saves and makes the candidate live. Review any publication diagnostics.",
                    "History & live URL opens your stable browser-source URL and retained publications. Treat that URL as a credential.",
                ]
            ),
        ]
    );
}
