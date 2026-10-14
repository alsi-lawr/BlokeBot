import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { CornerDragMotion } from "../../../src/BlokeBot.Core/wwwroot/Features/Dashboards/Page/DashboardDragMotion.js";
import { placementFromPointer } from "../../../src/BlokeBot.Core/wwwroot/Features/Dashboards/Page/DashboardGridDrag.js";

// Resolve Razor's web-root imports for the Node behavior fixture.
const source = await readFile(new URL("../../../src/BlokeBot.Core/Features/Dashboards/Page/DashboardPage.razor.js", import.meta.url), "utf8");
const menuAsset = new URL("../../../src/BlokeBot.Core/wwwroot/Components/EditorContextMenu.js", import.meta.url).href;
const dragAsset = new URL("../../../src/BlokeBot.Core/wwwroot/Features/Dashboards/Page/DashboardGridDrag.js", import.meta.url).href;
const settingsAsset = new URL("../../../src/BlokeBot.Core/wwwroot/Features/Dashboards/Page/DashboardSettingsDialog.js", import.meta.url).href;
const { create } = await import(`data:text/javascript;base64,${Buffer.from(source.replace("../../../Components/EditorContextMenu.js", menuAsset).replace("./DashboardGridDrag.js", dragAsset).replace("./DashboardSettingsDialog.js", settingsAsset)).toString("base64")}`);
const deferred = () => {
  let resolve;
  const promise = new Promise(r => { resolve = r; });
  return { promise, resolve };
};
const drain = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };

class Surface {
  listeners = new Map();
  style = {};
  dataset = {};
  attributes = new Map();
  children = [];
  isConnected = true;
  clientWidth = 1184;
  classes = new Set();
  classList = { add: x => this.classes.add(x), remove: x => this.classes.delete(x) };
  addEventListener(name, callback) {
    if (!this.listeners.has(name)) this.listeners.set(name, new Set());
    this.listeners.get(name).add(callback);
  }
  removeEventListener(name, callback) { this.listeners.get(name)?.delete(callback); }
  emit(name, event) { for (const callback of this.listeners.get(name) ?? []) callback(event); }
  querySelectorAll() { return []; }
  closest() { return null; }
  focus(options) { this.focused = options; }
  setAttribute(name, value) { this.attributes.set(name, value); }
  removeAttribute(name) { this.attributes.delete(name); if (name === "data-widget-id") delete this.dataset.widgetId; }
  hasAttribute(name) { return this.attributes.has(name); }
  append(child) { this.children.push(child); child.parent = this; }
  replaceChildren(...children) { this.children = children; }
  get firstElementChild() { return this.children[0]; }
  cloneNode() { const clone = new Surface(); clone.dataset = { ...this.dataset }; return clone; }
  showPopover() { this.open = true; }
  hidePopover() { this.open = false; }
  matches(selector) { return selector === ":popover-open" && this.open; }
  remove() { this.parent.children = this.parent.children.filter(c => c !== this); }
}
function fixture() {
  const root = new Surface(), grid = new Surface(), card = new Surface(), handle = new Surface();
  card.dataset.widgetId = "a";
  grid.dataset = { columns: "12", cellHeight: "56", arrangement: "Desktop" };
  root.dataset = { editing: "true", serverDirty: "false", serverNow: new Date().toISOString() };
  root.querySelector = selector => selector === "[data-dashboard-grid]" ? grid : null;
  grid.querySelectorAll = selector => selector === "[data-widget-id]" ? [card] : [];
  grid.getBoundingClientRect = () => ({ left: 10, top: 20, width: grid.clientWidth });
  card.getBoundingClientRect = () => ({
    left: 10 + (parseFloat(card.style.gridColumn || "1") - 1) * (grid.clientWidth + parseFloat(geometry.columnGap)) / Number(grid.dataset.columns),
    top: 20 + (parseFloat(card.style.gridRow || "1") - 1) * (parseFloat(geometry.gridAutoRows) + parseFloat(geometry.rowGap)),
    width: 384, height: 200,
  });
  handle.closest = selector => selector === "[data-widget-id]" ? card : selector.includes("data-grid-move") ? handle : null;
  handle.setPointerCapture = () => { handle.captured = true; };
  handle.hasPointerCapture = () => handle.captured;
  handle.releasePointerCapture = () => { handle.captured = false; };
  const window = new Surface(), document = new Surface(), viewport = new Surface(), reduced = new Surface();
  viewport.matches = false; reduced.matches = false; document.hidden = false;
  document.body = new Surface();
  const animations = [];
  document.createElement = () => {
    const element = new Surface();
    element.animate = (keyframes, options) => {
      const completion = deferred();
      const animation = { keyframes, options, finished: completion.promise, resolve: completion.resolve, cancel() { this.cancelled = true; } };
      animations.push(animation);
      return animation;
    };
    // Cloned cards use the same narrow animation protocol as the layer.
    element.cloneNode = () => document.createElement();
    return element;
  };
  card.cloneNode = () => document.createElement();
  window.matchMedia = query => query.includes("reduced-motion") ? reduced : viewport;
  const geometry = { columnGap: "16", rowGap: "16", gridAutoRows: "56", boxShadow: "none" };
  let nextFrame = 0, now = 0, token = 0;
  const frames = new Map(), callbacks = [];
  Object.assign(globalThis, {
    window, document, Element: Surface,
    performance: { now: () => now },
    getComputedStyle: () => geometry,
    MutationObserver: class { observe() {} disconnect() {} },
    requestAnimationFrame: callback => { callbacks.push(callback); frames.set(++nextFrame, callback); return nextFrame; },
    cancelAnimationFrame: id => frames.delete(id),
  });
  const calls = [], replies = new Map();
  const dotnet = { invokeMethodAsync(name, ...args) {
    calls.push({ name, args });
    return replies.get(name)?.(args) ?? Promise.resolve(name === "GestureStarted" ? ++token : undefined);
  } };
  const api = create(root, dotnet, 9, 0);
  const items = placement => [{ id: "a", placement: placement ?? { column: 0, row: 0, width: 4, height: 3, order: 0 } }];
  api.sync(items(), 0, false, 0);
  const event = (x, y, extra = {}) => ({ target: handle, button: 0, pointerId: 1, clientX: x, clientY: y, preventDefault() {}, stopPropagation() {}, ...extra });
  return {
    root, grid, card, handle, calls, replies, api, event, geometry, reduced, viewport, frames, callbacks, animations, items,
    get layer() { return document.body.children[0]; },
    get target() { return grid.children[0]; },
    paint(elapsed = 16) { now += elapsed; for (const [id, callback] of [...frames]) { frames.delete(id); callback(now); } },
    gesture(x = 10, y = 20) { root.emit("pointerdown", event(x, y)); },
    move(x, y) { root.emit("pointermove", event(x, y)); this.paint(); },
    release(x, y) { root.emit("pointerup", event(x, y)); },
  };
}

