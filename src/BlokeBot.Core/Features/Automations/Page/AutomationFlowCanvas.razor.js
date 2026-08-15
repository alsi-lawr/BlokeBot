const states = new WeakMap();
const gridSize = 24;
const obstacleMargin = 18;
const routeClearance = 12;
const curveSampleCount = 96;
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

function zoomScale(state) {
  return zoomSteps[state.zoomIndex].scale;
}

function applyTransform(state) {
  const step = zoomSteps[state.zoomIndex];
  state.stage.style.transform = `translate(${state.panX}px, ${state.panY}px) scale(${step.scale})`;
  state.zoomReset.textContent = step.label;
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
  const style = getComputedStyle(node);
  return { x: Number.parseFloat(style.left), y: Number.parseFloat(style.top) };
}

function setNodeGraphPosition(node, x, y) {
  node.dataset.automationGraphX = `${x}`;
  node.dataset.automationGraphY = `${y}`;
  node.style.left = `${x}px`;
  node.style.top = `${y}px`;
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
  const port = state.root.querySelector(
    `[data-automation-port][data-node-id="${CSS.escape(nodeId)}"][data-port-id="${CSS.escape(portId)}"][data-port-direction="${direction}"]`,
  );
  if (!(port instanceof HTMLElement)) return null;
  const node = port.closest("[data-automation-node]");
  if (!(node instanceof HTMLElement)) return null;
  const position = nodeGraphPosition(node);
  const portStyle = getComputedStyle(port);
  const portTransform = new DOMMatrixReadOnly(portStyle.transform);
  return {
    x: position.x
      + node.clientLeft
      + Number.parseFloat(portStyle.left)
      + Number.parseFloat(portStyle.width) / 2
      + portTransform.e,
    y: position.y
      + node.clientTop
      + Number.parseFloat(portStyle.top)
      + Number.parseFloat(portStyle.height) / 2
      + portTransform.f,
  };
}

function nodeObstacles(state, excluded) {
  return [...state.root.querySelectorAll("[data-automation-node]")]
    .filter((node) => !excluded.has(node.dataset.automationNode))
    .map((node) => nodeGraphRectangle(node, obstacleMargin));
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
    for (let second = first + 3; second < points.length; second += 1) {
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
    return !((previous.x === point.x && point.x === next.x)
      || (previous.y === point.y && point.y === next.y));
  });
}

