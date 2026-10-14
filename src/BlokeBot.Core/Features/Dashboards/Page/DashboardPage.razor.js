import { createEditorMenu, isEditable } from "../../../Components/EditorContextMenu.js";
import { createGridDrag } from './DashboardGridDrag.js';
import { createSettingsDialog } from './DashboardSettingsDialog.js';

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
  const report = error => {
    if (!state.disposed) console.error('Dashboard interaction failed', error);
  };
  const drag = createGridDrag(root, grid, state, dotnet, generation, report);
  const dialog = root.querySelector('[data-dashboard-settings]');
  const settings = dialog ? createSettingsDialog(root, dialog, dotnet, report) : null;
  const selectCard = event => {
    if (root.dataset.editing !== 'true' || isEditable(event.target)) return;
    const card = event.target.closest('[data-widget-id]');
    if (!card) return;
    // Native secondary-button focus would race the menu's captured selection/context.
    if (event.button === 2) { event.preventDefault(); return; }
    if (event.button === 0 && !event.target.closest('button,a')) card.focus({ preventScroll: true });
  };
  grid.addEventListener('pointerdown', selectCard);
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
  const drop = event => { void drag.drop(event).catch(report); };
  root.addEventListener('dragstart', dragStart);
  grid.addEventListener('dragover', dragOver);
  grid.addEventListener('drop', drop);
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
    for (const [id, p] of state.items) style(id, p);
  });
  observer.observe(grid, { childList: true });
  tick();
  const reset = (interaction) => {
    if (interaction < state.interaction) return;
    drag.cancel();
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
      for (const [id, p] of state.items) style(id, p);
      drag.acknowledge(acknowledged);
      state.dirty = dirty || state.pending.size > 0 || !!state.gesture?.dirtied;
    },
    restoreGridInputs() {
      const columns = root.querySelector("#dashboard-columns");
      const height = root.querySelector("#dashboard-cell-height");
      if (columns) columns.value = grid.dataset.columns;
      if (height) height.value = Number.parseFloat(getComputedStyle(grid).gridAutoRows);
    },
    flush: () => drag.flush(),
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
    focusAdd() {
      if (!state.disposed) root.querySelector('[data-dashboard-add]')?.focus();
    },
    focusGrid() {
      if (!state.disposed) grid.focus({ preventScroll: true });
    },
    syncSettings: (owner, id) => settings?.sync(owner, id),
    focusSettingsFields: (owner, position) => settings?.focus(owner, position),
    dispose() {
      state.disposed = true;
      menu?.dispose();
      settings?.dispose();
      grid.removeEventListener('pointerdown', selectCard);
      root.removeEventListener('keydown', historyKey);
      clearTimeout(timer);
      drag.dispose();
      observer.disconnect();
      viewport.removeEventListener("change", arrangementViewport);
      root.removeEventListener("dragstart", dragStart);
      grid.removeEventListener("dragover", dragOver);
      grid.removeEventListener("drop", drop);
      window.removeEventListener("beforeunload", beforeUnload);
      document.removeEventListener("click", navigate, true);
      document.removeEventListener("visibilitychange", visibility);
    },
  };
}
