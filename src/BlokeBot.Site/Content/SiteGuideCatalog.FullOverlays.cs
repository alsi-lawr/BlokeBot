namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateFullOverlayPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/full-overlays",
            Eyebrow = "Full overlays",
            Title = "Compose and publish a full overlay",
            Summary =
                "Arrange widgets and ordinary HTML in one editable document. Save drafts privately, then choose what your browser source shows.",
            Media = FullOverlayMedia(
                "editor",
                "A full overlay in Visual mode with its canvas, layers and selected widget settings.",
                "The same editor on a narrow screen, with separate Visual and HTML/CSS modes and reachable Save actions.",
                "Visual and HTML/CSS modes edit one working document."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Choose simple or full",
                    Paragraphs =
                    [
                        "Simple overlays remain independent browser sources. Their saved configuration becomes live immediately. You do not need to convert them to use a full overlay.",
                        "Open Overlays → Editor in the dashboard for a separate document, draft and publication history. Its browser URL is different from a simple overlay URL.",
                    ],
                    Links = [new("Simple browser sources", "overlays")],
                },
                new SiteGuideSection
                {
                    Heading = "Start an editable document",
                    Steps =
                    [
                        "Select your channel, then open Overlays → Editor.",
                        "Choose Blank, Stream companion or Community progress. Each creates an unpublished document with a fresh browser-source key.",
                        "Name the document. Starter headings, HTML and CSS are ordinary editable source, not a locked template.",
                        "Select a layer or use Add. Confirm destination setup for starter widgets before expecting live data.",
                    ],
                    Facts =
                    [
                        new(
                            "Native widgets",
                            "Guessing, cue player, giveaway, event feed, viewer queue, community goal and viewer-funded bounty reuse their existing channel tools."
                        ),
                        new(
                            "Other widgets",
                            "Add HTML, a sandboxed web page, uploaded image/audio/video or an available plugin widget."
                        ),
                        new(
                            "Repeated widgets",
                            "Instances retain independent settings, style and audio. Repeated event-feed widgets with the same binding share one feed queue; use distinct bindings for distinct queues."
                        ),
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Edit canvas and source together",
                    Steps =
                    [
                        "Select a widget or ordinary HTML element in the layer tree. On a larger screen, the canvas also supports selection, move and resize handles.",
                        "Use the inspector for position, size, anchors, transforms, order, visibility, locking and optional clipping. Alignment and keyboard controls are alternatives to dragging.",
                        "Use view-only zoom and pan to inspect the scene. Shift temporarily bypasses optional snapping. Focus fills the application window; the same control restores the normal app shell. Full screen enters the browser's fullscreen mode; Escape or the same button exits it.",
                        "Choose HTML/CSS, then HTML or CSS, to edit the authoritative source. Your visual edits are already included; there is no Apply or Sync step. Return to Visual without losing unsaved or incomplete source, the selected source buffer or its caret. Visual edits change only intended source ranges; comments, scripts and unfamiliar syntax remain yours.",
                        "The toolbar names the current Visual, HTML or CSS history. Undo and Redo follow that history. Each editor has its own history. A newer overlapping edit is kept and reported as a conflict; undo that edit in its editor before retrying.",
                    ],
                    Note =
                        "Canvas controls do not take over typing or text-selection shortcuts in source fields. On narrow screens, switch between Layers, Canvas and Inspector, or choose HTML/CSS. Action tips appear on keyboard focus; Escape dismisses them. Help remains beside the theme button in the normal app topbar; exit Focus to return to it.",
                    Code = """
                        <!-- A widget's stable anchor belongs to this document. -->
                        <section data-blokebot-widget="YOUR-WIDGET-GUID"></section>
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Style, motion and audio",
                    Paragraphs =
                    [
                        "The document CSS is arbitrary browser CSS. HTML scripts and event handlers execute in an isolated browser frame, not on the server or with dashboard credentials.",
                        "Native source Appearance CSS is a separate, restricted setting inherited from simple overlays. It is scoped to that widget instance; do not use it as a replacement for full document CSS.",
                        "Motion presets write editable CSS. These presets respect reduced-motion preferences. A changed widget state can trigger motion; an unchanged heartbeat does not.",
                        "Set document audio and each widget's mute/volume independently. Browser autoplay policy can prevent sound until interaction; a visual preview does not prove audible playback.",
                        "Cue-player widgets use the existing overlay-level cue owner. Advancement is duration-derived, not a guarantee that every media element played or that every widget acknowledged completion.",
                    ],
                    Links = [new("Cue configuration and queue policy", "overlays/cues")],
                },
                new SiteGuideSection
                {
                    Heading = "Preview an unsaved draft",
                    Steps =
                    [
                        "Choose Sample or Live preview to freeze the current working document, including unsaved source, widget settings and audio.",
                        "Inspect the actual rendered result marked Unsaved. This is not your live publication. Editing refreshes preview from a new frozen candidate; Replay repeats it without consuming production work.",
                        "Read diagnostics for absent source anchors, missing media, unresolved setup or failed widgets. A failed widget does not replace unrelated content.",
                    ],
                    Note =
                        "Private preview requires channel management access. It neither creates production presence nor starts, drains, advances or completes production cue/feed work. Trusted plugin preview handlers must respect the same non-consuming intent.",
                },
                new SiteGuideSection
                {
                    Heading = "Save and publish deliberately",
                    Steps =
                    [
                        "Save stores the working draft without changing the current live publication. Incomplete drafts can be saved.",
                        "Use Save and publish to save and select the candidate for live delivery together. Check publication diagnostics first; ordinary unresolved content can remain visible as warnings.",
                        "If another editor changed the saved revision, keep your working edits and resolve the reported conflict instead of silently overwriting it.",
                        "Open History & live URL to retrieve the same live URL while authorized. Paste it into an anonymous OBS browser source. OBS does not need a dashboard login.",
                    ],
                    Note =
                        "Treat the live URL as a bearer credential. This guide's screenshots do not show its key. A draft save does not restart production feed or cue owners. Publishing, rollback, archive and restore invalidate obsolete render work.",
                },
                new SiteGuideSection
                {
                    Heading = "Rollback and manage history",
                    Steps =
                    [
                        "Open History to inspect retained publications. Select a retained version directly to roll live delivery back without rebuilding its content.",
                        "Manage retention explicitly. Forgetting a retained publication does not rewrite the current draft. Later publication numbers may have gaps after live-selection changes.",
                        "Duplicate copies the current saved draft into an unpublished document with a new live key, preserved audio and no copied publication history. Export the working candidate instead when you need unsaved edits.",
                        "Archive stops live delivery; restore resumes the retained live selection. Permanent deletion requires confirmation and makes that document's key unavailable.",
                    ],
                    Note =
                        "Retained publications keep the document and widget configuration, not old media files or frozen live source data. Replaced media uses its current bytes; deleted media may be unavailable. Rollback warns about missing media but can proceed, leaving only affected widgets unavailable while the rest renders.",
                },
                new SiteGuideSection
                {
                    Heading = "Transfer a working document and current media",
                    Steps =
                    [
                        "Export from the editor to freeze its current working document, not just its last saved baseline. The ZIP contains verbatim HTML/CSS/scripts and current bytes for referenced uploaded image/audio/video widgets.",
                        "Select the destination channel and import the ZIP. A successful import creates one unpublished document with a fresh key, retained widget IDs/anchors and remapped managed-media references.",
                        "Complete each widget's destination setup, including optional withheld plugin fields. Confirm setup explicitly; changing style or audio alone does not bind source-host IDs.",
                        "Preview, save and publish on the destination when ready.",
                    ],
                    Paragraphs =
                    [
                        "Raw HTML/CSS/script URLs remain unchanged. They are not downloaded, rewritten or promised portable. Only managed media widgets carry uploaded bytes; historical media versions and historical publications do not transfer.",
                        "Application-managed credentials, history, private viewer data, runtime state, installation settings, storage and plugin package authority are excluded structurally. Authored source remains verbatim.",
                        "Plugin fields transfer only when their current declarations explicitly mark them portable. Destination bindings, withheld or undeclared values and unknown-plugin configuration need manual setup; widget identity, layout, audio and source are retained.",
                        "The decompressed document/manifest entry is limited by the server's existing configured per-file upload limit (50 MiB by default). Oversize imports are rejected, not truncated. This import-only rule does not limit existing drafts, ordinary Save, preview or export, and does not change media quotas.",
                        "Rejected imports commit no destination document or media changes. A committed import with a notification follow-up is already applied; do not import again to retry that notification.",
                    ],
                    Links =
                    [
                        new("Uploaded media", "overlays/media"),
                        new(
                            "Plugin widget declarations",
                            "plugin-development/manifest#public-overlay-widgets"
                        ),
                    ],
                },
            ],
            Next =
            [
                new("Simple browser sources", "overlays"),
                new("Plugin handlers", "plugin-development/handlers#public-widget-projections"),
            ],
        };
    }

    private static SiteMedia FullOverlayMedia(
        string view,
        string laptopAlt,
        string phoneAlt,
        string caption
    ) =>
        new(
            $"media/full-overlays/phone-dark-full-overlay-{view}.png",
            $"media/full-overlays/phone-light-full-overlay-{view}.png",
            $"media/full-overlays/laptop-dark-full-overlay-{view}.png",
            $"media/full-overlays/laptop-light-full-overlay-{view}.png",
            phoneAlt,
            laptopAlt,
            caption
        );
}
