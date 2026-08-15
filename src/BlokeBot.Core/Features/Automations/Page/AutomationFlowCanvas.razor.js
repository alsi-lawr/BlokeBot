const states = new WeakMap();

export function initialize(root, dotnet) {
  if (!(root instanceof HTMLElement) || states.has(root)) return;

  const state = { active: null };
  const finish = async (event) => {
    const active = state.active;
    if (active === null || active.pointerId !== event.pointerId) return;
    state.active = null;
    active.node.releasePointerCapture(event.pointerId);
    active.node.classList.remove("automation-node--moving");
    await dotnet.invokeMethodAsync(
      "MoveNodeFromCanvasAsync",
      active.node.dataset.automationNode ?? "",
      active.x,
      active.y,
    );
  };

  root.addEventListener("pointerdown", (event) => {
    const node = event.target instanceof Element
      ? event.target.closest("[data-automation-node]")
      : null;
    if (!(node instanceof HTMLButtonElement) || event.button !== 0) return;
    event.preventDefault();
    node.setPointerCapture(event.pointerId);
    node.classList.add("automation-node--moving");
    state.active = {
      node,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      originX: Number(node.dataset.nodeX),
      originY: Number(node.dataset.nodeY),
      x: Number(node.dataset.nodeX),
      y: Number(node.dataset.nodeY),
    };
  });

  root.addEventListener("pointermove", (event) => {
    const active = state.active;
    if (active === null || active.pointerId !== event.pointerId) return;
    active.x = Math.max(0, Math.round((active.originX + event.clientX - active.startX) / 24) * 24);
    active.y = Math.max(0, Math.round((active.originY + event.clientY - active.startY) / 24) * 24);
    active.node.style.left = `${active.x}px`;
    active.node.style.top = `${active.y}px`;
  });

  root.addEventListener("pointerup", (event) => void finish(event));
  root.addEventListener("pointercancel", (event) => void finish(event));
  states.set(root, state);
}
