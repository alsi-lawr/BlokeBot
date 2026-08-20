import {
  applyTransform,
  defaultZoomIndex,
  reconcileDisclosure,
  renderedDisclosure,
  requestDisclosure,
  restoreViewport,
  selectedEdgeId,
  selectionIds,
  setLocalDisclosure,
  setLocalSelection,
  updateEditorHeight,
} from "./AutomationFlowCanvasState.js";
import {
  beginConnectionDrag,
  cancelConnection,
  cancelConnectionDrag,
  compatibleTarget,
  completeConnection,
  finishConnectionDrag,
  startConnection,
  updateConnectionPreview,
} from "./AutomationFlowConnections.js";
import {
  beginBackgroundAction,
  beginNodeDrag,
  cancelMarqueeUpdate,
  finishBackgroundAction,
  finishNodeDrag,
  moveBackgroundAction,
  moveNodeDrag,
} from "./AutomationFlowGestures.js";
import { cancelDragRouteFrame } from "./AutomationFlowDragRouting.js";
import { cancelRoutePass, routeReference, scheduleRoutePass } from "./AutomationFlowRoutePass.js";
import { changeZoom, isEditingControl, moveSelectionByKeyboard, register } from "./AutomationFlowCanvasControls.js";

const states = new WeakMap();
const activeStates = new Set();

globalThis.__blokeBotAutomationRouteReference = (root = null) => {
  const state = root === null ? [...activeStates][0] : states.get(root);
  if (state === undefined) throw new Error("The automation canvas is not initialized.");
  return routeReference(state);
};

export function initialize(root, dotnet) {
  if (!(root instanceof HTMLElement) || states.has(root)) return;
  const shell = root.closest("[data-automation-canvas-shell]");
  const stage = root.querySelector(".automation-canvas-stage");
  const preview = root.querySelector("[data-connection-preview]");
  const marquee = root.querySelector("[data-automation-marquee]");
  const zoomReset = shell?.querySelector("[data-canvas-zoom-reset]");
  const zoomResetLabel = zoomReset?.querySelector("[data-canvas-zoom-label]");
  if (!(shell instanceof HTMLElement)
    || !(stage instanceof HTMLElement)
    || !(preview instanceof SVGPathElement)
    || !(marquee instanceof HTMLElement)
    || !(zoomReset instanceof HTMLButtonElement)
    || !(zoomResetLabel instanceof HTMLElement)) return;

  const state = {
    root,
    shell,
    stage,
    preview,
    marquee,
    zoomReset,
    zoomResetLabel,
    dotnet,
    viewportKey: shell.dataset.viewportKey ?? "",
    zoomIndex: defaultZoomIndex,
    panX: 0,
    panY: 0,
    zoomTransition: null,
    drag: null,
    panState: null,
    marqueeState: null,
    connection: null,
    connectionDrag: null,
    suppressPortClick: false,
    routePass: null,
    routedSignature: null,
    dragFrame: null,
    dragRouting: null,
    marqueeFrame: null,
    sceneCache: null,
    portOffsets: new Map(),
    nodeSignatures: new Map(),
    listeners: [],
    disclosureGeneration: renderedDisclosure(root).generation,
    pendingDisclosure: null,
    disclosureObserver: null,
  };

  state.disclosureObserver = new MutationObserver(() => reconcileDisclosure(state));
  state.disclosureObserver.observe(root, {
    attributes: true,
    subtree: true,
    attributeFilter: [
      "class",
      "aria-expanded",
      "data-disclosed-node-id",
      "data-disclosure-generation",
    ],
  });

  register(state, root, "pointerdown", (event) => {
    const port = event.target instanceof Element ? event.target.closest("[data-automation-port]") : null;
    if (
      port instanceof HTMLButtonElement
      && port.dataset.portDirection === "output"
      && event.button === 0
    ) {
      beginConnectionDrag(state, event, port);
      return;
    }
    if (port !== null) return;
    const node = event.target instanceof Element ? event.target.closest("[data-automation-node]") : null;
    if (node instanceof HTMLElement && event.button === 0 && !event.altKey) {
      beginNodeDrag(state, event, node);
      return;
    }
    beginBackgroundAction(state, event);
  });
  register(state, root, "pointermove", (event) => {
    if (moveNodeDrag(state, event) || moveBackgroundAction(state, event)) return;
    updateConnectionPreview(state, event.clientX, event.clientY);
  });
  register(state, root, "pointerup", (event) => {
    if (finishConnectionDrag(state, event)) return;
    if (finishNodeDrag(state, event)) return;
    finishBackgroundAction(state, event);
  });
  register(state, root, "pointercancel", (event) => {
    if (finishConnectionDrag(state, event, true)) return;
    if (finishNodeDrag(state, event, true)) return;
    finishBackgroundAction(state, event);
  });
  register(state, root, "click", (event) => {
    const selector = event.target instanceof Element
      ? event.target.closest("[data-automation-node-select]")
      : null;
    if (selector instanceof HTMLButtonElement) {
      event.preventDefault();
      event.stopPropagation();
      if (event.detail !== 0 || event.altKey || event.ctrlKey || event.metaKey) return;
      const nodeElement = selector.closest("[data-automation-node]");
      const nodeId = nodeElement?.dataset.automationNode;
      if (nodeId === undefined) return;
      if (event.shiftKey) {
        requestDisclosure(state, null);
        scheduleRoutePass(state);
        void state.dotnet.invokeMethodAsync("ToggleNodeSelectionFromCanvasAsync", nodeId);
        return;
      }
      const wasDisclosed = nodeElement.classList.contains("automation-node--disclosed");
      setLocalSelection(state.root, [nodeId], null);
      requestDisclosure(state, wasDisclosed ? null : nodeId);
      scheduleRoutePass(state);
      return;
    }
    const port = event.target instanceof Element ? event.target.closest("[data-automation-port]") : null;
    if (!(port instanceof HTMLButtonElement)) return;
    event.preventDefault();
    event.stopPropagation();
    if (state.suppressPortClick) {
      state.suppressPortClick = false;
      return;
    }
    if (port.dataset.portDirection === "output") {
      startConnection(state, port);
      return;
    }
    if (!compatibleTarget(state, port)) return;
    completeConnection(state, port);
  }, true);
  register(state, root, "wheel", (event) => {
    if (!event.ctrlKey || event.deltaY === 0) return;
    event.preventDefault();
    changeZoom(state, event.deltaY < 0 ? 1 : -1, event.clientX, event.clientY);
  }, { passive: false });
  register(state, root, "keydown", (event) => {
    if (event.key === "Escape") {
      cancelConnectionDrag(state);
      return;
    }
    if (isEditingControl(event.target)) return;
    if (moveSelectionByKeyboard(state, event.key)) {
      event.preventDefault();
      event.stopPropagation();
      return;
    }
    if (event.key !== "Delete" && event.key !== "Backspace") return;
    event.preventDefault();
    event.stopPropagation();
    void dotnet.invokeMethodAsync(
      "DeleteSelectionFromCanvasAsync",
      selectionIds(root),
      selectedEdgeId(root),
    );
  });
  register(state, zoomReset, "click", () => {
    state.zoomIndex = defaultZoomIndex;
    state.panX = 0;
    state.panY = 0;
    state.zoomTransition = null;
    applyTransform(state);
  });
  register(state, window, "resize", () => {
    updateEditorHeight(state);
    scheduleRoutePass(state);
  });

  states.set(root, state);
  activeStates.add(state);
  reconcileDisclosure(state);
  restoreViewport(state);
  updateEditorHeight(state);
  scheduleRoutePass(state);
}

