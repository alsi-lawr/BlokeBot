import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
// Razor collocated modules are served at the web root; resolve that one asset for Node.
const source = await readFile(new URL("../../../src/BlokeBot.Core/Features/Dashboards/Page/DashboardPage.razor.js", import.meta.url), "utf8");
const asset = new URL("../../../src/BlokeBot.Core/wwwroot/Components/EditorContextMenu.js", import.meta.url).href;
const { create, placementFromPointer } = await import(`data:text/javascript;base64,${Buffer.from(source.replace("../../../Components/EditorContextMenu.js", asset)).toString("base64")}`);

class Surface {
  listeners = new Map();
  style = {};
  dataset = {};
  clientWidth = 1184;
  addEventListener(name, callback) {
    this.listeners.set(name, callback);
  }
  removeEventListener(name, callback) {
    if (this.listeners.get(name) === callback) this.listeners.delete(name);
  }
  emit(name, event) {
    this.listeners.get(name)?.(event);
  }
  querySelectorAll() {
    return [];
  }
}
function fixture() {
  const root = new Surface(),
    grid = new Surface(),
    card = new Surface(),
    handle = new Surface();
  card.dataset.widgetId = "a";
  grid.dataset.columns = "12";
  root.dataset = {
    editing: "true",
    serverDirty: "false",
    serverNow: new Date().toISOString(),
  };
  root.querySelector = selector => selector === "[data-dashboard-grid]" ? grid : null;
  grid.querySelectorAll = () => [card];
  handle.closest = (selector) =>
    selector === "[data-widget-id]" ? card : handle;
  handle.hasAttribute = () => false;
  handle.setPointerCapture = () => {};
  handle.releasePointerCapture = () => {};
  const window = new Surface(),
    document = new Surface();
  document.hidden = false;
  document.addEventListener = document.addEventListener.bind(document);
  const viewport = new Surface();
  viewport.matches = false;
  window.matchMedia = () => viewport;
  const geometry = { columnGap: "16", rowGap: "16", gridAutoRows: "56" };
  let nextFrame = 0;
  const frames = new Map();
  Object.assign(globalThis, {
    window,
    document,
    getComputedStyle: () => geometry,
    MutationObserver: class {
      observe() {}
      disconnect() {}
    },
    requestAnimationFrame: (callback) => {
      frames.set(++nextFrame, callback);
      return nextFrame;
    },
    cancelAnimationFrame: (id) => frames.delete(id),
  });
  const calls = [];
  const dotnet = {
    invokeMethodAsync: (name, ...args) => {
      calls.push({ name, args });
      return new Promise(() => {});
    },
  };
  const api = create(root, dotnet, 9, 0);
  api.sync(
    [
      {
        id: "a",
        placement: { column: 0, row: 0, width: 4, height: 3, order: 0 },
      },
    ],
    0,
    false,
    0,
  );
  const event = (x, y) => ({
    target: handle,
    button: 0,
    pointerId: 1,
    clientX: x,
    clientY: y,
    preventDefault() {},
  });
  return {
    root,
    geometry,
    viewport,
    grid,
    card,
    handle,
    calls,
    dotnet,
    api,
    event,
    paint() {
      for (const [id, callback] of [...frames]) {
        frames.delete(id);
        callback();
      }
    },
  };
}
test("motion paints before release and interop; stale acknowledgements preserve newer local placement", () => {
  const f = fixture();
  f.root.emit("pointerdown", f.event(0, 0));
  f.root.emit("pointermove", f.event(200, 72));
  f.paint();
  assert.equal(f.card.style.gridColumn, "3 / span 4");
  assert.equal(f.card.style.gridRow, "2 / span 3");
  assert.equal(f.calls.filter((c) => c.name === "PlacementChanged").length, 0);
  f.root.emit("pointerup", f.event(200, 72));
  assert.equal(f.calls.filter((c) => c.name === "PlacementChanged").length, 1);
  f.root.emit("pointerdown", f.event(200, 72));
  f.root.emit("pointermove", f.event(300, 144));
  f.paint();
  f.api.sync(
    [
      {
        id: "a",
        placement: { column: 2, row: 1, width: 4, height: 3, order: 0 },
      },
    ],
    1,
    true,
    0,
  );
  assert.equal(f.card.style.gridColumn, "4 / span 4");
  assert.equal(f.card.style.gridRow, "3 / span 3");
  const pending = f.api.flush();
  assert.equal(pending.length, 1);
  assert.equal(pending[0].sequence, 2);
  f.api.dispose();
});
test("pointer cancellation restores pregeste and disposal prevents late movement/commit", () => {
  const f = fixture();
  f.root.emit("pointerdown", f.event(0, 0));
  f.root.emit("pointermove", f.event(200, 72));
  f.paint();
  f.root.emit("pointercancel", f.event(200, 72));
  assert.equal(f.card.style.gridColumn, "1 / span 4");
  assert.equal(f.api.flush().length, 0);
  f.api.dispose();
  f.root.emit("pointermove", f.event(500, 500));
  f.paint();
  assert.equal(f.calls.filter((c) => c.name === "PlacementChanged").length, 0);
});
test("move/resize snaps from current state and clamps owned grid boundary", () => {
  const p = { column: 2, row: 3, width: 4, height: 3, order: 7 };
  assert.deepEqual(
    placementFromPointer(p, 1000, -1000, { x: 100, y: 72 }, 12, false),
    { ...p, column: 8, row: 0 },
  );
  assert.deepEqual(
    placementFromPointer(p, 1000, -1000, { x: 100, y: 72 }, 12, true),
    { ...p, width: 10, height: 1 },
  );
});

