const states = new WeakMap();
const activeStates = new Set();
const savedViewports = new Map();
const metrics = {
  routeRecalculationCount: 0,
  routeEdgeCount: 0,
  routeCacheHitCount: 0,
  routeCacheMissCount: 0,
  routeComputationCount: 0,
  routeEdgeLiveCount: 0,
  routeEdgeLiveMaximumPerFrame: 0,
  dragFrames: 0,
  refreshCount: 0,
  uiUpdateCount: 0,
};
globalThis.__blokeBotAutomationMetrics = metrics;
globalThis.__resetBlokeBotAutomationMetrics = () => {
  for (const key of Object.keys(metrics)) metrics[key] = 0;
};
globalThis.__simulateBlokeBotAutomationUpdate = () => {
  metrics.uiUpdateCount += 1;
  metrics.refreshCount += 1;
  for (const state of activeStates) {
    if (state.drag === null) scheduleRoutePass(state);
  }
};
// Test-only complete-route evaluation. Every other node is an obstacle for the
// identical final geometry, with no route cache, no port-offset cache, and no
// relevant-region restriction: it shares only the core routing primitives with
// production and deliberately does not pass through edgeRouteInputs or
// relevantObstacleNodes, so it independently validates the selective
// invalidation it is compared against. Nothing on any pointer path calls it.
globalThis.__blokeBotAutomationRouteReference = (root = null) => {
  const state = root === null ? [...activeStates][0] : states.get(root);
  if (state === undefined) {
    throw new Error("The automation canvas is not initialized.");
  }
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
  const orientation = state.shell.dataset.orientation;
  const edgeStyle = state.shell.dataset.edgeStyle;
  const nodes = [...state.root.querySelectorAll("[data-automation-node]")]
    .filter((node) => node instanceof HTMLElement)
    .map((node) => ({
      id: node.dataset.automationNode,
      obstacle: nodeGraphRectangle(node, obstacleMargin),
      endpoint: nodeGraphRectangle(node),
    }));
  const viewBox = state.root.querySelector(".automation-edges")?.viewBox.baseVal;
  const labelBounds = viewBox === undefined
    ? null
    : {
      left: viewBox.x,
      top: viewBox.y,
      right: viewBox.x + viewBox.width,
      bottom: viewBox.y + viewBox.height,
    };
  return [...state.root.querySelectorAll("[data-automation-edge]")].map((group) => {
    const needsLabel = group.querySelector("[data-edge-label]") !== null;
    const sourceNode = group.dataset.sourceNode;
    const targetNode = group.dataset.targetNode;
    const start = uncachedPortPoint(sourceNode, group.dataset.sourcePort, "output");
    const end = uncachedPortPoint(targetNode, group.dataset.targetPort, "input");
    let route = null;
    if (start !== null && end !== null) {
      const nodeRectangles = nodes
        .filter((node) => node.id !== sourceNode && node.id !== targetNode)
        .map((node) => node.obstacle);
      const overlappingEndpoints = nodeRectangles.filter((rectangle) =>
        pointInsideRectangle(start, rectangle) || pointInsideRectangle(end, rectangle));
      const obstacles = nodeRectangles.filter(
        (rectangle) => !overlappingEndpoints.includes(rectangle),
      );
      const sourceEndpoint = nodes.find((node) => node.id === sourceNode)?.endpoint;
      const targetEndpoint = nodes.find((node) => node.id === targetNode)?.endpoint;
      const endpoints = [
        sourceEndpoint === undefined
          ? null
          : { rectangle: sourceEndpoint, endpoint: "source" },
        targetEndpoint === undefined
          ? null
          : { rectangle: targetEndpoint, endpoint: "target" },
        ...overlappingEndpoints.map((rectangle) => ({ rectangle, endpoint: "overlap" })),
      ].filter((endpoint) => endpoint !== null);
      if (edgeStyle === "smooth") {
        route = smoothRoute(start, end, orientation, obstacles, endpoints, needsLabel, labelBounds);
      } else {
        const routed = needsLabel
          ? runRouteSteps(
            routePointsWithLabelSteps(start, end, orientation, obstacles, endpoints, labelBounds),
          )
          : { points: routePoints(start, end, orientation, obstacles, endpoints), label: null };
        route = routed === null || routed.points === null
          ? null
          : { path: angularPath(routed.points), points: routed.points, label: routed.label };
      }
    }
    const accepted = route !== null && (!needsLabel || route.label !== null);
    return {
      edgeId: group.dataset.automationEdge,
      accepted,
      path: accepted ? route.path : "",
      label: accepted && needsLabel ? { x: route.label.x, y: route.label.y } : null,
    };
  });
};
const gridSize = 24;
const obstacleMargin = 18;
const routeClearance = 12;
const routeRegionMargin = 288;
const routePassFrameBudgetMs = 3;
const curveSampleCount = 96;
const labelHalfWidth = 20;
const labelHalfHeight = 12;
const zoomSteps = [
  { scale: 0.5, label: "50%" },
  { scale: 0.625, label: "62.5%" },
  { scale: 0.75, label: "75%" },
  { scale: 0.875, label: "87.5%" },
  { scale: 1, label: "100%" },
  { scale: 1.125, label: "112.5%" },
  { scale: 1.25, label: "125%" },
  { scale: 1.5, label: "150%" },
  { scale: 1.75, label: "175%" },
];
const defaultZoomIndex = zoomSteps.findIndex((step) => step.scale === 1);

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
    const isSelected = selected.has(node.dataset.automationNode);
    node.classList.toggle("automation-node--selected", isSelected);
    node.querySelector("[data-automation-node-select]")
      ?.setAttribute("aria-pressed", isSelected ? "true" : "false");
    if (!isSelected) {
      node.classList.remove("automation-node--disclosed");
      node.querySelector("[data-automation-node-select]")
        ?.setAttribute("aria-expanded", "false");
    }
  }
  for (const edge of root.querySelectorAll("[data-automation-edge]")) {
    edge.classList.toggle("automation-edge-group--selected", edge.dataset.automationEdge === edgeId);
  }
}

function setLocalDisclosure(root, nodeId) {
  for (const node of root.querySelectorAll("[data-automation-node]")) {
    const isDisclosed = node.dataset.automationNode === nodeId;
    node.classList.toggle("automation-node--disclosed", isDisclosed);
    node.querySelector("[data-automation-node-select]")
      ?.setAttribute("aria-expanded", isDisclosed ? "true" : "false");
  }
}

function notifyCompactSelection(state) {
  return state.dotnet.invokeMethodAsync(
    "SetSelectionFromCanvasAsync",
    selectionIds(state.root),
    selectedEdgeId(state.root),
  );
}

function notifyPointerSelection(state) {
  return state.dotnet.invokeMethodAsync(
    "SetPointerSelectionFromCanvasAsync",
    selectionIds(state.root),
  );
}

function zoomScale(state) {
  return zoomSteps[state.zoomIndex].scale;
}

function applyTransform(state) {
  const step = zoomSteps[state.zoomIndex];
  state.stage.style.transform = `translate(${state.panX}px, ${state.panY}px) scale(${step.scale})`;
  state.root.style.setProperty("--automation-grid-x", `${state.panX}px`);
  state.root.style.setProperty("--automation-grid-y", `${state.panY}px`);
  state.root.style.setProperty("--automation-grid-step", `${gridSize * step.scale}px`);
  state.root.style.setProperty("--automation-grid-dot", `${1.1 * step.scale}px`);
  state.zoomResetLabel.textContent = step.label;
  savedViewports.set(state.viewportKey, {
    zoomIndex: state.zoomIndex,
    panX: state.panX,
    panY: state.panY,
  });
}

function restoreViewport(state) {
  state.viewportKey = state.shell.dataset.viewportKey ?? "";
  const saved = savedViewports.get(state.viewportKey);
  state.zoomIndex = saved?.zoomIndex ?? defaultZoomIndex;
  state.panX = saved?.panX ?? 0;
  state.panY = saved?.panY ?? 0;
  state.zoomTransition = null;
  applyTransform(state);
}

function updateEditorHeight(state) {
  const editor = state.shell.closest(".automation-editor");
  if (!(editor instanceof HTMLElement)) return;
  if (window.matchMedia("(max-width: 48rem)").matches) {
    editor.style.removeProperty("--automation-editor-height");
    return;
  }
  const parent = editor.parentElement;
  const bottomPadding = parent instanceof HTMLElement
    ? Number.parseFloat(getComputedStyle(parent).paddingBottom) || 0
    : 0;
  const available = Math.max(480, window.innerHeight - editor.getBoundingClientRect().top - bottomPadding);
  editor.style.setProperty("--automation-editor-height", `${available}px`);
}

