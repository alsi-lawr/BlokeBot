import {
  applyTransform,
  nodeGraphPosition,
  nodeGraphRectangle,
  notifyCompactSelection,
  notifyPointerSelection,
  requestDisclosure,
  screenToGraph,
  selectionIds,
  setLocalSelection,
  setNodeGraphPosition,
  setNodeLiveGraphPosition,
  snap,
  viewportPoint,
  zoomScale,
} from "./AutomationFlowCanvasState.js";
import { cancelConnection } from "./AutomationFlowConnections.js";
import { cancelDragRouteFrame, scheduleDragRoutes } from "./AutomationFlowDragRouting.js";
import { cancelRoutePass, scheduleRoutePass } from "./AutomationFlowRoutePass.js";

export function beginNodeDrag(state, event, node) {
  event.preventDefault();
  event.stopPropagation();
  let selected = new Set(selectionIds(state.root));
  const nodeId = node.dataset.automationNode;
  if (event.shiftKey) {
    requestDisclosure(state, null);
    if (selected.has(nodeId)) selected.delete(nodeId);
    else selected.add(nodeId);
  } else if (!selected.has(nodeId)) {
    selected = new Set([nodeId]);
  }
  setLocalSelection(state.root, selected, null);
  void notifyPointerSelection(state);
  if (!selected.has(nodeId)) return;
  node.querySelector("[data-automation-node-select]")?.focus({ preventScroll: true });
  const nodes = [...state.root.querySelectorAll("[data-automation-node]")]
    .filter((candidate) => selected.has(candidate.dataset.automationNode))
    .map((candidate) => {
      const position = nodeGraphPosition(candidate);
      return {
        element: candidate,
        nodeId: candidate.dataset.automationNode,
        startX: position.x,
        startY: position.y,
        x: position.x,
        y: position.y,
      };
    });
  try {
    node.setPointerCapture(event.pointerId);
  } catch (error) {
    if (event.isTrusted) throw error;
  }
  state.dragRouting = null;
  state.drag = {
    pointerId: event.pointerId,
    capture: node,
    startX: event.clientX,
    startY: event.clientY,
    moved: false,
    discloseOnClick: !event.shiftKey && !event.altKey && !event.ctrlKey && !event.metaKey,
    nodes,
  };
}

export function moveNodeDrag(state, event) {
  const drag = state.drag;
  if (drag === null || drag.pointerId !== event.pointerId) return false;
  const scale = zoomScale(state);
  const deltaX = (event.clientX - drag.startX) / scale;
  const deltaY = (event.clientY - drag.startY) / scale;
  if (!drag.moved && (Math.abs(deltaX) > 2 || Math.abs(deltaY) > 2)) {
    drag.moved = true;
    cancelRoutePass(state);
    requestDisclosure(state, null);
    state.root.classList.add("automation-canvas--node-dragging");
    for (const item of drag.nodes) item.element.classList.add("automation-node--moving");
  }
  if (!drag.moved) return true;
  for (const item of drag.nodes) {
    item.x = Math.max(0, item.startX + deltaX);
    item.y = Math.max(0, item.startY + deltaY);
  }
  scheduleDragRoutes(state);
  return true;
}
export function finishNodeDrag(state, event, cancelled = false) {
  const drag = state.drag;
  if (drag === null || drag.pointerId !== event.pointerId) return false;
  state.drag = null;
  if (drag.capture.hasPointerCapture(event.pointerId)) {
    drag.capture.releasePointerCapture(event.pointerId);
  }
  state.root.classList.remove("automation-canvas--node-dragging");
  const vertical = state.shell.dataset.orientation === "vertical";
  const moves = drag.nodes.map((item) => {
    item.element.classList.remove("automation-node--moving");
    const displayX = snap(item.x);
    const displayY = snap(item.y);
    setNodeGraphPosition(item.element, displayX, displayY);
    return {
      nodeId: item.nodeId,
      x: vertical ? displayY : displayX,
      y: vertical ? displayX : displayY,
    };
  });
  cancelDragRouteFrame(state);
  state.dragRouting = null;
  if (drag.moved) void state.dotnet.invokeMethodAsync("MoveNodesFromCanvasAsync", moves);
  else if (!cancelled && drag.discloseOnClick) {
    const nodeId = drag.capture.dataset.automationNode;
    const wasDisclosed = drag.capture.classList.contains("automation-node--disclosed");
    requestDisclosure(state, wasDisclosed ? null : nodeId);
  }
  scheduleRoutePass(state);
  return true;
}