export function refresh(root) {
  const state = states.get(root);
  if (state === undefined) return;
  const stage = root.querySelector(".automation-canvas-stage");
  if (!(stage instanceof HTMLElement)) return;
  state.stage = stage;
  reconcileDisclosure(state);
  state.portOffsets.clear();
  state.preview = root.querySelector("[data-connection-preview]");
  state.marquee = root.querySelector("[data-automation-marquee]");
  if (state.viewportKey !== (state.shell.dataset.viewportKey ?? "")) {
    restoreViewport(state);
  } else {
    applyTransform(state);
  }
  requestAnimationFrame(() => {
    if (activeStates.has(state)) updateEditorHeight(state);
  });
  if (state.drag === null) {
    for (const node of root.querySelectorAll("[data-automation-node]")) {
      delete node.dataset.automationGraphX;
      delete node.dataset.automationGraphY;
      node.style.removeProperty("transform");
      node.style.removeProperty("left");
      node.style.removeProperty("top");
    }
  }
  cancelConnection(state);
  cancelDragRouteFrame(state);
  // A render can change geometry under an in-flight drag, so the session's
  // frame, port anchors, and base graph are rebuilt on the next drag frame.
  state.dragRouting = null;
  cancelRoutePass(state);
  scheduleRoutePass(state);
}

export function focusNode(root, nodeId) {
  root.querySelector(
    `[data-automation-node="${CSS.escape(nodeId)}"] [data-automation-node-select]`,
  )?.focus({ preventScroll: true });
}

export function dispose(root) {
  const state = states.get(root);
  if (state === undefined) return;
  cancelDragRouteFrame(state);
  cancelMarqueeUpdate(state);
  cancelRoutePass(state);
  state.disclosureObserver?.disconnect();
  for (const remove of state.listeners.reverse()) remove();
  activeStates.delete(state);
  states.delete(root);
}
