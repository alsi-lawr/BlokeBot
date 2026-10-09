namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _canvasStylesheet = """
        :root {
          background: transparent;
          color-scheme: only light;
        }

        html,
        body {
          width: 100%;
          height: 100%;
          margin: 0;
          overflow: hidden;
          background: transparent !important;
        }

        #overlay-root {
          position: fixed;
          inset: 0;
          overflow: hidden;
          background: transparent;
        }

        #overlay-canvas {
          display: block;
          width: 100%;
          height: 100%;
          overflow: hidden;
          background: transparent;
        }

        #cue-canvas {
          position: absolute;
          inset: 0;
          overflow: hidden;
          pointer-events: none;
        }

        .cue-run,
        .cue-layer {
          position: absolute;
          inset: 0;
        }

        .cue-layer {
          border: 0;
          background: transparent;
        }

        #overlay-root[data-test-pulse="active"] #overlay-canvas {
          animation: blokebot-overlay-test-pulse 1.5s ease-out;
          box-shadow: inset 0 0 0 24px rgba(59, 130, 246, 0);
        }

        """;

    internal const string Stylesheet = _canvasStylesheet + PresentationStylesheet;

    internal static string JavaScript { get; } =
        string.Join(
            "\n",
            _runtime,
            _text,
            _guessing,
            _giveaway,
            _eventFeed,
            _viewerQueue,
            _progress,
            _previewBridge,
            _cues,
            _transport
        );
}