export function beginBackgroundAction(state, event) {
  if (event.button !== 0) return;
  const target = event.target;
  if (!(target instanceof Element)) return;
  if (target.closest("[data-automation-node], [data-automation-edge], [data-automation-port]")) return;
  event.preventDefault();
  try {
    state.root.setPointerCapture(event.pointerId);
  } catch (error) {
    if (event.isTrusted) throw error;
  }
  if (event.altKey) {
    requestDisclosure(state, null);
    const start = screenToGraph(state, event.clientX, event.clientY);
    const nodes = [...state.root.querySelectorAll("[data-automation-node]")].map((node) => ({
      id: node.dataset.automationNode,
      rectangle: nodeGraphRectangle(node),
    }));
    state.marqueeState = { pointerId: event.pointerId, start, current: start, nodes };
    Object.assign(state.marquee.style, {
      left: `${start.x}px`,
      top: `${start.y}px`,
      width: "0px",
      height: "0px",
    });
    state.marquee.hidden = false;
    state.root.classList.add("automation-canvas--selecting");
    return;
  }
  cancelConnection(state);
  state.panState = {
    pointerId: event.pointerId,
    startX: event.clientX,
    startY: event.clientY,
    originX: state.panX,
    originY: state.panY,
    moved: false,
  };
  state.root.classList.add("automation-canvas--panning");
}

export function moveBackgroundAction(state, event) {
  if (state.panState?.pointerId === event.pointerId) {
    state.panState.moved ||= Math.abs(event.clientX - state.panState.startX) > 3
      || Math.abs(event.clientY - state.panState.startY) > 3;
    state.panX = state.panState.originX + event.clientX - state.panState.startX;
    state.panY = state.panState.originY + event.clientY - state.panState.startY;
    applyTransform(state);
    return true;
  }
  if (state.marqueeState?.pointerId !== event.pointerId) return false;
  state.marqueeState.current = screenToGraph(state, event.clientX, event.clientY);
  scheduleMarqueeUpdate(state);
  return true;
}

export function applyMarqueeSelection(state) {
  const marquee = state.marqueeState;
  if (marquee === null) return;
  const left = Math.min(marquee.start.x, marquee.current.x);
  const top = Math.min(marquee.start.y, marquee.current.y);
  const width = Math.abs(marquee.current.x - marquee.start.x);
  const height = Math.abs(marquee.current.y - marquee.start.y);
  Object.assign(state.marquee.style, {
    left: `${left}px`,
    top: `${top}px`,
    width: `${width}px`,
    height: `${height}px`,
  });
  const ids = marquee.nodes
    .filter(({ rectangle }) =>
      rectangle.left < left + width
      && rectangle.right > left
      && rectangle.top < top + height
      && rectangle.bottom > top)
    .map(({ id }) => id);
  setLocalSelection(state.root, ids, null);
}

export function scheduleMarqueeUpdate(state) {
  if (state.marqueeFrame !== null) return;
  state.marqueeFrame = requestAnimationFrame(() => {
    state.marqueeFrame = null;
    applyMarqueeSelection(state);
  });
}

export function cancelMarqueeUpdate(state) {
  if (state.marqueeFrame === null) return;
  cancelAnimationFrame(state.marqueeFrame);
  state.marqueeFrame = null;
}

export function finishBackgroundAction(state, event) {
  if (state.panState?.pointerId === event.pointerId) {
    const deselect = !state.panState.moved;
    if (deselect) {
      state.panX = state.panState.originX;
      state.panY = state.panState.originY;
      applyTransform(state);
    }
    state.panState = null;
    if (state.root.hasPointerCapture(event.pointerId)) {
      state.root.releasePointerCapture(event.pointerId);
    }
    state.root.classList.remove("automation-canvas--panning");
    if (deselect) {
      setLocalSelection(state.root, [], null);
      scheduleRoutePass(state);
      state.root.focus({ preventScroll: true });
      void notifyCompactSelection(state);
    }
    return true;
  }
  if (state.marqueeState?.pointerId !== event.pointerId) return false;
  cancelMarqueeUpdate(state);
  applyMarqueeSelection(state);
  state.marqueeState = null;
  if (state.root.hasPointerCapture(event.pointerId)) {
    state.root.releasePointerCapture(event.pointerId);
  }
  state.marquee.hidden = true;
  state.root.classList.remove("automation-canvas--selecting");
  scheduleRoutePass(state);
  void notifyCompactSelection(state);
  return true;
}
