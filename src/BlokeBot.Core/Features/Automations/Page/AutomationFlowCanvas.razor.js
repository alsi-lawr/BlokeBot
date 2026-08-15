const states = new WeakMap();
const gridSize = 24;
const obstacleMargin = 18;

function clamp(value, minimum, maximum) {
  return Math.min(maximum, Math.max(minimum, value));
}

function snap(value) {
  return Math.max(0, Math.round(value / gridSize) * gridSize);
}

function selectedNodes(root) {
  return [...root.querySelectorAll("[data-automation-node].automation-node--selected")];
}

function selectedEdgeId(root) {
  return root.querySelector("[data-automation-edge].automation-edge-group--selected")
    ?.dataset.automationEdge ?? null;
}

function selectionIds(root) {
  return selectedNodes(root).map((node) => node.dataset.automationNode);
}

function setLocalSelection(root, nodeIds, edgeId = null) {
  const selected = new Set(nodeIds);
  for (const node of root.querySelectorAll("[data-automation-node]")) {
    node.classList.toggle("automation-node--selected", selected.has(node.dataset.automationNode));
    node.setAttribute("aria-pressed", selected.has(node.dataset.automationNode) ? "true" : "false");
  }
  for (const edge of root.querySelectorAll("[data-automation-edge]")) {
    edge.classList.toggle("automation-edge-group--selected", edge.dataset.automationEdge === edgeId);
  }
}

function notifySelection(state) {
  return state.dotnet.invokeMethodAsync(
    "SetSelectionFromCanvasAsync",
    selectionIds(state.root),
    selectedEdgeId(state.root),
  );
}

function applyTransform(state) {
  state.stage.style.transform = `translate(${state.panX}px, ${state.panY}px) scale(${state.zoom})`;
  state.zoomReset.textContent = `${Math.round(state.zoom * 100)}%`;
}

function stagePoint(state, clientX, clientY) {
  const viewport = state.root.getBoundingClientRect();
  return {
    x: (clientX - viewport.left - state.panX) / state.zoom,
    y: (clientY - viewport.top - state.panY) / state.zoom,
  };
}

function portPoint(state, nodeId, portId, direction) {
  const port = state.root.querySelector(
    `[data-automation-port][data-node-id="${CSS.escape(nodeId)}"][data-port-id="${CSS.escape(portId)}"][data-port-direction="${direction}"]`,
  );
  if (!(port instanceof HTMLElement)) return null;
  const bounds = port.getBoundingClientRect();
  return stagePoint(state, bounds.left + bounds.width / 2, bounds.top + bounds.height / 2);
}

function nodeObstacles(state, excluded) {
  return [...state.root.querySelectorAll("[data-automation-node]")]
    .filter((node) => !excluded.has(node.dataset.automationNode))
    .map((node) => ({
      left: node.offsetLeft - obstacleMargin,
      top: node.offsetTop - obstacleMargin,
      right: node.offsetLeft + node.offsetWidth + obstacleMargin,
      bottom: node.offsetTop + node.offsetHeight + obstacleMargin,
    }));
}

function segmentHitsRectangle(first, second, rectangle) {
  if (first.x === second.x) {
    return first.x >= rectangle.left
      && first.x <= rectangle.right
      && Math.max(first.y, second.y) >= rectangle.top
      && Math.min(first.y, second.y) <= rectangle.bottom;
  }
  if (first.y === second.y) {
    return first.y >= rectangle.top
      && first.y <= rectangle.bottom
      && Math.max(first.x, second.x) >= rectangle.left
      && Math.min(first.x, second.x) <= rectangle.right;
  }
  return false;
}

function pathHitsObstacles(points, obstacles) {
  for (let index = 1; index < points.length; index += 1) {
    if (obstacles.some((obstacle) => segmentHitsRectangle(points[index - 1], points[index], obstacle))) {
      return true;
    }
  }
  return false;
}