test("pointer anchor floats continuously while authoritative source stays put; only valid release commits", async () => {
  const f = fixture();
  try {
    f.gesture(40, 50);
    assert.deepEqual(f.handle.focused, { preventScroll: true });
    f.move(253, 127);
    await drain();
    assert.equal(f.layer.style.transform, "translate3d(223px,97px,0)");
    assert.equal(f.layer.firstElementChild.style.transformOrigin, "30px 30px");
    assert.equal(f.card.style.gridColumn, "1 / span 4");
    assert.equal(f.card.style.gridRow, "1 / span 3");
    assert.equal(f.target.style.left, "200px");
    assert.equal(f.target.style.top, "72px");
    assert.equal(f.target.dataset.valid, "true");
    assert.equal(f.layer.inert, true);
    assert.equal(f.layer.attributes.get("popover"), "manual");
    assert.equal(f.calls.filter(c => c.name === "PlacementChanged").length, 0);
    for (let i = 0; i < 8; i++) f.move(254 + i, 127);
    await drain();
    assert.equal(f.calls.filter(c => c.name === "LocalEditStarted").length, 1);
    f.release(260, 127);
    await drain();
    const commit = f.calls.find(c => c.name === "PlacementChanged");
    assert.deepEqual(commit.args.slice(0, 3), [9, 0, "a"]);
    assert.deepEqual(commit.args[3], { column: 2, row: 1, width: 4, height: 3, order: 0 });
    assert.equal(commit.args[6], 1);
    const pending = await f.api.flush();
    assert.equal(pending.length, 1);
    assert.equal(pending[0].gesture, 1);
    f.api.sync(f.items(commit.args[3]), 1, true, 0);
    assert.equal((await f.api.flush()).length, 0);
    assert.equal(f.card.style.gridColumn, "3 / span 4");
  } finally { f.api.dispose(); }
});

