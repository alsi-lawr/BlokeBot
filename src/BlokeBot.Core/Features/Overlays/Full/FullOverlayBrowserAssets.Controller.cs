namespace BlokeBot.Core.Features.Overlays.Full;

internal static partial class FullOverlayBrowserAssets
{
    internal const string Controller = """
        (() => {
          "use strict";
          const frame = document.getElementById("full-overlay");
          const status = document.getElementById("render-status");
          const page = new AbortController();
          const preview = location.pathname.startsWith("/full-overlays/preview/");
          const credentials = preview ? "same-origin" : "omit";
          let port = null;
          let lifetime = null;
          let connection = null;
          let generation = null;
          let pending = null;
          let loaded = false;
          let retry = 500;
          const completions = new Map();
          const finished = new Set();
          const clear = () => {
            port?.close(); port = null; loaded = false; pending = null; lifetime = null;
            for (const timer of completions.values()) clearTimeout(timer);
            completions.clear(); finished.clear();
          };
          const notice = (state, diagnostics = []) => {
            document.body.dataset.status = state;
            document.body.dataset.diagnostics = JSON.stringify(diagnostics);
            if (diagnostics.some(item => item.code === "audio-blocked"))
              status.textContent = "Overlay audio blocked by browser autoplay";
            if (preview && parent !== window) parent.postMessage({
              kind: "blokebot-full-preview-status", previewId: location.pathname.split("/").pop(),
              state, diagnostics,
            }, location.origin);
          };
          frame.addEventListener("load", () => {
            // Authored document.write loads again without changing the render lifetime.
            if (!lifetime || port) return;
            const identity = lifetime;
            const channel = new MessageChannel();
            port = channel.port1;
            port.onmessage = (event) => {
              const value = event.data;
              if (lifetime !== identity || value?.lifetime !== identity) return;
              if (value.kind === "ready") {
                loaded = true;
                if (pending) port.postMessage({ kind: "render", lifetime: identity, ...pending });
              } else if (value.kind === "diagnostics" && Array.isArray(value.items)) {
                const allowed = new Set(pending?.widgets.map(widget => widget.id));
                const codes = new Set(["missing-anchor", "widget-unavailable", "audio-blocked"]);
                const items = value.items.filter(item => allowed.has(item?.widgetId) && codes.has(item?.code));
                notice("ready", items);
              }
            };
            port.start();
            frame.contentWindow.postMessage({ kind: "blokebot-full-init", lifetime: identity }, "*", [channel.port2]);
          });
          const apply = (value) => {
            if (!value || typeof value.connectionId !== "string" || !Number.isSafeInteger(value.generation)
              || typeof value.html !== "string" || typeof value.css !== "string" || !Array.isArray(value.widgets)) return false;
            if (connection !== value.connectionId || generation !== value.generation) {
              clear(); connection = value.connectionId; generation = value.generation;
              lifetime = crypto.randomUUID();
              frame.src = "/full-overlay/assets/frame";
            }
            pending = { html: value.html, css: value.css, widgets: value.widgets };
            if (loaded) port.postMessage({ kind: "render", lifetime, ...pending });
            const active = new Map(value.widgets.filter(widget => widget.kind === "cue")
              .flatMap(widget => widget.content).map(plan => [plan.runId, plan]));
            for (const [id, timer] of completions) if (!active.has(id)) { clearTimeout(timer); completions.delete(id); }
            for (const id of finished) if (!active.has(id)) finished.delete(id);
            if (!preview) for (const [id, plan] of active) if (!completions.has(id) && !finished.has(id)) {
              const identity = connection;
              completions.set(id, setTimeout(() => {
                completions.delete(id); finished.add(id);
                if (connection !== identity || page.signal.aborted) return;
                void fetch(`${location.pathname}/complete/${encodeURIComponent(identity)}/${encodeURIComponent(id)}`, {
                  method: "POST", credentials: "omit", cache: "no-store", signal: page.signal,
                }).catch(() => {});
              }, plan.durationMilliseconds));
            }
            retry = 500; status.textContent = ""; notice("ready"); return true;
          };
          const consume = async () => {
            const response = await fetch(`${location.pathname}/events`, { credentials, cache: "no-store",
              headers: { Accept: "text/event-stream" }, signal: page.signal });
            if (!response.ok || !response.body) return;
            const reader = response.body.getReader(); const decoder = new TextDecoder(); let buffer = "";
            try {
              while (!page.signal.aborted) {
                const next = await reader.read();
                buffer += decoder.decode(next.value ?? new Uint8Array(), { stream: !next.done });
                let boundary;
                while ((boundary = buffer.indexOf("\n\n")) >= 0) {
                  const block = buffer.slice(0, boundary); buffer = buffer.slice(boundary + 2);
                  const data = block.split("\n").filter(line => line.startsWith("data:")).map(line => line.slice(5)).join("\n");
                  if (data && !apply(JSON.parse(data))) return;
                }
                if (next.done) return;
              }
            } finally { await reader.cancel(); reader.releaseLock(); }
          };
          const run = async () => {
            while (!page.signal.aborted) {
              try { await consume(); } catch { if (page.signal.aborted) return; }
              clear(); connection = null; generation = null; frame.removeAttribute("src");
              status.textContent = "Overlay reconnecting"; notice("reconnecting");
              await new Promise(resolve => setTimeout(resolve, retry));
              retry = Math.min(30000, retry * 2);
            }
          };
          window.addEventListener("pagehide", () => { page.abort(); clear(); }, { once: true });
          void run();
        })();
        """;
}
