using System.Diagnostics;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

// Behaviour probes for the production AutomationFlowCanvas routing module. Each
// probe runs the real module inside Node.js against a scripted DOM double and
// asserts committed routes, labels, counters, and scheduling behaviour only.
public sealed class AutomationFlowRoutingTests
{
    [Test]
    public void Canvas_RepeatedAndOverlappingRenders_QueueOneRefreshPerRenderSignature()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(
            "./Features/Automations/Page/AutomationFlowCanvas.razor.js"
        );
        _ = module.SetupVoid("initialize", _ => true).SetVoidResult();
        var refresh = module.SetupVoid("refresh", _ => true);
        var definition = new CoreAutomationCatalogModule()
            .Definitions.Select(static value => value.Descriptor)
            .Single(static value => value.Id == AutomationDefinitionIds.SendChatAction);
        var node = AutomationEditorNode.Create(definition, new(new(48), new(72)));
        var canvas = context.Render<AutomationFlowCanvas>(parameters =>
            parameters
                .Add(component => component.Nodes, [node])
                .Add(component => component.Edges, [])
                .Add(component => component.ViewportKey, "flow-under-test")
        );

        refresh.Invocations.Count.ShouldBe(1);

        canvas.Render();

        refresh.Invocations.Count.ShouldBe(
            1,
            "An identical render must not queue duplicate refresh work while the first refresh is still pending."
        );

        node.Position = new(new(96), new(72));
        canvas.Render();