function pathAvoidsEndpoints(points, endpoints) {
  for (const endpoint of endpoints) {
    if (endpoint.endpoint === "source") {
      let exited = false;
      for (let index = 1; index < points.length; index += 1) {
        if (!exited) {
          exited = !pointInsideRectangle(points[index], endpoint.rectangle);
          continue;
        }
        if (segmentHitsRectangle(points[index - 1], points[index], endpoint.rectangle)) {
          return false;
        }
      }
      continue;
    }
    let exited = false;
    for (let index = points.length - 2; index >= 0; index -= 1) {
      if (!exited) {
        exited = !pointInsideRectangle(points[index], endpoint.rectangle);
        continue;
      }
      if (segmentHitsRectangle(points[index], points[index + 1], endpoint.rectangle)) {
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

function gridRoute(start, end, obstacles) {
  const coordinates = gridCoordinates(start, end, obstacles);
  const nodes = new Map();
  for (const x of coordinates.x) {
    for (const y of coordinates.y) {
      const point = { x, y };
      if (!obstacles.some((obstacle) => pointInsideRectangle(point, obstacle))) {
        nodes.set(coordinateKey(point), point);
      }
    }
  }
  const startKey = coordinateKey(start);
  const endKey = coordinateKey(end);
  nodes.set(startKey, start);
  nodes.set(endKey, end);
  const neighbours = new Map([...nodes.keys()].map((key) => [key, []]));
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
    connectLine([...nodes.values()].filter((point) => point.y === y).sort((a, b) => a.x - b.x));
  }
  for (const x of coordinates.x) {
    connectLine([...nodes.values()].filter((point) => point.x === x).sort((a, b) => a.y - b.y));
  }

  const distances = new Map([[startKey, 0]]);
  const previous = new Map();
  const unvisited = new Set(nodes.keys());
  while (unvisited.size > 0) {
    const currentKey = [...unvisited].reduce((best, candidate) =>
      (distances.get(candidate) ?? Number.POSITIVE_INFINITY)
        < (distances.get(best) ?? Number.POSITIVE_INFINITY) ? candidate : best);
    if (!distances.has(currentKey)) break;
    unvisited.delete(currentKey);
    if (currentKey === endKey) break;
    for (const neighbour of neighbours.get(currentKey)) {
      const neighbourKey = coordinateKey(neighbour.point);
      if (!unvisited.has(neighbourKey)) continue;
      const distance = distances.get(currentKey) + neighbour.distance;
      if (distance >= (distances.get(neighbourKey) ?? Number.POSITIVE_INFINITY)) continue;
      distances.set(neighbourKey, distance);
      previous.set(neighbourKey, currentKey);
    }
  }
  if (!distances.has(endKey)) return null;
  const result = [];
  let currentKey = endKey;
  while (currentKey !== undefined) {
    result.unshift(nodes.get(currentKey));
    if (currentKey === startKey) break;
    currentKey = previous.get(currentKey);
  }
  const route = normalizePath(result);
  return pathIsSafe(route, obstacles) ? route : null;
}

function routePoints(start, end, orientation, obstacles, endpoints = []) {
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
    const interior = gridRoute(startLead, endLead, [...obstacles, ...endpointRectangles]);
    if (interior !== null) {
      const directed = normalizePath([start, ...interior, end]);
      if (pathIsSafe(directed, obstacles, endpoints)) return directed;
    }
  }
  const fallback = gridRoute(start, end, obstacles);
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
  const distance = horizontal ? Math.abs(end.x - start.x) : Math.abs(end.y - start.y);
  const reach = Math.max(54, distance * 0.48);
  return {
    start,
    firstControl: horizontal
      ? { x: start.x + reach, y: start.y }
      : { x: start.x, y: start.y + reach },
    secondControl: horizontal
      ? { x: end.x - reach, y: end.y }
      : { x: end.x, y: end.y - reach },
    end,
  };
}

function curvePath(curve) {
  return `M ${curve.start.x} ${curve.start.y} C ${curve.firstControl.x} ${curve.firstControl.y}, ${curve.secondControl.x} ${curve.secondControl.y}, ${curve.end.x} ${curve.end.y}`;
}

function lineCurve(start, end) {
  return {
    start,
    firstControl: {
      x: start.x + (end.x - start.x) / 3,
      y: start.y + (end.y - start.y) / 3,
    },
    secondControl: {
      x: start.x + 2 * (end.x - start.x) / 3,
      y: start.y + 2 * (end.y - start.y) / 3,
    },
    end,
  };
}

function unitVector(start, end) {
  const length = Math.hypot(end.x - start.x, end.y - start.y);
  return { x: (end.x - start.x) / length, y: (end.y - start.y) / length, length };
}

function roundedRouteCurves(points, maximumRadius) {
  const curves = [];
  let cursor = points[0];
  for (let index = 1; index < points.length - 1; index += 1) {
    const corner = points[index];
    const incoming = unitVector(points[index - 1], corner);
    const outgoing = unitVector(corner, points[index + 1]);
    const radius = Math.min(maximumRadius, incoming.length * 0.45, outgoing.length * 0.45);
    const entry = {
      x: corner.x - incoming.x * radius,
      y: corner.y - incoming.y * radius,
    };
    const exit = {
      x: corner.x + outgoing.x * radius,
      y: corner.y + outgoing.y * radius,
    };
    if (!samePoint(cursor, entry)) curves.push(lineCurve(cursor, entry));
    const controlDistance = radius * 0.55228475;
    curves.push({
      start: entry,
      firstControl: {
        x: entry.x + incoming.x * controlDistance,
        y: entry.y + incoming.y * controlDistance,
      },
      secondControl: {
        x: exit.x - outgoing.x * controlDistance,
        y: exit.y - outgoing.y * controlDistance,
      },
      end: exit,
    });
    cursor = exit;
  }
  if (!samePoint(cursor, points.at(-1))) curves.push(lineCurve(cursor, points.at(-1)));
  return curves;
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

function curveLabel(curves) {
  const points = sampleCurves(curves);
  return points[Math.floor(points.length / 2)];
}

function smoothRoute(start, end, orientation, obstacles, endpoints = []) {
  const direct = directCurve(start, end, orientation);
  if (curvesAreSafe([direct], obstacles, endpoints)) {
    return { path: curvePath(direct), label: cubicPoint(direct, 0.5) };
  }
  const points = routePoints(start, end, orientation, obstacles, endpoints);
  if (points === null) return null;
  for (const radius of [48, 36, 24, 12, 6]) {
    const curves = roundedRouteCurves(points, radius);
    if (curvesAreSafe(curves, obstacles, endpoints)) {
      return { path: curvesPath(curves), label: curveLabel(curves) };
    }
  }
  const safeCurves = points.slice(1).map((point, index) => lineCurve(points[index], point));
  return curvesAreSafe(safeCurves, obstacles, endpoints)
    ? { path: curvesPath(safeCurves), label: curveLabel(safeCurves) }
    : null;
}

function routeEdge(state, group) {
  const sourceNode = group.dataset.sourceNode;
  const targetNode = group.dataset.targetNode;
  const start = portPoint(state, sourceNode, group.dataset.sourcePort, "output");
  const end = portPoint(state, targetNode, group.dataset.targetPort, "input");
  if (start === null || end === null) return;
  const orientation = state.shell.dataset.orientation;
  const obstacles = nodeObstacles(state, new Set([sourceNode, targetNode]));
  const endpoints = [
    endpointObstacle(state, sourceNode, "source"),
    endpointObstacle(state, targetNode, "target"),
  ].filter((endpoint) => endpoint !== null);
  const points = routePoints(start, end, orientation, obstacles, endpoints);
  const route = points === null
    ? null
    : state.shell.dataset.edgeStyle === "smooth"
    ? smoothRoute(start, end, orientation, obstacles, endpoints)
    : { path: angularPath(points), label: points[Math.floor(points.length / 2)] };
  const path = route?.path ?? "";
  for (const element of group.querySelectorAll("path")) element.setAttribute("d", path);
  const label = group.querySelector("text");
  if (label !== null && route !== null) {
    const anchor = route.label;
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
  return state.connection !== null
    && port.dataset.portDirection === "input"
    && port.dataset.nodeId !== state.connection.nodeId
    && port.dataset.portType === state.connection.type
    && port.dataset.portSensitivity === state.connection.sensitivity;
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
    if (selected.has(nodeId)) selected.delete(nodeId);
    else selected.add(nodeId);
  } else if (!selected.has(nodeId)) {
    selected = new Set([nodeId]);
  }
  setLocalSelection(state.root, selected, null);
  void notifySelection(state);
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
    nodes,
  };
  state.root.classList.add("automation-canvas--node-dragging");
  for (const item of nodes) item.element.classList.add("automation-node--moving");
}

function moveNodeDrag(state, event) {
  const drag = state.drag;
  if (drag === null || drag.pointerId !== event.pointerId) return false;
  const scale = zoomScale(state);
  const deltaX = (event.clientX - drag.startX) / scale;
  const deltaY = (event.clientY - drag.startY) / scale;
  drag.moved ||= Math.abs(deltaX) > 2 || Math.abs(deltaY) > 2;
  for (const item of drag.nodes) {
    item.x = Math.max(0, item.startX + deltaX);
    item.y = Math.max(0, item.startY + deltaY);
    setNodeGraphPosition(item.element, item.x, item.y);
  }
  routeAll(state);
  return true;
}

function finishNodeDrag(state, event) {
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
  try {
    state.root.setPointerCapture(event.pointerId);
  } catch (error) {
    if (event.isTrusted) throw error;
  }
  if (event.altKey) {
    const start = screenToGraph(state, event.clientX, event.clientY);
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
    .filter((node) => {
      const rectangle = nodeGraphRectangle(node);
      return rectangle.left < left + width
        && rectangle.right > left
        && rectangle.top < top + height
        && rectangle.bottom > top;
    })
    .map((node) => node.dataset.automationNode);
  setLocalSelection(state.root, ids, null);
  return true;
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
      state.root.focus({ preventScroll: true });
      void notifySelection(state);
    }
    return true;
  }
  if (state.marqueeState?.pointerId !== event.pointerId) return false;
  state.marqueeState = null;
  if (state.root.hasPointerCapture(event.pointerId)) {
    state.root.releasePointerCapture(event.pointerId);
  }
  state.marquee.hidden = true;
  state.root.classList.remove("automation-canvas--selecting");
  void notifySelection(state);
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
  routeAll(state);
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
    if (finishNodeDrag(state, event)) return;
    finishBackgroundAction(state, event);
  });
  register(state, root, "click", (event) => {
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
  if (state.drag === null) {
    for (const node of root.querySelectorAll("[data-automation-node]")) {
      delete node.dataset.automationGraphX;
      delete node.dataset.automationGraphY;
      node.style.removeProperty("left");
      node.style.removeProperty("top");
    }
  }
  cancelConnection(state);
  routeAll(state);
}

export function focusNode(root, nodeId) {
  root.querySelector(
    `[data-automation-node="${CSS.escape(nodeId)}"] [data-automation-node-select]`,
  )?.focus({ preventScroll: true });
}

export function dispose(root) {
  const state = states.get(root);
  if (state === undefined) return;
  for (const remove of state.listeners.reverse()) remove();
  states.delete(root);
}
