export function pointInsideRectangle(point, rectangle) {
  return point.x >= rectangle.left
    && point.x <= rectangle.right
    && point.y >= rectangle.top
    && point.y <= rectangle.bottom;
}

export function segmentHitsRectangle(first, second, rectangle) {
  // Exact bounding-box rejection: a segment can only touch the rectangle if its
  // own extent overlaps the rectangle inclusively on both axes.
  if (
    (first.x < rectangle.left && second.x < rectangle.left)
    || (first.x > rectangle.right && second.x > rectangle.right)
    || (first.y < rectangle.top && second.y < rectangle.top)
    || (first.y > rectangle.bottom && second.y > rectangle.bottom)
  ) {
    return false;
  }
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

export function boundsOverlapRectangle(bounds, rectangle) {
  return bounds.left <= rectangle.right
    && bounds.right >= rectangle.left
    && bounds.top <= rectangle.bottom
    && bounds.bottom >= rectangle.top;
}

export function routeBounds(points) {
  const bounds = { left: Infinity, top: Infinity, right: -Infinity, bottom: -Infinity };
  for (const point of points) {
    bounds.left = Math.min(bounds.left, point.x);
    bounds.right = Math.max(bounds.right, point.x);
    bounds.top = Math.min(bounds.top, point.y);
    bounds.bottom = Math.max(bounds.bottom, point.y);
  }
  return bounds;
}

export function pathHitsObstacles(points, obstacles) {
  if (points.length < 2) return false;
  // Densely sampled curves are checked against many rectangles, so obstacles
  // outside the path's own extent are dropped before the segment walk. The
  // filter is an exact necessary condition, not an approximation.
  const bounds = routeBounds(points);
  const relevant = obstacles.filter((obstacle) => boundsOverlapRectangle(bounds, obstacle));
  for (let index = 1; index < points.length; index += 1) {
    for (const obstacle of relevant) {
      if (segmentHitsRectangle(points[index - 1], points[index], obstacle)) return true;
    }
  }
  return false;
}

export function pointOnSegment(point, first, second) {
  const cross = (point.y - first.y) * (second.x - first.x)
    - (point.x - first.x) * (second.y - first.y);
  return Math.abs(cross) < 0.0001
    && point.x >= Math.min(first.x, second.x) - 0.0001
    && point.x <= Math.max(first.x, second.x) + 0.0001
    && point.y >= Math.min(first.y, second.y) - 0.0001
    && point.y <= Math.max(first.y, second.y) + 0.0001;
}

export function segmentDirection(first, second, third) {
  return (second.y - first.y) * (third.x - second.x)
    - (second.x - first.x) * (third.y - second.y);
}

export function segmentsIntersect(firstStart, firstEnd, secondStart, secondEnd) {
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

export function pathSelfIntersects(points) {
  for (let first = 1; first < points.length; first += 1) {
    for (let second = first + 2; second < points.length; second += 1) {
      if (segmentsIntersect(points[first - 1], points[first], points[second - 1], points[second])) {
        return true;
      }
    }
  }
  return false;
}

export function samePoint(first, second) {
  return Math.abs(first.x - second.x) < 0.0001
    && Math.abs(first.y - second.y) < 0.0001;
}

export function normalizePath(points) {
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

export function pathAvoidsEndpoints(points, endpoints) {
  if (points.length === 0) return true;
  const bounds = routeBounds(points);
  for (const endpoint of endpoints) {
    if (!boundsOverlapRectangle(bounds, endpoint.rectangle)) continue;
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

export function angularPath(points) {
  return points.reduce(
    (path, point, index) => `${path}${index === 0 ? "M" : " L"} ${point.x} ${point.y}`,
    "",
  );
}
