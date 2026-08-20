import { bendWeight } from "./AutomationFlowRouteFrame.js";

export function compareSearchEntries(first, second) {
  return first.priority - second.priority || first.state - second.state;
}

export function pushSearchEntry(heap, entry) {
  heap.push(entry);
  let index = heap.length - 1;
  while (index > 0) {
    const parent = (index - 1) >> 1;
    if (compareSearchEntries(heap[index], heap[parent]) >= 0) break;
    [heap[index], heap[parent]] = [heap[parent], heap[index]];
    index = parent;
  }
}

export function popSearchEntry(heap) {
  const top = heap[0];
  const last = heap.pop();
  if (heap.length === 0) return top;
  heap[0] = last;
  let index = 0;
  for (;;) {
    const left = index * 2 + 1;
    const right = left + 1;
    let smallest = index;
    if (left < heap.length && compareSearchEntries(heap[left], heap[smallest]) < 0) {
      smallest = left;
    }
    if (right < heap.length && compareSearchEntries(heap[right], heap[smallest]) < 0) {
      smallest = right;
    }
    if (smallest === index) return top;
    [heap[index], heap[smallest]] = [heap[smallest], heap[index]];
    index = smallest;
  }
}

// Reused expansion scratch: at most four axis-aligned moves leave a vertex, so
// the search relaxes them out of these buffers instead of allocating.
export const searchMoveVertices = new Int32Array(4);
export const searchMoveAxes = new Int32Array(4);
export const searchMoveDistances = new Float64Array(4);

// A* per edge over the shared graph with the lexicographic bend-then-distance
// cost encoded as bends * bendWeight + distance. The Manhattan-distance
// heuristic is admissible because every remaining path is at least that long
// and bends only add cost; equal-cost ties break on the numeric state index.
// Live drag routes additionally estimate the remaining bends, which prunes the
// bend-dominated frontier hard enough to fit an animation frame; the routing
// pass leaves the heuristic exactly as it was.
// Lower bound on the bends any remaining path must still pay, including the
// arrival penalty. Admissible: reaching a target that differs on both axes
// needs at least one axis change, and two when the current axis is already the
// arrival axis; a single-axis run needs one change unless it is already on that
// axis, plus one when it cannot arrive along the flow axis.
export function remainingBends(needX, needY, axis, flowAxis) {
  if (!needX && !needY) return 0;
  if (needX && needY) return axis === flowAxis ? 2 : 1;
  const travelAxis = needX ? 0 : 1;
  return (axis === travelAxis ? 0 : 1) + (travelAxis === flowAxis ? 0 : 1);
}

export function routeOverGraph(graph, startPoint, endPoint, flowAxis, estimateBends = false) {
  const startCol = graph.xIndex.get(startPoint.x);
  const startRow = graph.yIndex.get(startPoint.y);
  const endCol = graph.xIndex.get(endPoint.x);
  const endRow = graph.yIndex.get(endPoint.y);
  if (
    startCol === undefined
    || startRow === undefined
    || endCol === undefined
    || endRow === undefined
  ) {
    return null;
  }
  const { width, height, xCoords, yCoords, free, rightOpen, downOpen } = graph;
  const startVertex = startRow * width + startCol;
  const endVertex = endRow * width + endCol;
  if (free[startVertex] === 0 || free[endVertex] === 0) return null;
  if (startVertex === endVertex) return [startPoint];
  const stateCount = width * height * 2;
  // Search scratch lives on the graph, so a pass or drag frame allocates it once
  // instead of once per edge. Values are reset before every search.
  if (graph.searchCosts === undefined || graph.searchCosts.length < stateCount) {
    graph.searchCosts = new Float64Array(stateCount);
    graph.searchParents = new Int32Array(stateCount);
  }
  const bestCosts = graph.searchCosts.fill(Infinity, 0, stateCount);
  const parents = graph.searchParents.fill(-1, 0, stateCount);
  const heap = [];
  const moveVertices = searchMoveVertices;
  const moveAxes = searchMoveAxes;
  const moveDistances = searchMoveDistances;
  const heuristic = (vertex, axis) => {
    const col = vertex % width;
    const row = (vertex - col) / width;
    const deltaX = xCoords[col] - endPoint.x;
    const deltaY = yCoords[row] - endPoint.y;
    const distance = Math.abs(deltaX) + Math.abs(deltaY);
    return estimateBends
      ? distance + bendWeight * remainingBends(deltaX !== 0, deltaY !== 0, axis, flowAxis)
      : distance;
  };
  const startState = startVertex * 2 + flowAxis;
  bestCosts[startState] = 0;
  pushSearchEntry(heap, { priority: heuristic(startVertex, flowAxis), cost: 0, state: startState });
  while (heap.length > 0) {
    const current = popSearchEntry(heap);
    if (current.cost > bestCosts[current.state]) continue;
    const axis = current.state % 2;
    const vertex = (current.state - axis) / 2;
    if (vertex === endVertex) {
      const points = [];
      let state = current.state;
      for (;;) {
        const stateAxis = state % 2;
        const stateVertex = (state - stateAxis) / 2;
        const col = stateVertex % width;
        const row = (stateVertex - col) / width;
        points.unshift({ x: xCoords[col], y: yCoords[row] });
        if (state === startState) break;
        state = parents[state];
      }
      return points;
    }
    const col = vertex % width;
    const row = (vertex - col) / width;
    let moveCount = 0;
    if (col > 0 && rightOpen[vertex - 1] === 1) {
      moveVertices[moveCount] = vertex - 1;
      moveAxes[moveCount] = 0;
      moveDistances[moveCount] = xCoords[col] - xCoords[col - 1];
      moveCount += 1;
    }
    if (col < width - 1 && rightOpen[vertex] === 1) {
      moveVertices[moveCount] = vertex + 1;
      moveAxes[moveCount] = 0;
      moveDistances[moveCount] = xCoords[col + 1] - xCoords[col];
      moveCount += 1;
    }
    if (row > 0 && downOpen[vertex - width] === 1) {
      moveVertices[moveCount] = vertex - width;
      moveAxes[moveCount] = 1;
      moveDistances[moveCount] = yCoords[row] - yCoords[row - 1];
      moveCount += 1;
    }
    if (row < height - 1 && downOpen[vertex] === 1) {
      moveVertices[moveCount] = vertex + width;
      moveAxes[moveCount] = 1;
      moveDistances[moveCount] = yCoords[row + 1] - yCoords[row];
      moveCount += 1;
    }
    for (let index = 0; index < moveCount; index += 1) {
      const moveVertex = moveVertices[index];
      const moveAxis = moveAxes[index];
      let cost = current.cost + moveDistances[index] + (moveAxis === axis ? 0 : bendWeight);
      if (moveVertex === endVertex && moveAxis !== flowAxis) cost += bendWeight;
      const nextState = moveVertex * 2 + moveAxis;
      if (cost >= bestCosts[nextState]) continue;
      bestCosts[nextState] = cost;
      parents[nextState] = current.state;
      pushSearchEntry(heap, {
        priority: cost + heuristic(moveVertex, moveAxis),
        cost,
        state: nextState,
      });
    }
  }
  return null;
}
