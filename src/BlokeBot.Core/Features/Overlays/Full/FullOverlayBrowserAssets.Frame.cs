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
          let diagnosticRequestId = null;
          let currentDiagnostics = [];
          const reportCurrent = () => {
            if (!port) return;
            port.postMessage({ kind: "diagnostics", lifetime, requestId: diagnosticRequestId,
              items: [...currentDiagnostics, ...[...widgets].flatMap(([widgetId, widget]) =>
                widget.anchor.isConnected && document.querySelector(`[data-blokebot-widget="${CSS.escape(widgetId)}"]`) === widget.anchor
                  ? [...widget.renderer.diagnostics].map(code => ({ widgetId, code })) : [])] });
          };
          const observe = () => {
            if (!observation || !port) return;
            const properties = ["left","right","top","bottom","width","height","font-family","font-size","font-weight","color","background-color","background-image","text-align","opacity","gap","padding","border-radius","box-shadow","transform","rotate","scale","translate","display","overflow","--blokebot-x","--blokebot-y","--blokebot-anchor-x","--blokebot-anchor-y"];
            const items = observation.selectors.flatMap(item => {
              let node;
              try { const nodes = document.querySelectorAll(item.selector); if (nodes.length !== 1) return []; node = nodes[0]; } catch { return []; }
              const rect = node.getBoundingClientRect(); const computed = getComputedStyle(node);
              const parent = node.offsetParent;
              const parentStyle = parent ? getComputedStyle(parent) : null;
              const initialContainingBlock = parent === document.body && parentStyle.position === "static"
                && parentStyle.transform === "none" && parentStyle.perspective === "none" && parentStyle.filter === "none"
                && parentStyle.contain === "none" && parentStyle.willChange === "auto";
              return [{ key: item.key, x: rect.x, y: rect.y, width: rect.width, height: rect.height, layoutX: node.offsetLeft, layoutY: node.offsetTop,
                containingWidth: initialContainingBlock ? innerWidth : parent?.clientWidth ?? innerWidth,
                containingHeight: initialContainingBlock ? innerHeight : parent?.clientHeight ?? innerHeight,
                visible: computed.display !== "none" && !["hidden", "collapse"].includes(computed.visibility),
                styles: Object.fromEntries(properties.map(property => [property, computed.getPropertyValue(property)])) }];
            });
            port.postMessage({ kind: "observations", lifetime, requestId: observation.requestId,
              ...(presentation ? { gestureId: presentation.gestureId, sequence: presentation.sequence } : {}),
              viewport: { width: innerWidth, height: innerHeight }, items });
          };
          const measure = () => privatePreview ? observe() : requestAnimationFrame(observe);
          const dispose = () => { for (const widget of widgets.values()) widget.renderer.dispose(); widgets.clear(); };
          const restorePresentation = () => {
            if (!presentation) return;
            for (const patch of presentation.patches) for (const [key, value] of Object.entries(patch.attributes))
              if (value === null) patch.node.removeAttribute(key); else patch.node.setAttribute(key, value);
            authored.textContent = presentation.css;
            presentation = null;
          };
          const disconnect = () => {
            restorePresentation(); dispose(); port?.close();
            if (privatePreview) { port = null; lifetime = null; }
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
                if (privatePreview) { diagnosticRequestId = data.requestId; reportCurrent(); }
                measure(); return;
              }
              if (data?.kind === "present" && data.lifetime === lifetime) {
                if (!privatePreview || !authored || data.requestId !== observation?.requestId || typeof data.gestureId !== "string"
                  || !Number.isSafeInteger(data.sequence) || data.sequence <= presentationSequence) return;
                const value = data.presentation;
                if (value !== null && (!value || typeof value.css !== "string" || !Array.isArray(value.patches) || !value.patches.length
                  || new Set(value.patches.map(patch => patch?.selector)).size !== value.patches.length
                  || !value.patches.every(patch => patch && patch.attributes && typeof patch.attributes === "object" && !Array.isArray(patch.attributes)
                    && Object.entries(patch.attributes).every(([key, value]) => ["style", "data-blokebot-element"].includes(key) && typeof value === "string")
                    && observation.selectors.some(item => item.selector === patch.selector)))) return;
                const targets = [];
                if (value !== null) for (const patch of value.patches) {
                  let nodes;
                  try { nodes = document.querySelectorAll(patch.selector); } catch { return; }
                  if (nodes.length !== 1 || targets.some(target => target.node === nodes[0])) return;
                  targets.push({ node: nodes[0], attributes: patch.attributes });
                }
                // Admission of every native target precedes even restoration of a prior held value.
                presentationSequence = data.sequence; restorePresentation();
                if (value !== null) {
                  presentation = { patches: targets.map(patch => ({ node: patch.node,
                      attributes: Object.fromEntries(Object.keys(patch.attributes).map(key => [key, patch.node.getAttribute(key)])) })),
                    gestureId: data.gestureId, sequence: data.sequence, css: authored.textContent };
                  for (const patch of targets) for (const [key, attributeValue] of Object.entries(patch.attributes)) patch.node.setAttribute(key, attributeValue);
                  authored.textContent = value.css;
                }
                measure(); return;
              }
              if (data?.kind !== "render" || data.lifetime !== lifetime || typeof data.html !== "string"
                || typeof data.css !== "string" || !Array.isArray(data.widgets)) return;
              if (privatePreview && typeof data.requestId === "string" && Array.isArray(data.selectors))
                observation = { requestId: data.requestId, selectors: data.selectors };
              const diagnostics = [];
              if (privatePreview) { currentDiagnostics = diagnostics; diagnosticRequestId = data.requestId; }
              const report = (widgetId, code) => {
                diagnostics.push({ widgetId, code });
                if (privatePreview) reportCurrent();
                else port.postMessage({ kind: "diagnostics", lifetime, requestId: data.requestId, items: diagnostics });
              };
              if (source !== data.html + "\0" + data.css) {
                restorePresentation();
                source = data.html + "\0" + data.css;
                if (privatePreview && html === data.html) {
                  authored.textContent = data.css;
                } else {
                  dispose(); html = data.html;
                  document.open(); document.write(data.html); document.close();
                  if (privatePreview) window.addEventListener("pagehide", disconnect, { once: true });
                  const baseline = document.createElement("style");
                  baseline.textContent = "html,body{margin:0;background:transparent} [data-full-widget-renderer]{position:relative;width:100%;height:100%} [data-full-widget-renderer]>iframe{width:100%;height:100%;border:0} [data-full-widget-renderer] svg{display:block;width:100%;height:100%;overflow:visible} [data-full-widget-renderer] .cue-run,[data-full-widget-renderer] .cue-layer{position:absolute;border:0} [data-full-widget-renderer] .cue-run{inset:0}";
                  const sheet = document.createElement("link"); sheet.rel = "stylesheet";
                  sheet.href = "/full-overlay/assets/presentation.css";
                  authored = document.createElement("style"); authored.textContent = data.css;
                  document.head.prepend(baseline, sheet); document.head.append(authored);
                }
              }
              const current = new Set(data.widgets.map(widget => widget.id));
              for (const [id, widget] of widgets) if (!current.has(id)) { widget.renderer.dispose(); widgets.delete(id); }
              for (const widget of data.widgets) {
                if (!widget || typeof widget.id !== "string" || typeof widget.kind !== "string") continue;
                const anchor = document.querySelector(`[data-blokebot-widget="${CSS.escape(widget.id)}"]`);
                if (privatePreview && widgets.has(widget.id) && widgets.get(widget.id).anchor !== anchor) {
                  widgets.get(widget.id).renderer.dispose(); widgets.delete(widget.id);
                }
                if (!anchor) { report(widget.id, "missing-anchor"); continue; }
                try {
                  if (!widgets.has(widget.id)) {
                    const entry = { anchor, renderer: null };
                    const identity = lifetime;
                    const mountedReport = privatePreview ? () => {
                      if (lifetime === identity && widgets.get(widget.id) === entry && anchor.isConnected
                        && document.querySelector(`[data-blokebot-widget="${CSS.escape(widget.id)}"]`) === anchor) reportCurrent();
                    } : report;
                    entry.renderer = mount(anchor, widget, mountedReport, privatePreview);
                    widgets.set(widget.id, entry);
                  }
                  widgets.get(widget.id).renderer.update(widget);
                } catch {
                  widgets.get(widget.id)?.renderer.dispose(); widgets.delete(widget.id);
                  report(widget.id, "widget-unavailable");
                }
              }
              if (privatePreview) reportCurrent();
              else port.postMessage({ kind: "diagnostics", lifetime, requestId: data.requestId, items: diagnostics });
              measure();
              if (privatePreview && data.sourceUpdate === true)
                port.postMessage({ kind: "source-complete", lifetime, requestId: data.requestId });
            };
            port.start(); port.postMessage({ kind: "ready", lifetime });
          });
          window.addEventListener("resize", measure);
          window.addEventListener("pagehide", disconnect, { once: true });
        })();
        """;
}
