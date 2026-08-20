import { commitRoute, runRouteSteps } from "./AutomationFlowRouteCurves.js";
import { framePortPoint, routingFrame } from "./AutomationFlowRouteFrame.js";
import { computeSceneSteps } from "./AutomationFlowRouteScene.js";
import { nodeGraphPosition } from "./AutomationFlowCanvasState.js";

const routePassFrameBudgetMs = 3;

export function routeReference(state) {
  const uncachedPortPoint = (nodeId, portId, direction) => {
    const port = state.root.querySelector(
      `[data-automation-port][data-node-id="${CSS.escape(nodeId)}"][data-port-id="${CSS.escape(portId)}"][data-port-direction="${direction}"]`,
    );
    if (!(port instanceof HTMLElement)) return null;
    const node = port.closest("[data-automation-node]");
    if (!(node instanceof HTMLElement)) return null;
    const position = nodeGraphPosition(node);
    const portStyle = getComputedStyle(port);
    const portTransform = new DOMMatrixReadOnly(portStyle.transform);
    const offsetX = node.clientLeft
      + Number.parseFloat(portStyle.left)
      + Number.parseFloat(portStyle.width) / 2
      + portTransform.e;
    const offsetY = node.clientTop
      + Number.parseFloat(portStyle.top)
      + Number.parseFloat(portStyle.height) / 2
      + portTransform.f;
    return { x: position.x + offsetX, y: position.y + offsetY };
  };
  const frame = routingFrame(state);
  const results = runRouteSteps(computeSceneSteps(frame, {
    orientation: state.shell.dataset.orientation,
    edgeStyle: state.shell.dataset.edgeStyle,
    resolvePort: uncachedPortPoint,
  }));
  return frame.edges.map((edge) => {
    const route = results.get(edge.edgeId)?.route ?? null;
    const needsLabel = edge.label !== null;
    const accepted = route !== null && (!needsLabel || route.label !== null);
    return {
      edgeId: edge.edgeId,
      accepted,
      path: accepted ? route.path : "",
      label: accepted && needsLabel ? { x: route.label.x, y: route.label.y } : null,
    };
  });
}

export function cancelRoutePass(state) {
  const pass = state.routePass;
  if (pass === null) return;
  if (pass.timer !== null) clearTimeout(pass.timer);
  if (pass.begun) state.routedSignature = null;
  state.routePass = null;
}

export function scheduleRoutePassSlice(state, pass) {
  pass.timer = setTimeout(() => {
    if (state.routePass !== pass) return;
    pass.timer = null;
    runRoutePassSlice(state, pass);
  }, 0);
}

// Every pass commits all routes and labels at once: compute slices never touch
// the DOM, so a pass is structurally single-settle regardless of slicing.
export function commitScene(state, frame, results) {
  for (const edge of frame.edges) {
    const route = results.get(edge.edgeId)?.route ?? null;
    commitRoute(edge.group, edge.label, route);
  }
}

export function finishRoutePass(state, pass) {
  state.routePass = null;
  state.routedSignature = pass.routingFrame.signature;
  state.root.dataset.automationCanvasReady = "true";
}

export function runRoutePassSlice(state, pass) {
  const started = performance.now();
  const withinBudget = () => performance.now() - started < routePassFrameBudgetMs;
  if (!pass.begun || pass.stale) {
    const frame = routingFrame(state);
    if (!pass.begun && frame.signature === state.routedSignature) {
      state.routePass = null;
      state.root.dataset.automationCanvasReady = "true";
      return;
    }
    if (!pass.begun || frame.signature !== pass.routingFrame.signature) {
      pass.routingFrame = frame;
      pass.compute = null;
    }
    pass.begun = true;
    pass.stale = false;
    if (!withinBudget()) {
      scheduleRoutePassSlice(state, pass);
      return;
    }
  }
  const frame = pass.routingFrame;
  if (state.sceneCache !== null && state.sceneCache.signature === frame.signature) {
    commitScene(state, frame, state.sceneCache.results);
    finishRoutePass(state, pass);
    return;
  }
  if (pass.compute === null) {
    pass.compute = computeSceneSteps(frame, {
      orientation: state.shell.dataset.orientation,
      edgeStyle: state.shell.dataset.edgeStyle,
      resolvePort: (nodeId, portId, direction) =>
        framePortPoint(state, frame, nodeId, portId, direction),
    });
  }
  let next = pass.compute.next();
  while (!next.done && withinBudget()) next = pass.compute.next();
  if (!next.done) {
    scheduleRoutePassSlice(state, pass);
    return;
  }
  const results = next.value;
  state.sceneCache = { signature: frame.signature, results };
  commitScene(state, frame, results);
  finishRoutePass(state, pass);
}

export function scheduleRoutePass(state) {
  if (state.drag !== null && state.drag.moved) return;
  if (state.routePass !== null) {
    state.routePass.stale = true;
    return;
  }
  const pass = {
    timer: null,
    routingFrame: null,
    compute: null,
    begun: false,
    stale: false,
  };
  state.routePass = pass;
  scheduleRoutePassSlice(state, pass);
}

// Live drag routing. One session per drag holds the routing frame, the shared
// visibility graph of everything standing still, and the last known route of
// every edge. Each animation frame patches that graph for the moving nodes and
// reroutes only the affected edges - the dragged nodes' own edges plus every
// edge whose corridor the moving rectangles sweep - through the same A*,
// nudging, and smoothing the drop pass uses. Edges beyond the per-frame cap or
// budget keep their previous route and are retried first on the next frame;
// whatever is still outstanding at release is settled by the drop pass.