function viewportPoint(state, clientX, clientY) {
  const viewport = state.root.getBoundingClientRect();
  return {
    x: clientX - viewport.left - state.root.clientLeft,
    y: clientY - viewport.top - state.root.clientTop,
  };
}

function screenToGraph(state, clientX, clientY) {
  const point = viewportPoint(state, clientX, clientY);
  const scale = zoomScale(state);
  return {
    x: (point.x - state.panX) / scale,
    y: (point.y - state.panY) / scale,
  };
}

function nodeGraphPosition(node) {
  if (node.dataset.automationGraphX !== undefined) {
    return {
      x: Number(node.dataset.automationGraphX),
      y: Number(node.dataset.automationGraphY),
    };
  }
  return {
    x: Number.parseFloat(node.style.getPropertyValue("--automation-node-x")),
    y: Number.parseFloat(node.style.getPropertyValue("--automation-node-y")),
  };
}

function setNodeGraphPosition(node, x, y) {
  node.dataset.automationGraphX = `${x}`;
  node.dataset.automationGraphY = `${y}`;
  node.style.removeProperty("transform");
  node.style.left = `${x}px`;
  node.style.top = `${y}px`;
}

function setNodeLiveGraphPosition(item) {
  item.element.dataset.automationGraphX = `${item.x}`;
  item.element.dataset.automationGraphY = `${item.y}`;
  item.element.style.transform = `translate(${item.x - item.startX}px, ${item.y - item.startY}px)`;
}

function nodeGraphRectangle(node, margin = 0) {
  const position = nodeGraphPosition(node);
  return {
    left: position.x - margin,
    top: position.y - margin,
    right: position.x + node.offsetWidth + margin,
    bottom: position.y + node.offsetHeight + margin,
  };
}

function portPoint(state, nodeId, portId, direction) {
  const key = `${nodeId}|${portId}|${direction}`;
  const cached = state.portOffsets.get(key);
  if (cached?.node.isConnected) {
    const position = nodeGraphPosition(cached.node);
    return { x: position.x + cached.x, y: position.y + cached.y };
  }
  const port = state.root.querySelector(
    `[data-automation-port][data-node-id="${CSS.escape(nodeId)}"][data-port-id="${CSS.escape(portId)}"][data-port-direction="${direction}"]`,
  );
  if (!(port instanceof HTMLElement)) return null;
  const node = port.closest("[data-automation-node]");
  if (!(node instanceof HTMLElement)) return null;
  const position = nodeGraphPosition(node);
  const portStyle = getComputedStyle(port);
  const portTransform = new DOMMatrixReadOnly(portStyle.transform);
  const offset = {
    node,
    x: node.clientLeft
      + Number.parseFloat(portStyle.left)
      + Number.parseFloat(portStyle.width) / 2
      + portTransform.e,
    y: node.clientTop
      + Number.parseFloat(portStyle.top)
      + Number.parseFloat(portStyle.height) / 2
      + portTransform.f,
  };
  state.portOffsets.set(key, offset);
  return { x: position.x + offset.x, y: position.y + offset.y };
}

function endpointObstacle(state, nodeId, endpoint) {
  const node = state.root.querySelector(
    `[data-automation-node="${CSS.escape(nodeId)}"]`,
  );
  return node instanceof HTMLElement
    ? { rectangle: nodeGraphRectangle(node), endpoint }
    : null;
}

function pointInsideRectangle(point, rectangle) {
  return point.x >= rectangle.left
    && point.x <= rectangle.right
    && point.y >= rectangle.top
    && point.y <= rectangle.bottom;
}

function segmentHitsRectangle(first, second, rectangle) {
  const deltaX = second.x - first.x;
  const deltaY = second.y - first.y;
  let minimum = 0;
  let maximum = 1;
  const boundaries = [
    [-deltaX, first.x - rectangle.left],
    [deltaX, rectangle.right - first.x],
    [-deltaY, first.y - rectangle.top],
    [deltaY, rectangle.bottom - first.y],
  ];
  for (const [direction, distance] of boundaries) {
    if (direction === 0) {
      if (distance < 0) return false;
      continue;
    }
    const ratio = distance / direction;
    if (direction < 0) minimum = Math.max(minimum, ratio);
    else maximum = Math.min(maximum, ratio);
    if (minimum > maximum) return false;
  }
  return true;
}

function pathHitsObstacles(points, obstacles) {
  for (let index = 1; index < points.length; index += 1) {
    if (obstacles.some((obstacle) => segmentHitsRectangle(points[index - 1], points[index], obstacle))) {
      return true;
    }
  }
  return false;
}

function pointOnSegment(point, first, second) {
  const cross = (point.y - first.y) * (second.x - first.x)
    - (point.x - first.x) * (second.y - first.y);
  return Math.abs(cross) < 0.0001
    && point.x >= Math.min(first.x, second.x) - 0.0001
    && point.x <= Math.max(first.x, second.x) + 0.0001
    && point.y >= Math.min(first.y, second.y) - 0.0001
    && point.y <= Math.max(first.y, second.y) + 0.0001;
}

function segmentDirection(first, second, third) {
  return (second.y - first.y) * (third.x - second.x)
    - (second.x - first.x) * (third.y - second.y);
}

function segmentsIntersect(firstStart, firstEnd, secondStart, secondEnd) {
  if (Math.max(firstStart.x, firstEnd.x) + 0.0001 < Math.min(secondStart.x, secondEnd.x)
    || Math.max(secondStart.x, secondEnd.x) + 0.0001 < Math.min(firstStart.x, firstEnd.x)
    || Math.max(firstStart.y, firstEnd.y) + 0.0001 < Math.min(secondStart.y, secondEnd.y)
    || Math.max(secondStart.y, secondEnd.y) + 0.0001 < Math.min(firstStart.y, firstEnd.y)) {
    return false;
  }
  const firstDirection = segmentDirection(firstStart, firstEnd, secondStart);
  const secondDirection = segmentDirection(firstStart, firstEnd, secondEnd);
  const thirdDirection = segmentDirection(secondStart, secondEnd, firstStart);
  const fourthDirection = segmentDirection(secondStart, secondEnd, firstEnd);
  if (((firstDirection > 0 && secondDirection < 0) || (firstDirection < 0 && secondDirection > 0))
    && ((thirdDirection > 0 && fourthDirection < 0) || (thirdDirection < 0 && fourthDirection > 0))) {
    return true;
  }
  return (Math.abs(firstDirection) < 0.0001 && pointOnSegment(secondStart, firstStart, firstEnd))
    || (Math.abs(secondDirection) < 0.0001 && pointOnSegment(secondEnd, firstStart, firstEnd))
    || (Math.abs(thirdDirection) < 0.0001 && pointOnSegment(firstStart, secondStart, secondEnd))
    || (Math.abs(fourthDirection) < 0.0001 && pointOnSegment(firstEnd, secondStart, secondEnd));
}

function pathSelfIntersects(points) {
  for (let first = 1; first < points.length; first += 1) {
    for (let second = first + 2; second < points.length; second += 1) {
      if (segmentsIntersect(points[first - 1], points[first], points[second - 1], points[second])) {
        return true;
      }
    }
  }
  return false;
}

function samePoint(first, second) {
  return Math.abs(first.x - second.x) < 0.0001
    && Math.abs(first.y - second.y) < 0.0001;
}

function normalizePath(points) {
  const unique = points.filter((point, index) => index === 0 || !samePoint(point, points[index - 1]));
  return unique.filter((point, index) => {
    if (index === 0 || index === unique.length - 1) return true;
    const previous = unique[index - 1];
    const next = unique[index + 1];
    const betweenX = point.x >= Math.min(previous.x, next.x)
      && point.x <= Math.max(previous.x, next.x);
    const betweenY = point.y >= Math.min(previous.y, next.y)
      && point.y <= Math.max(previous.y, next.y);
    return !((previous.x === point.x && point.x === next.x && betweenY)
      || (previous.y === point.y && point.y === next.y && betweenX));
  });
}