function routePoints(start, end, orientation, obstacles) {
  if (orientation === "vertical") {
    const middleY = start.y + (end.y - start.y) / 2;
    const direct = [start, { x: start.x, y: middleY }, { x: end.x, y: middleY }, end];
    if (!pathHitsObstacles(direct, obstacles)) return direct;
    const left = Math.min(start.x, end.x, ...obstacles.map((item) => item.left)) - 30;
    const right = Math.max(start.x, end.x, ...obstacles.map((item) => item.right)) + 30;
    const leftDistance = Math.abs(start.x - left) + Math.abs(end.x - left);
    const detourX = leftDistance <= Math.abs(right - start.x) + Math.abs(right - end.x)
      ? left
      : right;
    return [
      start,
      { x: start.x, y: start.y + 24 },
      { x: detourX, y: start.y + 24 },
      { x: detourX, y: end.y - 24 },
      { x: end.x, y: end.y - 24 },
      end,
    ];
  }

  const middleX = start.x + (end.x - start.x) / 2;
  const direct = [start, { x: middleX, y: start.y }, { x: middleX, y: end.y }, end];
  if (!pathHitsObstacles(direct, obstacles)) return direct;
  const above = Math.min(start.y, end.y, ...obstacles.map((item) => item.top)) - 30;
  const below = Math.max(start.y, end.y, ...obstacles.map((item) => item.bottom)) + 30;
  const aboveDistance = Math.abs(start.y - above) + Math.abs(end.y - above);
  const detourY = aboveDistance <= Math.abs(below - start.y) + Math.abs(below - end.y)
    ? above
    : below;
  return [
    start,
    { x: start.x + 24, y: start.y },
    { x: start.x + 24, y: detourY },
    { x: end.x - 24, y: detourY },
    { x: end.x - 24, y: end.y },
    end,
  ];
}

function angularPath(points) {
  return points.reduce(
    (path, point, index) => `${path}${index === 0 ? "M" : " L"} ${point.x} ${point.y}`,
    "",
  );
}

function smoothPath(points) {
  if (points.length < 3) return angularPath(points);
  let path = `M ${points[0].x} ${points[0].y}`;
  for (let index = 1; index < points.length - 1; index += 1) {
    const previous = points[index - 1];
    const current = points[index];
    const next = points[index + 1];
    const incoming = Math.min(14, Math.hypot(current.x - previous.x, current.y - previous.y) / 2);
    const outgoing = Math.min(14, Math.hypot(next.x - current.x, next.y - current.y) / 2);
    const before = {
      x: current.x + Math.sign(previous.x - current.x) * incoming,
      y: current.y + Math.sign(previous.y - current.y) * incoming,
    };
    const after = {
      x: current.x + Math.sign(next.x - current.x) * outgoing,
      y: current.y + Math.sign(next.y - current.y) * outgoing,
    };
    path += ` L ${before.x} ${before.y} Q ${current.x} ${current.y} ${after.x} ${after.y}`;
  }
  const last = points.at(-1);
  return `${path} L ${last.x} ${last.y}`;
}

function routeEdge(state, group) {
  const sourceNode = group.dataset.sourceNode;
  const targetNode = group.dataset.targetNode;
  const start = portPoint(state, sourceNode, group.dataset.sourcePort, "output");
  const end = portPoint(state, targetNode, group.dataset.targetPort, "input");
  if (start === null || end === null) return;
  const points = routePoints(
    start,
    end,
    state.shell.dataset.orientation,
    nodeObstacles(state, new Set([sourceNode, targetNode])),
  );
  const path = state.shell.dataset.edgeStyle === "smooth"
    ? smoothPath(points)
    : angularPath(points);
  for (const element of group.querySelectorAll("path")) element.setAttribute("d", path);
  const label = group.querySelector("text");
  if (label !== null) {
    const anchor = points[Math.floor(points.length / 2)];
    label.setAttribute("x", `${anchor.x + 8}`);
    label.setAttribute("y", `${anchor.y - 8}`);
  }
}