test("cancelled-origin calls stay old while new gestures survive stale sync", () => {
  const f = fixture();
  f.root.emit("pointerdown", f.event(0, 0));
  f.root.emit("pointermove", f.event(200, 72));
  f.paint();
  f.root.emit("pointerup", f.event(200, 72));
  const old = f.calls.find((c) => c.name === "PlacementChanged");
  assert.equal(old.args[1], 0);
  f.api.cancel(1);
  f.root.emit("pointerdown", f.event(0, 0));
  f.root.emit("pointermove", f.event(300, 144));
  f.paint();
  f.api.sync(
    [
      {
        id: "a",
        placement: { column: 2, row: 1, width: 4, height: 3, order: 0 },
      },
    ],
    100,
    false,
    0,
  );
  assert.equal(f.card.style.gridColumn, "4 / span 4");
  assert.equal(f.card.style.gridRow, "3 / span 3");
  const pending = f.api.flush();
  assert.equal(pending[0].interaction, 1);
  assert.equal(old.args[1], 0);
  const latest = f.calls.filter((c) => c.name === "PlacementChanged").at(-1);
  assert.equal(latest.args[1], 1);
  f.api.dispose();
});

test("active grid pitch drives motion and a geometry boundary discards the previous origin", () => {
  const f = fixture();
  f.geometry.columnGap = "8";
  f.geometry.rowGap = "12";
  f.geometry.gridAutoRows = "96";
  f.grid.dataset.columns = "4";
  f.grid.clientWidth = 392;
  f.api.sync([{ id: "a", placement: { column: 0, row: 0, width: 2, height: 3, order: 0 } }], 0, false, 0);
  f.root.emit("pointerdown", f.event(0, 0));
  f.root.emit("pointermove", f.event(100, 108));
  f.paint();
  assert.equal(f.card.style.gridColumn, "2 / span 2");
  assert.equal(f.card.style.gridRow, "2 / span 3");
  f.api.cancel(1);
  f.root.emit("pointerup", f.event(100, 108));
  assert.equal(f.api.flush().length, 0);
  assert.equal(f.calls.filter(c => c.name === "PlacementChanged").length, 0);
  f.root.emit("pointerdown", f.event(0, 0));
  f.root.emit("pointermove", f.event(100, 216));
  const pending = f.api.flush();
  assert.equal(pending[0].placement.row, 2);
  assert.equal(pending[0].interaction, 1);
  f.api.dispose();
  f.viewport.emit("change", {});
  assert.equal(f.calls.filter(c => c.name === "ViewportArrangementChanged").length, 1);
});

test("viewport teardown contains a synchronously disconnected interop callback", async () => {
  const f = fixture();
  f.dotnet.invokeMethodAsync = () => { throw new Error("disconnected"); };
  const queued = f.viewport.listeners.get("change");
  const original = console.error, errors = [];
  console.error = (...args) => errors.push(args);
  try {
    await assert.doesNotReject(queued());
    assert.equal(errors.length, 1);
    f.api.dispose();
    await assert.doesNotReject(queued());
    assert.equal(errors.length, 1);
  } finally {
    console.error = original;
  }
});