function pathAvoidsEndpoints(points, endpoints) {
  for (const endpoint of endpoints) {
    const startsInside = pointInsideRectangle(points[0], endpoint.rectangle);
    const endsInside = pointInsideRectangle(points.at(-1), endpoint.rectangle);
    let firstOutside = 0;
    while (
      startsInside
      && firstOutside < points.length
      && pointInsideRectangle(points[firstOutside], endpoint.rectangle)
    ) {
      firstOutside += 1;
    }
    let lastOutside = points.length - 1;
    while (
      endsInside
      && lastOutside >= 0
      && pointInsideRectangle(points[lastOutside], endpoint.rectangle)
    ) {
      lastOutside -= 1;
    }
    const firstCheckedSegment = startsInside ? firstOutside + 1 : 1;
    const lastCheckedSegment = endsInside ? lastOutside : points.length - 1;
    for (let index = firstCheckedSegment; index <= lastCheckedSegment; index += 1) {
      if (segmentHitsRectangle(points[index - 1], points[index], endpoint.rectangle)) {
        return false;
      }
    }
  }
  return true;
}

function pathIsSafe(points, obstacles, endpoints = []) {
  return points.length >= 2
    && !pathHitsObstacles(points, obstacles)
    && pathAvoidsEndpoints(points, endpoints)
    && !pathSelfIntersects(points);
}

function coordinateKey(point) {
  return `${point.x}:${point.y}`;
}

function gridCoordinates(start, end, obstacles) {
  const xValues = new Set([start.x, end.x]);
  const yValues = new Set([start.y, end.y]);
  for (const obstacle of obstacles) {
    xValues.add(obstacle.left - routeClearance);
    xValues.add(obstacle.right + routeClearance);
    yValues.add(obstacle.top - routeClearance);
    yValues.add(obstacle.bottom + routeClearance);
  }
  return {
    x: [...xValues].sort((first, second) => first - second),
    y: [...yValues].sort((first, second) => first - second),
  };
}

function routeAxis(first, second) {
  return first.x === second.x ? "vertical" : "horizontal";
}

function compareRouteCost(first, second) {
  return first.bends - second.bends || first.distance - second.distance;
}

function comparePendingEntries(first, second) {
  return (
    compareRouteCost(first.cost, second.cost) || first.key.localeCompare(second.key)
  );
}

function pushPendingEntry(heap, entry) {
  heap.push(entry);
  let index = heap.length - 1;
  while (index > 0) {
    const parent = (index - 1) >> 1;
    if (comparePendingEntries(heap[index], heap[parent]) >= 0) break;
    [heap[index], heap[parent]] = [heap[parent], heap[index]];
    index = parent;
  }
}

function popPendingEntry(heap) {
  const top = heap[0];
  const last = heap.pop();
  if (heap.length === 0) return top;
  heap[0] = last;
  let index = 0;
  for (;;) {
    const left = index * 2 + 1;
    const right = left + 1;
    let smallest = index;
    if (left < heap.length && comparePendingEntries(heap[left], heap[smallest]) < 0) {
      smallest = left;
    }
    if (right < heap.length && comparePendingEntries(heap[right], heap[smallest]) < 0) {
      smallest = right;
    }
    if (smallest === index) return top;
    [heap[index], heap[smallest]] = [heap[smallest], heap[index]];
    index = smallest;
  }
}

function segmentsHitRectangle(points, rectangle) {
  for (let index = 1; index < points.length; index += 1) {
    if (segmentHitsRectangle(points[index - 1], points[index], rectangle)) return true;
  }
  return false;
}

function* gridRouteSteps(start, end, obstacles, initialAxis = null, finalAxis = null) {
  const envelope = {
    left: Math.min(start.x, end.x) - routeClearance,
    top: Math.min(start.y, end.y) - routeClearance,
    right: Math.max(start.x, end.x) + routeClearance,
    bottom: Math.max(start.y, end.y) + routeClearance,
  };
  let active = obstacles.filter((obstacle) => rectanglesIntersect(obstacle, envelope));
  for (;;) {
    const route = yield* gridRouteThroughObstaclesSteps(start, end, active, initialAxis, finalAxis);
    if (route === null) {
      if (active.length === obstacles.length) return null;
      active = obstacles;
      continue;
    }
    const violated = obstacles.filter(
      (obstacle) => !active.includes(obstacle) && segmentsHitRectangle(route, obstacle),
    );
    if (violated.length === 0) {
      return pathIsSafe(route, obstacles) ? route : null;
    }
    active = [...active, ...violated];
  }
}

function gridRoute(start, end, obstacles, initialAxis = null, finalAxis = null) {
  return runRouteSteps(gridRouteSteps(start, end, obstacles, initialAxis, finalAxis));
}

function* gridRouteThroughObstaclesSteps(start, end, obstacles, initialAxis, finalAxis) {
  const coordinates = gridCoordinates(start, end, obstacles);
  const nodes = new Map();
  for (const x of coordinates.x) {
    for (const y of coordinates.y) {
      const point = { x, y };
      if (!obstacles.some((obstacle) => pointInsideRectangle(point, obstacle))) {
        nodes.set(coordinateKey(point), point);
      }
    }
    yield;
  }
  const startKey = coordinateKey(start);
  const endKey = coordinateKey(end);
  nodes.set(startKey, start);
  nodes.set(endKey, end);
  const neighbours = new Map([...nodes.keys()].map((key) => [key, []]));
  const rows = new Map();
  const columns = new Map();
  for (const point of nodes.values()) {
    if (!rows.has(point.y)) rows.set(point.y, []);
    rows.get(point.y).push(point);
    if (!columns.has(point.x)) columns.set(point.x, []);
    columns.get(point.x).push(point);
  }
  const connectLine = (line) => {
    for (let index = 1; index < line.length; index += 1) {
      const first = line[index - 1];
      const second = line[index];
      if (obstacles.some((obstacle) => segmentHitsRectangle(first, second, obstacle))) continue;
      const distance = Math.abs(first.x - second.x) + Math.abs(first.y - second.y);
      neighbours.get(coordinateKey(first)).push({ point: second, distance });
      neighbours.get(coordinateKey(second)).push({ point: first, distance });
    }
  };
  for (const y of coordinates.y) {
    connectLine((rows.get(y) ?? []).sort((a, b) => a.x - b.x));
    yield;
  }
  for (const x of coordinates.x) {
    connectLine((columns.get(x) ?? []).sort((a, b) => a.y - b.y));
    yield;
  }

  const stateKey = (pointKey, axis) => `${pointKey}|${axis ?? "none"}`;
  const startStateKey = stateKey(startKey, initialAxis);
  const costs = new Map([[startStateKey, { bends: 0, distance: 0 }]]);
  const previous = new Map();
  const pending = [];
  pushPendingEntry(pending, {
    pointKey: startKey,
    axis: initialAxis,
    key: startStateKey,
    cost: costs.get(startStateKey),
  });
  const visited = new Set();
  let bestEnd = null;
  let processed = 0;
  while (pending.length > 0) {
    processed += 1;
    if (processed % 64 === 0) yield;
    const current = popPendingEntry(pending);
    if (visited.has(current.key)) continue;
    visited.add(current.key);
    const currentCost = costs.get(current.key);
    if (current.pointKey === endKey) {
      const total = {
        bends: currentCost.bends
          + (finalAxis !== null && current.axis !== null && current.axis !== finalAxis ? 1 : 0),
        distance: currentCost.distance,
      };
      if (bestEnd === null || compareRouteCost(total, bestEnd.cost) < 0) {
        bestEnd = { key: current.key, cost: total };
      }
      continue;
    }
    for (const neighbour of neighbours.get(current.pointKey)) {
      const neighbourKey = coordinateKey(neighbour.point);
      const nextAxis = routeAxis(nodes.get(current.pointKey), neighbour.point);
      const nextKey = stateKey(neighbourKey, nextAxis);
      const cost = {
        bends: currentCost.bends
          + (current.axis !== null && current.axis !== nextAxis ? 1 : 0),
        distance: currentCost.distance + neighbour.distance,
      };
      const existing = costs.get(nextKey);
      if (existing !== undefined && compareRouteCost(cost, existing) >= 0) continue;
      costs.set(nextKey, cost);
      previous.set(nextKey, current.key);
      pushPendingEntry(pending, {
        pointKey: neighbourKey,
        axis: nextAxis,
        key: nextKey,
        cost,
      });
    }
  }
  if (bestEnd === null) return null;
  const result = [];
  let currentStateKey = bestEnd.key;
  while (currentStateKey !== undefined) {
    const separator = currentStateKey.lastIndexOf("|");
    const pointKey = currentStateKey.slice(0, separator);
    result.unshift(nodes.get(pointKey));
    if (currentStateKey === startStateKey) break;
    currentStateKey = previous.get(currentStateKey);
  }
  const route = normalizePath(result);
  return pathIsSafe(route, obstacles) ? route : null;
}