function routeAll(state) {
  for (const edge of state.root.querySelectorAll("[data-automation-edge]")) routeEdge(state, edge);
  state.root.dataset.automationCanvasReady = "true";
}

function cancelConnection(state) {
  state.connection = null;
  state.preview.setAttribute("d", "");
  state.root.classList.remove("automation-canvas--connecting");
  for (const port of state.root.querySelectorAll("[data-automation-port]")) {
    port.classList.remove("automation-port--compatible", "automation-port--source");
  }
}

function startConnection(state, port) {
  cancelConnection(state);
  state.connection = {
    nodeId: port.dataset.nodeId,
    portId: port.dataset.portId,
    type: port.dataset.portType,
    sensitivity: port.dataset.portSensitivity,
  };
  port.classList.add("automation-port--source");
  state.root.classList.add("automation-canvas--connecting");
  for (const input of state.root.querySelectorAll('[data-port-direction="input"]')) {
    const compatible = input.dataset.nodeId !== state.connection.nodeId
      && input.dataset.portType === state.connection.type
      && input.dataset.portSensitivity === state.connection.sensitivity;
    input.classList.toggle("automation-port--compatible", compatible);
  }
}

function compatibleTarget(state, port) {
  return state.connection !== null
    && port.dataset.portDirection === "input"
    && port.dataset.nodeId !== state.connection.nodeId
    && port.dataset.portType === state.connection.type
    && port.dataset.portSensitivity === state.connection.sensitivity;
}

function updateConnectionPreview(state, clientX, clientY) {
  if (state.connection === null) return;
  const start = portPoint(
    state,
    state.connection.nodeId,
    state.connection.portId,
    "output",
  );
  if (start === null) return;
  const end = stagePoint(state, clientX, clientY);
  const orientation = state.shell.dataset.orientation;
  const points = orientation === "vertical"
    ? [start, { x: start.x, y: start.y + (end.y - start.y) / 2 }, { x: end.x, y: start.y + (end.y - start.y) / 2 }, end]
    : [start, { x: start.x + (end.x - start.x) / 2, y: start.y }, { x: start.x + (end.x - start.x) / 2, y: end.y }, end];
  state.preview.setAttribute(
    "d",
    state.shell.dataset.edgeStyle === "smooth" ? smoothPath(points) : angularPath(points),
  );
}

function beginNodeDrag(state, event, node) {
  event.preventDefault();
  event.stopPropagation();
  let selected = new Set(selectionIds(state.root));
  const nodeId = node.dataset.automationNode;
  if (event.shiftKey) {
    if (selected.has(nodeId)) selected.delete(nodeId);
    else selected.add(nodeId);
  } else if (!selected.has(nodeId)) {
    selected = new Set([nodeId]);
  }
  setLocalSelection(state.root, selected, null);
  void notifySelection(state);
  if (!selected.has(nodeId)) return;
  const nodes = [...state.root.querySelectorAll("[data-automation-node]")]
    .filter((candidate) => selected.has(candidate.dataset.automationNode))
    .map((candidate) => ({
      element: candidate,
      nodeId: candidate.dataset.automationNode,
      startLeft: candidate.offsetLeft,
      startTop: candidate.offsetTop,
    }));
  node.setPointerCapture(event.pointerId);
  state.drag = {
    pointerId: event.pointerId,
    capture: node,
    startX: event.clientX,
    startY: event.clientY,
    moved: false,
    nodes,
  };
  state.root.classList.add("automation-canvas--node-dragging");
  for (const item of nodes) item.element.classList.add("automation-node--moving");
}