test("flush cancels an unreleased preview instead of turning it into a model edit", async () => {
  const f = fixture();
  try {
    f.gesture(); f.move(223, 97);
    assert.deepEqual(await f.api.flush(), []);
    f.release(223, 97); await drain();
    assert.equal(f.layer.open, false);
    assert.equal(f.calls.filter(c => c.name === "PlacementChanged").length, 0);
    assert.equal(f.calls.find(c => c.name === "GestureCancelled").args.at(-1), false);
  } finally { f.api.dispose(); }
});

test("busy Save boundary finalizes unreleased local dirty even after the next frame notices disabled editing", async () => {
  const f = fixture();
  try {
    f.gesture(); f.move(223, 97); await drain();
    f.root.dataset.editing = "false";
    f.paint();
    assert.deepEqual(await f.api.flush(), []);
    await drain();
    assert.equal(f.calls.filter(c => c.name === "PlacementChanged").length, 0);
    assert.equal(f.calls.filter(c => c.name === "GestureCancelled").length, 1);
    assert.equal(f.calls.find(c => c.name === "GestureCancelled").args.at(-1), false);
  } finally { f.api.dispose(); }
});

for (const boundary of ["pointercancel", "lostpointercapture", "Escape", "settings", "arrangement", "owner", "dispose"]) {
  test(`${boundary} abandons motion; queued old frames cannot touch a newer grab or commit`, async () => {
    const f = fixture();
    try {
      f.gesture(); f.move(223, 97);
      const oldFrame = f.callbacks.at(-1);
      if (boundary === "Escape") f.root.emit("keydown", f.event(223, 97, { key: "Escape" }));
      else if (boundary === "settings") { f.grid.dataset.cellHeight = "96"; f.paint(); }
      else if (boundary === "arrangement") { f.grid.dataset.arrangement = "Phone"; f.paint(); }
      else if (boundary === "owner") f.api.cancel(1);
      else if (boundary === "dispose") f.api.dispose();
      else f.root.emit(boundary, f.event(223, 97));
      f.release(223, 97); await drain();
      assert.equal(f.calls.filter(c => c.name === "PlacementChanged").length, 0);
      if (boundary !== "dispose") {
        f.gesture(40, 50); f.move(80, 90);
        const transform = f.layer.style.transform;
        const frameCount = f.frames.size;
        oldFrame(10000);
        assert.equal(f.layer.style.transform, transform);
        assert.equal(f.frames.size, frameCount);
        assert.equal(f.layer.open, true);
      }
    } finally { f.api.dispose(); }
  });
}

test("out-of-bounds candidates are shown invalid, never silently clamped or released to the server", async () => {
  const f = fixture();
  try {
    f.gesture(); f.move(-203, -97);
    assert.equal(f.target.dataset.valid, "false");
    f.release(-203, -97); await drain();
    assert.equal(f.calls.filter(c => c.name === "PlacementChanged").length, 0);
    assert.deepEqual(await f.api.flush(), []);
    const p = { column: 2, row: 3, width: 4, height: 3, order: 7 };
    assert.deepEqual(placementFromPointer(p, 1000, -1000, { x: 100, y: 72 }, false), { ...p, column: 12, row: -11 });
    assert.deepEqual(placementFromPointer(p, 1000, -1000, { x: 100, y: 72 }, true), { ...p, width: 14, height: 1 });
  } finally { f.api.dispose(); }
});

test("active phone cell pitch drives targets; cancelling a later preview retains an already released edit", async () => {
  const f = fixture();
  try {
    f.geometry.columnGap = "8"; f.geometry.rowGap = "12"; f.geometry.gridAutoRows = "96";
    f.grid.dataset = { columns: "4", cellHeight: "96", arrangement: "Phone" }; f.grid.clientWidth = 392;
    f.api.sync(f.items({ column: 0, row: 0, width: 2, height: 3, order: 0 }), 0, false, 0);
    f.gesture(); f.move(110, 128); f.release(110, 128); await drain();
    f.gesture(); f.move(110, 236);
    f.root.emit("pointercancel", f.event(110, 236)); await drain();
    const pending = await f.api.flush();
    assert.equal(pending.length, 1);
    assert.deepEqual(pending[0].placement, { column: 1, row: 1, width: 2, height: 3, order: 0 });
    assert.equal(f.calls.filter(c => c.name === "GestureCancelled").at(-1).args.at(-1), true);
    f.api.sync(f.items(pending[0].placement), pending[0].sequence, true, 0);
    assert.deepEqual(await f.api.flush(), []);
  } finally { f.api.dispose(); }
});

