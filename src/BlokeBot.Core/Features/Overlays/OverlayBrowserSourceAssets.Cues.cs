namespace BlokeBot.Core.Features.Overlays;

internal static partial class OverlayBrowserSourceAssets
{
    private const string _cues = """
          const clearCues = () => {
            cueCanvas.replaceChildren();
            for (const timers of cueTimers.values()) {
              for (const timer of timers) {
                window.clearTimeout(timer);
              }
            }
            cueTimers.clear();
          };

          const validRectangle = (rectangle) =>
            typeof rectangle === "object" &&
            rectangle !== null &&
            ["xPercent", "yPercent", "widthPercent", "heightPercent"].every(
              (name) =>
                typeof rectangle[name] === "number" &&
                Number.isFinite(rectangle[name]),
            );

          const layerElement = (layer) => {
            let element;
            if (layer.kind === "externalWeb" && typeof layer.url === "string") {
              element = document.createElement("iframe");
              element.src = layer.url;
              element.setAttribute("sandbox", "allow-scripts");
              element.referrerPolicy = "no-referrer";
              element.title = "External cue content";
            } else if (
              (layer.kind === "uploadedMedia" ||
                layer.kind === "remoteMedia") &&
              (layer.mediaKind === "video" ||
                layer.mediaKind === "audio" ||
                (layer.kind === "uploadedMedia" && layer.mediaKind === "image"))
            ) {
              element = document.createElement(
                layer.mediaKind === "image" ? "img" : layer.mediaKind,
              );
              if (layer.mediaKind === "image") {
                element.alt = "";
                element.decoding = "async";
              } else {
                element.autoplay = true;
                element.preload = "auto";
                element.controls = false;
                element.volume =
                  typeof layer.volume === "number"
                    ? Math.min(1, Math.max(0, layer.volume))
                    : 1;
              }
              if (layer.kind === "remoteMedia" && typeof layer.url === "string") {
                element.src = layer.url;
              } else if (
                typeof layer.assetId === "string" &&
                Number.isSafeInteger(layer.contentRevision)
              ) {
                element.src = `${root.dataset.mediaUrl}/${encodeURIComponent(
                  layer.assetId,
                )}/${layer.contentRevision}`;
              } else {
                return null;
              }
              element.style.objectFit =
                layer.fit === "cover" || layer.fit === "fill"
                  ? layer.fit
                  : "contain";
            } else {
              return null;
            }

            if (!validRectangle(layer.rectangle)) {
              return null;
            }
            if (element instanceof HTMLMediaElement && options.audio) {
              element.muted = options.audio.isMuted;
              element.volume *= options.audio.volume;
            }
            element.className = "cue-layer";
            element.style.left = `${layer.rectangle.xPercent}%`;
            element.style.top = `${layer.rectangle.yPercent}%`;
            element.style.width = `${layer.rectangle.widthPercent}%`;
            element.style.height = `${layer.rectangle.heightPercent}%`;
            element.style.zIndex = String(layer.zIndex);
            return element;
          };

          const completeCue = async (runId) => {
            const run = cueCanvas.querySelector(`[data-cue-run="${CSS.escape(runId)}"]`);
            run?.remove();
            const timers = cueTimers.get(runId) ?? [];
            for (const timer of timers) {
              window.clearTimeout(timer);
            }
            cueTimers.delete(runId);
            if (options.onCueComplete) {
              options.onCueComplete(runId);
              return;
            }
            try {
              await fetch(
                `${root.dataset.completionUrl}/${encodeURIComponent(runId)}`,
                {
                  method: "POST",
                  credentials,
                  cache: "no-store",
                },
              );
            } catch {
              // Server-side expiry still advances the transient queue.
            }
          };

          const renderCue = (payload) => {
            if (
              payload?.overlayType !== "cuePlayer" ||
              payload.schemaVersion !== 1 ||
              typeof payload.runId !== "string" ||
              !Number.isSafeInteger(payload.durationMilliseconds) ||
              payload.durationMilliseconds < (options.startTransport === false ? 1 : 100) ||
              payload.durationMilliseconds > 300000 ||
              !Array.isArray(payload.layers)
            ) {
              return false;
            }
            const run = document.createElement("div");
            run.className = "cue-run";
            run.dataset.cueRun = payload.runId;
            cueCanvas.append(run);
            const timers = [];
            for (const layer of payload.layers) {
              if (
                !Number.isSafeInteger(layer.startOffsetMilliseconds) ||
                !Number.isSafeInteger(layer.durationMilliseconds) ||
                !Number.isSafeInteger(layer.zIndex)
              ) {
                continue;
              }
              timers.push(
                window.setTimeout(() => {
                  const element = layerElement(layer);
                  if (element === null) {
                    return;
                  }
                  run.append(element);
                  if (element instanceof HTMLMediaElement && options.onAudioBlocked)
                    void element.play().catch(options.onAudioBlocked);
                  timers.push(
                    window.setTimeout(
                      () => element.remove(),
                      layer.durationMilliseconds,
                    ),
                  );
                }, layer.startOffsetMilliseconds),
              );
            }
            timers.push(
              window.setTimeout(
                () => void completeCue(payload.runId),
                payload.durationMilliseconds,
              ),
            );
            cueTimers.set(payload.runId, timers);
            return true;
          };

          const stopCue = (runId) => {
            if (typeof runId !== "string") {
              return false;
            }
            const run = cueCanvas.querySelector(`[data-cue-run="${CSS.escape(runId)}"]`);
            run?.remove();
            const timers = cueTimers.get(runId) ?? [];
            for (const timer of timers) {
              window.clearTimeout(timer);
            }
            cueTimers.delete(runId);
            return true;
          };
        """;
}
