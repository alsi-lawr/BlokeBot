namespace BlokeBot.Core.Features.Overlays.Full;

internal static partial class FullOverlayBrowserAssets
{
    internal const string Frame = """
        (() => {
          "use strict";
          const mount = window.blokeBotFullWidgets.mount;
          let port = null;
          let lifetime = null;
          let source = null;
          let html = null;
          let authored = null;
          let presentation = null;
          let presentationSequence = 0;
          const widgets = new Map();
          let observation = null;
          let privatePreview = false;
          const observe = () => {
            if (!observation || !port) return;
            const properties = ["left","right","top","bottom","width","height","font-family","font-size","font-weight","color","background-color","padding","border-radius","box-shadow","transform","rotate","scale","translate","display","overflow"];
            const items = observation.selectors.flatMap(item => {
              let node;
              try { node = document.querySelector(item.selector); } catch { return []; }
              if (!node) return [];
              const rect = node.getBoundingClientRect(); const computed = getComputedStyle(node);
              return [{ key: item.key, x: rect.x, y: rect.y, width: rect.width, height: rect.height, layoutX: node.offsetLeft, layoutY: node.offsetTop,
                styles: Object.fromEntries(properties.map(property => [property, computed.getPropertyValue(property)])) }];
            });
            port.postMessage({ kind: "observations", lifetime, requestId: observation.requestId,
              ...(presentation ? { gestureId: presentation.gestureId, sequence: presentation.sequence } : {}),
              viewport: { width: innerWidth, height: innerHeight }, items });
          };
          const measure = () => privatePreview ? observe() : requestAnimationFrame(observe);
          const dispose = () => { for (const widget of widgets.values()) widget.dispose(); widgets.clear(); };
          const restorePresentation = () => {
            if (!presentation) return;
            for (const [key, value] of Object.entries(presentation.attributes))
              if (value === null) presentation.node.removeAttribute(key); else presentation.node.setAttribute(key, value);
            authored.textContent = presentation.css;
            presentation = null;
          };
          window.addEventListener("message", (event) => {
            if (port || event.source !== parent || parent === window || event.ports.length !== 1) return;
            const value = event.data;
            if (value?.kind !== "blokebot-full-init" || typeof value.lifetime !== "string") return;
            lifetime = value.lifetime; privatePreview = value.privatePreview === true; port = event.ports[0];
            port.onmessage = (message) => {
              const data = message.data;
              if (data?.kind === "observe" && data.lifetime === lifetime && typeof data.requestId === "string" && Array.isArray(data.selectors)) {
                restorePresentation();
                observation = { requestId: data.requestId, selectors: data.selectors.filter(item => typeof item?.key === "string" && typeof item.selector === "string") };
                measure(); return;
              }
              if (data?.kind === "present" && data.lifetime === lifetime) {
                if (!authored || data.requestId !== observation?.requestId || typeof data.gestureId !== "string"
                  || !Number.isSafeInteger(data.sequence) || data.sequence <= presentationSequence) return;
                const value = data.presentation;
                if (value !== null && (!value || typeof value.css !== "string" || !value.attributes || typeof value.attributes !== "object"
                  || !Object.entries(value.attributes).every(([key, value]) => ["style", "data-blokebot-element"].includes(key) && typeof value === "string")
                  || !observation.selectors.some(item => item.selector === value.selector))) return;
                presentationSequence = data.sequence; restorePresentation();
                if (value !== null) {
                  let node;
                  try { node = document.querySelector(value.selector); } catch { return; }
                  if (!node) return;
                  presentation = { node, gestureId: data.gestureId, sequence: data.sequence, css: authored.textContent,
                    attributes: Object.fromEntries(Object.keys(value.attributes).map(key => [key, node.getAttribute(key)])) };
                  for (const [key, attributeValue] of Object.entries(value.attributes)) node.setAttribute(key, attributeValue);
                  authored.textContent = value.css;
                }
                measure(); return;
              }
              if (data?.kind !== "render" || data.lifetime !== lifetime || typeof data.html !== "string"
                || typeof data.css !== "string" || !Array.isArray(data.widgets)) return;
              if (privatePreview && typeof data.requestId === "string" && Array.isArray(data.selectors))
                observation = { requestId: data.requestId, selectors: data.selectors };
              const diagnostics = [];
              const report = (widgetId, code) => {
                diagnostics.push({ widgetId, code });
                port.postMessage({ kind: "diagnostics", lifetime, requestId: data.requestId, items: diagnostics });
              };
              if (source !== data.html + "\0" + data.css) {
                restorePresentation();
                source = data.html + "\0" + data.css;
                if (privatePreview && html === data.html) {
                  authored.textContent = data.css;
                } else {
                  dispose(); html = data.html;
                  document.open(); document.write(data.html); document.close();
                  const baseline = document.createElement("style");
                  baseline.textContent = "html,body{margin:0;background:transparent} [data-full-widget-renderer]{position:relative;width:100%;height:100%} [data-full-widget-renderer]>iframe{width:100%;height:100%;border:0} [data-full-widget-renderer] svg{display:block;width:100%;height:100%;overflow:visible} [data-full-widget-renderer] .cue-run,[data-full-widget-renderer] .cue-layer{position:absolute;border:0} [data-full-widget-renderer] .cue-run{inset:0}";
                  const sheet = document.createElement("link"); sheet.rel = "stylesheet";
                  sheet.href = "/full-overlay/assets/presentation.css";
                  authored = document.createElement("style"); authored.textContent = data.css;
                  document.head.prepend(baseline, sheet); document.head.append(authored);
                }
              }
              const current = new Set(data.widgets.map(widget => widget.id));
              for (const [id, widget] of widgets) if (!current.has(id)) { widget.dispose(); widgets.delete(id); }
              for (const widget of data.widgets) {
                if (!widget || typeof widget.id !== "string" || typeof widget.kind !== "string") continue;
                const anchor = document.querySelector(`[data-blokebot-widget="${CSS.escape(widget.id)}"]`);
                if (!anchor) { report(widget.id, "missing-anchor"); continue; }
                try {
                  if (!widgets.has(widget.id)) widgets.set(widget.id, mount(anchor, widget, report));
                  widgets.get(widget.id).update(widget);
                } catch {
                  widgets.get(widget.id)?.dispose(); widgets.delete(widget.id);
                  report(widget.id, "widget-unavailable");
                }
              }
              port.postMessage({ kind: "diagnostics", lifetime, requestId: data.requestId, items: diagnostics });
              measure();
            };
            port.start(); port.postMessage({ kind: "ready", lifetime });
          });
          window.addEventListener("resize", measure);
          window.addEventListener("pagehide", () => { restorePresentation(); dispose(); port?.close(); }, { once: true });
        })();
        """;
}
