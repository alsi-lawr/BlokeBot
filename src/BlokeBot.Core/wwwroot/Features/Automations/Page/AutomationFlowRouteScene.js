import {
  angularPath,
  normalizePath,
  pathSelfIntersects,
  pointInsideRectangle,
} from "./AutomationFlowRouteGeometry.js";
import {
  branchLabelObstacles,
  branchLabelPoint,
  curvePath,
  curveSampleCount,
  curvesPath,
  directCurve,
  guideCurve,
  pathMidpoint,
  sampleCurves,
  sampledCurvesClear,
  splineRouteCurves,
} from "./AutomationFlowRouteCurves.js";
import {
  buildVisibilityGraph,
  nudgeMaximumSpread,
  nudgeSpacing,
  resolveEdgeAnchors,
} from "./AutomationFlowRouteFrame.js";
import { routeOverGraph } from "./AutomationFlowRouteSearch.js";

export function collectNudgeSegments(items) {
  const segments = [];
  for (const item of items) {
    const points = item.points;
    if (points === null) continue;
    for (let index = 2; index <= points.length - 2; index += 1) {
      const first = points[index - 1];
      const second = points[index];
      if (first.x === second.x && first.y !== second.y) {
        segments.push({
          item,
          index,
          axis: 1,
          coordinate: first.x,
          from: Math.min(first.y, second.y),
          to: Math.max(first.y, second.y),
        });
      } else if (first.y === second.y && first.x !== second.x) {
        segments.push({
          item,
          index,
          axis: 0,
          coordinate: first.y,
          from: Math.min(first.x, second.x),
          to: Math.max(first.x, second.x),
        });
      }
    }
  }
  return segments;
}

export function separateCluster(cluster) {
  const spacing = Math.min(nudgeSpacing, nudgeMaximumSpread / (cluster.length - 1));
  cluster.forEach((segment, position) => {
    const offset = (position - (cluster.length - 1) / 2) * spacing;
    if (offset === 0) return;
    const points = segment.item.points;
    const first = points[segment.index - 1];
    const second = points[segment.index];
    if (segment.axis === 1) {
      points[segment.index - 1] = { x: segment.coordinate + offset, y: first.y };
      points[segment.index] = { x: segment.coordinate + offset, y: second.y };
    } else {
      points[segment.index - 1] = { x: first.x, y: segment.coordinate + offset };
      points[segment.index] = { x: second.x, y: segment.coordinate + offset };
    }
  });
}

// One global nudging pass: overlapping parallel interior segments in the same
// channel are ordered deterministically and separated into lanes. Anchor stubs
// stay fixed at their ports, so same-port fan-outs still share their stub.
export function nudgeSceneRoutes(items) {
  const segments = collectNudgeSegments(items);
  for (const axis of [0, 1]) {
    const channels = new Map();
    for (const segment of segments) {
      if (segment.axis !== axis) continue;
      if (!channels.has(segment.coordinate)) channels.set(segment.coordinate, []);
      channels.get(segment.coordinate).push(segment);
    }
    for (const channel of channels.values()) {
      channel.sort((first, second) =>
        first.from - second.from
        || first.to - second.to
        || first.item.edge.edgeId.localeCompare(second.item.edge.edgeId)
        || first.index - second.index);
      let cluster = [];
      let clusterEnd = -Infinity;
      const flush = () => {
        if (cluster.length > 1) separateCluster(cluster);
        cluster = [];
      };
      for (const segment of channel) {
        if (cluster.length > 0 && segment.from >= clusterEnd) flush();
        cluster.push(segment);
        clusterEnd = Math.max(clusterEnd, segment.to);
      }
      flush();
    }
  }
}

export function edgeObstaclesAndEndpoints(frame, edge, start, end) {
  const nodeRectangles = frame.nodes
    .filter((node) => node.id !== edge.sourceNode && node.id !== edge.targetNode)
    .map((node) => node.obstacle);
  const overlappingEndpoints = nodeRectangles.filter((rectangle) =>
    pointInsideRectangle(start, rectangle) || pointInsideRectangle(end, rectangle));
  const obstacles = nodeRectangles.filter(
    (rectangle) => !overlappingEndpoints.includes(rectangle),
  );
  const sourceEndpoint = frame.nodesById.get(edge.sourceNode)?.endpoint;
  const targetEndpoint = frame.nodesById.get(edge.targetNode)?.endpoint;
  const endpoints = [
    sourceEndpoint === undefined
      ? null
      : { rectangle: sourceEndpoint, endpoint: "source" },
    targetEndpoint === undefined
      ? null
      : { rectangle: targetEndpoint, endpoint: "target" },
    ...overlappingEndpoints.map((rectangle) => ({ rectangle, endpoint: "overlap" })),
  ].filter((endpoint) => endpoint !== null);
  return { obstacles, endpoints };
}

