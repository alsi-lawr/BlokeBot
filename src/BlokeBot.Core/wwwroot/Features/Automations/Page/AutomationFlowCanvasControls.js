import {
  applyTransform,
  clamp,
  gridSize,
  nodeGraphPosition,
  selectedNodes,
  setNodeGraphPosition,
  viewportPoint,
  zoomScale,
  zoomSteps,
} from "./AutomationFlowCanvasState.js";
import { scheduleRoutePass } from "./AutomationFlowRoutePass.js";

export function register(state, element, name, handler, options) {
  element.addEventListener(name, handler, options);
  state.listeners.push(() => element.removeEventListener(name, handler, options));
}

export function changeZoom(state, direction, clientX, clientY) {
  const targetIndex = clamp(state.zoomIndex + direction, 0, zoomSteps.length - 1);
  if (targetIndex === state.zoomIndex) return;
  const pointer = viewportPoint(state, clientX, clientY);
  const previous = {
    zoomIndex: state.zoomIndex,
    panX: state.panX,
    panY: state.panY,
  };
  const reverse = state.zoomTransition !== null
    && state.zoomTransition.to.zoomIndex === state.zoomIndex
    && state.zoomTransition.from.zoomIndex === targetIndex
    && state.zoomTransition.pointer.x === pointer.x
    && state.zoomTransition.pointer.y === pointer.y
    && state.zoomTransition.to.panX === state.panX
    && state.zoomTransition.to.panY === state.panY;
  if (reverse) {
    state.zoomIndex = state.zoomTransition.from.zoomIndex;
    state.panX = state.zoomTransition.from.panX;
    state.panY = state.zoomTransition.from.panY;
  } else {
    const graphX = (pointer.x - state.panX) / zoomScale(state);
    const graphY = (pointer.y - state.panY) / zoomScale(state);
    state.zoomIndex = targetIndex;
    state.panX = pointer.x - graphX * zoomScale(state);
    state.panY = pointer.y - graphY * zoomScale(state);
  }
  state.zoomTransition = {
    from: previous,
    to: {
      zoomIndex: state.zoomIndex,
      panX: state.panX,
      panY: state.panY,
    },
    pointer,
  };
  applyTransform(state);
}

export function isEditingControl(target) {
  return target instanceof Element
    && target.closest("input, textarea, select, [contenteditable]:not([contenteditable='false']), [role='textbox']") !== null;
}

export function moveSelectionByKeyboard(state, key) {
  const movement = {
    ArrowLeft: { x: -gridSize, y: 0 },
    ArrowRight: { x: gridSize, y: 0 },
    ArrowUp: { x: 0, y: -gridSize },
    ArrowDown: { x: 0, y: gridSize },
  }[key];
  if (movement === undefined) return false;
  const vertical = state.shell.dataset.orientation === "vertical";
  const moves = selectedNodes(state.root).map((node) => {
    const position = nodeGraphPosition(node);
    const x = Math.max(0, position.x + movement.x);
    const y = Math.max(0, position.y + movement.y);
    setNodeGraphPosition(node, x, y);
    return {
      nodeId: node.dataset.automationNode,
      x: vertical ? y : x,
      y: vertical ? x : y,
    };
  });
  if (moves.length === 0) return true;
  scheduleRoutePass(state);
  void state.dotnet.invokeMethodAsync("MoveNodesFromCanvasAsync", moves);
  return true;
}
