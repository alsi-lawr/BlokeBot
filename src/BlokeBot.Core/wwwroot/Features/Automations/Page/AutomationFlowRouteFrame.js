import {
  gridSize,
  nodeGraphPosition,
  nodeGraphRectangle,
  portPoint,
} from "./AutomationFlowCanvasState.js";

export const obstacleMargin = 18;
export const routeClearance = 12;

export function routingFrame(state) {
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
  const edges = [...state.root.querySelectorAll("[data-automation-edge]")].map((group) => ({
    group,
    edgeId: group.dataset.automationEdge,
    sourceNode: group.dataset.sourceNode,
    sourcePort: group.dataset.sourcePort,
    targetNode: group.dataset.targetNode,
    targetPort: group.dataset.targetPort,
    label: group.querySelector("[data-edge-label]"),
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
  const settingsSignature = `${state.shell.dataset.orientation}|${state.shell.dataset.edgeStyle}|${labelBounds === null
    ? "none"
    : `${labelBounds.left},${labelBounds.top},${labelBounds.right},${labelBounds.bottom}`}`;
  const edgeSignature = edges
    .map((edge) =>
      `${edge.edgeId}:${edge.sourceNode}:${edge.sourcePort}>${edge.targetNode}:${edge.targetPort}:${edge.label !== null}`)
    .join(";");
  const frame = {
    nodes,
    nodesById: new Map(nodes.map((node) => [node.id, node])),
    edges,
    ports: new Map(),
    labelBounds,
    settingsSignature,
    signature: "",
  };
  for (const node of nodes) {
    if (state.nodeSignatures.get(node.id) === node.signature) continue;
    for (const key of [...state.portOffsets.keys()]) {
      if (key.startsWith(`${node.id}|`)) state.portOffsets.delete(key);
    }
  }
  state.nodeSignatures = new Map(nodes.map((node) => [node.id, node.signature]));
  const anchorSignature = edges.map((edge) => {
    const source = framePortPoint(
      state,
      frame,
      edge.sourceNode,
      edge.sourcePort,
      "output",
    );
    const target = framePortPoint(
      state,
      frame,
      edge.targetNode,
      edge.targetPort,
      "input",
    );
    const point = (value) => value === null ? "missing" : `${value.x},${value.y}`;
    return `${edge.edgeId}:${point(source)}>${point(target)}`;
  }).join(";");
  frame.signature = `${settingsSignature}|${nodes.map((node) => node.signature).join("|")}|${edgeSignature}|${anchorSignature}`;
  return frame;
}

export function framePortPoint(state, frame, nodeId, portId, direction) {
  const key = `${nodeId}|${portId}|${direction}`;
  if (!frame.ports.has(key)) frame.ports.set(key, portPoint(state, nodeId, portId, direction));
  return frame.ports.get(key);
}

export const bendWeight = 2 ** 20;
export const nudgeSpacing = 8;
export const nudgeMaximumSpread = 20;

export function uniqueSorted(values) {
  return [...new Set(values)].sort((first, second) => first - second);
}

export function pointInsideAnyObstacle(point, obstacles) {
  return obstacles.some((obstacle) =>
    point.x > obstacle.left
    && point.x < obstacle.right
    && point.y > obstacle.top
    && point.y < obstacle.bottom);
}

export function resolveEdgeAnchors(resolvePort, edge, vertical, obstacles) {
  const start = resolvePort(edge.sourceNode, edge.sourcePort, "output");
  const end = resolvePort(edge.targetNode, edge.targetPort, "input");
  if (start === null || end === null) return null;
  const step = vertical ? { x: 0, y: gridSize } : { x: gridSize, y: 0 };
  let sourceLead = { x: start.x + step.x, y: start.y + step.y };
  for (let guard = 0; guard < 32 && pointInsideAnyObstacle(sourceLead, obstacles); guard += 1) {
    sourceLead = { x: sourceLead.x + step.x, y: sourceLead.y + step.y };
  }
  let targetLead = { x: end.x - step.x, y: end.y - step.y };
  for (let guard = 0; guard < 32 && pointInsideAnyObstacle(targetLead, obstacles); guard += 1) {
    targetLead = { x: targetLead.x - step.x, y: targetLead.y - step.y };
  }
  return { start, end, sourceLead, targetLead };
}

// One shared orthogonal visibility graph for the whole scene: interesting
// coordinates come from every obstacle boundary plus clearance and from the
// route anchors, and axis-aligned adjacency is open exactly where the segment
// between neighbouring coordinates crosses no obstacle interior.
export function buildVisibilityGraph(obstacles, anchorPoints) {
  const xs = [];
  const ys = [];
  for (const obstacle of obstacles) {
    xs.push(obstacle.left - routeClearance, obstacle.right + routeClearance);
    ys.push(obstacle.top - routeClearance, obstacle.bottom + routeClearance);
  }
  for (const point of anchorPoints) {
    xs.push(point.x);
    ys.push(point.y);
  }
  const xCoords = uniqueSorted(xs);
  const yCoords = uniqueSorted(ys);
  const width = xCoords.length;
  const height = yCoords.length;
  const free = new Uint8Array(width * height);
  for (let row = 0; row < height; row += 1) {
    for (let col = 0; col < width; col += 1) {
      free[row * width + col] = pointInsideAnyObstacle(
        { x: xCoords[col], y: yCoords[row] },
        obstacles,
      )
        ? 0
        : 1;
    }
  }
  const rightOpen = new Uint8Array(width * height);
  const downOpen = new Uint8Array(width * height);
  for (let row = 0; row < height; row += 1) {
    const y = yCoords[row];
    for (let col = 0; col < width - 1; col += 1) {
      const vertex = row * width + col;
      if (free[vertex] === 0 || free[vertex + 1] === 0) continue;
      const left = xCoords[col];
      const right = xCoords[col + 1];
      rightOpen[vertex] = obstacles.some((obstacle) =>
        y > obstacle.top && y < obstacle.bottom && left < obstacle.right && right > obstacle.left)
        ? 0
        : 1;
    }
  }
  for (let col = 0; col < width; col += 1) {
    const x = xCoords[col];
    for (let row = 0; row < height - 1; row += 1) {
      const vertex = row * width + col;
      if (free[vertex] === 0 || free[vertex + width] === 0) continue;
      const top = yCoords[row];
      const bottom = yCoords[row + 1];
      downOpen[vertex] = obstacles.some((obstacle) =>
        x > obstacle.left && x < obstacle.right && top < obstacle.bottom && bottom > obstacle.top)
        ? 0
        : 1;
    }
  }
  return {
    xCoords,
    yCoords,
    width,
    height,
    free,
    rightOpen,
    downOpen,
    xIndex: new Map(xCoords.map((value, index) => [value, index])),
    yIndex: new Map(yCoords.map((value, index) => [value, index])),
  };
}

// Incremental maintenance of that graph for a drag frame. The base graph holds
// every standing-still obstacle and route anchor and is built once per drag;
// this re-adds only the moving nodes' coordinate contributions, inherits every
// cell whose row and column both survive unchanged, evaluates only the inserted
// rows and columns against the static obstacles, and blocks only the cells the
// moving rectangles now cover. No frame rebuilds the whole graph.
export function patchVisibilityGraph(base, staticObstacles, extraXs, extraYs, movingObstacles) {
  const xCoords = uniqueSorted([...base.xCoords, ...extraXs]);
  const yCoords = uniqueSorted([...base.yCoords, ...extraYs]);
  const width = xCoords.length;
  const height = yCoords.length;
  const baseColumns = xCoords.map((value) => base.xIndex.get(value) ?? -1);
  const baseRows = yCoords.map((value) => base.yIndex.get(value) ?? -1);
  const free = new Uint8Array(width * height);
  const rightOpen = new Uint8Array(width * height);
  const downOpen = new Uint8Array(width * height);
  for (let row = 0; row < height; row += 1) {
    const y = yCoords[row];
    const baseRow = baseRows[row];
    const spanning = staticObstacles.filter((obstacle) => y > obstacle.top && y < obstacle.bottom);
    const moving = movingObstacles.filter((obstacle) => y > obstacle.top && y < obstacle.bottom);
    for (let col = 0; col < width; col += 1) {
      const x = xCoords[col];
      const inherited = baseRow >= 0 && baseColumns[col] >= 0;
      let open = inherited
        ? base.free[baseRow * base.width + baseColumns[col]]
        : (spanning.some((obstacle) => x > obstacle.left && x < obstacle.right) ? 0 : 1);
      if (open === 1 && moving.some((obstacle) => x > obstacle.left && x < obstacle.right)) {
        open = 0;
      }
      free[row * width + col] = open;
    }
    for (let col = 0; col < width - 1; col += 1) {
      const vertex = row * width + col;
      if (free[vertex] === 0 || free[vertex + 1] === 0) continue;
      const left = xCoords[col];
      const right = xCoords[col + 1];
      const inherited = baseRow >= 0
        && baseColumns[col] >= 0
        && baseColumns[col + 1] === baseColumns[col] + 1;
      let open = inherited
        ? base.rightOpen[baseRow * base.width + baseColumns[col]]
        : (spanning.some((obstacle) => left < obstacle.right && right > obstacle.left) ? 0 : 1);
      if (open === 1 && moving.some((obstacle) => left < obstacle.right && right > obstacle.left)) {
        open = 0;
      }
      rightOpen[vertex] = open;
    }
  }
  for (let col = 0; col < width; col += 1) {
    const x = xCoords[col];
    const baseColumn = baseColumns[col];
    const spanning = staticObstacles.filter((obstacle) => x > obstacle.left && x < obstacle.right);
    const moving = movingObstacles.filter((obstacle) => x > obstacle.left && x < obstacle.right);
    for (let row = 0; row < height - 1; row += 1) {
      const vertex = row * width + col;
      if (free[vertex] === 0 || free[vertex + width] === 0) continue;
      const top = yCoords[row];
      const bottom = yCoords[row + 1];
      const inherited = baseColumn >= 0
        && baseRows[row] >= 0
        && baseRows[row + 1] === baseRows[row] + 1;
      let open = inherited
        ? base.downOpen[baseRows[row] * base.width + baseColumn]
        : (spanning.some((obstacle) => top < obstacle.bottom && bottom > obstacle.top) ? 0 : 1);
      if (open === 1 && moving.some((obstacle) => top < obstacle.bottom && bottom > obstacle.top)) {
        open = 0;
      }
      downOpen[vertex] = open;
    }
  }
  return {
    xCoords,
    yCoords,
    width,
    height,
    free,
    rightOpen,
    downOpen,
    xIndex: new Map(xCoords.map((value, index) => [value, index])),
    yIndex: new Map(yCoords.map((value, index) => [value, index])),
  };
}