// Smooth mode drapes a spline over the nudged orthogonal skeleton, falling back
// to the angular skeleton itself when no sampled spline stays clear.
export function smoothSkeleton(points, frame, edge, anchors, sampleCount) {
  const scope = edgeObstaclesAndEndpoints(frame, edge, anchors.start, anchors.end);
  if (!pathSelfIntersects(points)) {
    const guided = guideCurve(points);
    if (guided !== null && sampledCurvesClear(guided, scope.obstacles, scope.endpoints, sampleCount)) {
      return { path: curvesPath(guided), points: sampleCurves(guided, sampleCount), label: null };
    }
    for (const tension of [0.42, 0.34, 0.26, 0.18, 0.12, 0.08, 0.04]) {
      const curves = splineRouteCurves(points, tension);
      if (sampledCurvesClear(curves, scope.obstacles, scope.endpoints, sampleCount)) {
        return { path: curvesPath(curves), points: sampleCurves(curves, sampleCount), label: null };
      }
    }
  }
  return { path: angularPath(points), points, label: null };
}

// The smooth fast path: a direct curve is taken whenever it stays clear of the
// edge's obstacles and endpoints, so no graph search is needed for it.
export function directSmoothRoute(frame, edge, anchors, orientation, sampleCount) {
  const scope = edgeObstaclesAndEndpoints(frame, edge, anchors.start, anchors.end);
  const curve = directCurve(anchors.start, anchors.end, orientation);
  return sampledCurvesClear([curve], scope.obstacles, scope.endpoints, sampleCount)
    ? { path: curvePath(curve), points: sampleCurves([curve], sampleCount), label: null }
    : null;
}

export function skeletonRoute(skeleton, frame, edge, anchors, smooth, sampleCount) {
  const points = normalizePath(skeleton);
  return smooth
    ? smoothSkeleton(points, frame, edge, anchors, sampleCount)
    : { path: angularPath(points), points, label: null };
}

// The scene pipeline: resolve anchors, take the smooth direct-curve fast path,
// build one shared visibility graph, route every remaining edge over it, nudge
// globally, then smooth and place labels. This is a pure function of the frame
// geometry; the caller commits the returned routes in one atomic step.
export function* computeSceneSteps(frame, options) {
  const vertical = options.orientation === "vertical";
  const smooth = options.edgeStyle === "smooth";
  const obstacles = frame.nodes.map((node) => node.obstacle);
  const items = frame.edges.map((edge) => ({
    edge,
    needsLabel: edge.label !== null,
    anchors: null,
    direct: false,
    points: null,
    route: null,
  }));
  for (const item of items) {
    item.anchors = resolveEdgeAnchors(options.resolvePort, item.edge, vertical, obstacles);
  }
  yield;
  if (smooth) {
    for (const item of items) {
      if (item.anchors === null) continue;
      item.route = directSmoothRoute(
        frame,
        item.edge,
        item.anchors,
        options.orientation,
        curveSampleCount,
      );
      item.direct = item.route !== null;
      yield;
    }
  }
  const anchorPoints = [];
  for (const item of items) {
    if (item.anchors === null || item.direct) continue;
    anchorPoints.push(item.anchors.sourceLead, item.anchors.targetLead);
  }
  const graph = buildVisibilityGraph(obstacles, anchorPoints);
  yield;
  const flowAxis = vertical ? 1 : 0;
  for (const item of items) {
    if (item.anchors === null || item.direct) continue;
    const inner = routeOverGraph(graph, item.anchors.sourceLead, item.anchors.targetLead, flowAxis);
    item.points = inner === null
      ? null
      : normalizePath([item.anchors.start, ...inner, item.anchors.end]);
    yield;
  }
  nudgeSceneRoutes(items);
  yield;
  for (const item of items) {
    if (item.points !== null) {
      item.route = skeletonRoute(
        item.points,
        frame,
        item.edge,
        item.anchors,
        smooth,
        curveSampleCount,
      );
    }
    if (item.route !== null && item.needsLabel) {
      const scope = edgeObstaclesAndEndpoints(
        frame,
        item.edge,
        item.anchors.start,
        item.anchors.end,
      );
      const rectangles = branchLabelObstacles(scope.obstacles, scope.endpoints);
      item.route.label = branchLabelPoint(item.route.points, rectangles, frame.labelBounds)
        ?? pathMidpoint(item.route.points);
    }
    yield;
  }
  return new Map(items.map((item) => [item.edge.edgeId, { route: item.route }]));
}