function* routePointsSteps(start, end, orientation, obstacles, endpoints = []) {
  const vertical = orientation === "vertical";
  const startLead = vertical
    ? { x: start.x, y: start.y + gridSize }
    : { x: start.x + gridSize, y: start.y };
  const endLead = vertical
    ? { x: end.x, y: end.y - gridSize }
    : { x: end.x - gridSize, y: end.y };
  const endpointRectangles = endpoints.map((endpoint) => endpoint.rectangle);
  if (pathIsSafe([start, startLead], obstacles, endpoints.filter((item) => item.endpoint === "source"))
    && pathIsSafe([endLead, end], obstacles, endpoints.filter((item) => item.endpoint === "target"))) {
    const leadAxis = vertical ? "vertical" : "horizontal";
    const interior = yield* gridRouteSteps(
      startLead,
      endLead,
      [...obstacles, ...endpointRectangles],
      leadAxis,
      leadAxis,
    );
    if (interior !== null) {
      const directed = normalizePath([start, ...interior, end]);
      if (pathIsSafe(directed, obstacles, endpoints)) return directed;
    }
  }
  const fallback = yield* gridRouteSteps(start, end, obstacles);
  return fallback !== null && pathIsSafe(fallback, obstacles, endpoints) ? fallback : null;
}

function routePoints(start, end, orientation, obstacles, endpoints = []) {
  return runRouteSteps(routePointsSteps(start, end, orientation, obstacles, endpoints));
}

function* routeFromSourceSteps(start, end, orientation, obstacles, endpoints) {
  const vertical = orientation === "vertical";
  const lead = vertical
    ? { x: start.x, y: start.y + gridSize }
    : { x: start.x + gridSize, y: start.y };
  const leadAxis = vertical ? "vertical" : "horizontal";
  if (pathIsSafe([start, lead], obstacles, endpoints)) {
    const endpointRectangles = endpoints
      .map((endpoint) => endpoint.rectangle)
      .filter((rectangle) => !pointInsideRectangle(lead, rectangle));
    const interior = yield* gridRouteSteps(lead, end, [...obstacles, ...endpointRectangles], leadAxis);
    if (interior !== null) {
      const directed = normalizePath([start, ...interior]);
      if (pathIsSafe(directed, obstacles, endpoints)) return directed;
    }
  }
  const fallback = yield* gridRouteSteps(start, end, obstacles);
  return fallback !== null && pathIsSafe(fallback, obstacles, endpoints) ? fallback : null;
}

function* routeToTargetSteps(start, end, orientation, obstacles, endpoints) {
  const vertical = orientation === "vertical";
  const lead = vertical
    ? { x: end.x, y: end.y - gridSize }
    : { x: end.x - gridSize, y: end.y };
  const leadAxis = vertical ? "vertical" : "horizontal";
  if (pathIsSafe([lead, end], obstacles, endpoints)) {
    const endpointRectangles = endpoints
      .map((endpoint) => endpoint.rectangle)
      .filter((rectangle) => !pointInsideRectangle(lead, rectangle));
    const interior = yield* gridRouteSteps(start, lead, [...obstacles, ...endpointRectangles], null, leadAxis);
    if (interior !== null) {
      const directed = normalizePath([...interior, end]);
      if (pathIsSafe(directed, obstacles, endpoints)) return directed;
    }
  }
  const fallback = yield* gridRouteSteps(start, end, obstacles);
  return fallback !== null && pathIsSafe(fallback, obstacles, endpoints) ? fallback : null;
}

function angularPath(points) {
  return points.reduce(
    (path, point, index) => `${path}${index === 0 ? "M" : " L"} ${point.x} ${point.y}`,
    "",
  );
}

function cubicPoint(curve, progress) {
  const inverse = 1 - progress;
  const firstWeight = inverse ** 3;
  const secondWeight = 3 * inverse ** 2 * progress;
  const thirdWeight = 3 * inverse * progress ** 2;
  const fourthWeight = progress ** 3;
  return {
    x: firstWeight * curve.start.x
      + secondWeight * curve.firstControl.x
      + thirdWeight * curve.secondControl.x
      + fourthWeight * curve.end.x,
    y: firstWeight * curve.start.y
      + secondWeight * curve.firstControl.y
      + thirdWeight * curve.secondControl.y
      + fourthWeight * curve.end.y,
  };
}

function directCurve(start, end, orientation) {
  const horizontal = orientation !== "vertical";
  const offset = horizontal ? end.x - start.x : end.y - start.y;
  const direction = Math.sign(offset) || 1;
  const distance = Math.abs(offset);
  const reach = Math.min(Math.max(54, distance * 0.48), distance / 2);
  return {
    start,
    firstControl: horizontal
      ? { x: start.x + direction * reach, y: start.y }
      : { x: start.x, y: start.y + direction * reach },
    secondControl: horizontal
      ? { x: end.x - direction * reach, y: end.y }
      : { x: end.x, y: end.y - direction * reach },
    end,
  };
}

function curvePath(curve) {
  return `M ${curve.start.x} ${curve.start.y} C ${curve.firstControl.x} ${curve.firstControl.y}, ${curve.secondControl.x} ${curve.secondControl.y}, ${curve.end.x} ${curve.end.y}`;
}

function guideCurve(points) {
  return points.length === 4
    ? [{
      start: points[0],
      firstControl: points[1],
      secondControl: points[2],
      end: points[3],
    }]
    : null;
}

function splineRouteCurves(points, tension) {
  const segmentLengths = points.slice(1).map((point, index) =>
    Math.hypot(point.x - points[index].x, point.y - points[index].y));
  const tangents = points.map((point, index) => {
    const previous = points[Math.max(0, index - 1)];
    const next = points[Math.min(points.length - 1, index + 1)];
    const deltaX = next.x - previous.x;
    const deltaY = next.y - previous.y;
    const length = Math.hypot(deltaX, deltaY);
    if (length > 0) return { x: deltaX / length, y: deltaY / length };
    const adjacent = index === points.length - 1 ? previous : next;
    const adjacentLength = Math.hypot(adjacent.x - point.x, adjacent.y - point.y);
    return adjacentLength === 0
      ? { x: 0, y: 0 }
      : {
        x: (adjacent.x - point.x) / adjacentLength,
        y: (adjacent.y - point.y) / adjacentLength,
      };
  });
  const handles = points.map((_, index) => {
    const previousLength = segmentLengths[Math.max(0, index - 1)];
    const nextLength = segmentLengths[Math.min(segmentLengths.length - 1, index)];
    return Math.min(previousLength, nextLength) * tension;
  });
  return points.slice(1).map((end, index) => {
    const start = points[index];
    return {
      start,
      firstControl: {
        x: start.x + tangents[index].x * handles[index],
        y: start.y + tangents[index].y * handles[index],
      },
      secondControl: {
        x: end.x - tangents[index + 1].x * handles[index + 1],
        y: end.y - tangents[index + 1].y * handles[index + 1],
      },
      end,
    };
  });
}

function sampleCurves(curves) {
  const points = [curves[0].start];
  for (const curve of curves) {
    for (let step = 1; step <= curveSampleCount; step += 1) {
      points.push(cubicPoint(curve, step / curveSampleCount));
    }
  }
  return points;
}

function curvesAreSafe(curves, obstacles, endpoints = []) {
  if (curves.length === 0) return false;
  const points = sampleCurves(curves);
  return !pathHitsObstacles(points, obstacles)
    && pathAvoidsEndpoints(points, endpoints)
    && !pathSelfIntersects(points);
}

function curvesPath(curves) {
  return curves.reduce(
    (path, curve, index) => `${path}${index === 0 ? `M ${curve.start.x} ${curve.start.y}` : ""} C ${curve.firstControl.x} ${curve.firstControl.y}, ${curve.secondControl.x} ${curve.secondControl.y}, ${curve.end.x} ${curve.end.y}`,
    "",
  );
}

function rectanglesIntersect(first, second) {
  return first.left < second.right
    && first.right > second.left
    && first.top < second.bottom
    && first.bottom > second.top;
}