function moveNodeDrag(state, event) {
  const drag = state.drag;
  if (drag === null || drag.pointerId !== event.pointerId) return false;
  const deltaX = (event.clientX - drag.startX) / state.zoom;
  const deltaY = (event.clientY - drag.startY) / state.zoom;
  drag.moved ||= Math.abs(deltaX) > 2 || Math.abs(deltaY) > 2;
  for (const item of drag.nodes) {
    item.element.style.left = `${Math.max(0, item.startLeft + deltaX)}px`;
    item.element.style.top = `${Math.max(0, item.startTop + deltaY)}px`;
  }
  routeAll(state);
  return true;
}

function finishNodeDrag(state, event) {
  const drag = state.drag;
  if (drag === null || drag.pointerId !== event.pointerId) return false;
  state.drag = null;
  drag.capture.releasePointerCapture(event.pointerId);
  state.root.classList.remove("automation-canvas--node-dragging");
  const vertical = state.shell.dataset.orientation === "vertical";
  const moves = drag.nodes.map((item) => {
    item.element.classList.remove("automation-node--moving");
    const displayX = snap(item.element.offsetLeft);
    const displayY = snap(item.element.offsetTop);
    item.element.style.left = `${displayX}px`;
    item.element.style.top = `${displayY}px`;
    return {
      nodeId: item.nodeId,
      x: vertical ? displayY : displayX,
      y: vertical ? displayX : displayY,
    };
  });
  routeAll(state);
  if (drag.moved) void state.dotnet.invokeMethodAsync("MoveNodesFromCanvasAsync", moves);
  return true;
}

function beginBackgroundAction(state, event) {
  if (event.button !== 0) return;
  const target = event.target;
  if (!(target instanceof Element)) return;
  if (target.closest("[data-automation-node], [data-automation-edge], [data-automation-port]")) return;
  event.preventDefault();
  state.root.setPointerCapture(event.pointerId);
  if (event.altKey) {
    const start = stagePoint(state, event.clientX, event.clientY);
    state.marqueeState = { pointerId: event.pointerId, start, current: start };
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
  };
  state.root.classList.add("automation-canvas--panning");
}

function moveBackgroundAction(state, event) {
  if (state.panState?.pointerId === event.pointerId) {
    state.panX = state.panState.originX + event.clientX - state.panState.startX;
    state.panY = state.panState.originY + event.clientY - state.panState.startY;
    applyTransform(state);
    return true;
  }
  if (state.marqueeState?.pointerId !== event.pointerId) return false;
  state.marqueeState.current = stagePoint(state, event.clientX, event.clientY);
  const left = Math.min(state.marqueeState.start.x, state.marqueeState.current.x);
  const top = Math.min(state.marqueeState.start.y, state.marqueeState.current.y);
  const width = Math.abs(state.marqueeState.current.x - state.marqueeState.start.x);
  const height = Math.abs(state.marqueeState.current.y - state.marqueeState.start.y);
  Object.assign(state.marquee.style, {
    left: `${left}px`,
    top: `${top}px`,
    width: `${width}px`,
    height: `${height}px`,
  });
  const ids = [...state.root.querySelectorAll("[data-automation-node]")]
    .filter((node) => node.offsetLeft < left + width
      && node.offsetLeft + node.offsetWidth > left
      && node.offsetTop < top + height
      && node.offsetTop + node.offsetHeight > top)
    .map((node) => node.dataset.automationNode);
  setLocalSelection(state.root, ids, null);
  return true;
}

function finishBackgroundAction(state, event) {
  if (state.panState?.pointerId === event.pointerId) {
    state.panState = null;
    state.root.releasePointerCapture(event.pointerId);
    state.root.classList.remove("automation-canvas--panning");
    return true;
  }
  if (state.marqueeState?.pointerId !== event.pointerId) return false;
  state.marqueeState = null;
  state.root.releasePointerCapture(event.pointerId);
  state.marquee.hidden = true;
  state.root.classList.remove("automation-canvas--selecting");
  void notifySelection(state);
  return true;
}

function register(state, element, name, handler, options) {
  element.addEventListener(name, handler, options);
  state.listeners.push(() => element.removeEventListener(name, handler, options));
}

