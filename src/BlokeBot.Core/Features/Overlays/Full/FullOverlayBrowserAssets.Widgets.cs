namespace BlokeBot.Core.Features.Overlays.Full;

internal static partial class FullOverlayBrowserAssets
{
    internal const string Widgets = """
        (() => {
          "use strict";
          const create = window.blokeBotOverlayRenderer.create;
          const mount = (anchor, initial, report, privatePreview = false) => {
            const host = document.createElement("div"); host.dataset.fullWidgetRenderer = ""; anchor.append(host);
            let engine = null; let appearanceStylesheet = null; let fingerprint = null; let presentation = null; let kind = null; let plugin = null; let nestedPort = null;
            const runs = new Set();
            const diagnostics = new Set();
            let reportLifetime = {};
            const reporter = (widget) => {
              const identity = reportLifetime;
              return (code) => {
                if (privatePreview) {
                  if (reportLifetime !== identity) return;
                  diagnostics.add(code);
                }
                report(widget.id, code);
              };
            };
            const cleanup = () => {
              reportLifetime = {}; diagnostics.clear();
              engine?.dispose(); engine = null; appearanceStylesheet = null; nestedPort?.close(); nestedPort = null;
              plugin = null; runs.clear(); host.replaceChildren();
            };
            const sourceEngine = (widget) => {
              const blocked = reporter(widget);
              host.dataset.overlayRoot = ""; host.id = `blokebot-native-${widget.id}`;
              const canvas = document.createElementNS("http://www.w3.org/2000/svg", "svg");
              const cueCanvas = document.createElement("div"); cueCanvas.style.cssText = "position:absolute;inset:0";
              appearanceStylesheet = document.createElement("style");
              host.append(canvas, cueCanvas, appearanceStylesheet);
              engine = create(host, canvas, cueCanvas, appearanceStylesheet, {
                credentials: "omit", startTransport: false, audio: widget.audio,
                clipPrefix: `${widget.id}-`,
                onCueComplete: () => {},
                onAudioBlocked: () => blocked("audio-blocked"),
              });
              return canvas;
            };
            let canvas = null;
            const update = (widget) => {
              if (kind !== widget.kind) { cleanup(); kind = widget.kind; fingerprint = null; presentation = null; canvas = null; }
              const next = JSON.stringify(widget.content);
              const changed = next !== fingerprint;
              const motion = kind === "source"
                ? JSON.stringify([widget.content.overlayType, widget.content.serverEpoch, widget.content.sequence, widget.content.state])
                : next;
              if (motion !== presentation) {
                const phase = kind === "unavailable" ? "exit" : presentation === null ? "entrance" : "state";
                anchor.removeAttribute("data-blokebot-phase");
                if (phase !== "entrance") void anchor.offsetWidth;
                anchor.dataset.blokebotPhase = phase;
                presentation = motion;
              }
              fingerprint = next;
              if (kind === "cue") {
                if (!engine) canvas = sourceEngine(widget);
                const active = new Set(widget.content.map(plan => plan.runId));
                for (const id of runs) if (!active.has(id)) { engine.stopCue(id); runs.delete(id); }
                for (const plan of widget.content) if (!runs.has(plan.runId)) {
                  host.dataset.mediaUrl = plan.mediaUrl;
                  engine.renderCue(plan); runs.add(plan.runId);
                }
                return;
              }
              if (!changed) return;
              if (kind === "source") {
                diagnostics.delete("widget-unavailable");
                if (!engine) canvas = sourceEngine(widget);
                appearanceStylesheet.textContent = widget.appearanceCss;
                const snapshot = widget.content;
                if (snapshot.appearance) canvas.setAttribute("viewBox", `${snapshot.appearance.x} ${snapshot.appearance.y} ${snapshot.appearance.width} ${snapshot.appearance.height}`);
                if (!engine.applyPresentation(snapshot, snapshot.sequence, snapshot.serverEpoch, snapshot.generatedAtUtc))
                  reporter(widget)("widget-unavailable");
                return;
              }
              if (kind === "plugin" && plugin?.src === new URL(widget.content.url, location.href).href) {
                plugin.contentWindow.postMessage({ kind: "blokebot-widget-state", state: widget.content.state }, "*"); return;
              }
              cleanup();
              if (kind === "unavailable") { host.dataset.status = "unavailable"; reporter(widget)("widget-unavailable"); return; }
              delete host.dataset.status;
              if (kind === "html" || kind === "web" || kind === "plugin") {
                const child = document.createElement("iframe"); child.sandbox = "allow-scripts";
                child.referrerPolicy = "no-referrer"; child.allow = "autoplay";
                child.title = "Overlay widget"; host.append(child);
                if (kind === "html") {
                  const identity = crypto.randomUUID();
                  child.addEventListener("load", () => {
                    const channel = new MessageChannel(); nestedPort = channel.port1;
                    nestedPort.onmessage = (message) => {
                      if (message.data?.kind === "ready" && message.data.lifetime === identity)
                        nestedPort.postMessage({ kind: "render", lifetime: identity,
                          html: widget.content.html, css: widget.content.css, widgets: [] });
                    };
                    nestedPort.start(); child.contentWindow.postMessage({ kind: "blokebot-full-init", lifetime: identity }, "*", [channel.port2]);
                  }, { once: true });
                  child.src = "/full-overlay/assets/frame";
                } else {
                  child.src = widget.content.url;
                  if (kind === "plugin") {
                    plugin = child;
                    child.addEventListener("load", () => child.contentWindow.postMessage({ kind: "blokebot-widget-state", state: widget.content.state }, "*"));
                  }
                }
              } else if (kind === "media") {
                const type = widget.content.contentType.split("/")[0];
                if (!["image", "audio", "video"].includes(type)) { reporter(widget)("widget-unavailable"); return; }
                const media = document.createElement(type === "image" ? "img" : type);
                media.src = widget.content.url;
                if (media instanceof HTMLMediaElement) {
                  media.autoplay = true; media.loop = widget.content.loop; media.muted = widget.audio.isMuted;
                  media.volume = widget.audio.volume;
                  const blocked = reporter(widget);
                  host.append(media); void media.play().catch(() => blocked("audio-blocked"));
                } else { media.alt = ""; host.append(media); }
              }
            };
            const dispose = () => { cleanup(); host.remove(); };
            return { update, dispose, diagnostics };
          };
          window.blokeBotFullWidgets = { mount };
        })();
        """;
}