function expandRectangle(rectangle, amount) {
  return {
    left: rectangle.left - amount,
    right: rectangle.right + amount,
    top: rectangle.top - amount,
    bottom: rectangle.bottom + amount,
  };
}

function branchLabelObstacles(obstacles, endpoints) {
  return [
    ...obstacles,
    ...endpoints.map((endpoint) => expandRectangle(endpoint.rectangle, 2)),
  ];
}

function branchLabelPoint(points, obstacles, bounds = null) {
  const offsets = [16, -16, 28, -28, 0];
  const lengths = [];
  let totalLength = 0;
  for (let index = 1; index < points.length; index += 1) {
    const length = Math.hypot(
      points[index].x - points[index - 1].x,
      points[index].y - points[index - 1].y,
    );
    lengths.push(length);
    totalLength += length;
  }
  let traversed = 0;
  const locations = [];
  for (let index = 0; index < lengths.length; index += 1) {
    const length = lengths[index];
    if (length === 0) continue;
    const start = points[index];
    const end = points[index + 1];
    const progressValues = length >= 38 ? [0.5, 0.25, 0.75] : [0.5];
    for (const progress of progressValues) {
      locations.push({
        distanceFromMiddle: Math.abs(traversed + length * progress - totalLength / 2),
        point: {
          x: start.x + (end.x - start.x) * progress,
          y: start.y + (end.y - start.y) * progress,
        },
        normal: {
          x: -(end.y - start.y) / length,
          y: (end.x - start.x) / length,
        },
      });
    }
    traversed += length;
  }
  locations.sort((first, second) => first.distanceFromMiddle - second.distanceFromMiddle);
  for (const location of locations) {
    for (const offset of offsets) {
      const candidate = {
        x: location.point.x + location.normal.x * offset,
        y: location.point.y + location.normal.y * offset,
      };
      const rectangle = {
        left: candidate.x - labelHalfWidth,
        right: candidate.x + labelHalfWidth,
        top: candidate.y - labelHalfHeight,
        bottom: candidate.y + labelHalfHeight,
      };
      if (
        bounds !== null
        && (rectangle.left < bounds.left
          || rectangle.right > bounds.right
          || rectangle.top < bounds.top
          || rectangle.bottom > bounds.bottom)
      ) {
        continue;
      }
      if (!obstacles.some((obstacle) => rectanglesIntersect(rectangle, obstacle))) {
        return candidate;
      }
    }
  }
  return null;
}

function routeCost(points) {
  let bends = 0;
  let distance = 0;
  for (let index = 1; index < points.length; index += 1) {
    distance += Math.abs(points[index].x - points[index - 1].x)
      + Math.abs(points[index].y - points[index - 1].y);
    if (index < 2) continue;
    if (routeAxis(points[index - 2], points[index - 1]) !== routeAxis(points[index - 1], points[index])) {
      bends += 1;
    }
  }
  return { bends, distance };
}

function labelLanes(start, end, orientation, rectangles) {
  const clearance = routeClearance + 32;
  const halfLength = 36;
  const offsets = [clearance, clearance * 2, clearance * 3];
  if (orientation === "vertical") {
    const top = Math.min(start.y, end.y, ...rectangles.map((rectangle) => rectangle.top));
    const bottom = Math.max(start.y, end.y, ...rectangles.map((rectangle) => rectangle.bottom));
    const left = Math.min(start.x, end.x, ...rectangles.map((rectangle) => rectangle.left));
    const right = Math.max(start.x, end.x, ...rectangles.map((rectangle) => rectangle.right));
    const xValues = new Set([
      (start.x + end.x) / 2,
      ...offsets.flatMap((offset) => [Math.max(12, left - offset), right + offset]),
    ]);
    const ranges = offsets.flatMap((offset) => {
      const exterior = [{ start: bottom + offset, end: bottom + offset + halfLength * 2 }];
      const topEnd = top - offset;
      if (topEnd - halfLength * 2 >= 12) {
        exterior.push({ start: topEnd - halfLength * 2, end: topEnd });
      }
      return exterior;
    });
    return [...xValues].flatMap((x) =>
      ranges.flatMap((range) => [
        { start: { x, y: range.start }, end: { x, y: range.end } },
        { start: { x, y: range.end }, end: { x, y: range.start } },
      ]));
  }

  const left = Math.min(start.x, end.x, ...rectangles.map((rectangle) => rectangle.left));
  const right = Math.max(start.x, end.x, ...rectangles.map((rectangle) => rectangle.right));
  const top = Math.min(start.y, end.y, ...rectangles.map((rectangle) => rectangle.top));
  const bottom = Math.max(start.y, end.y, ...rectangles.map((rectangle) => rectangle.bottom));
  const yValues = new Set([
    (start.y + end.y) / 2,
    ...offsets.flatMap((offset) => [Math.max(12, top - offset), bottom + offset]),
  ]);
  const ranges = offsets.flatMap((offset) => {
    const exterior = [{ start: right + offset, end: right + offset + halfLength * 2 }];
    const leftEnd = left - offset;
    if (leftEnd - halfLength * 2 >= 12) {
      exterior.push({ start: leftEnd - halfLength * 2, end: leftEnd });
    }
    return exterior;
  });
  return [...yValues].flatMap((y) =>
    ranges.flatMap((range) => [
      { start: { x: range.start, y }, end: { x: range.end, y } },
      { start: { x: range.end, y }, end: { x: range.start, y } },
    ]));
}

function* routeThroughLabelLaneSteps(start, end, orientation, obstacles, endpoints, lane) {
  const firstEndpoints = endpoints.filter((endpoint) =>
    pointInsideRectangle(start, endpoint.rectangle));
  const secondEndpoints = endpoints.filter((endpoint) =>
    pointInsideRectangle(end, endpoint.rectangle));
  const firstObstacles = [
    ...obstacles,
    ...endpoints
      .filter((endpoint) => !firstEndpoints.includes(endpoint))
      .map((endpoint) => endpoint.rectangle),
  ];
  const secondObstacles = [
    ...obstacles,
    ...endpoints
      .filter((endpoint) => !secondEndpoints.includes(endpoint))
      .map((endpoint) => endpoint.rectangle),
  ];
  const first = yield* routeFromSourceSteps(
    start,
    lane.start,
    orientation,
    firstObstacles,
    firstEndpoints,
  );
  if (first === null) return null;
  yield;
  const second = yield* routeToTargetSteps(
    lane.end,
    end,
    orientation,
    secondObstacles,
    secondEndpoints,
  );
  if (second === null) return null;
  const points = normalizePath([...first, lane.end, ...second.slice(1)]);
  return pathIsSafe(points, obstacles, endpoints) ? points : null;
}

function directExteriorCorridors(start, end, orientation, rectangles) {
  const clearance = routeClearance + 32;
  const offsets = [clearance, clearance * 2, clearance * 3];
  if (orientation === "vertical") {
    const left = Math.min(start.x, end.x, ...rectangles.map((rectangle) => rectangle.left));
    const right = Math.max(start.x, end.x, ...rectangles.map((rectangle) => rectangle.right));
    return offsets.flatMap((offset) => [
      normalizePath([
        start,
        { x: Math.max(12, left - offset), y: start.y },
        { x: Math.max(12, left - offset), y: end.y },
        end,
      ]),
      normalizePath([
        start,
        { x: right + offset, y: start.y },
        { x: right + offset, y: end.y },
        end,
      ]),
    ]);
  }

  const top = Math.min(start.y, end.y, ...rectangles.map((rectangle) => rectangle.top));
  const bottom = Math.max(start.y, end.y, ...rectangles.map((rectangle) => rectangle.bottom));
  return offsets.flatMap((offset) => [
    normalizePath([
      start,
      { x: start.x, y: Math.max(12, top - offset) },
      { x: end.x, y: Math.max(12, top - offset) },
      end,
    ]),
    normalizePath([
      start,
      { x: start.x, y: bottom + offset },
      { x: end.x, y: bottom + offset },
      end,
    ]),
  ]);
}

