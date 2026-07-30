namespace BlokeBot.Core.Features.Overlays;

internal static class OverlayBrowserSourceAssets
{
    internal const string Stylesheet = """
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
        """;

    internal const string JavaScript = """
        (() => {
          "use strict";

          const root = document.getElementById("overlay-root");
          const canvas = document.getElementById("overlay-canvas");
          if (!(root instanceof HTMLElement) || !(canvas instanceof SVGSVGElement)) {
            return;
          }

          const loadCurrentState = async () => {
            root.dataset.status = "loading";
            canvas.replaceChildren();
            try {
              const response = await fetch(root.dataset.stateUrl, {
                cache: "no-store",
                credentials: "omit",
                headers: { Accept: "application/json" },
              });
              if (!response.ok) {
                throw new Error("Overlay state is unavailable.");
              }

              const snapshot = await response.json();
              root.dataset.overlayType = snapshot.overlayType;
              root.dataset.schemaVersion = String(snapshot.schemaVersion);
              root.dataset.serverEpoch = snapshot.serverEpoch;
              root.dataset.sequence = String(snapshot.sequence);
              root.dataset.generatedAtUtc = snapshot.generatedAtUtc;
              root.dataset.status = "ready";
            } catch {
              root.dataset.status = "unavailable";
            }
          };

          void loadCurrentState();
        })();
        """;
}