        refresh.Invocations.Count.ShouldBe(
            2,
            "A changed-geometry render must queue exactly one further refresh."
        );
    }

    [Test]
    public Task NoMovementClick_SchedulesOnlyBoundedDisclosureRoutingAndSkipsUnchangedGeometry() =>
        RunModuleProbeAsync(_scenario1);

    [Test]
    public Task ChangedSnappedDrop_RecomputesTheSceneOffThePointerEvent() =>
        RunModuleProbeAsync(_scenario2);

    [Test]
    public Task GroupDrag_PersistsEverySelectedNodeAndBoundsLiveRoutes() =>
        RunModuleProbeAsync(_scenario3);

    [Test]
    public Task ObstacleAndEndpointInvalidation_PreservesRouteSafety() =>
        RunModuleProbeAsync(_scenario4);

    [Test]
    public Task RefreshCoalescing_DelayedOverlappingRenderCannotQueueDuplicateOrStaleWork() =>
        RunModuleProbeAsync(_scenario5);

    [Test]
    public Task RouteAndLabelCommits_AreAtomicAndTheSceneSettlesOnce() =>
        RunModuleProbeAsync(_scenario6);

    [Test]
    public Task VerticalOrientation_RoutesMatchTheUncachedReferenceAndSwapPersistedCoordinates() =>
        RunModuleProbeAsync(_scenario7);

    [Test]
    public Task SmoothStyle_RoutesMatchTheUncachedReference() => RunModuleProbeAsync(_scenario8);

    [Test]
    public Task GlobalNudging_SeparatesOverlappingChannelSegments() =>
        RunModuleProbeAsync(_scenario9);

    [Test]
    public Task DisclosureActivation_SettlesRoutingOnceAcrossTheRenderRoundTrip() =>
        RunModuleProbeAsync(_scenario10);

    [Test]
    public Task DragFrames_RouteOnlyTheAffectedEdgesAndAvoidObstaclesLive() =>
        RunModuleProbeAsync(_scenario11);

    [Test]
    public Task DragFrames_CapLiveCollisionRoutesAndCompleteDeferredWorkOnDrop() =>
        RunModuleProbeAsync(_scenario12);

    private static async Task RunModuleProbeAsync(string scenario)
    {
        var source = string.Join(
            "\n",
            "import assert from \"node:assert/strict\";",
            _probePrelude,
            Source("AutomationFlowCanvas.razor.js"),
            _probeFixture,
            scenario
        );
        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--input-type=module");
        startInfo.ArgumentList.Add("--eval");
        startInfo.ArgumentList.Add(source);
        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Node.js.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        process.ExitCode.ShouldBe(0, $"{await output}\n{await error}");
    }

    private static string Source(string fileName) =>
        File.ReadAllText(
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "src",
                    "BlokeBot.Core",
                    "Features",
                    "Automations",
                    "Page",
                    fileName
                )
            )
        );

    private const string _probePrelude = """

class FakeStyle {
  constructor() {
    this.properties = new Map();
  }
  setProperty(name, value) {
    this.properties.set(name, String(value));
  }
  removeProperty(name) {
    this.properties.delete(name);
  }
  getPropertyValue(name) {
    return this.properties.get(name) ?? "";
  }
}
for (const name of ["left", "top", "width", "height", "display", "transform", "cursor"]) {
  Object.defineProperty(FakeStyle.prototype, name, {
    get() {
      return this.properties.get(name) ?? "";
    },
    set(value) {
      this.properties.set(name, String(value));
    },
  });
}

function datasetKey(attribute) {
  return attribute
    .slice(5)
    .replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
}

class FakeElement {
  constructor(tag, { classes = [], dataset = {}, parent = null } = {}) {
    this.tagName = tag.toUpperCase();
    this.children = [];
    this.parentElement = parent;
    this.classes = new Set(classes);
    this.dataset = { ...dataset };
    this.attributes = new Map();
    this.style = new FakeStyle();
    this.listeners = new Map();
    this.hidden = false;
    this.textContent = "";
    this.offsetWidth = 0;
    this.baseOffsetHeight = 0;
    this.disclosedOffsetHeight = null;
    this.clientLeft = 0;
    this.clientTop = 0;
    this.computed = null;
    this.captured = new Set();
    if (parent !== null) parent.children.push(this);
  }
  get offsetHeight() {
    return this.disclosedOffsetHeight !== null
      && this.classes.has("automation-node--disclosed")
      ? this.disclosedOffsetHeight
      : this.baseOffsetHeight;
  }
  get classList() {
    const classes = this.classes;
    return {
      add: (...names) => names.forEach((name) => classes.add(name)),
      remove: (...names) => names.forEach((name) => classes.delete(name)),
      contains: (name) => classes.has(name),
      toggle: (name, force) => {
        const target = force ?? !classes.has(name);
        if (target) classes.add(name);
        else classes.delete(name);
        return target;
      },
    };
  }
  append(...children) {
    for (const child of children) {
      child.parentElement = this;
      this.children.push(child);
    }
  }
  setAttribute(name, value) {
    if (name.startsWith("data-")) this.dataset[datasetKey(name)] = String(value);
    else this.attributes.set(name, String(value));
  }
  getAttribute(name) {
    if (name.startsWith("data-")) return this.dataset[datasetKey(name)] ?? null;
    return this.attributes.get(name) ?? null;
  }
  removeAttribute(name) {
    if (name.startsWith("data-")) delete this.dataset[datasetKey(name)];
    else this.attributes.delete(name);
  }
  addEventListener(name, handler) {
    if (!this.listeners.has(name)) this.listeners.set(name, []);
    this.listeners.get(name).push(handler);
  }
  removeEventListener(name, handler) {
    const handlers = this.listeners.get(name) ?? [];
    const index = handlers.indexOf(handler);
    if (index >= 0) handlers.splice(index, 1);
  }
  setPointerCapture(pointerId) {
    this.captured.add(pointerId);
  }
  hasPointerCapture(pointerId) {
    return this.captured.has(pointerId);
  }
  releasePointerCapture(pointerId) {
    this.captured.delete(pointerId);
  }
  focus() {}
  getBoundingClientRect() {
    return { left: 0, top: 0, right: 1600, bottom: 1200, width: 1600, height: 1200 };
  }
  matchesCompound(compound) {
    const pattern = /\[([a-zA-Z0-9-]+)(?:="([^"]*)")?\]|\.([a-zA-Z0-9_-]+)|^[a-zA-Z]+/g;
    let token;
    let matchedAny = false;
    while ((token = pattern.exec(compound)) !== null) {
      matchedAny = true;
      if (token[3] !== undefined) {
        if (!this.classes.has(token[3])) return false;
      } else if (token[1] !== undefined) {
        const name = token[1];
        const value = name.startsWith("data-")
          ? this.dataset[datasetKey(name)]
          : this.attributes.get(name);
        if (value === undefined) return false;
        if (token[2] !== undefined && value !== token[2]) return false;
      } else if (token[0].toUpperCase() !== this.tagName) {
        return false;
      }
    }
    return matchedAny;
  }
  matches(selector) {
    return selector
      .split(",")
      .map((part) => part.trim())
      .some((part) => part.length > 0 && this.matchesCompound(part));
  }
  closest(selector) {
    let current = this;
    while (current !== null) {
      if (current.matches(selector)) return current;
      current = current.parentElement;
    }
    return null;
  }
  *descendants() {
    for (const child of this.children) {
      yield child;
      yield* child.descendants();
    }
  }
  querySelector(selector) {
    for (const element of this.descendants()) {
      if (element.matches(selector)) return element;
    }
    return null;
  }
  querySelectorAll(selector) {
    return [...this.descendants()].filter((element) => element.matches(selector));
  }
}

class FakeButton extends FakeElement {}
class FakePath extends FakeElement {}

globalThis.Element = FakeElement;
globalThis.HTMLElement = FakeElement;
globalThis.HTMLButtonElement = FakeButton;
globalThis.SVGPathElement = FakePath;
globalThis.CSS = { escape: (value) => value };
globalThis.DOMMatrixReadOnly = class {
  constructor() {
    this.e = 0;
    this.f = 0;
  }
};
globalThis.getComputedStyle = (element) =>
  element.computed ?? { left: "0", top: "0", width: "0", height: "0", transform: "none", paddingBottom: "0" };
globalThis.window = {
  addEventListener() {},
  removeEventListener() {},
  matchMedia: () => ({ matches: true }),
  innerHeight: 1000,
};
globalThis.document = {
  querySelector: () => null,
  querySelectorAll: () => [],
  elementFromPoint: () => null,
};

let clock = 0;
globalThis.performance = { now: () => (clock += 1) };
const timerQueue = [];
const frameQueue = [];
let timerId = 1;
globalThis.setTimeout = (callback) => {
  const id = timerId++;
  timerQueue.push({ id, callback });
  return id;
};
globalThis.clearTimeout = (id) => {
  const index = timerQueue.findIndex((entry) => entry.id === id);
  if (index >= 0) timerQueue.splice(index, 1);
};
globalThis.setInterval = () => {
  throw new Error("setInterval is not expected in the production module.");
};
globalThis.clearInterval = () => {};
globalThis.requestAnimationFrame = (callback) => {
  const id = timerId++;
  frameQueue.push({ id, callback });
  return id;
};
globalThis.cancelAnimationFrame = (id) => {
  const index = frameQueue.findIndex((entry) => entry.id === id);
  if (index >= 0) frameQueue.splice(index, 1);
};

function flushOneScheduled() {
  if (frameQueue.length > 0) {
    frameQueue.shift().callback(clock);
    return true;
  }
  if (timerQueue.length > 0) {
    timerQueue.shift().callback();
    return true;
  }
  return false;
}

function flushAllScheduled(limit = 100000) {
  let steps = 0;
  while (flushOneScheduled()) {
    steps += 1;
    if (steps > limit) throw new Error("Scheduled work did not settle.");
  }
}

function buildPort(node, { nodeId, portId, direction, type = "Flow", left, top }) {
  const port = new FakeButton("button", {
    dataset: {
      automationPort: "",
      nodeId,
      portId,
      portDirection: direction,
      portType: type,
      portSensitivity: "Insensitive",
      portNullability: "NonNullable",
    },
    parent: node,
  });
  port.computed = { left: `${left}`, top: `${top}`, width: "12", height: "12", transform: "none" };
  return port;
}

function buildNode(root, { id, kind = "action", x, y, width = 120, height = 60, disclosedHeight = null }) {
  const node = new FakeElement("article", {
    classes: ["automation-node"],
    dataset: { automationNode: id, nodeKind: kind },
    parent: root,
  });
  node.style.setProperty("--automation-node-x", `${x}`);
  node.style.setProperty("--automation-node-y", `${y}`);
  node.offsetWidth = width;
  node.baseOffsetHeight = height;
  node.disclosedOffsetHeight = disclosedHeight;
  node.clientLeft = 1;
  node.clientTop = 1;
  const select = new FakeButton("button", {
    classes: ["automation-node-select"],
    dataset: { automationNodeSelect: "" },
    parent: node,
  });
  select.setAttribute("aria-pressed", "false");
  select.setAttribute("aria-expanded", "false");
  return node;
}

function buildEdge(svg, { id, sourceNode, sourcePort, targetNode, targetPort, labelled = false }) {
  const edge = new FakeElement("g", {
    classes: ["automation-edge-group"],
    dataset: { automationEdge: id, sourceNode, sourcePort, targetNode, targetPort },
    parent: svg,
  });
  const hit = new FakePath("path", { classes: ["automation-edge-hit"], parent: edge });
  hit.setAttribute("d", "");
  const visible = new FakePath("path", { classes: ["automation-edge"], parent: edge });
  visible.setAttribute("d", "");
  if (labelled) {
    const label = new FakeElement("g", {
      classes: ["automation-edge-label"],
      dataset: { edgeLabel: "" },
      parent: edge,
    });
    label.style.display = "none";
  }
  return edge;
}

function buildCanvas({ orientation = "horizontal", edgeStyle = "angular" } = {}) {
  const shell = new FakeElement("div", {
    dataset: {
      automationCanvasShell: "",
      viewportKey: "flow-under-test",
      orientation,
      edgeStyle,
    },
  });
  const root = new FakeElement("div", {
    classes: ["automation-canvas"],
    dataset: { automationCanvas: "" },
    parent: shell,
  });
  const svg = new FakeElement("svg", { classes: ["automation-edges"], parent: root });
  svg.viewBox = { baseVal: { x: 0, y: 0, width: 1600, height: 1200 } };
  const stage = new FakeElement("div", { classes: ["automation-canvas-stage"], parent: root });
  const preview = new FakePath("path", { dataset: { connectionPreview: "" }, parent: svg });
  preview.setAttribute("d", "");
  const marquee = new FakeElement("div", { dataset: { automationMarquee: "" }, parent: root });
  const zoomReset = new FakeButton("button", { dataset: { canvasZoomReset: "" }, parent: shell });
  const zoomLabel = new FakeElement("span", { dataset: { canvasZoomLabel: "" }, parent: zoomReset });
  zoomLabel.textContent = "100%";
  return { shell, root, svg, stage, preview, marquee };
}

function makeDotnet() {
  const calls = [];
  return {
    calls,
    invokeMethodAsync(method, ...values) {
      calls.push({ method, values });
      return Promise.resolve();
    },
  };
}

function dispatch(root, name, init = {}) {
  const event = {
    pointerId: 1,
    clientX: 0,
    clientY: 0,
    button: 0,
    buttons: 1,
    altKey: false,
    shiftKey: false,
    ctrlKey: false,
    metaKey: false,
    detail: 1,
    isTrusted: false,
    preventDefault() {},
    stopPropagation() {},
    target: root,
    ...init,
  };
  for (const handler of [...(root.listeners.get(name) ?? [])]) handler(event);
  return event;
}

function metricsSnapshot() {
  return { ...globalThis.__blokeBotAutomationMetrics };
}

function committedEdge(edge) {
  const label = edge.querySelector("[data-edge-label]");
  return {
    path: edge.querySelector(".automation-edge").getAttribute("d") ?? "",
    labelTransform: label?.getAttribute("transform") ?? null,
    labelHidden: label === null ? null : label.style.display === "none",
  };
}

function assertMatchesReference(root, message) {
  const reference = globalThis.__blokeBotAutomationRouteReference(root);
  for (const expected of reference) {
    const edge = root.querySelector(`[data-automation-edge="${expected.edgeId}"]`);
    const committed = committedEdge(edge);
    const expectedTransform = expected.label === null
      ? null
      : `translate(${expected.label.x} ${expected.label.y})`;
    assert.equal(committed.path, expected.path, `${message}: path ${expected.edgeId}`);
    if (committed.labelHidden !== null) {
      assert.equal(committed.labelTransform, expectedTransform, `${message}: label ${expected.edgeId}`);
      assert.equal(committed.labelHidden, expected.label === null, `${message}: label state ${expected.edgeId}`);
    }
  }
}

function pathPoints(path) {
  return path
    .split(/[ML]/)
    .map((part) => part.trim())
    .filter((part) => part.length > 0)
    .map((part) => {
      const [x, y] = part.split(/\s+/).map(Number);
      return { x, y };
    });
}

function segmentTouchesRectangle(first, second, rectangle) {
  const steps = 64;
  for (let index = 0; index <= steps; index += 1) {
    const x = first.x + ((second.x - first.x) * index) / steps;
    const y = first.y + ((second.y - first.y) * index) / steps;
    if (
      x > rectangle.left && x < rectangle.right && y > rectangle.top && y < rectangle.bottom
    ) {
      return true;
    }
  }
  return false;
}

function pathTouchesRectangle(path, rectangle) {
  const points = pathPoints(path);
  for (let index = 1; index < points.length; index += 1) {
    if (segmentTouchesRectangle(points[index - 1], points[index], rectangle)) return true;
  }
  return false;
}

function nodeRectangle(node) {
  const x = Number.parseFloat(
    node.dataset.automationGraphX ?? node.style.getPropertyValue("--automation-node-x"),
  );
  const y = Number.parseFloat(
    node.dataset.automationGraphY ?? node.style.getPropertyValue("--automation-node-y"),
  );
  return { left: x, top: y, right: x + node.offsetWidth, bottom: y + node.offsetHeight };
}
""";

    private const string _probeFixture = """
// Combined prototype of all routing probe scenarios.
const moduleExports = { initialize, refresh, dispose };

function buildStandardFixture(options = {}) {
  const canvas = buildCanvas(options);
  const { root } = canvas;
  const a = buildNode(root, { id: "node-a", kind: "source", x: 0, y: 96, disclosedHeight: 140 });
  buildPort(a, { nodeId: "node-a", portId: "flow", direction: "output", left: 113, top: 23 });
  buildPort(a, { nodeId: "node-a", portId: "yes", direction: "output", left: 113, top: 41 });
  const b = buildNode(root, { id: "node-b", kind: "action", x: 480, y: 96 });
  buildPort(b, { nodeId: "node-b", portId: "in", direction: "input", left: -7, top: 23 });
  const c = buildNode(root, { id: "node-c", kind: "transform", x: 240, y: 72 });
  const d = buildNode(root, { id: "node-d", kind: "control", x: 480, y: 288 });
  buildPort(d, { nodeId: "node-d", portId: "in", direction: "input", left: -7, top: 23 });
  const f = buildNode(root, { id: "node-f", kind: "source", x: 0, y: 720 });
  buildPort(f, { nodeId: "node-f", portId: "flow", direction: "output", left: 113, top: 23 });
  const g = buildNode(root, { id: "node-g", kind: "action", x: 480, y: 720 });
  buildPort(g, { nodeId: "node-g", portId: "in", direction: "input", left: -7, top: 23 });
  const svg = canvas.svg;
  const e1 = buildEdge(svg, { id: "edge-1", sourceNode: "node-a", sourcePort: "flow", targetNode: "node-b", targetPort: "in" });
  const e2 = buildEdge(svg, { id: "edge-2", sourceNode: "node-a", sourcePort: "yes", targetNode: "node-d", targetPort: "in", labelled: true });
  const e3 = buildEdge(svg, { id: "edge-3", sourceNode: "node-f", sourcePort: "flow", targetNode: "node-g", targetPort: "in" });
  const e4 = buildEdge(svg, { id: "edge-4", sourceNode: "node-a", sourcePort: "ghost", targetNode: "node-b", targetPort: "in" });
  return { ...canvas, a, b, c, d, f, g, e1, e2, e3, e4 };
}

// The probe clock advances on every reading, which would let the drag frame
// budget end a frame after its first edge. Scenarios that assert how many edges
// a frame routes freeze it so the per-frame cap is the only bound.
function withFrozenClock(action) {
  const ticking = globalThis.performance;
  globalThis.performance = { now: () => clock };
  try {
    return action();
  } finally {
    globalThis.performance = ticking;
  }
}

function dragNode(root, node, deltaX, deltaY, pointerId = 7) {
  const button = node.querySelector("[data-automation-node-select]");
  dispatch(root, "pointerdown", { pointerId, target: button, clientX: 10, clientY: 10 });
  dispatch(root, "pointermove", { pointerId, clientX: 10 + deltaX / 2, clientY: 10 + deltaY / 2 });
  dispatch(root, "pointermove", { pointerId, clientX: 10 + deltaX, clientY: 10 + deltaY });
  flushAllScheduled();
  dispatch(root, "pointerup", { pointerId, clientX: 10 + deltaX, clientY: 10 + deltaY });
}
""";

    private const string _scenario1 = """
// Scenario 1: no-movement click ---
{
  const fixture = buildStandardFixture();
  const dotnet = makeDotnet();
  moduleExports.initialize(fixture.root, dotnet);
  flushAllScheduled();
  const initial = metricsSnapshot();
  assert.equal(initial.routeEdgeCount, 4, "initial entries");
  assert.equal(initial.routeCacheMissCount, 4, "initial misses");
  assert.equal(initial.routeComputationCount, 3, "initial computations exclude the unroutable edge");
  assert.equal(fixture.root.dataset.automationCanvasReady, "true", "ready flag");

  const before = metricsSnapshot();
  const button = fixture.a.querySelector("[data-automation-node-select]");
  dispatch(fixture.root, "pointerdown", { pointerId: 3, target: button, clientX: 20, clientY: 20 });
  dispatch(fixture.root, "pointerup", { pointerId: 3, clientX: 20, clientY: 20 });
  const atRelease = metricsSnapshot();
  assert.equal(atRelease.routeComputationCount, before.routeComputationCount, "no synchronous computation on release");
  assert.equal(atRelease.routeEdgeCount, before.routeEdgeCount, "no synchronous route entries on release");
  assert.equal(fixture.a.classes.has("automation-node--disclosed"), true, "click discloses");
  assert.deepEqual(dotnet.calls.at(-1).method, "ActivateNodeFromCanvasAsync");

  flushAllScheduled();
  const disclosed = metricsSnapshot();
  assert.equal(disclosed.routeEdgeCount - before.routeEdgeCount, 4, "bounded pass enumerates edges once");
  assert.equal(disclosed.routeComputationCount - before.routeComputationCount, 3, "one bounded scene recompute covers the routable edges");
  assert.equal(disclosed.routeRecalculationCount - before.routeRecalculationCount <= 2, true, "disclosure schedules a bounded number of passes");
  assertMatchesReference(fixture.root, "disclosed geometry");

  const again = metricsSnapshot();
  dispatch(fixture.root, "pointerdown", { pointerId: 4, target: button, clientX: 20, clientY: 20 });
  dispatch(fixture.root, "pointerup", { pointerId: 4, clientX: 20, clientY: 20 });
  assert.equal(fixture.a.classes.has("automation-node--disclosed"), false, "second click closes the local disclosure without a flash");
  assert.deepEqual(dotnet.calls.at(-1).method, "ActivateNodeFromCanvasAsync");
  flushAllScheduled();
  const closed = metricsSnapshot();
  assert.equal(closed.routeComputationCount - again.routeComputationCount, 3, "toggle close recomputes the shrunken scene once");
  assertMatchesReference(fixture.root, "toggle-closed geometry");

  const fButton = fixture.f.querySelector("[data-automation-node-select]");
  const beforeUnchanged = metricsSnapshot();
  dispatch(fixture.root, "pointerdown", { pointerId: 5, target: fButton, clientX: 20, clientY: 20 });
  dispatch(fixture.root, "pointerup", { pointerId: 5, clientX: 20, clientY: 20 });
  assert.equal(fixture.f.classes.has("automation-node--disclosed"), true, "click on another node moves disclosure to it");
  assert.deepEqual(dotnet.calls.at(-1).method, "ActivateNodeFromCanvasAsync");
  flushAllScheduled();
  const unchanged = metricsSnapshot();
  assert.equal(unchanged.routeComputationCount, beforeUnchanged.routeComputationCount, "unchanged geometry computes nothing");
  assert.equal(unchanged.routeEdgeCount, beforeUnchanged.routeEdgeCount, "unchanged geometry enumerates nothing");
  assert.equal(unchanged.routeRecalculationCount, beforeUnchanged.routeRecalculationCount, "unchanged geometry runs no pass");
  moduleExports.dispose(fixture.root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 1 ok");
}
""";

    private const string _scenario2 = """
// Scenario 2: changed snapped drop ---
{
  const fixture = buildStandardFixture();
  const dotnet = makeDotnet();
  moduleExports.initialize(fixture.root, dotnet);
  flushAllScheduled();
  assert.equal(pathPoints(committedEdge(fixture.e1).path).length > 2, true, "obstacle forces a detour");
  assert.equal(pathTouchesRectangle(committedEdge(fixture.e1).path, nodeRectangle(fixture.c)), false, "detour avoids the obstacle");

  const before = metricsSnapshot();
  dragNode(fixture.root, fixture.c, 0, 192, 11);
  const atRelease = metricsSnapshot();
  assert.equal(atRelease.routeComputationCount, before.routeComputationCount, "release does not recompute synchronously");
  const move = dotnet.calls.find((call) => call.method === "MoveNodesFromCanvasAsync");
  assert.deepEqual(move.values[0], [{ nodeId: "node-c", x: 240, y: 264 }], "snapped drop persists through the .NET move path");
  assert.equal(fixture.c.dataset.automationGraphX, "240", "released snapped x");
  assert.equal(fixture.c.dataset.automationGraphY, "264", "released snapped y");

  flushAllScheduled();
  const settled = metricsSnapshot();
  assert.equal(settled.routeComputationCount - before.routeComputationCount, 3, "one scene recompute covers the routable edges");
  assert.equal(settled.routeEdgeCount - before.routeEdgeCount, settled.routeCacheHitCount - before.routeCacheHitCount + (settled.routeCacheMissCount - before.routeCacheMissCount), "entries split into hits and misses");
  assert.equal(pathPoints(committedEdge(fixture.e1).path).length, 2, "moved obstacle restores the direct route");
  assertMatchesReference(fixture.root, "changed snapped drop");

  // Simulated unchanged-geometry updates reuse the cache.
  const beforeUpdates = metricsSnapshot();
  globalThis.__simulateBlokeBotAutomationUpdate();
  flushAllScheduled();
  const afterUpdates = metricsSnapshot();
  assert.equal(afterUpdates.routeComputationCount, beforeUpdates.routeComputationCount, "simulated update computes nothing for unchanged geometry");
  moduleExports.dispose(fixture.root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 2 ok");
}
""";

    private const string _scenario3 = """
// Scenario 3: group drag ---
{
  const fixture = buildStandardFixture();
  const dotnet = makeDotnet();
  moduleExports.initialize(fixture.root, dotnet);
  flushAllScheduled();
  fixture.a.classes.add("automation-node--selected");
  fixture.c.classes.add("automation-node--selected");
  const before = metricsSnapshot();
  withFrozenClock(() => dragNode(fixture.root, fixture.a, 48, 24, 13));
  const move = dotnet.calls.find((call) => call.method === "MoveNodesFromCanvasAsync");
  assert.deepEqual(move.values[0], [
    { nodeId: "node-a", x: 48, y: 120 },
    { nodeId: "node-c", x: 288, y: 96 },
  ], "group drag persists every selected node");
  flushAllScheduled();
  const after = metricsSnapshot();
  assert.equal(after.routeEdgeLiveMaximumPerFrame, 3, "live collision routing stays bounded to the affected edges");
  assert.equal(after.dragFrames >= 1, true, "live frames are animation-frame bounded");
  assert.equal(after.dragGraphBuildCount, 1, "one shared graph is built for the drag");
  assert.equal(after.dragGraphPatchCount >= after.dragFrames, true, "each drag frame patches that graph");
  assertMatchesReference(fixture.root, "group drag");
  moduleExports.dispose(fixture.root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 3 ok");
}
""";

    private const string _scenario4 = """
// Scenario 4: invalidation and route safety ---
{
  const fixture = buildStandardFixture();
  const dotnet = makeDotnet();
  moduleExports.initialize(fixture.root, dotnet);
  flushAllScheduled();
  assert.equal(committedEdge(fixture.e4).path, "", "invalid retained edge stays uncommitted");
  const straight = committedEdge(fixture.e3).path;
  assert.equal(pathPoints(straight).length, 2, "far edge routes directly");

  // Obstacle moves into the previously unaffected path.
  let before = metricsSnapshot();
  dragNode(fixture.root, fixture.c, 0, 624, 17);
  flushAllScheduled();
  let after = metricsSnapshot();
  assert.equal(pathPoints(committedEdge(fixture.e3).path).length > 2, true, "edge detours around the arriving obstacle");
  assert.equal(pathTouchesRectangle(committedEdge(fixture.e3).path, nodeRectangle(fixture.c)), false, "detour avoids the arriving obstacle");
  assertMatchesReference(fixture.root, "obstacle into path");

  // Obstacle moves back out of the path.
  dragNode(fixture.root, fixture.c, 0, -624, 19);
  flushAllScheduled();
  assert.equal(pathPoints(committedEdge(fixture.e3).path).length, 2, "edge returns to the direct route");
  assertMatchesReference(fixture.root, "obstacle out of path");

  // Endpoint movement recomputes incident edges and keeps far cache entries.
  before = metricsSnapshot();
  dragNode(fixture.root, fixture.b, 48, 24, 23);
  flushAllScheduled();
  after = metricsSnapshot();
  assert.equal(after.routeEdgeCount - before.routeEdgeCount, (after.routeCacheHitCount - before.routeCacheHitCount) + (after.routeCacheMissCount - before.routeCacheMissCount), "entries split into hits and misses");
  assert.equal(committedEdge(fixture.e4).path, "", "invalid retained edge stays uncommitted after endpoint movement");
  assertMatchesReference(fixture.root, "endpoint movement");

  // Labelled branch keeps an atomic label with its route.
  const labelled = committedEdge(fixture.e2);
  assert.equal(labelled.labelHidden, false, "labelled branch shows its label");
  assert.notEqual(labelled.labelTransform, null, "labelled branch has a placed label");
  moduleExports.dispose(fixture.root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 4 ok");
}
""";

    private const string _scenario5 = """
// Scenario 5: refresh coalescing and stale-commit guard ---
{
  const fixture = buildStandardFixture();
  const dotnet = makeDotnet();
  moduleExports.initialize(fixture.root, dotnet);
  flushAllScheduled();

  // One coalesced pass for consecutive refreshes at the final signature.
  let before = metricsSnapshot();
  moduleExports.refresh(fixture.root);
  fixture.c.style.setProperty("--automation-node-x", "216");
  fixture.c.style.setProperty("--automation-node-y", "48");
  moduleExports.refresh(fixture.root);
  flushAllScheduled();
  let after = metricsSnapshot();
  assert.equal(after.routeRecalculationCount - before.routeRecalculationCount, 1, "overlapping refreshes coalesce into one pass");
  assertMatchesReference(fixture.root, "coalesced refresh");

  // A delayed overlapping render cannot commit stale routes.
  before = metricsSnapshot();
  moduleExports.refresh(fixture.root);
  flushOneScheduled();
  flushOneScheduled();
  fixture.c.style.setProperty("--automation-node-x", "240");
  fixture.c.style.setProperty("--automation-node-y", "72");
  moduleExports.refresh(fixture.root);
  flushAllScheduled();
  assertMatchesReference(fixture.root, "delayed overlapping render");

  // An unchanged-geometry refresh reuses every cache entry.
  before = metricsSnapshot();
  moduleExports.refresh(fixture.root);
  flushAllScheduled();
  after = metricsSnapshot();
  assert.equal(after.routeComputationCount, before.routeComputationCount, "unchanged refresh performs no computation");
  assert.equal(after.routeCacheHitCount - before.routeCacheHitCount, 4, "unchanged refresh reuses valid cache entries");
  moduleExports.dispose(fixture.root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 5 ok");
}
""";

    private const string _scenario6 = """
// Scenario 6: atomic route and label commits ---
{
  const fixture = buildStandardFixture();
  const dotnet = makeDotnet();
  moduleExports.initialize(fixture.root, dotnet);
  flushAllScheduled();
  const oldPair = committedEdge(fixture.e2);
  const button = fixture.c.querySelector("[data-automation-node-select]");
  dispatch(fixture.root, "pointerdown", { pointerId: 29, target: button, clientX: 30, clientY: 30 });
  dispatch(fixture.root, "pointermove", { pointerId: 29, clientX: 30 - 72, clientY: 30 + 144 });
  dispatch(fixture.root, "pointerup", { pointerId: 29, clientX: 30 - 72, clientY: 30 + 144 });
  const observed = [];
  const allEdges = () => [fixture.e1, fixture.e2, fixture.e3, fixture.e4]
    .map((edge) => committedEdge(edge).path)
    .join("#");
  let scene = allEdges();
  let sceneChanges = 0;
  while (flushOneScheduled()) {
    observed.push(committedEdge(fixture.e2));
    const current = allEdges();
    if (current !== scene) {
      sceneChanges += 1;
      scene = current;
    }
  }
  assert.equal(sceneChanges, 1, "the whole scene commits exactly once per geometry change");
  const finalPair = committedEdge(fixture.e2);
  assert.notDeepEqual(finalPair, oldPair, "the disclosure change reroutes the labelled edge");
  for (const pair of observed) {
    const matchesOld = JSON.stringify(pair) === JSON.stringify(oldPair);
    const matchesNew = JSON.stringify(pair) === JSON.stringify(finalPair);
    assert.equal(matchesOld || matchesNew, true, "route and label commit together");
  }
  assertMatchesReference(fixture.root, "atomic commits");
  moduleExports.dispose(fixture.root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 6 ok");
}
""";

    private const string _scenario7 = """
// Scenario 7: vertical orientation ---
{
  const canvas = buildCanvas({ orientation: "vertical" });
  const root = canvas.root;
  const top = buildNode(root, { id: "node-top", x: 96, y: 0 });
  buildPort(top, { nodeId: "node-top", portId: "flow", direction: "output", left: 53, top: 53 });
  const bottom = buildNode(root, { id: "node-bottom", x: 96, y: 480 });
  buildPort(bottom, { nodeId: "node-bottom", portId: "in", direction: "input", left: 53, top: -7 });
  const wall = buildNode(root, { id: "node-wall", x: 72, y: 240 });
  buildEdge(canvas.svg, { id: "edge-v", sourceNode: "node-top", sourcePort: "flow", targetNode: "node-bottom", targetPort: "in" });
  const dotnet = makeDotnet();
  moduleExports.initialize(root, dotnet);
  flushAllScheduled();
  const committed = committedEdge(root.querySelector('[data-automation-edge="edge-v"]'));
  assert.equal(committed.path.length > 0, true, "vertical route commits");
  assert.equal(pathTouchesRectangle(committed.path, nodeRectangle(wall)), false, "vertical route avoids the obstacle");
  assertMatchesReference(root, "vertical orientation");

  // Vertical drop maps display deltas onto swapped model coordinates.
  dragNode(root, wall, 48, 24, 31);
  const move = dotnet.calls.find((call) => call.method === "MoveNodesFromCanvasAsync");
  assert.deepEqual(move.values[0], [{ nodeId: "node-wall", x: 264, y: 120 }], "vertical drop swaps persisted coordinates");
  flushAllScheduled();
  assertMatchesReference(root, "vertical drop");
  moduleExports.dispose(root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 7 ok");
}
""";

    private const string _scenario8 = """
// Scenario 8: smooth style ---
{
  const fixture = buildStandardFixture({ edgeStyle: "smooth" });
  const dotnet = makeDotnet();
  moduleExports.initialize(fixture.root, dotnet);
  flushAllScheduled();
  const committed = committedEdge(fixture.e1);
  assert.equal(committed.path.startsWith("M "), true, "smooth route commits");
  assert.equal(committed.path.includes("C "), true, "smooth route uses curves");
  const labelled = committedEdge(fixture.e2);
  assert.equal(labelled.labelHidden, false, "smooth labelled branch shows its label");
  assertMatchesReference(fixture.root, "smooth style");
  dragNode(fixture.root, fixture.c, 0, 192, 37);
  flushAllScheduled();
  assertMatchesReference(fixture.root, "smooth changed drop");
  moduleExports.dispose(fixture.root);
  console.log("scenario 8 ok");
}
""";

    private const string _scenario9 = """
// Scenario 9: global nudging separates overlapping channel segments ---
{
  const canvas = buildCanvas();
  const root = canvas.root;
  const wall = buildNode(root, { id: "node-wall", kind: "transform", x: 240, y: 0, width: 120, height: 600 });
  const l1 = buildNode(root, { id: "node-l1", x: 0, y: 48 });
  buildPort(l1, { nodeId: "node-l1", portId: "flow", direction: "output", left: 113, top: 23 });
  const l2 = buildNode(root, { id: "node-l2", x: 0, y: 192 });
  buildPort(l2, { nodeId: "node-l2", portId: "flow", direction: "output", left: 113, top: 23 });
  const r1 = buildNode(root, { id: "node-r1", x: 480, y: 48 });
  buildPort(r1, { nodeId: "node-r1", portId: "in", direction: "input", left: -7, top: 23 });
  const r2 = buildNode(root, { id: "node-r2", x: 480, y: 192 });
  buildPort(r2, { nodeId: "node-r2", portId: "in", direction: "input", left: -7, top: 23 });
  const eTop = buildEdge(canvas.svg, { id: "edge-top", sourceNode: "node-l1", sourcePort: "flow", targetNode: "node-r1", targetPort: "in" });
  const eSecond = buildEdge(canvas.svg, { id: "edge-second", sourceNode: "node-l2", sourcePort: "flow", targetNode: "node-r2", targetPort: "in" });
  const dotnet = makeDotnet();
  moduleExports.initialize(root, dotnet);
  flushAllScheduled();
  const firstPath = committedEdge(eTop).path;
  const secondPath = committedEdge(eSecond).path;
  assert.equal(pathTouchesRectangle(firstPath, nodeRectangle(wall)), false, "first route avoids the wall");
  assert.equal(pathTouchesRectangle(secondPath, nodeRectangle(wall)), false, "second route avoids the wall");
  const channelY = (path) => Math.min(...pathPoints(path).map((point) => point.y));
  const firstChannel = channelY(firstPath);
  const secondChannel = channelY(secondPath);
  assert.equal(Math.abs(firstChannel - secondChannel), 8, "overlapping channel segments separate into distinct lanes");
  assertMatchesReference(root, "nudged channels");
  moduleExports.dispose(root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 9 ok");
}
""";

    // Disclosed card content is pre-rendered hidden, so the card reaches its final
    // disclosed size from the local class toggle alone and the .NET render that
    // follows is bookkeeping only. The refresh that render queues must therefore
    // find identical geometry and commit no second visible route change.
    private const string _scenario10 = """
// Scenario 10: one routing settle per disclosure activation ---
{
  const canvas = buildCanvas();
  const root = canvas.root;
  const source = buildNode(root, { id: "node-source", kind: "source", x: 0, y: 96 });
  buildPort(source, { nodeId: "node-source", portId: "flow", direction: "output", left: 113, top: 23 });
  const target = buildNode(root, { id: "node-target", x: 480, y: 96 });
  buildPort(target, { nodeId: "node-target", portId: "in", direction: "input", left: -7, top: 23 });
  // The disclosure target sits clear of the straight route while compact and blocks
  // it once disclosed, so any settle is visible in the committed path.
  const middle = buildNode(root, { id: "node-middle", kind: "transform", x: 240, y: 72, height: 24, disclosedHeight: 180 });
  const edge = buildEdge(canvas.svg, { id: "edge-main", sourceNode: "node-source", sourcePort: "flow", targetNode: "node-target", targetPort: "in" });
  const dotnet = makeDotnet();
  moduleExports.initialize(root, dotnet);
  flushAllScheduled();
  assert.equal(pathPoints(committedEdge(edge).path).length, 2, "the compact scene routes directly");
  const button = middle.querySelector("[data-automation-node-select]");

  const activate = (pointerId) => {
    dispatch(root, "pointerdown", { pointerId, target: button, clientX: 20, clientY: 20 });
    dispatch(root, "pointerup", { pointerId, clientX: 20, clientY: 20 });
    flushAllScheduled();
  };

  // Opening: the local echo alone produces the whole geometry change.
  const beforeOpen = metricsSnapshot();
  activate(41);
  assert.equal(middle.classes.has("automation-node--disclosed"), true, "activation discloses locally");
  assert.equal(dotnet.calls.at(-1).method, "ActivateNodeFromCanvasAsync", "activation still notifies .NET");
  const echoed = metricsSnapshot();
  assert.equal(echoed.routeVisualCommitCount - beforeOpen.routeVisualCommitCount, 1, "the local echo settles the scene once");
  assert.equal(pathPoints(committedEdge(edge).path).length > 2, true, "the disclosed card reroutes the edge");
  assertMatchesReference(root, "single-stage disclosed geometry");

  // The .NET render round trip refreshes with unchanged geometry and must not settle again.
  moduleExports.refresh(root);
  flushAllScheduled();
  const afterRender = metricsSnapshot();
  assert.equal(afterRender.routeVisualCommitCount, echoed.routeVisualCommitCount, "the render round trip commits no second visible route change");
  assert.equal(afterRender.routeComputationCount, echoed.routeComputationCount, "the render round trip reuses the settled scene");
  assert.equal(afterRender.routeCacheHitCount - echoed.routeCacheHitCount, 1, "the render round trip reuses every cache entry");
  assertMatchesReference(root, "geometry after the disclosure render");

  // Closing behaves identically through the BLOKEBOT-238 toggle.
  const beforeClose = metricsSnapshot();
  activate(43);
  assert.equal(middle.classes.has("automation-node--disclosed"), false, "the toggle closes disclosure locally");
  const closed = metricsSnapshot();
  assert.equal(closed.routeVisualCommitCount - beforeClose.routeVisualCommitCount, 1, "the toggle close settles the scene once");
  assert.equal(pathPoints(committedEdge(edge).path).length, 2, "the closed card restores the direct route");
  moduleExports.refresh(root);
  flushAllScheduled();
  const afterCloseRender = metricsSnapshot();
  assert.equal(afterCloseRender.routeVisualCommitCount, closed.routeVisualCommitCount, "the close round trip commits no second visible route change");
  assertMatchesReference(root, "geometry after the close render");
  moduleExports.dispose(root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 10 ok");
}
""";

    // A drag frame must reroute the edges the moving node actually affects -
    // its own edges and every edge whose corridor it sweeps - collision-aware,
    // and must leave every other edge's committed route untouched.
    private const string _scenario11 = """
// Scenario 11: affected-edge selection during a drag ---
{
  const canvas = buildCanvas();
  const root = canvas.root;
  const nearSource = buildNode(root, { id: "node-near-source", kind: "source", x: 0, y: 96 });
  buildPort(nearSource, { nodeId: "node-near-source", portId: "flow", direction: "output", left: 113, top: 23 });
  const nearTarget = buildNode(root, { id: "node-near-target", x: 480, y: 96 });
  buildPort(nearTarget, { nodeId: "node-near-target", portId: "in", direction: "input", left: -7, top: 23 });
  const farSource = buildNode(root, { id: "node-far-source", kind: "source", x: 0, y: 720 });
  buildPort(farSource, { nodeId: "node-far-source", portId: "flow", direction: "output", left: 113, top: 23 });
  const farTarget = buildNode(root, { id: "node-far-target", x: 480, y: 720 });
  buildPort(farTarget, { nodeId: "node-far-target", portId: "in", direction: "input", left: -7, top: 23 });
  // The blocker owns no edge, so everything it causes comes from corridor
  // intersection rather than from being an endpoint.
  const blocker = buildNode(root, { id: "node-blocker", kind: "transform", x: 240, y: 336 });
  const near = buildEdge(canvas.svg, { id: "edge-near", sourceNode: "node-near-source", sourcePort: "flow", targetNode: "node-near-target", targetPort: "in" });
  const far = buildEdge(canvas.svg, { id: "edge-far", sourceNode: "node-far-source", sourcePort: "flow", targetNode: "node-far-target", targetPort: "in" });
  const dotnet = makeDotnet();
  moduleExports.initialize(root, dotnet);
  flushAllScheduled();
  const nearBefore = committedEdge(near).path;
  const farBefore = committedEdge(far).path;
  assert.equal(pathPoints(nearBefore).length, 2, "the near edge starts as a direct route");
  assert.equal(pathPoints(farBefore).length, 2, "the far edge starts as a direct route");

  // Drag the blocker up onto the near edge, one animation frame at a time.
  const button = blocker.querySelector("[data-automation-node-select]");
  const duringDrag = withFrozenClock(() => {
    dispatch(root, "pointerdown", { pointerId: 61, target: button, clientX: 300, clientY: 400 });
    dispatch(root, "pointermove", { pointerId: 61, clientX: 300, clientY: 280 });
    flushAllScheduled();
    dispatch(root, "pointermove", { pointerId: 61, clientX: 300, clientY: 160 });
    flushAllScheduled();
    return metricsSnapshot();
  });
  const nearDuring = committedEdge(near).path;
  assert.equal(blocker.dataset.automationGraphY, "96", "the blocker follows the pointer live");
  assert.notEqual(nearDuring, nearBefore, "the swept edge is rerouted during the drag");
  assert.equal(pathPoints(nearDuring).length > 2, true, "the live route detours");
  assert.equal(pathTouchesRectangle(nearDuring, nodeRectangle(blocker)), false, "the live route avoids the moving node");
  assert.equal(committedEdge(far).path, farBefore, "the distant edge is not recomputed");
  assert.equal(duringDrag.routeEdgeLiveMaximumPerFrame, 1, "only the affected edge is routed per frame");
  assert.equal(duringDrag.routeEdgeLiveDeferredCount, 0, "nothing is deferred below the cap");
  assert.equal(duringDrag.dragGraphBuildCount, 1, "the shared graph is built once for the drag");
  assert.equal(duringDrag.dragGraphPatchCount, duringDrag.dragFrames, "every drag frame patches that graph");

  dispatch(root, "pointerup", { pointerId: 61, clientX: 300, clientY: 160 });
  flushAllScheduled();
  assert.equal(pathTouchesRectangle(committedEdge(near).path, nodeRectangle(blocker)), false, "the settled route avoids the moved node");
  assertMatchesReference(root, "drop after a corridor drag");

  // Dragging the blocker away again releases the detour.
  withFrozenClock(() => {
    dispatch(root, "pointerdown", { pointerId: 63, target: button, clientX: 300, clientY: 160 });
    dispatch(root, "pointermove", { pointerId: 63, clientX: 300, clientY: 400 });
    flushAllScheduled();
  });
  const releasedDuring = committedEdge(near).path;
  assert.equal(pathPoints(releasedDuring).length, 2, "the live route returns to direct once the node leaves the corridor");
  dispatch(root, "pointerup", { pointerId: 63, clientX: 300, clientY: 400 });
  flushAllScheduled();
  assertMatchesReference(root, "drop after leaving the corridor");
  moduleExports.dispose(root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 11 ok");
}
""";

    // The per-frame cap bounds live collision routing; the edges it defers keep
    // their previous route and the drop pass settles them.
    private const string _scenario12 = """
// Scenario 12: per-frame cap, deferral, and completion on drop ---
{
  const canvas = buildCanvas({ edgeStyle: "smooth" });
  const root = canvas.root;
  const hub = buildNode(root, { id: "node-hub", kind: "source", x: 0, y: 600, width: 120, height: 60 });
  const edges = [];
  const edgeCount = 26;
  for (let index = 0; index < edgeCount; index += 1) {
    buildPort(hub, { nodeId: "node-hub", portId: `out-${index}`, direction: "output", left: 113, top: 23 });
    const target = buildNode(root, { id: `node-t${index}`, x: 720, y: index * 96 });
    buildPort(target, { nodeId: `node-t${index}`, portId: "in", direction: "input", left: -7, top: 23 });
    edges.push(buildEdge(canvas.svg, {
      id: `edge-${index}`,
      sourceNode: "node-hub",
      sourcePort: `out-${index}`,
      targetNode: `node-t${index}`,
      targetPort: "in",
    }));
  }
  const dotnet = makeDotnet();
  moduleExports.initialize(root, dotnet);
  flushAllScheduled();
  const before = edges.map((edge) => committedEdge(edge).path);
  assert.equal(before.filter((path) => path.length > 0).length, edgeCount, "every edge routes before the drag");

  const button = hub.querySelector("[data-automation-node-select]");
  const duringDrag = withFrozenClock(() => {
    dispatch(root, "pointerdown", { pointerId: 71, target: button, clientX: 60, clientY: 630 });
    dispatch(root, "pointermove", { pointerId: 71, clientX: 156, clientY: 678 });
    flushAllScheduled();
    return metricsSnapshot();
  });
  assert.equal(duringDrag.routeEdgeLiveAffectedCount >= edgeCount, true, "every edge of the dragged node is affected");
  assert.equal(duringDrag.routeEdgeLiveMaximumPerFrame, 24, "live collision routes stop at the per-frame cap");
  assert.equal(duringDrag.routeEdgeLiveDeferredCount, edgeCount - 24, "the edges beyond the cap are deferred");
  const during = edges.map((edge) => committedEdge(edge).path);
  assert.equal(during.filter((path, index) => path === before[index]).length >= edgeCount - 24, true, "deferred edges keep their previous route");

  dispatch(root, "pointerup", { pointerId: 71, clientX: 156, clientY: 678 });
  flushAllScheduled();
  const move = dotnet.calls.find((call) => call.method === "MoveNodesFromCanvasAsync");
  assert.deepEqual(move.values[0], [{ nodeId: "node-hub", x: 96, y: 648 }], "the drop persists the snapped position");
  const settled = edges.map((edge) => committedEdge(edge).path);
  assert.equal(settled.filter((path, index) => path !== before[index]).length, edgeCount, "the drop pass completes every edge, deferred or not");
  assertMatchesReference(root, "drop completes the deferred work");
  moduleExports.dispose(root);
  globalThis.__resetBlokeBotAutomationMetrics();
  console.log("scenario 12 ok");
}
""";
}