function* routePointCandidateSteps(
  start,
  end,
  orientation,
  obstacles,
  endpoints,
  labelBounds = null,
) {
  const rectangles = branchLabelObstacles(obstacles, endpoints);
  const direct = yield* routePointsSteps(start, end, orientation, obstacles, endpoints);
  const candidates = [];
  const seen = new Set();
  const addCandidate = (points) => {
    if (points === null) return;
    if (!pathIsSafe(points, obstacles, endpoints)) return;
    const key = angularPath(points);
    if (seen.has(key)) return;
    const label = branchLabelPoint(points, rectangles, labelBounds);
    if (label === null) return;
    seen.add(key);
    candidates.push({ points, label, cost: routeCost(points) });
  };
  addCandidate(direct);
  yield;
  for (const corridor of directExteriorCorridors(start, end, orientation, rectangles)) {
    addCandidate(corridor);
    yield;
  }
  for (const lane of labelLanes(start, end, orientation, rectangles)) {
    addCandidate(
      yield* routeThroughLabelLaneSteps(start, end, orientation, obstacles, endpoints, lane),
    );
    yield;
  }
  candidates.sort((first, second) => compareRouteCost(first.cost, second.cost));
  return candidates;
}

function* routePointsWithLabelSteps(
  start,
  end,
  orientation,
  obstacles,
  endpoints,
  labelBounds = null,
) {
  const rectangles = branchLabelObstacles(obstacles, endpoints);
  const direct = yield* routePointsSteps(start, end, orientation, obstacles, endpoints);
  if (direct !== null) {
    const label = branchLabelPoint(direct, rectangles, labelBounds);
    if (label !== null) return { points: direct, label };
  }
  yield;
  const candidates = yield* routePointCandidateSteps(
    start,
    end,
    orientation,
    obstacles,
    endpoints,
    labelBounds,
  );
  return candidates[0] ?? null;
}

function* smoothRouteSteps(
  start,
  end,
  orientation,
  obstacles,
  endpoints = [],
  needsLabel = false,
  labelBounds = null,
) {
  const rectangles = branchLabelObstacles(obstacles, endpoints);
  const direct = directCurve(start, end, orientation);
  if (curvesAreSafe([direct], obstacles, endpoints)) {
    const points = sampleCurves([direct]);
    const label = needsLabel ? branchLabelPoint(points, rectangles, labelBounds) : null;
    if (!needsLabel || label !== null) return { path: curvePath(direct), points, label };
  }
  yield;
  const routedCandidates = needsLabel
    ? yield* routePointCandidateSteps(
      start,
      end,
      orientation,
      obstacles,
      endpoints,
      labelBounds,
    )
    : [
      {
        points: yield* routePointsSteps(start, end, orientation, obstacles, endpoints),
        label: null,
      },
    ];
  for (const routed of routedCandidates) {
    if (routed.points === null) continue;
    const guided = guideCurve(routed.points);
    if (guided !== null && curvesAreSafe(guided, obstacles, endpoints)) {
      const points = sampleCurves(guided);
      const label = needsLabel ? branchLabelPoint(points, rectangles, labelBounds) : null;
      if (!needsLabel || label !== null) return { path: curvesPath(guided), points, label };
    }
    for (const tension of [0.42, 0.34, 0.26, 0.18, 0.12, 0.08, 0.04]) {
      const curves = splineRouteCurves(routed.points, tension);
      if (curvesAreSafe(curves, obstacles, endpoints)) {
        const points = sampleCurves(curves);
        const label = needsLabel ? branchLabelPoint(points, rectangles, labelBounds) : null;
        if (!needsLabel || label !== null) return { path: curvesPath(curves), points, label };
      }
    }
    yield;
  }
  return null;
}

function runRouteSteps(steps) {
  for (;;) {
    const next = steps.next();
    if (next.done) return next.value;
  }
}

function smoothRoute(
  start,
  end,
  orientation,
  obstacles,
  endpoints = [],
  needsLabel = false,
  labelBounds = null,
) {
  return runRouteSteps(
    smoothRouteSteps(start, end, orientation, obstacles, endpoints, needsLabel, labelBounds),
  );
}

function commitRoute(group, label, route) {
  const accepted = route !== null && (label === null || route.label !== null);
  const path = accepted ? route.path : "";
  for (const element of group.querySelectorAll("path")) element.setAttribute("d", path);
  if (label === null) return;
  if (!accepted) {
    label.style.display = "none";
    label.removeAttribute("transform");
    return;
  }
  label.setAttribute("transform", `translate(${route.label.x} ${route.label.y})`);
  label.style.display = "";
}

function liveAngularPoints(start, end, orientation) {
  if (orientation === "vertical") {
    const middle = (start.y + end.y) / 2;
    return normalizePath([start, { x: start.x, y: middle }, { x: end.x, y: middle }, end]);
  }
  const middle = (start.x + end.x) / 2;
  return normalizePath([start, { x: middle, y: start.y }, { x: middle, y: end.y }, end]);
}

function pathMidpoint(points) {
  const segments = points.slice(1).map((point, index) => ({
    start: points[index],
    end: point,
    length: Math.hypot(point.x - points[index].x, point.y - points[index].y),
  }));
  const target = segments.reduce((length, segment) => length + segment.length, 0) / 2;
  let travelled = 0;
  for (const segment of segments) {
    if (travelled + segment.length < target) {
      travelled += segment.length;
      continue;
    }
    const progress = segment.length === 0 ? 0 : (target - travelled) / segment.length;
    return {
      x: segment.start.x + (segment.end.x - segment.start.x) * progress,
      y: segment.start.y + (segment.end.y - segment.start.y) * progress,
    };
  }
  return points.at(-1);
}

function routeEdgeLive(state, group) {
  metrics.routeEdgeLiveCount += 1;
  const start = portPoint(
    state,
    group.dataset.sourceNode,
    group.dataset.sourcePort,
    "output",
  );
  const end = portPoint(
    state,
    group.dataset.targetNode,
    group.dataset.targetPort,
    "input",
  );
  const label = group.querySelector("[data-edge-label]");
  if (start === null || end === null) {
    commitRoute(group, label, null);
    return;
  }

  const orientation = state.shell.dataset.orientation;
  if (state.shell.dataset.edgeStyle === "smooth") {
    const curve = directCurve(start, end, orientation);
    commitRoute(group, label, {
      path: curvePath(curve),
      label: label === null ? null : cubicPoint(curve, 0.5),
    });
    return;
  }

  const points = liveAngularPoints(start, end, orientation);
  commitRoute(group, label, {
    path: angularPath(points),
    label: label === null ? null : pathMidpoint(points),
  });
}

function routingFrame(state) {
  const nodes = [...state.root.querySelectorAll("[data-automation-node]")]
    .filter((node) => node instanceof HTMLElement)
    .map((node) => {
      const position = nodeGraphPosition(node);
      return {
        id: node.dataset.automationNode,
        obstacle: nodeGraphRectangle(node, obstacleMargin),
        endpoint: nodeGraphRectangle(node),
        signature: `${node.dataset.automationNode}:${position.x},${position.y},${node.offsetWidth},${node.offsetHeight},${node.clientLeft},${node.clientTop}`,
      };
    });
  const viewBox = state.root.querySelector(".automation-edges")?.viewBox.baseVal;
  const labelBounds = viewBox === undefined
    ? null
    : {
      left: viewBox.x,
      top: viewBox.y,
      right: viewBox.x + viewBox.width,
      bottom: viewBox.y + viewBox.height,
    };
  const settingsSignature = `${state.shell.dataset.orientation}|${state.shell.dataset.edgeStyle}|${labelBounds === null
    ? "none"
    : `${labelBounds.left},${labelBounds.top},${labelBounds.right},${labelBounds.bottom}`}`;
  return {
    nodes,
    nodesById: new Map(nodes.map((node) => [node.id, node])),
    ports: new Map(),
    labelBounds,
    settingsSignature,
    signature: `${settingsSignature}|${nodes.map((node) => node.signature).join("|")}`,
  };
}

function framePortPoint(state, frame, nodeId, portId, direction) {
  const key = `${nodeId}|${portId}|${direction}`;
  if (!frame.ports.has(key)) frame.ports.set(key, portPoint(state, nodeId, portId, direction));
  return frame.ports.get(key);
}

