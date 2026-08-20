import {
  nodeGraphRectangle,
  portPoint,
  setNodeLiveGraphPosition,
} from "./AutomationFlowCanvasState.js";
import {
  normalizePath,
  routeBounds,
  segmentHitsRectangle,
} from "./AutomationFlowRouteGeometry.js";
import {
  commitRoute,
  pathMidpoint,
  rectanglesIntersect,
} from "./AutomationFlowRouteCurves.js";
import {
  buildVisibilityGraph,
  obstacleMargin,
  patchVisibilityGraph,
  resolveEdgeAnchors,
  routeClearance,
  routingFrame,
} from "./AutomationFlowRouteFrame.js";
import { routeOverGraph } from "./AutomationFlowRouteSearch.js";
import {
  directSmoothRoute,
  nudgeSceneRoutes,
  skeletonRoute,
} from "./AutomationFlowRouteScene.js";

export const dragLiveRouteCap = 24;
// Live previews check clearance on a coarser sampling of the identical curves.
// The committed path is the same Bezier either way; only the granularity of the
// preview's collision test changes, and the drop pass re-checks at full detail.
export const dragCurveSampleCount = 32;
// Budget for the search phase of a frame. The smoothing, nudging, and commit
// that follow it cost roughly as much again for the edges the search accepted,
// so this is deliberately about half of the frame time a drag may spend.
export const dragRouteFrameBudgetMs = 0.8;

export function beginDragRouting(state) {
  const frame = routingFrame(state);
  const movingIds = new Set(state.drag.nodes.map((item) => item.nodeId));
  // Selecting a node changes its border metrics, so its cached port offsets are
  // resolved again once here rather than every frame.
  for (const key of [...state.portOffsets.keys()]) {
    if (movingIds.has(key.slice(0, key.indexOf("|")))) state.portOffsets.delete(key);
  }
  const moving = state.drag.nodes
    .map((item) => ({ item, node: frame.nodesById.get(item.nodeId) }))
    .filter((entry) => entry.node !== undefined)
    .map((entry) => ({
      ...entry,
      width: entry.node.endpoint.right - entry.node.endpoint.left,
      height: entry.node.endpoint.bottom - entry.node.endpoint.top,
      previous: entry.node.obstacle,
    }));
  const staticObstacles = frame.nodes
    .filter((node) => !movingIds.has(node.id))
    .map((node) => node.obstacle);
  const orientation = state.shell.dataset.orientation;
  const vertical = orientation === "vertical";
  const resolvePort = (nodeId, portId, direction) => portPoint(state, nodeId, portId, direction);
  const settled = state.sceneCache?.results ?? null;
  const items = frame.edges.map((edge) => {
    const route = settled?.get(edge.edgeId)?.route ?? null;
    const item = {
      edge,
      incident: movingIds.has(edge.sourceNode) || movingIds.has(edge.targetNode),
      anchors: null,
      points: route === null ? null : route.points,
      bounds: route === null ? null : routeBounds(route.points),
      deferred: false,
      probed: false,
    };
    // Anchors of an edge between standing-still nodes cannot move during the
    // drag, so they are resolved once here. Their lead points join the graph
    // per frame, only while the edge is actually being rerouted.
    if (!item.incident) {
      item.anchors = resolveEdgeAnchors(resolvePort, edge, vertical, staticObstacles);
    }
    return item;
  });
  return {
    frame,
    moving,
    staticObstacles,
    baseGraph: buildVisibilityGraph(staticObstacles, []),
    items,
    orientation,
    vertical,
    smooth: state.shell.dataset.edgeStyle === "smooth",
    resolvePort,
  };
}

// The moving rectangles for this frame, returning the region each node swept
// since the previous frame. The sweep is what an edge's corridor is tested
// against, so an edge the node has just left is rerouted as well as one it has
// just entered.
export function sweepMovingNodes(session) {
  return session.moving.map((entry) => {
    const obstacle = {
      left: entry.item.x - obstacleMargin,
      top: entry.item.y - obstacleMargin,
      right: entry.item.x + entry.width + obstacleMargin,
      bottom: entry.item.y + entry.height + obstacleMargin,
    };
    const swept = {
      left: Math.min(obstacle.left, entry.previous.left) - routeClearance,
      top: Math.min(obstacle.top, entry.previous.top) - routeClearance,
      right: Math.max(obstacle.right, entry.previous.right) + routeClearance,
      bottom: Math.max(obstacle.bottom, entry.previous.bottom) + routeClearance,
    };
    entry.previous = obstacle;
    entry.node.obstacle = obstacle;
    entry.node.endpoint = {
      left: entry.item.x,
      top: entry.item.y,
      right: entry.item.x + entry.width,
      bottom: entry.item.y + entry.height,
    };
    return swept;
  });
}

