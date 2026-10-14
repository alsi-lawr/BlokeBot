import { CornerDragMotion, createDragPreview } from './DashboardDragMotion.js';

export function placementFromPointer(start, dx, dy, cell, resize) {
  return resize
    ? { ...start, width: Math.max(1, start.width + Math.round(dx / cell.x)), height: Math.max(1, start.height + Math.round(dy / cell.y)) }
    : { ...start, column: start.column + Math.round(dx / cell.x), row: start.row + Math.round(dy / cell.y) };
}

export function createGridDrag(root, grid, state, dotnet, generation, report) {
  const preview = createDragPreview(grid), reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  let frame = 0, operation = 0;
  const cards = () => new Map([...grid.querySelectorAll('[data-widget-id]')].map(e => [e.dataset.widgetId, e]));
  const signature = () => `${grid.dataset.arrangement}|${grid.dataset.columns}|${grid.dataset.cellHeight}`;
  // Save disables editing before flush; the owned unreleased preview still needs cancellation.
  const current = g => !state.disposed && root.isConnected
    && g.interaction === state.interaction && g.signature === signature();
  const owns = g => current(g) && root.dataset.editing === 'true';
  const same = (a, b) => a.column === b.column && a.row === b.row && a.width === b.width && a.height === b.height;
  const valid = g => g.target.column >= 0 && g.target.row >= 0 && g.target.width > 0
    && g.target.height > 0 && g.target.column + g.target.width <= g.columns;
  const geometry = () => {
    const computed = getComputedStyle(grid), columns = Number(grid.dataset.columns);
    const gap = { x: parseFloat(computed.columnGap) || 0, y: parseFloat(computed.rowGap) || 0 };
    return { columns, gap, cell: { x: (grid.clientWidth + gap.x) / columns, y: parseFloat(computed.gridAutoRows) + gap.y } };
  };
  const begin = () => dotnet.invokeMethodAsync('GestureStarted', generation, state.interaction).catch(report);
  const dirty = g => {
    if (g.dirtied) return;
    state.dirty = true;
    g.dirtied = g.started.then(token => {
      if (token && owns(g) && !g.abandoned) {
        return dotnet.invokeMethodAsync('LocalEditStarted', generation, g.interaction, token);
      }
    }).catch(report);
  };
  const notifyCancel = g => {
    void g.started.then(token => {
      if (token && current(g)) {
        return dotnet.invokeMethodAsync('GestureCancelled', generation, g.interaction, token, state.pending.size > 0);
      }
    }).catch(report);
  };
  const releaseCapture = g => {
    if (g.handle.hasPointerCapture?.(g.pointer)) g.handle.releasePointerCapture(g.pointer);
  };
  const stopFrame = () => {
    cancelAnimationFrame(frame);
    frame = 0;
  };
  const abandon = (notify = true) => {
    ++operation;
    const g = state.gesture;
    state.gesture = null;
    stopFrame();
    preview.clear();
    if (!g) return;
    g.abandoned = true;
    releaseCapture(g);
    if (notify) notifyCancel(g);
  };
  const paint = (g, now) => {
    if (state.gesture !== g || !owns(g)) {
      if (state.gesture === g) abandon();
      return;
    }
    const rect = grid.getBoundingClientRect();
    g.cell.x = (rect.width + g.gap.x) / g.columns;
    g.left = g.resize ? g.rect.left : g.x - g.grabX;
    g.top = g.resize ? g.rect.top : g.y - g.grabY;
    g.target = placementFromPointer(
      g.start,
      g.resize ? g.x - g.beginX : g.left - rect.left - g.start.column * g.cell.x,
      g.resize ? g.y - g.beginY : g.top - rect.top - g.start.row * g.cell.y,
      g.cell,
      g.resize,
    );
    g.motion.reduced = reduced.matches;
    preview.paint(g, now, g.cell, g.gap, valid(g));
  };
  const schedule = g => {
    if (frame) return;
    frame = requestAnimationFrame(now => {
      if (state.gesture !== g) return;
      frame = 0;
      if (!owns(g)) {
        abandon();
        return;
      }
      paint(g, now);
      if (state.gesture === g && !reduced.matches) schedule(g);
    });
  };
  const finish = (event, cancelled = false) => {
    const g = state.gesture;
    if (!g || event && event.pointerId !== g.pointer) return;
    if (cancelled) {
      abandon();
      return;
    }
    if (event) {
      g.x = event.clientX;
      g.y = event.clientY;
    }
    paint(g, performance.now());
    if (state.gesture !== g) return;
    state.gesture = null;
    stopFrame();
    releaseCapture(g);
    if (!valid(g) || same(g.start, g.target)) {
      g.abandoned = true;
      preview.clear();
      notifyCancel(g);
      return;
    }
    dirty(g);
    const pending = { placement: g.target, resize: g.resize, interaction: g.interaction, sequence: ++state.sequence, gesture: 0, owner: g };
    pending.ready = g.started.then(token => { pending.gesture = token; });
    state.pending.set(g.id, pending);
    void (async () => {
      const token = await g.started;
      await g.dirtied;
      if (!token || !owns(g) || g.abandoned) return;
      await dotnet.invokeMethodAsync('PlacementChanged', generation, g.interaction, g.id, pending.placement, pending.resize, pending.sequence, token);
    })().catch(report);
  };
  const down = event => {
    const handle = event.target.closest('[data-grid-move], [data-grid-resize]');
    if (!handle || event.button !== 0 || root.dataset.editing !== 'true' || state.gesture || state.disposed) return;
    const card = handle.closest('[data-widget-id]'), id = card?.dataset.widgetId, start = state.items.get(id);
    if (!start) return;
    const rect = card.getBoundingClientRect();
    ++operation;
    event.preventDefault();
    handle.focus({ preventScroll: true });
    handle.setPointerCapture(event.pointerId);
    const g = {
      id, card, start: { ...start }, interaction: state.interaction, signature: signature(), rect, handle,
      pointer: event.pointerId, resize: handle.hasAttribute('data-grid-resize'),
      x: event.clientX, y: event.clientY, beginX: event.clientX, beginY: event.clientY,
      grabX: event.clientX - rect.left, grabY: event.clientY - rect.top, started: begin(),
      ...geometry(), motion: new CornerDragMotion(event.clientX, event.clientY, reduced.matches),
    };
    state.gesture = g;
    preview.start(g);
    paint(g, performance.now());
    schedule(g);
  };
  const move = event => {
    const g = state.gesture;
    if (!g || g.pointer !== event.pointerId) return;
    g.x = event.clientX;
    g.y = event.clientY;
    if (g.x !== g.beginX || g.y !== g.beginY) dirty(g);
    schedule(g);
  };
  const cancel = event => finish(event, true);
  const key = event => {
    if (event.key === 'Escape' && state.gesture) {
      event.preventDefault();
      event.stopPropagation();
      abandon();
    } else if (event.target.closest('[data-grid-move], [data-grid-resize]') && event.key.startsWith('Arrow')) {
      event.preventDefault();
    }
  };
  const scroll = () => { if (state.gesture) schedule(state.gesture); };
  const motionChanged = () => { if (state.gesture) schedule(state.gesture); else preview.clear(); };
  root.addEventListener('pointerdown', down);
  root.addEventListener('pointermove', move);
  root.addEventListener('pointerup', finish);
  root.addEventListener('pointercancel', cancel);
  root.addEventListener('lostpointercapture', cancel);
  root.addEventListener('keydown', key);
  window.addEventListener('scroll', scroll, true);
  window.addEventListener('resize', scroll);
  reduced.addEventListener('change', motionChanged);
  return {
    cancel() { abandon(false); },
    // Interop may return before Blazor updates the DOM; only this sync has the destination geometry.
    acknowledge(sequence) {
      for (const [id, pending] of state.pending) {
        if (pending.sequence > sequence) continue;
        if (owns(pending.owner) && !pending.owner.abandoned) preview.land(pending.owner, cards().get(id));
        state.pending.delete(id);
      }
    },
    async flush() {
      abandon();
      await Promise.all([...state.pending.values()].map(p => p.ready));
      return [...state.pending].filter(([, p]) => p.gesture).map(([id, p]) => ({
        id, placement: p.placement, resize: p.resize, interaction: p.interaction, sequence: p.sequence, gesture: p.gesture,
      }));
    },
    dispose() {
      abandon(false);
      preview.dispose();
      root.removeEventListener('pointerdown', down);
      root.removeEventListener('pointermove', move);
      root.removeEventListener('pointerup', finish);
      root.removeEventListener('pointercancel', cancel);
      root.removeEventListener('lostpointercapture', cancel);
      root.removeEventListener('keydown', key);
      window.removeEventListener('scroll', scroll, true);
      window.removeEventListener('resize', scroll);
      reduced.removeEventListener('change', motionChanged);
    },
    async drop(event) {
      const kind = event.dataTransfer.getData('application/x-blokebot-dashboard-widget');
      if (!kind || root.dataset.editing !== 'true' || state.disposed) return;
      event.preventDefault();
      abandon(false);
      const owner = { interaction: state.interaction, signature: signature() }, current = operation;
      const owned = () => current === operation && owns(owner);
      const token = await begin(), interaction = owner.interaction;
      if (!token || !owned()) return;
      const rect = grid.getBoundingClientRect(), { cell } = geometry();
      state.dirty = true;
      await dotnet.invokeMethodAsync('LocalEditStarted', generation, interaction, token);
      if (!owned()) return;
      await dotnet.invokeMethodAsync('ToolboxDropped', generation, interaction, kind,
        Math.floor((event.clientX - rect.left) / cell.x), Math.floor((event.clientY - rect.top) / cell.y));
    },
  };
}