function relevantObstacleNodes(frame, sourceNode, targetNode, start, end) {
  let region = {
    left: Math.min(start.x, end.x) - routeRegionMargin,
    top: Math.min(start.y, end.y) - routeRegionMargin,
    right: Math.max(start.x, end.x) + routeRegionMargin,
    bottom: Math.max(start.y, end.y) + routeRegionMargin,
  };
  const include = (rectangle) => {
    region = {
      left: Math.min(region.left, rectangle.left - routeRegionMargin),
      top: Math.min(region.top, rectangle.top - routeRegionMargin),
      right: Math.max(region.right, rectangle.right + routeRegionMargin),
      bottom: Math.max(region.bottom, rectangle.bottom + routeRegionMargin),
    };
  };
  const source = frame.nodesById.get(sourceNode);
  const target = frame.nodesById.get(targetNode);
  if (source !== undefined) include(source.obstacle);
  if (target !== undefined) include(target.obstacle);
  const remaining = frame.nodes.filter(
    (node) => node.id !== sourceNode && node.id !== targetNode,
  );
  const relevant = [];
  let added = true;
  while (added) {
    added = false;
    for (let index = 0; index < remaining.length; index += 1) {
      const node = remaining[index];
      if (node === null || !rectanglesIntersect(node.obstacle, region)) continue;
      remaining[index] = null;
      relevant.push(node);
      include(node.obstacle);
      added = true;
    }
  }
  return relevant;
}

function edgeRouteInputs(state, frame, group, needsLabel) {
  const sourceNode = group.dataset.sourceNode;
  const targetNode = group.dataset.targetNode;
  const start = framePortPoint(state, frame, sourceNode, group.dataset.sourcePort, "output");
  const end = framePortPoint(state, frame, targetNode, group.dataset.targetPort, "input");
  const source = frame.nodesById.get(sourceNode);
  const target = frame.nodesById.get(targetNode);
  const base = `${frame.settingsSignature}|${needsLabel}|${source?.signature ?? "missing"}|${target?.signature ?? "missing"}`;
  if (start === null || end === null) {
    return { start: null, end: null, key: `${base}|unroutable` };
  }
  const relevant = relevantObstacleNodes(frame, sourceNode, targetNode, start, end);
  const relevantSignature = relevant
    .map((node) => node.signature)
    .sort()
    .join(";");
  return {
    start,
    end,
    source,
    target,
    relevant,
    key: `${base}|${start.x},${start.y}>${end.x},${end.y}|${relevantSignature}`,
  };
}

function* computeEdgeRouteSteps(state, frame, inputs, needsLabel) {
  const nodeRectangles = inputs.relevant.map((node) => node.obstacle);
  const overlappingEndpoints = nodeRectangles.filter((rectangle) =>
    pointInsideRectangle(inputs.start, rectangle)
    || pointInsideRectangle(inputs.end, rectangle));
  const obstacles = nodeRectangles.filter((rectangle) => !overlappingEndpoints.includes(rectangle));
  const endpoints = [
    inputs.source === undefined
      ? null
      : { rectangle: inputs.source.endpoint, endpoint: "source" },
    inputs.target === undefined
      ? null
      : { rectangle: inputs.target.endpoint, endpoint: "target" },
    ...overlappingEndpoints.map((rectangle) => ({ rectangle, endpoint: "overlap" })),
  ].filter((endpoint) => endpoint !== null);
  const orientation = state.shell.dataset.orientation;
  const labelBounds = frame.labelBounds;
  const start = inputs.start;
  const end = inputs.end;
  if (state.shell.dataset.edgeStyle === "smooth") {
    return yield* smoothRouteSteps(
      start,
      end,
      orientation,
      obstacles,
      endpoints,
      needsLabel,
      labelBounds,
    );
  }
  const routed = needsLabel
    ? yield* routePointsWithLabelSteps(start, end, orientation, obstacles, endpoints, labelBounds)
    : {
      points: yield* routePointsSteps(start, end, orientation, obstacles, endpoints),
      label: null,
    };
  return routed === null || routed.points === null
    ? null
    : { path: angularPath(routed.points), points: routed.points, label: routed.label };
}

function cancelRoutePass(state) {
  const pass = state.routePass;
  if (pass === null) return;
  if (pass.timer !== null) clearTimeout(pass.timer);
  if (pass.begun) state.routedSignature = null;
  state.routePass = null;
}

const routePassSliceSpacingMs = 12;

function scheduleRoutePassSlice(state, pass) {
  pass.timer = setTimeout(() => {
    if (state.routePass !== pass) return;
    pass.timer = null;
    runRoutePassSlice(state, pass);
  }, routePassSliceSpacingMs);
}

function runRoutePassSlice(state, pass) {
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
      pass.edges = [...state.root.querySelectorAll("[data-automation-edge]")];
      pass.index = 0;
      pass.job = null;
      metrics.routeRecalculationCount += 1;
      for (const node of frame.nodes) {
        if (state.nodeSignatures.get(node.id) === node.signature) continue;
        for (const key of [...state.portOffsets.keys()]) {
          if (key.startsWith(`${node.id}|`)) state.portOffsets.delete(key);
        }
      }
      state.nodeSignatures = new Map(frame.nodes.map((node) => [node.id, node.signature]));
      for (const group of pass.edges) {
        framePortPoint(state, frame, group.dataset.sourceNode, group.dataset.sourcePort, "output");
        framePortPoint(state, frame, group.dataset.targetNode, group.dataset.targetPort, "input");
      }
    }
    pass.begun = true;
    pass.stale = false;
    if (pass.index < pass.edges.length && !withinBudget()) {
      scheduleRoutePassSlice(state, pass);
      return;
    }
  }
  while (pass.index < pass.edges.length) {
    if (pass.job === null) {
      const group = pass.edges[pass.index];
      metrics.routeEdgeCount += 1;
      const label = group.querySelector("[data-edge-label]");
      const inputs = edgeRouteInputs(state, pass.routingFrame, group, label !== null);
      const cached = state.routeCache.get(group.dataset.automationEdge);
      if (cached?.key === inputs.key) {
        metrics.routeCacheHitCount += 1;
        commitRoute(group, label, cached.route);
        pass.index += 1;
        if (!withinBudget()) break;
        continue;
      }
      metrics.routeCacheMissCount += 1;
      if (inputs.start === null || inputs.end === null) {
        state.routeCache.set(group.dataset.automationEdge, { key: inputs.key, route: null });
        commitRoute(group, label, null);
        pass.index += 1;
        if (!withinBudget()) break;
        continue;
      }
      metrics.routeComputationCount += 1;
      pass.job = {
        group,
        label,
        inputs,
        steps: computeEdgeRouteSteps(state, pass.routingFrame, inputs, label !== null),
      };
    }
    let next = pass.job.steps.next();
    while (!next.done && withinBudget()) next = pass.job.steps.next();
    if (!next.done) break;
    state.routeCache.set(pass.job.group.dataset.automationEdge, {
      key: pass.job.inputs.key,
      route: next.value,
    });
    commitRoute(pass.job.group, pass.job.label, next.value);
    pass.job = null;
    pass.index += 1;
    if (!withinBudget()) break;
  }
  if (pass.index < pass.edges.length) {
    scheduleRoutePassSlice(state, pass);
    return;
  }
  state.routePass = null;
  state.routedSignature = pass.routingFrame.signature;
  state.root.dataset.automationCanvasReady = "true";
}

function scheduleRoutePass(state) {
  if (state.drag !== null && state.drag.moved) return;
  if (state.routePass !== null) {
    state.routePass.stale = true;
    return;
  }
  const pass = {
    timer: null,
    routingFrame: null,
    edges: [],
    index: 0,
    job: null,
    begun: false,
    stale: false,
  };
  state.routePass = pass;
  scheduleRoutePassSlice(state, pass);
}

function cancelDragRouteFrame(state) {
  if (state.dragFrame === null) return;
  cancelAnimationFrame(state.dragFrame);
  state.dragFrame = null;
}

function scheduleDragRoutes(state) {
  if (state.dragFrame !== null || state.drag === null) return;
  state.dragFrame = requestAnimationFrame(() => {
    state.dragFrame = null;
    if (state.drag === null) return;
    metrics.dragFrames += 1;
    metrics.routeEdgeLiveMaximumPerFrame = Math.max(
      metrics.routeEdgeLiveMaximumPerFrame,
      state.drag.edges.length,
    );
    for (const item of state.drag.nodes) setNodeLiveGraphPosition(item);
    for (const edge of state.drag.edges) routeEdgeLive(state, edge);
  });
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
    nullability: port.dataset.portNullability,
    sourceKind: port.closest("[data-automation-node]")?.dataset.nodeKind,
  };
  port.classList.add("automation-port--source");
  state.root.classList.add("automation-canvas--connecting");
  for (const input of state.root.querySelectorAll('[data-port-direction="input"]')) {
    const compatible = compatibleTarget(state, input);
    input.classList.toggle("automation-port--compatible", compatible);
  }
}

