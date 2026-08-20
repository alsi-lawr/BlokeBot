const savedViewports = new Map();

export const gridSize = 24;
export const zoomSteps = [
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
export const defaultZoomIndex = zoomSteps.findIndex((step) => step.scale === 1);

export function clamp(value, minimum, maximum) {
  return Math.min(maximum, Math.max(minimum, value));
}

export function snap(value) {
  return Math.max(0, Math.round(value / gridSize) * gridSize);
}

export function selectedNodes(root) {
  return [...root.querySelectorAll("[data-automation-node].automation-node--selected")];
}

export function selectedEdgeId(root) {
  return root.querySelector("[data-automation-edge].automation-edge-group--selected")
    ?.dataset.automationEdge ?? null;
}

export function selectionIds(root) {
  return selectedNodes(root).map((node) => node.dataset.automationNode);
}

export function setLocalSelection(root, nodeIds, edgeId = null) {
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

export function setLocalDisclosure(root, nodeId) {
  for (const node of root.querySelectorAll("[data-automation-node]")) {
    const isDisclosed = node.dataset.automationNode === nodeId;
    if (node.classList.contains("automation-node--disclosed") !== isDisclosed) {
      node.classList.toggle("automation-node--disclosed", isDisclosed);
    }
    const selector = node.querySelector("[data-automation-node-select]");
    const expanded = isDisclosed ? "true" : "false";
    if (selector?.getAttribute("aria-expanded") !== expanded) {
      selector?.setAttribute("aria-expanded", expanded);
    }
  }
}

export function renderedDisclosure(root) {
  const generation = Number.parseInt(root.dataset.disclosureGeneration ?? "0", 10);
  return {
    generation: Number.isSafeInteger(generation) ? generation : 0,
    nodeId: root.dataset.disclosedNodeId || null,
  };
}

export function reconcileDisclosure(state) {
  const rendered = renderedDisclosure(state.root);
  if (
    state.pendingDisclosure !== null
    && rendered.generation < state.pendingDisclosure.generation
  ) {
    setLocalDisclosure(state.root, state.pendingDisclosure.nodeId);
    return;
  }

  state.disclosureGeneration = Math.max(state.disclosureGeneration, rendered.generation);
  state.pendingDisclosure = null;
  setLocalDisclosure(state.root, rendered.nodeId);
}

export function requestDisclosure(state, nodeId) {
  const generation = state.disclosureGeneration + 1;
  state.disclosureGeneration = generation;
  state.pendingDisclosure = { generation, nodeId };
  setLocalDisclosure(state.root, nodeId);
  void state.dotnet.invokeMethodAsync(
    "SetNodeDisclosureFromCanvasAsync",
    nodeId,
    generation,
  );
}

export function notifyCompactSelection(state) {
  return state.dotnet.invokeMethodAsync(
    "SetSelectionFromCanvasAsync",
    selectionIds(state.root),
    selectedEdgeId(state.root),
  );
}

export function notifyPointerSelection(state) {
  return state.dotnet.invokeMethodAsync(
    "SetPointerSelectionFromCanvasAsync",
    selectionIds(state.root),
  );
}

export function zoomScale(state) {
  return zoomSteps[state.zoomIndex].scale;
}

export function applyTransform(state) {
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

export function restoreViewport(state) {
  state.viewportKey = state.shell.dataset.viewportKey ?? "";
  const saved = savedViewports.get(state.viewportKey);
  state.zoomIndex = saved?.zoomIndex ?? defaultZoomIndex;
  state.panX = saved?.panX ?? 0;
  state.panY = saved?.panY ?? 0;
  state.zoomTransition = null;
  applyTransform(state);
}

export function updateEditorHeight(state) {
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

export function viewportPoint(state, clientX, clientY) {
  const viewport = state.root.getBoundingClientRect();
  return {
    x: clientX - viewport.left - state.root.clientLeft,
    y: clientY - viewport.top - state.root.clientTop,
  };
}

export function screenToGraph(state, clientX, clientY) {
  const point = viewportPoint(state, clientX, clientY);
  const scale = zoomScale(state);
  return {
    x: (point.x - state.panX) / scale,
    y: (point.y - state.panY) / scale,
  };
}

export function nodeGraphPosition(node) {
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

export function setNodeGraphPosition(node, x, y) {
  node.dataset.automationGraphX = `${x}`;
  node.dataset.automationGraphY = `${y}`;
  node.style.removeProperty("transform");
  node.style.left = `${x}px`;
  node.style.top = `${y}px`;
}

export function setNodeLiveGraphPosition(item) {
  item.element.dataset.automationGraphX = `${item.x}`;
  item.element.dataset.automationGraphY = `${item.y}`;
  item.element.style.transform = `translate(${item.x - item.startX}px, ${item.y - item.startY}px)`;
}

export function nodeGraphRectangle(node, margin = 0) {
  const position = nodeGraphPosition(node);
  return {
    left: position.x - margin,
    top: position.y - margin,
    right: position.x + node.offsetWidth + margin,
    bottom: position.y + node.offsetHeight + margin,
  };
}

export function portPoint(state, nodeId, portId, direction) {
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