export function routeCrossesRectangles(points, rectangles) {
  for (let index = 1; index < points.length; index += 1) {
    for (const rectangle of rectangles) {
      if (segmentHitsRectangle(points[index - 1], points[index], rectangle)) return true;
    }
  }
  return false;
}

// Affected edges in the order they earn the frame's budget: the dragged nodes'
// own edges first, deferred work ahead of fresh work inside each group so no
// edge can starve, then edges whose corridor the sweep crosses, then edges with
// no known route yet (probed once per drag, conservatively).
export function dragAffectedItems(session, swept) {
  const groups = [[], [], [], [], []];
  for (const item of session.items) {
    if (item.incident) {
      groups[item.deferred ? 0 : 1].push(item);
      continue;
    }
    if (item.points === null) {
      if (!item.probed) groups[4].push(item);
      continue;
    }
    if (!swept.some((rectangle) => rectanglesIntersect(rectangle, item.bounds))) continue;
    if (!routeCrossesRectangles(item.points, swept)) continue;
    groups[item.deferred ? 2 : 3].push(item);
  }
  return groups.flat();
}

export function commitDragRoute(item) {
  if (item.route === null) return;
  if (item.edge.label !== null) {
    // Live previews keep the cheap midpoint label they have always used; the
    // authoritative lane placement happens in the drop pass.
    item.route.label = pathMidpoint(item.route.points);
  }
  // An unchanged route is not written back, so a frame only repaints the edges
  // it actually moved.
  if (item.route.path !== item.committedPath) {
    commitRoute(item.edge.group, item.edge.label, item.route);
    item.committedPath = item.route.path;
  }
  item.points = item.route.points;
  item.bounds = routeBounds(item.route.points);
}

export function runDragRouteFrame(state) {
  const started = performance.now();
  state.dragRouting ??= beginDragRouting(state);
  const session = state.dragRouting;
  for (const item of state.drag.nodes) setNodeLiveGraphPosition(item);
  const swept = sweepMovingNodes(session);
  const movingObstacles = session.moving.map((entry) => entry.node.obstacle);
  const obstacles = session.frame.nodes.map((node) => node.obstacle);
  const extraXs = [];
  const extraYs = [];
  for (const obstacle of movingObstacles) {
    extraXs.push(obstacle.left - routeClearance, obstacle.right + routeClearance);
    extraYs.push(obstacle.top - routeClearance, obstacle.bottom + routeClearance);
  }
  for (const item of session.items) {
    if (!item.incident) continue;
    item.anchors = resolveEdgeAnchors(session.resolvePort, item.edge, session.vertical, obstacles);
  }
  const affected = dragAffectedItems(session, swept);
  for (const item of affected) {
    if (item.anchors === null) continue;
    extraXs.push(item.anchors.sourceLead.x, item.anchors.targetLead.x);
    extraYs.push(item.anchors.sourceLead.y, item.anchors.targetLead.y);
  }
  const graph = patchVisibilityGraph(
    session.baseGraph,
    session.staticObstacles,
    extraXs,
    extraYs,
    movingObstacles,
  );
  const flowAxis = session.vertical ? 1 : 0;
  const routed = [];
  const skeletons = [];
  for (const item of affected) {
    if (
      routed.length >= dragLiveRouteCap
      || (routed.length > 0 && performance.now() - started >= dragRouteFrameBudgetMs)
    ) {
      item.deferred = true;
      continue;
    }
    item.deferred = false;
    item.probed = true;
    item.route = null;
    routed.push(item);
    if (item.anchors === null) continue;
    if (session.smooth) {
      item.route = directSmoothRoute(
        session.frame,
        item.edge,
        item.anchors,
        session.orientation,
        dragCurveSampleCount,
      );
      if (item.route !== null) continue;
    }
    // The last argument turns on the remaining-bend estimate, which only live
    // routing uses.
    const inner = routeOverGraph(
      graph,
      item.anchors.sourceLead,
      item.anchors.targetLead,
      flowAxis,
      true,
    );
    if (inner === null) continue;
    skeletons.push({
      owner: item,
      edge: item.edge,
      points: normalizePath([item.anchors.start, ...inner, item.anchors.end]),
    });
  }
  nudgeSceneRoutes(skeletons);
  for (const skeleton of skeletons) {
    skeleton.owner.route = skeletonRoute(
      skeleton.points,
      session.frame,
      skeleton.owner.edge,
      skeleton.owner.anchors,
      session.smooth,
      dragCurveSampleCount,
    );
  }
  for (const item of routed) commitDragRoute(item);
}

export function cancelDragRouteFrame(state) {
  if (state.dragFrame === null) return;
  cancelAnimationFrame(state.dragFrame);
  state.dragFrame = null;
}

export function scheduleDragRoutes(state) {
  if (state.dragFrame !== null || state.drag === null) return;
  state.dragFrame = requestAnimationFrame(() => {
    state.dragFrame = null;
    if (state.drag === null) return;
    runDragRouteFrame(state);
  });
}
