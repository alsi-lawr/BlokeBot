namespace BlokeBot.Core.Features.Overlays.Full;

internal static partial class FullOverlayBrowserAssets
{
    internal const string OuterDocument = """
        <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Full overlay</title><link rel="stylesheet" href="/full-overlay/assets/outer.css"><script src="/full-overlay/assets/controller.js" defer></script></head><body><iframe id="full-overlay" title="Overlay content" sandbox="allow-scripts" allow="autoplay" referrerpolicy="no-referrer"></iframe><output id="render-status" aria-live="polite"></output></body></html>
        """;
    internal const string FrameDocument = """
        <!doctype html><html><head><meta charset="utf-8"><meta name="referrer" content="no-referrer"><script src="/overlay/assets/blokebot-overlay.js"></script><script src="/full-overlay/assets/widgets.js"></script><script src="/full-overlay/assets/frame.js" defer></script></head><body></body></html>
        """;
    internal const string OuterCss = """
        html, body { margin: 0; width: 100%; height: 100%; background: transparent; overflow: hidden; }
        #full-overlay { display: block; width: 100%; height: 100%; border: 0; background: transparent; }
        #render-status { position: fixed; left: 1rem; bottom: 1rem; color: white; background: #0f172a; font: 14px system-ui; }
        #render-status:empty { display: none; }
        """;
}
