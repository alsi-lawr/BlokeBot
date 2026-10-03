using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Overlays.Full;

internal enum FullOverlayTemplate
{
    Blank,
    StreamCompanion,
    CommunityProgress,
}

internal static class FullOverlayTemplates
{
    internal static FullOverlayDocument Create(
        FullOverlayTemplate template,
        FullOverlayWidgetRegistry registry
    )
    {
        var document = FullOverlayDocument.Blank();
        var kinds = template switch
        {
            FullOverlayTemplate.Blank => Array.Empty<string>(),
            FullOverlayTemplate.StreamCompanion =>
            [
                "guessing",
                "giveaway",
                "event-feed",
                "cue-player",
            ],
            FullOverlayTemplate.CommunityProgress =>
            [
                "community-goal",
                "viewer-funded-bounty",
                "viewer-queue",
            ],
        };
        var widgets = kinds
            .Select(kind => registry.Create(new(kind), document.Id)! with { RequiresSetup = true })
            .ToImmutableArray();
        var title = template switch
        {
            FullOverlayTemplate.Blank => "Your stream",
            FullOverlayTemplate.StreamCompanion => "Live with the community",
            FullOverlayTemplate.CommunityProgress => "Building together",
        };
        return document with
        {
            Html = $"""
                <main class="stream-scene">
                  <header class="stream-heading"><p>ON STREAM</p><h1>{title}</h1></header>
                  <section class="stream-widgets" aria-label="Stream widgets">
                {string.Join(
                    '\n',
                    widgets.Select(widget =>
                        $"    <div data-blokebot-widget=\"{widget.Id.Value}\"></div>"
                    )
                )}
                  </section>
                </main>
                """,
            Css = """
                /* Edit this document freely; visual edits and source share this text. */
                .stream-scene { box-sizing: border-box; min-height: 100vh; padding: clamp(24px, 4vw, 72px); font-family: system-ui, sans-serif; color: #f8fafc; }
                .stream-heading { display: inline-block; padding: 18px 26px; border-radius: 18px; background: #172033e8; border-left: 4px solid #8bd5ca; }
                .stream-heading p { margin: 0 0 8px; font-size: 14px; letter-spacing: .15em; color: #8bd5ca; }
                .stream-heading h1 { margin: 0; font-size: clamp(24px, 3vw, 48px); }
                .stream-widgets { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 24px; align-items: start; margin-top: 24px; }
                .stream-widgets > div { min-height: 80px; }
                """,
            Widgets = widgets,
        };
    }
}
