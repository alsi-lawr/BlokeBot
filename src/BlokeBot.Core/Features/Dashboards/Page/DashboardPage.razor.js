import { createEditorMenu, isEditable } from "../../../Components/EditorContextMenu.js";

export function placementFromPointer(start, dx, dy, cell, columns, resize) {
  return resize
    ? {
        ...start,
        width: Math.max(
          1,
          Math.min(
            columns - start.column,
            start.width + Math.round(dx / cell.x),
          ),
        ),
        height: Math.max(1, start.height + Math.round(dy / cell.y)),
      }
    : {
        ...start,
        column: Math.max(
          0,
          Math.min(
            columns - start.width,
            start.column + Math.round(dx / cell.x),
          ),
        ),
        row: Math.max(0, start.row + Math.round(dy / cell.y)),
      };
}
export function create(root, dotnet, generation, interaction) {
  const grid = root.querySelector("[data-dashboard-grid]");
  const menuOwner = {
    accepts: target => root.dataset.editing === 'true' && !!target.closest('[data-dashboard-grid]'),
    fallback: () => grid,
    async prepare(target, point, retainSelection = false) {
      const context = root.dataset.editorMenuContext;
      const hitGrid = !!target.closest('[data-dashboard-grid]') && (!retainSelection || !!target.closest('[data-widget-id]'));
      const id = hitGrid ? target.closest('[data-widget-id]')?.dataset.widgetId ?? null : null;
      try {
        return await dotnet.invokeMethodAsync('PrepareEditorMenuAsync', context, hitGrid, id);
      } catch (error) {
        report(error);
        return null;
      }
    },
    current: captured => !state.disposed && root.dataset.editing === 'true' && root.dataset.editorMenuContext === captured.context,
    async execute(action, captured) {
      try {
        await dotnet.invokeMethodAsync('ApplyEditorMenuAsync', action, captured.context);
      } catch (error) {
        report(error);
      }
    },
  };
  const menu = root.querySelector('[data-editor-menu-open]') ? createEditorMenu(root, menuOwner) : null;
  const deleteSelection = async target => {
    const prepared = await menuOwner.prepare(target, undefined, true);
    if (prepared?.delete && menuOwner.current(prepared)) await menuOwner.execute('delete', prepared);
  };
  const historyKey = event => {
    if (event.defaultPrevented || root.dataset.editing !== 'true'
        || !(event.target instanceof Element) || isEditable(event.target) || event.target.closest('[data-editor-menu],[data-editor-toolbox]')) return;
    if (event.key === 'Delete' && !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey
        && event.target.closest('[data-dashboard-grid]')) {
      event.preventDefault();
      void deleteSelection(event.target).catch(report);
      return;
    }
    if (!event.ctrlKey || event.altKey || event.metaKey || event.shiftKey) return;
    const action = event.key.toLowerCase() === 'z' ? 'undo' : event.key.toLowerCase() === 'y' ? 'redo' : null;
    if (!action) return;
    event.preventDefault();
    dotnet.invokeMethodAsync('ApplyEditorMenuAsync', action, root.dataset.editorMenuContext).catch(report);
  };
  root.addEventListener('keydown', historyKey);

  const state = {
    interaction,
    items: new Map(),
    pending: new Map(),
    sequence: 0,
    gesture: null,
    frame: 0,
    disposed: false,
    dirty: false,
    visible: !document.hidden,
  };
  let cards = new Map(
    [...grid.querySelectorAll("[data-widget-id]")].map((e) => [
      e.dataset.widgetId,
      e,
    ]),
  );
  const style = (id, p) => {
    const card = cards.get(id);
    if (card) {
      const column = `${p.column + 1} / span ${p.width}`,
        row = `${p.row + 1} / span ${p.height}`;
      if (card.style.gridColumn !== column) card.style.gridColumn = column;
      if (card.style.gridRow !== row) card.style.gridRow = row;
    }
  };
  const paint = () => {
    state.frame = 0;
    if (!state.disposed) {
      for (const [id, pending] of state.pending) style(id, pending.placement);
    }
  };
  const requestPaint = () => {
    if (!state.frame) state.frame = requestAnimationFrame(paint);
  };
  const markDirty = () => {
    if (!state.dirty) {
      state.dirty = true;
      dotnet
        .invokeMethodAsync("LocalEditStarted", generation, state.interaction)
        .catch(report);
    }
  };
  const report = (error) => {
    if (!state.disposed) console.error("Dashboard interaction failed", error);
  };
  const send = async (id, pending) => {
    try {
      await dotnet.invokeMethodAsync(
        "PlacementChanged",
        generation,
        pending.interaction,
        id,
        pending.placement,
        pending.resize,
        pending.sequence,
      );
    } catch (error) {
      report(error);
    }
  };
  const end = (event, cancelled = false) => {
    const gesture = state.gesture;
    if (!gesture || (event && gesture.pointer !== event.pointerId)) return;
    state.gesture = null;
    gesture.handle.releasePointerCapture?.(gesture.pointer);
    if (cancelled) {
      if (gesture.previous) state.pending.set(gesture.id, gesture.previous);
      else state.pending.delete(gesture.id);
      style(gesture.id, gesture.start);
      dotnet
        .invokeMethodAsync("GestureCancelled", generation, gesture.interaction)
        .catch(report);
    } else {
      const pending = state.pending.get(gesture.id);
      if (pending) void send(gesture.id, pending);
    }
  };
  const geometry = () => {
    const computed = getComputedStyle(grid);
    const columns = Number(grid.dataset.columns);
    const columnGap = Number.parseFloat(computed.columnGap) || 0;
    const rowGap = Number.parseFloat(computed.rowGap) || 0;
    return {
      columns,
      cell: {
        x: (grid.clientWidth + columnGap) / columns,
        y: Number.parseFloat(computed.gridAutoRows) + rowGap,
      },
    };
  };
  const down = (event) => {
    const handle = event.target.closest("[data-grid-move], [data-grid-resize]");
    if (
      !handle ||
      event.button !== 0 ||
      root.dataset.editing !== "true"
    )
      return;
    const card = handle.closest("[data-widget-id]");
    const id = card?.dataset.widgetId;
    const start = state.pending.get(id)?.placement ?? state.items.get(id);
    if (!start) return;
    event.preventDefault();
    dotnet.invokeMethodAsync('GestureStarted', generation, state.interaction).catch(report);
    handle.setPointerCapture(event.pointerId);
    const {columns, cell} = geometry();
    state.gesture = {
      id,
      interaction: state.interaction,
      previous: state.pending.get(id),
      start: { ...start },
      x: event.clientX,
      y: event.clientY,
      pointer: event.pointerId,
      handle,
      resize: handle.hasAttribute("data-grid-resize"),
      columns,
      cell,
    };
  };
  const move = (event) => {
    const g = state.gesture;
    if (
      !g ||
      event.pointerId !== g.pointer ||
      g.interaction !== state.interaction
    )
      return;
    const placement = placementFromPointer(
      g.start,
      event.clientX - g.x,
      event.clientY - g.y,
      g.cell,
      g.columns,
      g.resize,
    );
    if (
      JSON.stringify(placement) ===
      JSON.stringify(state.pending.get(g.id)?.placement ?? g.start)
    )
      return;
    markDirty();
    state.pending.set(g.id, {
      placement,
      resize: g.resize,
      interaction: g.interaction,
      sequence: ++state.sequence,
    });
    requestPaint();
  };
  const cancel = (event) => end(event, true);
  const beforeUnload = (event) => {
    if (state.dirty || state.gesture) {
      event.preventDefault();
      event.returnValue = "";
    }
  };
  const navigate = (event) => {
    if (
      !state.dirty ||
      root.dataset.serverDirty === "true" ||
      !event.target.closest("a[href]") ||
      event.defaultPrevented
    )
      return;
    if (!window.confirm("Discard unsaved dashboard changes?")) {
      event.preventDefault();
      event.stopImmediatePropagation();
    }
  };
  const visibility = () => {
    state.visible = !document.hidden;
    dotnet
      .invokeMethodAsync("VisibilityChanged", generation, state.visible)
      .catch(report);
    if (state.visible) tick();
  };
  let timer = 0;
  let clockOffset = Date.parse(root.dataset.serverNow) - Date.now();
  const tick = () => {
    clearTimeout(timer);
    if (state.disposed || !state.visible) return;
    for (const element of root.querySelectorAll("[data-countdown-deadline]")) {
      const remaining = Math.max(
        0,
        Math.ceil(
          (Date.parse(element.dataset.countdownDeadline) -
            (Date.now() + clockOffset)) /
            1000,
        ),
      );
      element.textContent = `${Math.floor(remaining / 60)
        .toString()
        .padStart(2, "0")}:${(remaining % 60).toString().padStart(2, "0")}`;
    }
    timer = setTimeout(tick, 250);
  };
  const key = (event) => {
    if (
      event.target.closest("[data-grid-move], [data-grid-resize]") &&
      ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)
    )
      event.preventDefault();
  };
  const viewport = window.matchMedia("(max-width: 700px)");
  const arrangementViewport = async () => {
    if (state.disposed) return;
    try {
      await dotnet.invokeMethodAsync("ViewportArrangementChanged", generation, viewport.matches);
    } catch (error) {
      report(error);
    }
  };
  viewport.addEventListener("change", arrangementViewport);
  void arrangementViewport();
  root.addEventListener("keydown", key);
  const dragStart = (event) => {
    const source = event.target.closest("[data-toolbox-kind]");
    if (source)
      event.dataTransfer.setData(
        "application/x-blokebot-dashboard-widget",
        source.dataset.toolboxKind,
      );
  };
  const dragOver = (event) => {
    if (
      root.dataset.editing === "true" &&
      event.dataTransfer.types.includes(
        "application/x-blokebot-dashboard-widget",
      )
    )
      event.preventDefault();
  };
  const drop = (event) => {
    const kind = event.dataTransfer.getData(
      "application/x-blokebot-dashboard-widget",
    );
    if (!kind || root.dataset.editing !== "true") return;
    event.preventDefault();
    dotnet.invokeMethodAsync('GestureStarted', generation, state.interaction).catch(report);
    const rect = grid.getBoundingClientRect();
    const {columns, cell} = geometry();
    markDirty();
    dotnet
      .invokeMethodAsync(
        "ToolboxDropped",
        generation,
        state.interaction,
        kind,
        Math.max(
          0,
          Math.min(
            columns - Math.min(6, columns),
            Math.floor(
              (event.clientX - rect.left) / cell.x,
            ),
          ),
        ),
        Math.max(0, Math.floor((event.clientY - rect.top) / cell.y)),
      )
      .catch(report);
  };
  root.addEventListener("dragstart", dragStart);
  grid.addEventListener("dragover", dragOver);
  grid.addEventListener("drop", drop);
  root.addEventListener("pointerdown", down);
  root.addEventListener("pointermove", move);
  root.addEventListener("pointerup", end);
  root.addEventListener("pointercancel", cancel);
  window.addEventListener("beforeunload", beforeUnload);
  document.addEventListener("click", navigate, true);
  document.addEventListener("visibilitychange", visibility);
  const observer = new MutationObserver(() => {
    cards = new Map(
      [...grid.querySelectorAll("[data-widget-id]")].map((e) => [
        e.dataset.widgetId,
        e,
      ]),
    );
    requestPaint();
  });
  observer.observe(grid, { childList: true });
  tick();
  const reset = (interaction) => {
    if (interaction < state.interaction) return;
    end(null, true);
    state.interaction = interaction;
    state.pending.clear();
    state.dirty = false;
    for (const [id, p] of state.items) style(id, p);
  };
  return {
    sync(items, acknowledged, dirty, interaction) {
      if (state.disposed || interaction < state.interaction) return;
      if (interaction !== state.interaction) reset(interaction);
      menu?.refresh();
      clockOffset = Date.parse(root.dataset.serverNow) - Date.now();
      cards = new Map(
        [...grid.querySelectorAll("[data-widget-id]")].map((e) => [
          e.dataset.widgetId,
          e,
        ]),
      );
      state.items = new Map(items.map((item) => [item.id, item.placement]));
      for (const [id, pending] of state.pending)
        if (pending.sequence <= acknowledged) state.pending.delete(id);
      state.dirty = dirty || state.pending.size > 0;
      for (const [id, p] of state.items)
        style(id, state.pending.get(id)?.placement ?? p);
    },
    restoreGridInputs() {
      const columns = root.querySelector("#dashboard-columns");
      const height = root.querySelector("#dashboard-cell-height");
      if (columns) columns.value = grid.dataset.columns;
      if (height) height.value = Number.parseFloat(getComputedStyle(grid).gridAutoRows);
    },
    flush() {
      end(null);
      if (state.frame) {
        cancelAnimationFrame(state.frame);
        paint();
      }
      return [...state.pending].map(([id, p]) => ({ id, ...p }));
    },
    cancel: reset,
    focusManagement() {
      root.querySelector("#dashboard-management button")?.focus();
    },
    focusManage() {
      root.querySelector("[data-dashboard-manage]")?.focus();
    },
    focusName() {
      root.querySelector(".dashboard-toolbar-selection input")?.focus();
    },
    focusSettings(id) {
      root.querySelector(`[data-settings-for="${id}"]`)?.focus();
    },
    focusPanel() {
      root.querySelector("[data-dashboard-settings] input")?.focus();
    },
    dispose() {
      state.disposed = true;
      menu?.dispose();
      root.removeEventListener('keydown', historyKey);
      clearTimeout(timer);
      cancelAnimationFrame(state.frame);
      observer.disconnect();
      viewport.removeEventListener("change", arrangementViewport);
      root.removeEventListener("keydown", key);
      root.removeEventListener("dragstart", dragStart);
      grid.removeEventListener("dragover", dragOver);
      grid.removeEventListener("drop", drop);
      root.removeEventListener("pointerdown", down);
      root.removeEventListener("pointermove", move);
      root.removeEventListener("pointerup", end);
      root.removeEventListener("pointercancel", cancel);
      window.removeEventListener("beforeunload", beforeUnload);
      document.removeEventListener("click", navigate, true);
      document.removeEventListener("visibilitychange", visibility);
    },
  };
}
