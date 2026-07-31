export async function copyText(value) {
  await navigator.clipboard.writeText(value);
}

const editorStates = new WeakMap();

export function initializeAppearance(dotnet) {
  const editor = document.querySelector("[data-appearance-editor]");
  const frame = document.querySelector("[data-appearance-preview]");
  if (!(editor instanceof SVGSVGElement) || !(frame instanceof HTMLIFrameElement)) return;

  const read = () => ({
    x: Number(editor.dataset.x),
    y: Number(editor.dataset.y),
    width: Number(editor.dataset.width),
    height: Number(editor.dataset.height),
  });
  const existing = editorStates.get(editor);
  if (existing !== undefined) {
    existing.resync(frame);
    return;
  }

  let activeFrame = frame;
  let geometry = read();
  let scopedCss = "";
  let desiredCss = editor.dataset.renderedCss ?? "";
  let validatedCssIdentity = null;
  let cssGeneration = 0;
  let selectionGeneration = 0;
  let frameLoaded = false;
  let activeRequestId = null;
  let requestSequence = 0;
  const boundFrames = new WeakSet();
  const boundCssInputs = new WeakSet();

  const draftChoices = () => ({
    type: activeFrame.dataset.draftType ?? "",
    showGuessCount: activeFrame.dataset.showGuessCount === "true",
    giveawayTitle: activeFrame.dataset.giveawayTitle ?? "",
    showEntrantCount: activeFrame.dataset.showEntrantCount === "true",
    showCountdown: activeFrame.dataset.showCountdown === "true",
    showJoinCommand: activeFrame.dataset.showJoinCommand === "true",
  });
  const sendDraft = (requestId) => {
    activeFrame.contentWindow?.postMessage(
      { kind: "blokebot-dashboard-draft", requestId, overlayId: activeFrame.dataset.overlayId ?? "", appearance: geometry, css: scopedCss, choices: draftChoices() },
      window.location.origin,
    );
  };
  const cssInput = () => document.querySelector("[data-appearance-css]");
  const cssIdentity = (rawCss) => `${activeFrame.dataset.overlayId ?? ""}\n${rawCss}`;
  const hideFrame = (target) => {
    target.classList.remove("overlay-preview-frame--ready");
    target.setAttribute("aria-busy", "true");
  };
  const requestDraftInstall = () => {
    if (!frameLoaded || validatedCssIdentity !== cssIdentity(desiredCss)) return;
    const requestId = `${selectionGeneration}:${++requestSequence}`;
    activeRequestId = requestId;
    sendDraft(requestId);
  };
  const validateAndSendCss = async (rawCss = desiredCss) => {
    geometry = read();
    desiredCss = rawCss;
    const identity = cssIdentity(rawCss);
    if (identity === validatedCssIdentity) {
      requestDraftInstall();
      return;
    }

    const generation = ++cssGeneration;
    const result = await dotnet.invokeMethodAsync("ScopeDraftCss", rawCss);
    if (generation !== cssGeneration || cssIdentity(desiredCss) !== identity) return;
    scopedCss = typeof result === "string" ? result : "";
    validatedCssIdentity = identity;
    requestDraftInstall();
  };
  const bindDraftSources = (nextFrame) => {
    const selectionChanged = nextFrame !== activeFrame;
    activeFrame = nextFrame;
    if (!boundFrames.has(nextFrame)) {
      boundFrames.add(nextFrame);
      nextFrame.addEventListener("load", () => {
        if (nextFrame !== activeFrame) return;
        frameLoaded = true;
        requestDraftInstall();
      });
    }
    const input = cssInput();
    if (input instanceof HTMLTextAreaElement && !boundCssInputs.has(input)) {
      boundCssInputs.add(input);
      input.addEventListener("input", () => void validateAndSendCss(input.value));
    }
    const renderedGeometry = read();
    if (selectionChanged || selectionGeneration === 0 || (!dotNetBusy && pendingForDotNet === null)) {
      geometry = renderedGeometry;
    } else {
      paint(geometry, false);
    }
    desiredCss = editor.dataset.renderedCss ?? "";
    if (selectionChanged || selectionGeneration === 0) {
      selectionGeneration++;
      cssGeneration++;
      frameLoaded = nextFrame.contentDocument?.readyState === "complete";
      activeRequestId = null;
      validatedCssIdentity = null;
      scopedCss = "";
      hideFrame(nextFrame);
    }
    void validateAndSendCss(desiredCss);
  };

  window.addEventListener("message", (event) => {
    if (event.origin !== window.location.origin || event.source !== activeFrame.contentWindow) return;
    const value = event.data;
    if (
      typeof value !== "object"
      || value === null
      || value.kind !== "blokebot-dashboard-draft-ready"
      || value.requestId !== activeRequestId
      || value.overlayId !== (activeFrame.dataset.overlayId ?? "")
    ) return;
    activeFrame.classList.add("overlay-preview-frame--ready");
    activeFrame.removeAttribute("aria-busy");
  });

  let pendingForDotNet = null;
  let dotNetBusy = false;
  const flushDotNet = async () => {
    if (dotNetBusy || pendingForDotNet === null) return;
    dotNetBusy = true;
    const value = pendingForDotNet;
    pendingForDotNet = null;
    try {
      await dotnet.invokeMethodAsync("UpdateAppearance", value.x, value.y, value.width, value.height);
    } finally {
      dotNetBusy = false;
      if (pendingForDotNet !== null) void flushDotNet();
    }
  };

  const setRect = (selector, x, y, width, height) => {
    const rect = editor.querySelector(selector);
    if (!(rect instanceof SVGRectElement)) return;
    rect.setAttribute("x", String(x));
    rect.setAttribute("y", String(y));
    rect.setAttribute("width", String(width));
    rect.setAttribute("height", String(height));
  };
  const paint = (value, notifyDotNet = true) => {
    geometry = value;
    editor.dataset.x = String(value.x);
    editor.dataset.y = String(value.y);
    editor.dataset.width = String(value.width);
    editor.dataset.height = String(value.height);
    setRect("[data-selection-line]", value.x, value.y, value.width, value.height);
    setRect('[data-appearance-action="move"]', value.x, value.y, value.width, value.height);
    setRect('[data-appearance-action="w"]', value.x - 14, value.y + 28, 28, value.height - 56);
    setRect('[data-appearance-action="e"]', value.x + value.width - 14, value.y + 28, 28, value.height - 56);
    setRect('[data-appearance-action="n"]', value.x + 28, value.y - 14, value.width - 56, 28);
    setRect('[data-appearance-action="s"]', value.x + 28, value.y + value.height - 14, value.width - 56, 28);
    setRect('[data-appearance-action="nw"]', value.x - 20, value.y - 20, 40, 40);
    setRect('[data-appearance-action="ne"]', value.x + value.width - 20, value.y - 20, 40, 40);
    setRect('[data-appearance-action="sw"]', value.x - 20, value.y + value.height - 20, 40, 40);
    setRect('[data-appearance-action="se"]', value.x + value.width - 20, value.y + value.height - 20, 40, 40);
    for (const [id, field] of [["appearance-x", "x"], ["appearance-y", "y"], ["appearance-width", "width"], ["appearance-height", "height"]]) {
      const input = document.getElementById(id);
      if (input instanceof HTMLInputElement) input.value = String(value[field]);
    }
    requestDraftInstall();
    if (!notifyDotNet) return;
    pendingForDotNet = value;
    void flushDotNet();
  };

  const constrain = (start, action, dx, dy) => {
    if (action === "move") {
      return { ...start, x: Math.max(0, Math.min(1920 - start.width, Math.round(start.x + dx))), y: Math.max(0, Math.min(1080 - start.height, Math.round(start.y + dy))) };
    }
    let left = start.x;
    let top = start.y;
    let right = start.x + start.width;
    let bottom = start.y + start.height;
    if (action.includes("w")) left = Math.max(0, Math.min(right - 160, Math.round(start.x + dx)));
    if (action.includes("e")) right = Math.min(1920, Math.max(left + 160, Math.round(start.x + start.width + dx)));
    if (action.includes("n")) top = Math.max(0, Math.min(bottom - 90, Math.round(start.y + dy)));
    if (action.includes("s")) bottom = Math.min(1080, Math.max(top + 90, Math.round(start.y + start.height + dy)));
    return { x: left, y: top, width: right - left, height: bottom - top };
  };

  let scheduled = null;
  let animationFrame = null;
  const schedule = (value) => {
    scheduled = value;
    if (animationFrame !== null) return;
    animationFrame = window.requestAnimationFrame(() => {
      animationFrame = null;
      if (scheduled !== null) paint(scheduled);
      scheduled = null;
    });
  };

  for (const target of editor.querySelectorAll("[data-appearance-action]")) {
    if (!(target instanceof SVGRectElement)) continue;
    const action = target.dataset.appearanceAction ?? "";
    target.addEventListener("keydown", (event) => {
      if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) return;
      event.preventDefault();
      const amount = event.shiftKey ? 10 : 1;
      const dx = event.key === "ArrowLeft" ? -amount : event.key === "ArrowRight" ? amount : 0;
      const dy = event.key === "ArrowUp" ? -amount : event.key === "ArrowDown" ? amount : 0;
      schedule(constrain(geometry, action, dx, dy));
    });
    target.addEventListener("pointerdown", (event) => {
      event.preventDefault();
      target.setPointerCapture(event.pointerId);
      const start = geometry;
      const matrix = editor.getScreenCTM();
      if (matrix === null) return;
      const point = (source) => {
        const value = editor.createSVGPoint();
        value.x = source.clientX;
        value.y = source.clientY;
        return value.matrixTransform(matrix.inverse());
      };
      const origin = point(event);
      const move = (next) => {
        const current = point(next);
        schedule(constrain(start, action, current.x - origin.x, current.y - origin.y));
      };
      const finish = () => {
        target.removeEventListener("pointermove", move);
        target.removeEventListener("pointerup", finish);
        target.removeEventListener("pointercancel", finish);
      };
      target.addEventListener("pointermove", move);
      target.addEventListener("pointerup", finish);
      target.addEventListener("pointercancel", finish);
    });
  }
  editorStates.set(editor, {
    resync: (nextFrame) => bindDraftSources(nextFrame),
  });
  bindDraftSources(frame);
}
