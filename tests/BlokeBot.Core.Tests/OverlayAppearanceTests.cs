using System.Diagnostics;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class OverlayAppearanceTests
{
    [Test]
    public void ExistingConfigurations_ReceiveTheirEquivalentFixedGeometry()
    {
        var guessing = OverlayConfiguration
            .Parse(
                OverlayType.Guessing,
                """{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":8}"""
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Valid>()
            .Value.ShouldBeOfType<OverlayConfiguration.GuessingV1>();
        var giveaway = OverlayConfiguration
            .Parse(
                OverlayType.Giveaway,
                """{"schemaVersion":1,"title":"Community giveaway","showEntrantCount":true,"showCountdown":true,"showJoinCommand":true}"""
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Valid>()
            .Value.ShouldBeOfType<OverlayConfiguration.GiveawayV1>();

        guessing.Appearance.ShouldBe(OverlayAppearance.GuessingDefault);
        giveaway.Appearance.ShouldBe(OverlayAppearance.GiveawayDefault);
    }

    [Test]
    public void Appearance_RoundTripsGeometryAndSafeScopedCss()
    {
        var configuration = new OverlayConfiguration.GuessingV1(
            true,
            8,
            new OverlayAppearance(
                20,
                30,
                640,
                360,
                ".card { fill: #111827; }\n.title { font-size: 64px; }"
            )
        );

        var parsed = OverlayConfiguration
            .Parse(OverlayType.Guessing, configuration.ToPersistenceJson())
            .ShouldBeOfType<OverlayConfigurationParseResult.Valid>()
            .Value.ShouldBeOfType<OverlayConfiguration.GuessingV1>();

        parsed.ShouldBe(configuration);
        parsed.Appearance.ToScopedCss().ShouldContain("#overlay-root .card");
        parsed.Appearance.ToScopedCss().ShouldContain("#overlay-root .title");
    }

    [Arguments(".card { background-image: url(https://example.com/a.png); }")]
    [Arguments(".card { background: URL (https://example.com/a.png); }")]
    [Arguments(".card { background: u\\72l(https://example.com/a.png); }")]
    [Arguments("@import 'https://example.com/a.css';")]
    [Arguments("body { color: red; }")]
    [Arguments(".card { position: fixed; }")]
    [Arguments(".card { color: red; } <script>alert(1)</script>")]
    [Test]
    public void Appearance_RejectsNetworkScopeEscapeAndMarkup(string css) =>
        Should.Throw<ArgumentException>(() => new OverlayAppearance(0, 0, 640, 360, css));

    [Test]
    public async Task DashboardDraft_RevealsOnlyLoadedValidatedCurrentSelectionAfterAcknowledgement()
    {
        var editor = Source("OverlaySourcesPanel.razor.js");
        var harness = $$"""
            import assert from "node:assert/strict";

            class FakeRect {
              constructor(action = null) {
                this.dataset = action === null ? {} : { appearanceAction: action };
                this.listeners = new Map();
                this.attributes = new Map();
              }
              addEventListener(type, listener) { this.listeners.set(type, listener); }
              removeEventListener(type) { this.listeners.delete(type); }
              setAttribute(name, value) { this.attributes.set(name, value); }
              setPointerCapture() {}
              emit(type, value) { this.listeners.get(type)?.(value); }
            }
            class FakeSvg {
              constructor() {
                this.dataset = { x: "0", y: "0", width: "640", height: "360", renderedCss: ".a {}" };
                this.selection = new FakeRect();
                this.actions = new Map(
                  ["move", "w", "e", "n", "s", "nw", "ne", "sw", "se"].map(
                    (action) => [action, new FakeRect(action)],
                  ),
                );
              }
              querySelector(selector) {
                if (selector === "[data-selection-line]") return this.selection;
                const match = selector.match(/data-appearance-action="([^"]+)"/);
                return match === null ? null : this.actions.get(match[1]) ?? null;
              }
              querySelectorAll(selector) {
                return selector === "[data-appearance-action]"
                  ? [...this.actions.values()]
                  : [];
              }
              getScreenCTM() { return { inverse() { return {}; } }; }
              createSVGPoint() {
                return {
                  x: 0,
                  y: 0,
                  matrixTransform() { return { x: this.x, y: this.y }; },
                };
              }
            }
            class FakeFrame {
              constructor(overlayId) {
                this.dataset = { overlayId };
                this.messages = [];
                this.contentWindow = { postMessage: (message) => this.messages.push(message) };
                this.listeners = new Map();
                this.contentDocument = { readyState: "loading" };
                this.classes = new Set(["overlay-preview-frame"]);
                this.classList = {
                  add: (value) => this.classes.add(value),
                  remove: (value) => this.classes.delete(value),
                };
                this.attributes = new Map();
              }
              addEventListener(type, listener) { this.listeners.set(type, listener); }
              setAttribute(name, value) { this.attributes.set(name, value); }
              removeAttribute(name) { this.attributes.delete(name); }
              load() {
                this.contentDocument.readyState = "complete";
                this.listeners.get("load")?.();
              }
            }
            class FakeInput {
              constructor() {
                this.value = ".a {}";
                this.listeners = new Map();
              }
              addEventListener(type, listener) { this.listeners.set(type, listener); }
            }
            class FakeNumberInput {
              constructor() { this.value = ""; }
            }

            globalThis.SVGSVGElement = FakeSvg;
            globalThis.SVGRectElement = FakeRect;
            globalThis.HTMLIFrameElement = FakeFrame;
            globalThis.HTMLTextAreaElement = FakeInput;
            globalThis.HTMLInputElement = FakeNumberInput;
            const svg = new FakeSvg();
            const frameA = new FakeFrame("A");
            const frameB = new FakeFrame("B");
            const frameA2 = new FakeFrame("A");
            let frame = frameA;
            const input = new FakeInput();
            const numberInputs = new Map(
              ["appearance-x", "appearance-y", "appearance-width", "appearance-height"].map(
                (id) => [id, new FakeNumberInput()],
              ),
            );
            globalThis.document = {
              querySelector(selector) {
                if (selector === "[data-appearance-editor]") return svg;
                if (selector === "[data-appearance-preview]") return frame;
                if (selector === "[data-appearance-css]") return input;
                return null;
              },
              getElementById(id) { return numberInputs.get(id) ?? null; },
            };
            const windowListeners = new Map();
            globalThis.window = {
              location: { origin: "https://example.test" },
              addEventListener(type, listener) { windowListeners.set(type, listener); },
              requestAnimationFrame(callback) { callback(); return 1; },
            };

            const pending = [];
            const appearanceUpdates = [];
            const dotnet = {
              invokeMethodAsync(method, ...values) {
                if (method === "ScopeDraftCss") {
                  const [css] = values;
                  return new Promise((resolve) => pending.push({ css, resolve }));
                }
                assert.equal(method, "UpdateAppearance");
                return new Promise((resolve) => appearanceUpdates.push({ values, resolve }));
              },
            };

            {{editor}}

            const settle = async () => {
              await Promise.resolve();
              await Promise.resolve();
            };

            initializeAppearance(dotnet);
            assert.deepEqual(pending.map((request) => request.css), [".a {}"]);
            pending[0].resolve("#overlay-root .a {}");
            await settle();
            assert.equal(frameA.messages.length, 0);
            assert.equal(frameA.classes.has("overlay-preview-frame--ready"), false);
            frameA.load();
            assert.equal(frameA.messages.at(-1).css, "#overlay-root .a {}");
            assert.equal(frameA.classes.has("overlay-preview-frame--ready"), false);

            frame = frameB;
            svg.dataset.renderedCss = ".b {}";
            initializeAppearance(dotnet);
            assert.deepEqual(pending.map((request) => request.css), [".a {}", ".b {}"]);
            frameB.load();
            assert.equal(frameB.messages.length, 0);

            frame = frameA2;
            svg.dataset.renderedCss = ".a {}";
            initializeAppearance(dotnet);
            assert.deepEqual(pending.map((request) => request.css), [".a {}", ".b {}", ".a {}"]);
            frameA2.load();
            assert.equal(frameA2.messages.length, 0);

            pending[1].resolve("#overlay-root .b {}");
            await settle();
            assert.equal(frameB.messages.length, 0);

            pending[2].resolve("#overlay-root .a-fresh {}");
            await settle();
            const readyDraft = frameA2.messages.at(-1);
            assert.equal(readyDraft.css, "#overlay-root .a-fresh {}");
            assert.equal(frameA2.classes.has("overlay-preview-frame--ready"), false);

            windowListeners.get("message")({
              origin: "https://example.test",
              source: frameA.contentWindow,
              data: { kind: "blokebot-dashboard-draft-ready", requestId: frameA.messages.at(-1).requestId, overlayId: "A" },
            });
            assert.equal(frameA2.classes.has("overlay-preview-frame--ready"), false);

            windowListeners.get("message")({
              origin: "https://example.test",
              source: frameA2.contentWindow,
              data: { kind: "blokebot-dashboard-draft-ready", requestId: readyDraft.requestId, overlayId: "A" },
            });
            assert.equal(frameA2.classes.has("overlay-preview-frame--ready"), true);
            assert.equal(frameA2.attributes.has("aria-busy"), false);

            const move = svg.actions.get("move");
            const pointer = (clientX, clientY) => ({
              clientX,
              clientY,
              pointerId: 7,
              preventDefault() {},
            });
            move.emit("pointerdown", pointer(0, 0));
            move.emit("pointermove", pointer(25, 30));
            assert.deepEqual(appearanceUpdates.map((update) => update.values), [[25, 30, 640, 360]]);
            assert.equal(svg.dataset.x, "25");
            assert.equal(svg.dataset.y, "30");

            svg.dataset.x = "0";
            svg.dataset.y = "0";
            initializeAppearance(dotnet);
            assert.equal(svg.dataset.x, "25");
            assert.equal(svg.dataset.y, "30");
            assert.equal(move.attributes.get("x"), "25");
            assert.equal(move.attributes.get("y"), "30");
            assert.equal(appearanceUpdates.length, 1);
            appearanceUpdates[0].resolve();
            await settle();
            """;

        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--input-type=module");
        startInfo.ArgumentList.Add("--eval");
        startInfo.ArgumentList.Add(harness);
        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Node.js.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);
        process.ExitCode.ShouldBe(0, $"{await output}\n{await error}");
    }

    [Test]
    public async Task DashboardDraft_Reinstall_CancelsActivePresentationAnimationBeforeReplacement()
    {
        var browserSource = OverlayBrowserSourceAssets.JavaScript;
        var harness = $$"""
            import assert from "node:assert/strict";

            class FakeElement {
              constructor(namespaceURI = null) {
                this.namespaceURI = namespaceURI;
                this.dataset = {};
                this.children = [];
                this.attributes = new Map();
                this.textContent = "";
              }
              append(...children) { this.children.push(...children); }
              replaceChildren(...children) { this.children = [...children]; }
              setAttribute(name, value) {
                this.attributes.set(name, value);
                if (name === "class") this.className = value;
              }
              removeAttribute(name) { this.attributes.delete(name); }
              querySelector() { return null; }
              querySelectorAll() { return []; }
            }
            class FakeHtmlElement extends FakeElement {}
            class FakeSvgElement extends FakeElement {
              constructor() { super("http://www.w3.org/2000/svg"); }
            }
            class FakeLinkElement extends FakeElement {
              constructor() {
                super();
                this.href = "https://example.test/overlays/appearance.css";
                this.sheet = new FakeStyleSheet();
              }
            }
            class FakeStyleSheet {
              constructor() { this.cssRules = []; }
              insertRule(rule, index) { this.cssRules.splice(index, 0, rule); }
              deleteRule(index) { this.cssRules.splice(index, 1); }
            }

            globalThis.HTMLElement = FakeHtmlElement;
            globalThis.SVGSVGElement = FakeSvgElement;
            globalThis.HTMLLinkElement = FakeLinkElement;
            globalThis.CSSStyleSheet = FakeStyleSheet;

            const root = new FakeHtmlElement();
            root.dataset.credentials = "same-origin";
            root.dataset.liveEnabled = "false";
            root.dataset.stateUrl = "/overlays/state";
            const canvas = new FakeSvgElement();
            const cueCanvas = new FakeHtmlElement();
            const appearanceStylesheet = new FakeLinkElement();
            globalThis.document = {
              getElementById(id) {
                if (id === "overlay-root") return root;
                if (id === "overlay-canvas") return canvas;
                if (id === "cue-canvas") return cueCanvas;
                if (id === "overlay-appearance-style") return appearanceStylesheet;
                return null;
              },
              createElementNS() { return new FakeSvgElement(); },
              createElement() { return new FakeHtmlElement(); },
            };

            const listeners = new Map();
            const timers = [];
            const clearedTimers = [];
            const parent = { postMessage() {} };
            globalThis.window = {
              parent,
              location: {
                origin: "https://example.test",
                pathname: "/overlays/preview/overlay-1",
                href: "https://example.test/overlays/preview/overlay-1",
              },
              addEventListener(type, listener) { listeners.set(type, listener); },
              setTimeout(callback, milliseconds) {
                timers.push({ callback, milliseconds });
                return timers.length;
              },
              clearTimeout(timer) { clearedTimers.push(timer); },
              requestAnimationFrame(callback) { callback(); return 1; },
            };

            const projection = {
              schemaVersion: 1,
              overlayType: "guessing",
              resultDurationMilliseconds: 8000,
              animation: "result",
              appearance: { x: 100, y: 100, width: 800, height: 270 },
              state: {
                phase: "completed",
                roundName: "Final answer",
                guessCount: 4,
                winningAnswer: "42",
                winners: ["viewer"],
                awardedPointsPerWinner: "10",
                pointLabel: "points",
              },
              sequence: 7,
              serverEpoch: "epoch-1",
              generatedAtUtc: "2026-08-01T00:00:00Z",
            };
            globalThis.fetch = async () => ({
              ok: true,
              json: async () => projection,
            });

            {{browserSource}}

            const settle = async () => {
              for (let index = 0; index < 8; index += 1) await Promise.resolve();
            };
            await settle();

            assert.equal(root.dataset.status, "representative");
            assert.equal(root.dataset.animation, "result");
            assert.equal(timers.length, 1);
            assert.equal(timers[0].milliseconds, 8000);
            const firstPresentation = canvas.children[0].children[0];
            assert.equal(firstPresentation.className, "guessing-presentation");

            listeners.get("message")({
              origin: "https://example.test",
              source: parent,
              data: {
                kind: "blokebot-dashboard-draft",
                requestId: "draft-1",
                overlayId: "overlay-1",
                appearance: { x: 120, y: 140, width: 900, height: 300 },
                css: "",
                choices: { showGuessCount: true },
              },
            });

            const replacementPresentation = canvas.children[0].children[0];
            assert.notEqual(replacementPresentation, firstPresentation);
            assert.equal(replacementPresentation.className, "guessing-presentation");
            assert.equal("animation" in root.dataset, false);
            assert.deepEqual(clearedTimers, [1]);
            assert.equal(timers.length, 1);
            """;

        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--input-type=module");
        startInfo.ArgumentList.Add("--eval");
        startInfo.ArgumentList.Add(harness);
        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Node.js.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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
                    "Overlays",
                    fileName
                )
            )
        );
}