function beginConnectionDrag(state, event, port) {
  event.preventDefault();
  event.stopPropagation();
  startConnection(state, port);
  try {
    port.setPointerCapture(event.pointerId);
  } catch (error) {
    if (event.isTrusted) throw error;
  }
  state.connectionDrag = {
    pointerId: event.pointerId,
    capture: port,
    startX: event.clientX,
    startY: event.clientY,
    moved: false,
  };
  updateConnectionPreview(state, event.clientX, event.clientY);
}

function compatibleTarget(state, port) {
  if (
    state.connection === null
    || port.dataset.portDirection !== "input"
    || port.dataset.nodeId === state.connection.nodeId
    || port.dataset.portType !== state.connection.type
  ) return false;
  const data = state.connection.type !== "Flow";
  return (!data || state.connection.sourceKind !== "action")
    && (!data || port.dataset.portOccupied !== "true")
    && (!data
      || state.connection.nullability !== "Nullable"
      || port.dataset.portNullability === "Nullable")
    && (!data
      || state.connection.sensitivity !== "Sensitive"
      || port.dataset.portSensitivity === "Sensitive");
}

function connectionReleaseTarget(state, clientX, clientY) {
  const hit = document.elementFromPoint(clientX, clientY);
  if (!(hit instanceof Element)) return { port: null, rejected: false };
  const port = hit.closest("[data-automation-port]");
  if (port instanceof HTMLButtonElement) {
    if (
      port.dataset.portDirection === "output"
      && port.dataset.nodeId === state.connection?.nodeId
      && port.dataset.portId === state.connection?.portId
    ) {
      return { port: null, rejected: false };
    }
    return { port: compatibleTarget(state, port) ? port : null, rejected: true };
  }
  const node = hit.closest("[data-automation-node]");
  if (!(node instanceof HTMLElement)) return { port: null, rejected: false };
  const compatible = [...node.querySelectorAll('[data-port-direction="input"]')]
    .filter((candidate) => compatibleTarget(state, candidate));
  return {
    port: compatible.length === 1 ? compatible[0] : null,
    rejected: compatible.length !== 1,
  };
}

function completeConnection(state, port) {
  const connection = state.connection;
  if (connection === null) return;
  cancelConnection(state);
  void state.dotnet.invokeMethodAsync(
    "ConnectFromCanvasAsync",
    connection.nodeId,
    connection.portId,
    port.dataset.nodeId,
    port.dataset.portId,
  );
}

function finishConnectionDrag(state, event, cancelled = false) {
  const drag = state.connectionDrag;
  if (drag === null || drag.pointerId !== event.pointerId) return false;
  state.connectionDrag = null;
  if (drag.capture.hasPointerCapture(event.pointerId)) {
    drag.capture.releasePointerCapture(event.pointerId);
  }
  const target = cancelled
    ? { port: null, rejected: false }
    : connectionReleaseTarget(state, event.clientX, event.clientY);
  if (target.port !== null) completeConnection(state, target.port);
  else {
    cancelConnection(state);
    if (target.rejected) void state.dotnet.invokeMethodAsync("RejectConnectionFromCanvasAsync");
  }
  state.suppressPortClick = drag.moved;
  return true;
}

function cancelConnectionDrag(state) {
  const drag = state.connectionDrag;
  if (drag !== null && drag.capture.hasPointerCapture(drag.pointerId)) {
    drag.capture.releasePointerCapture(drag.pointerId);
  }
  state.connectionDrag = null;
  state.suppressPortClick = true;
  cancelConnection(state);
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
  const end = screenToGraph(state, clientX, clientY);
  const orientation = state.shell.dataset.orientation;
  const sourceEndpoint = endpointObstacle(state, state.connection.nodeId, "source");
  const endpoints = sourceEndpoint === null ? [] : [sourceEndpoint];
  const points = routePoints(start, end, orientation, [], endpoints);
  const route = state.shell.dataset.edgeStyle === "smooth"
    ? smoothRoute(start, end, orientation, [], endpoints)
    : points === null ? null : { path: angularPath(points) };
  state.preview.setAttribute(
    "d",
    route?.path ?? "",
  );
  if (state.connectionDrag !== null) {
    state.connectionDrag.moved ||= Math.abs(clientX - state.connectionDrag.startX) > 3
      || Math.abs(clientY - state.connectionDrag.startY) > 3;
  }
}

function beginNodeDrag(state, event, node) {
  event.preventDefault();
  event.stopPropagation();
  let selected = new Set(selectionIds(state.root));
  const nodeId = node.dataset.automationNode;
  if (event.shiftKey) {
    setLocalDisclosure(state.root, null);
    void state.dotnet.invokeMethodAsync("CloseNodeDisclosureFromCanvasAsync");
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
  state.drag = {
    pointerId: event.pointerId,
    capture: node,
    startX: event.clientX,
    startY: event.clientY,
    moved: false,
    discloseOnClick: !event.shiftKey && !event.altKey && !event.ctrlKey && !event.metaKey,
    nodes,
    edges: [...state.root.querySelectorAll("[data-automation-edge]")].filter((edge) =>
      selected.has(edge.dataset.sourceNode) || selected.has(edge.dataset.targetNode)),
  };
}

function moveNodeDrag(state, event) {
  const drag = state.drag;
  if (drag === null || drag.pointerId !== event.pointerId) return false;
  const scale = zoomScale(state);
  const deltaX = (event.clientX - drag.startX) / scale;
  const deltaY = (event.clientY - drag.startY) / scale;
  if (!drag.moved && (Math.abs(deltaX) > 2 || Math.abs(deltaY) > 2)) {
    drag.moved = true;
    cancelRoutePass(state);
    setLocalDisclosure(state.root, null);
    void state.dotnet.invokeMethodAsync("CloseNodeDisclosureFromCanvasAsync");
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

function finishNodeDrag(state, event, cancelled = false) {
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
  if (drag.moved) void state.dotnet.invokeMethodAsync("MoveNodesFromCanvasAsync", moves);
  else if (!cancelled && drag.discloseOnClick) {
    const nodeId = drag.capture.dataset.automationNode;
    setLocalDisclosure(state.root, nodeId);
    void state.dotnet.invokeMethodAsync("ActivateNodeFromCanvasAsync", nodeId);
  }
  scheduleRoutePass(state);
  return true;
}

function beginBackgroundAction(state, event) {
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
    setLocalDisclosure(state.root, null);
    void state.dotnet.invokeMethodAsync("CloseNodeDisclosureFromCanvasAsync");
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

function moveBackgroundAction(state, event) {
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

function applyMarqueeSelection(state) {
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

function scheduleMarqueeUpdate(state) {
  if (state.marqueeFrame !== null) return;
  state.marqueeFrame = requestAnimationFrame(() => {
    state.marqueeFrame = null;
    applyMarqueeSelection(state);
  });
}

function cancelMarqueeUpdate(state) {
  if (state.marqueeFrame === null) return;
  cancelAnimationFrame(state.marqueeFrame);
  state.marqueeFrame = null;
}

function finishBackgroundAction(state, event) {
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

function register(state, element, name, handler, options) {
  element.addEventListener(name, handler, options);
  state.listeners.push(() => element.removeEventListener(name, handler, options));
}

function changeZoom(state, direction, clientX, clientY) {
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

function isEditingControl(target) {
  return target instanceof Element
    && target.closest("input, textarea, select, [contenteditable]:not([contenteditable='false']), [role='textbox']") !== null;
}

function moveSelectionByKeyboard(state, key) {
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
    marqueeFrame: null,
    routeCache: new Map(),
    portOffsets: new Map(),
    nodeSignatures: new Map(),
    listeners: [],
  };

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
      const nodeId = selector.closest("[data-automation-node]")?.dataset.automationNode;
      if (nodeId === undefined) return;
      if (event.shiftKey) {
        setLocalDisclosure(state.root, null);
        scheduleRoutePass(state);
        void state.dotnet.invokeMethodAsync("ToggleNodeSelectionFromCanvasAsync", nodeId);
        return;
      }
      setLocalSelection(state.root, [nodeId], null);
      setLocalDisclosure(state.root, nodeId);
      scheduleRoutePass(state);
      void state.dotnet.invokeMethodAsync("ActivateNodeFromCanvasAsync", nodeId);
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
  cancelRoutePass(state);
  state.routedSignature = null;
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
  for (const remove of state.listeners.reverse()) remove();
  activeStates.delete(state);
  states.delete(root);
}
