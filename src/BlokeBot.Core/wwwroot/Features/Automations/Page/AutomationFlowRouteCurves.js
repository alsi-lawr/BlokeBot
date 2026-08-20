import {
  normalizePath,
  pathAvoidsEndpoints,
  pathHitsObstacles,
} from "./AutomationFlowRouteGeometry.js";

export const curveSampleCount = 96;
const labelHalfWidth = 20;
const labelHalfHeight = 12;

export function cubicPoint(curve, progress) {
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

export function directCurve(start, end, orientation) {
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

export function curvePath(curve) {
  return `M ${curve.start.x} ${curve.start.y} C ${curve.firstControl.x} ${curve.firstControl.y}, ${curve.secondControl.x} ${curve.secondControl.y}, ${curve.end.x} ${curve.end.y}`;
}

export function guideCurve(points) {
  return points.length === 4
    ? [{
      start: points[0],
      firstControl: points[1],
      secondControl: points[2],
      end: points[3],
    }]
    : null;
}

export function splineRouteCurves(points, tension) {
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

export function sampleCurves(curves, sampleCount = curveSampleCount) {
  const points = [curves[0].start];
  for (const curve of curves) {
    for (let step = 1; step <= sampleCount; step += 1) {
      points.push(cubicPoint(curve, step / sampleCount));
    }
  }
  return points;
}

// Sampled curves are checked for obstacle and endpoint collisions; loops are
// prevented structurally by checking the orthogonal skeleton for
// self-intersection instead of the dense sample polyline, because the direct
// curve is monotone along its axis and clamped spline handles keep each curve
// segment inside its skeleton segment's corridor.
export function sampledCurvesClear(curves, obstacles, endpoints, sampleCount) {
  if (curves.length === 0) return false;
  const points = sampleCurves(curves, sampleCount);
  return !pathHitsObstacles(points, obstacles) && pathAvoidsEndpoints(points, endpoints);
}

export function curvesPath(curves) {
  return curves.reduce(
    (path, curve, index) => `${path}${index === 0 ? `M ${curve.start.x} ${curve.start.y}` : ""} C ${curve.firstControl.x} ${curve.firstControl.y}, ${curve.secondControl.x} ${curve.secondControl.y}, ${curve.end.x} ${curve.end.y}`,
    "",
  );
}

export function rectanglesIntersect(first, second) {
  return first.left < second.right
    && first.right > second.left
    && first.top < second.bottom
    && first.bottom > second.top;
}

export function expandRectangle(rectangle, amount) {
  return {
    left: rectangle.left - amount,
    right: rectangle.right + amount,
    top: rectangle.top - amount,
    bottom: rectangle.bottom + amount,
  };
}

export function branchLabelObstacles(obstacles, endpoints) {
  return [
    ...obstacles,
    ...endpoints.map((endpoint) => expandRectangle(endpoint.rectangle, 2)),
  ];
}

export function branchLabelPoint(points, obstacles, bounds = null) {
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

export function runRouteSteps(steps) {
  for (;;) {
    const next = steps.next();
    if (next.done) return next.value;
  }
}

export function commitRoute(group, label, route) {
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

export function liveAngularPoints(start, end, orientation) {
  if (orientation === "vertical") {
    const middle = (start.y + end.y) / 2;
    return normalizePath([start, { x: start.x, y: middle }, { x: end.x, y: middle }, end]);
  }
  const middle = (start.x + end.x) / 2;
  return normalizePath([start, { x: middle, y: start.y }, { x: middle, y: end.y }, end]);
}

export function pathMidpoint(points) {
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