export function initialize(root, dotnet) {
  if (!(root instanceof HTMLElement) || states.has(root)) return;
  const shell = root.closest("[data-automation-canvas-shell]");
  const stage = root.querySelector(".automation-canvas-stage");
  const preview = root.querySelector("[data-connection-preview]");
  const marquee = root.querySelector("[data-automation-marquee]");
  const zoomReset = shell?.querySelector("[data-canvas-zoom-reset]");
  if (!(shell instanceof HTMLElement)
    || !(stage instanceof HTMLElement)
    || !(preview instanceof SVGPathElement)
    || !(marquee instanceof HTMLElement)
    || !(zoomReset instanceof HTMLButtonElement)) return;

  const state = {
    root,
    shell,
    stage,
    preview,
    marquee,
    zoomReset,
    dotnet,
    zoom: 1,
    panX: 0,
    panY: 0,
    drag: null,
    panState: null,
    marqueeState: null,
    connection: null,
    listeners: [],
  };

  register(state, root, "pointerdown", (event) => {
    const port = event.target instanceof Element ? event.target.closest("[data-automation-port]") : null;
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
    if (finishNodeDrag(state, event)) return;
    finishBackgroundAction(state, event);
  });
  register(state, root, "pointercancel", (event) => {
    if (finishNodeDrag(state, event)) return;
    finishBackgroundAction(state, event);
  });
  register(state, root, "click", (event) => {
    const port = event.target instanceof Element ? event.target.closest("[data-automation-port]") : null;
    if (!(port instanceof HTMLButtonElement)) return;
    event.preventDefault();
    event.stopPropagation();
    if (port.dataset.portDirection === "output") {
      startConnection(state, port);
      return;
    }
    if (!compatibleTarget(state, port)) return;
    const connection = state.connection;
    cancelConnection(state);
    void dotnet.invokeMethodAsync(
      "ConnectFromCanvasAsync",
      connection.nodeId,
      connection.portId,
      port.dataset.nodeId,
      port.dataset.portId,
    );
  }, true);
  register(state, root, "wheel", (event) => {
    if (!event.ctrlKey) return;
    event.preventDefault();
    const viewport = root.getBoundingClientRect();
    const pointerX = event.clientX - viewport.left;
    const pointerY = event.clientY - viewport.top;
    const previousZoom = state.zoom;
    state.zoom = clamp(previousZoom * (event.deltaY < 0 ? 1.1 : 0.9), 0.5, 1.75);
    state.panX = pointerX - ((pointerX - state.panX) * state.zoom / previousZoom);
    state.panY = pointerY - ((pointerY - state.panY) * state.zoom / previousZoom);
    applyTransform(state);
  }, { passive: false });
  register(state, root, "keydown", (event) => {
    if (event.key === "Escape") {
      cancelConnection(state);
      return;
    }
    if (event.key !== "Delete" && event.key !== "Backspace") return;
    if (event.target instanceof HTMLInputElement
      || event.target instanceof HTMLTextAreaElement
      || event.target instanceof HTMLSelectElement) return;
    event.preventDefault();
    void dotnet.invokeMethodAsync(
      "DeleteSelectionFromCanvasAsync",
      selectionIds(root),
      selectedEdgeId(root),
    );
  });
  register(state, zoomReset, "click", () => {
    state.zoom = 1;
    state.panX = 0;
    state.panY = 0;
    applyTransform(state);
  });
  register(state, window, "resize", () => routeAll(state));

  states.set(root, state);
  applyTransform(state);
  routeAll(state);
}

export function refresh(root) {
  const state = states.get(root);
  if (state === undefined) return;
  state.preview = root.querySelector("[data-connection-preview]");
  state.marquee = root.querySelector("[data-automation-marquee]");
  cancelConnection(state);
  routeAll(state);
}

export function dispose(root) {
  const state = states.get(root);
  if (state === undefined) return;
  for (const remove of state.listeners.reverse()) remove();
  states.delete(root);
}
