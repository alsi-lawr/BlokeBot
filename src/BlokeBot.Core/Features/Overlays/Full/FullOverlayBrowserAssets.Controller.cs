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
          let observation = null;
          let localSource = null;
          let sourceCompletion = null;
          let authorizedWidgetIds = null;
          let authorizationRevision = -1;
          let currentDiagnostics = [];
          let presentationSequence = 0;
          let presentation = null;
          const completions = new Map();
          const finished = new Set();
          const clear = () => {
            port?.close(); port = null; loaded = false; pending = null; lifetime = null; presentation = null; sourceCompletion = null; currentDiagnostics = [];
            for (const timer of completions.values()) clearTimeout(timer);
            completions.clear(); finished.clear();
          };
          const notice = (state, diagnostics = preview ? currentDiagnostics : []) => {
            if (preview) {
              const allowed = new Set(pending ? renderWidgets().map(widget => widget.id) : []);
              diagnostics = diagnostics.filter(item => allowed.has(item.widgetId));
              currentDiagnostics = diagnostics;
              if (state === "ready") status.textContent = "";
            }
            document.body.dataset.status = state;
            document.body.dataset.diagnostics = JSON.stringify(diagnostics);
            if (diagnostics.some(item => item.code === "audio-blocked"))
              status.textContent = "Overlay audio blocked by browser autoplay";
            if (preview && parent !== window) parent.postMessage({
              kind: "blokebot-full-preview-status", previewId: location.pathname.split("/").pop(),
              state, diagnostics, lifetime, revision: localSource?.revision ?? observation?.revision ?? null, requestId: observation?.requestId,
            }, location.origin);
          };
          const observe = () => {
            if (preview && !page.signal.aborted && loaded && observation && !presentation)
              port.postMessage({ kind: "observe", lifetime, ...observation });
          };
          const renderWidgets = () => authorizedWidgetIds ? pending.widgets.filter(widget => authorizedWidgetIds.includes(widget.id)) : pending.widgets;
          const render = (completion = null) => {
            if (!loaded || !pending) return;
            port.postMessage({ kind: "render", lifetime, ...pending,
              ...(preview && observation ? { requestId: observation.requestId, selectors: observation.selectors } : {}),
              ...(completion ? { sourceUpdate: true } : {}),
              ...(preview && authorizedWidgetIds ? { widgets: renderWidgets() } : {}),
              ...(localSource ? { html: localSource.html, css: localSource.css } : {}) });
          };
          if (preview) window.addEventListener("resize", observe, { signal: page.signal });
          window.addEventListener("message", (event) => {
            if (!preview || parent === window || event.source !== parent || event.origin !== location.origin) return;
            const data = event.data;
            if (data?.previewId !== location.pathname.split("/").pop()) return;
            if (data.kind === "blokebot-full-source") {
              if (typeof data.requestId !== "string" || !Number.isSafeInteger(data.revision) || data.revision < (localSource?.revision ?? -1)
                || data.revision < authorizationRevision || data.lifetime !== lifetime || !Array.isArray(data.widgetIds)
                || !data.widgetIds.every(id => typeof id === "string")) return;
              if (data.authorizationOnly === true) {
                authorizedWidgetIds = data.widgetIds; authorizationRevision = data.revision;
                render(); notice("ready"); return;
              }
              if (typeof data.html !== "string" || typeof data.css !== "string" || !Array.isArray(data.selectors)) return;
              presentation = null;
              localSource = { revision: data.revision, html: data.html, css: data.css };
              authorizedWidgetIds = data.widgetIds; authorizationRevision = data.revision;
              observation = { requestId: data.requestId, revision: data.revision,
                selectors: data.selectors.filter(item => typeof item?.key === "string" && typeof item.selector === "string") };
              sourceCompletion = { requestId: data.requestId, revision: data.revision };
              render(sourceCompletion); notice("ready"); return;
            }
            if (data.kind === "blokebot-full-present") {
              if (!loaded || !observation || data.requestId !== observation.requestId || data.revision !== observation.revision
                || typeof data.gestureId !== "string" || !Number.isSafeInteger(data.sequence) || data.sequence <= presentationSequence) return;
              const value = data.presentation;
              if (value !== null && (!value || typeof value.css !== "string" || !value.attributes || typeof value.attributes !== "object"
                || !Object.entries(value.attributes).every(([key, value]) => ["style", "data-blokebot-element"].includes(key) && typeof value === "string")
                || !observation.selectors.some(item => item.selector === value.selector))) return;
              presentationSequence = data.sequence;
              presentation = value === null ? null : { gestureId: data.gestureId, sequence: data.sequence,
                renderedSequence: presentation?.gestureId === data.gestureId ? presentation.renderedSequence : 0 };
              port.postMessage({ kind: "present", lifetime, requestId: data.requestId, gestureId: data.gestureId, sequence: data.sequence, presentation: value });
              return;
            }
            if (data.kind !== "blokebot-full-observe" || typeof data.requestId !== "string" || !Number.isSafeInteger(data.revision) || !Array.isArray(data.selectors)) return;
            presentation = null;
            observation = { requestId: data.requestId, revision: data.revision, selectors: data.selectors.filter(item => typeof item?.key === "string" && typeof item.selector === "string") };
            observe();
          });
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
                render(sourceCompletion);
              } else if (preview && value.kind === "source-complete" && value.requestId === sourceCompletion?.requestId) {
                const completed = sourceCompletion; sourceCompletion = null;
                parent.postMessage({ kind: "blokebot-full-source-complete", previewId: location.pathname.split("/").pop(),
                  lifetime, ...completed }, location.origin);
              } else if (preview && value.kind === "observations" && value.requestId === observation?.requestId && Array.isArray(value.items)) {
                if (value.gestureId !== undefined) {
                  if (value.gestureId !== presentation?.gestureId || !Number.isSafeInteger(value.sequence)
                    || value.sequence <= presentation.renderedSequence || value.sequence > presentation.sequence) return;
                  presentation.renderedSequence = value.sequence;
                }
                if (value.gestureId === undefined && presentation) return;
                const allowed = new Set(observation.selectors.map(item => item.key));
                const items = value.items.filter(item => allowed.has(item?.key)
                  && [item.x,item.y,item.width,item.height,item.layoutX,item.layoutY].every(Number.isFinite) && item.width >= 0 && item.height >= 0
                  && item.styles && Object.values(item.styles).every(style => typeof style === "string"));
                const viewport = value.viewport;
                if (viewport && [viewport.width,viewport.height].every(size => Number.isFinite(size) && size > 0))
                  parent.postMessage({ kind: "blokebot-full-observations", previewId: location.pathname.split("/").pop(), requestId: value.requestId, revision: observation.revision,
                    ...(presentation ? { gestureId: value.gestureId, sequence: value.sequence } : {}), viewport, items }, location.origin);
              } else if (value.kind === "diagnostics" && Array.isArray(value.items)) {
                if (preview && value.requestId !== observation?.requestId) return;
                const allowed = new Set(pending ? renderWidgets().map(widget => widget.id) : []);
                const codes = new Set(["missing-anchor", "widget-unavailable", "audio-blocked"]);
                const items = value.items.filter(item => allowed.has(item?.widgetId) && codes.has(item?.code));
                notice("ready", items);
              }
            };
            port.start();
            frame.contentWindow.postMessage({ kind: "blokebot-full-init", lifetime: identity, privatePreview: preview }, "*", [channel.port2]);
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
            render();
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