test("late landing promises cannot clear a new grab; reduced motion uses neither swing nor landing", async () => {
  const f = fixture();
  try {
    f.gesture(); f.move(223, 97); f.release(223, 97); await drain();
    assert.equal(f.animations.length, 0, "interop response is not proof of authoritative DOM geometry");
    f.api.sync(f.items({ column: 2, row: 1, width: 4, height: 3, order: 0 }), 1, true, 0);
    assert.equal(f.animations.length, 2);
    const destination = f.card.getBoundingClientRect();
    assert.equal(f.animations[0].keyframes.at(-1).transform, `translate3d(${destination.left}px,${destination.top}px,0)`);
    const old = [...f.animations];
    f.gesture(40, 50); f.move(80, 90);
    const transform = f.layer.style.transform;
    old.forEach(a => a.resolve()); await drain();
    assert.equal(f.layer.open, true);
    assert.equal(f.layer.style.transform, transform);
    assert.ok(old.every(a => a.cancelled));
    f.root.emit("pointercancel", f.event(80, 90));
    f.reduced.matches = true;
    f.gesture(); f.move(323, 97);
    assert.equal(f.layer.firstElementChild.style.transform, "none");
    f.release(323, 97); await drain();
    f.api.sync(f.items({ column: 3, row: 1, width: 4, height: 3, order: 0 }), 2, true, 0);
    assert.equal(f.animations.length, 2);
    assert.equal(f.layer.open, false);
  } finally { f.api.dispose(); }
});

test("delayed gesture tokens and toolbox delivery are fenced by the current local owner", async () => {
  const f = fixture();
  try {
    const start = deferred(); f.replies.set("GestureStarted", () => start.promise);
    f.gesture(); f.move(223, 97); f.root.emit("pointercancel", f.event(223, 97));
    start.resolve(10); await drain();
    assert.equal(f.calls.filter(c => c.name === "LocalEditStarted").length, 0);
    assert.equal(f.calls.filter(c => c.name === "PlacementChanged").length, 0);
    const dropStart = deferred(); f.replies.set("GestureStarted", () => dropStart.promise);
    f.grid.emit("drop", f.event(0, 0, { dataTransfer: { getData: () => "Countdown" } }));
    f.api.cancel(1); dropStart.resolve(11); await drain();
    assert.equal(f.calls.filter(c => c.name === "ToolboxDropped").length, 0);
  } finally { f.api.dispose(); }
});

test("approved axis response stays finite after stalls, settles without opposite overcorrection, and favors vertical swing", () => {
  globalThis.performance = { now: () => 0 };
  const simulate = (dx, dy) => {
    const motion = new CornerDragMotion(0, 0, false), card = { style: {} };
    for (let i = 1; i <= 12; i++) motion.paint(card, i * 16, dx * i, dy * i);
    const peak = motion.angle.position;
    const stopped = [];
    for (let i = 13; i <= 100; i++) { motion.paint(card, i * 16, dx * 12, dy * 12); stopped.push(motion.angle.position); }
    assert.ok(stopped.every(value => value >= -0.001));
    assert.ok(Math.abs(stopped.at(-1)) < 0.01);
    motion.paint(card, 100000, dx * 12, dy * 12);
    assert.ok([motion.angle.position, motion.pitch.position, motion.roll.position, motion.lift.position].every(Number.isFinite));
    return peak;
  };
  assert.ok(simulate(0, 8) > simulate(8, 0) * 1.5);
});

test("viewport teardown contains a synchronously disconnected interop callback", async () => {
  const f = fixture(), queued = [...f.viewport.listeners.get("change")][0];
  f.replies.set("ViewportArrangementChanged", () => { throw new Error("disconnected"); });
  const original = console.error, errors = [];
  console.error = (...args) => errors.push(args);
  try {
    await assert.doesNotReject(queued());
    assert.equal(errors.length, 1);
    f.api.dispose();
    await queued();
    assert.equal(errors.length, 1);
  } finally { console.error = original; f.api.dispose(); }
});

test('right-click leaves selection to captured menu prepare, while primary card selection remains focused', () => {
  const f = fixture();
  const target = new Surface();
  target.closest = selector => selector === '[data-widget-id]' ? f.card : null;
  try {
    let prevented = false;
    f.grid.emit('pointerdown', { target, button: 2, preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    assert.equal(f.card.focused, undefined);
    f.grid.emit('pointerdown', { target, button: 0 });
    assert.deepEqual(f.card.focused, { preventScroll: true });
  } finally { f.api.dispose(); }
});
