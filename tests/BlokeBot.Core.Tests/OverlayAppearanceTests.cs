using System.Diagnostics;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Http;
using Shouldly;
using TUnit.Core;

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
    public void Renderer_MakesGiveawayAbsenceTransparentAndUsesSavedGeometry()
    {
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("if (state.phase === \"idle\")");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("appearance.width / 1600");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("validAppearance");

        var document = OverlayBrowserSourceDocument.Render(
            PathString.Empty,
            "/overlay/private/state",
            "/overlay/private/events",
            OverlayBrowserSourceCredentials.Omit,
            liveEnabled: true
        );
        document.ShouldContain("/overlay/private/appearance.css");
        Source("OverlayBrowserSourceEndpoints.cs").ShouldContain("style-src 'self'");
    }

    [Test]
    public void DashboardSource_SeparatesPagesAndUsesCompactSemanticControls()
    {
        var overlays = Source("OverlaysPage.razor");
        var cues = Source("CuesPage.razor");
        var media = Source("MediaLibraryPage.razor");

        overlays.ShouldNotContain("type=\"checkbox\"");
        cues.ShouldNotContain("type=\"checkbox\"");
        overlays.ShouldNotContain("data-card-owner=\"overlay-cue-workspace\"");
        overlays.ShouldNotContain("media-library-title");
        overlays
            .IndexOf("overlay-preview-title", StringComparison.Ordinal)
            .ShouldBeLessThan(overlays.IndexOf("overlay-editor-title", StringComparison.Ordinal));
        overlays.ShouldContain("Advanced styling");
        overlays.ShouldContain("data-appearance-editor");
        overlays.ShouldContain("value => value is not GiveawayOverlaySampleState.Idle");
        cues.ShouldContain("@page \"/overlays/cues\"");
        cues.ShouldContain("href=\"overlays/media\"");
        media.ShouldContain("@page \"/overlays/media\"");
        media.ShouldContain("UploadAsync");
        cues.ShouldContain("data-card-owner=\"cue-workspace-columns\"");
        cues.ShouldContain("<div class=\"cue-workspace\"");
        cues.ShouldContain("class=\"card cue-workspace__inventory p-5\"");
        cues.ShouldContain("class=\"card cue-workspace__editor p-5\"");
        cues.ShouldNotContain("xl:items-start");
        media.ShouldContain("Accept=\"@OverlayMediaTypes.AcceptedBrowserMedia\"");
        media.ShouldContain(
            "<div class=\"mt-4 grid gap-3\"><Field Id=\"media-name\" Label=\"Media name\""
        );
        media.ShouldContain(
            "<FileDropInput Label=\"Media file\" Accept=\"@OverlayMediaTypes.AcceptedBrowserMedia\" Disabled=\"_busy\" OnChange=\"UploadAsync\" />"
        );
        media.ShouldNotContain(
            "<FileDropInput Label=\"Media file\" Accept=\"@OverlayMediaTypes.AcceptedBrowserMedia\" Compact=\"true\""
        );
        media.ShouldContain(
            "<FileDropInput Label=\"Replace file\" Accept=\"@OverlayMediaTypes.AcceptedBrowserMedia\" Disabled=\"_busy\" OnChange=\"@(args => ReplaceAsync(asset, args))\" />"
        );
        media.ShouldNotContain("<InputFile");
        media.ShouldNotContain("MIME", Case.Insensitive);
        media.ShouldNotContain("MP3", Case.Insensitive);
        media.ShouldNotContain("MP4", Case.Insensitive);

        var editor = Source("OverlaysPage.razor.js");
        editor.ShouldContain("setPointerCapture");
        editor.ShouldContain("event.shiftKey ? 10 : 1");
        editor.ShouldContain("\"ArrowLeft\"");
        editor.ShouldContain("requestAnimationFrame");
        editor.ShouldContain("pendingForDotNet");
        editor.ShouldContain("blokebot-dashboard-draft");
        editor.ShouldContain("action.includes(\"w\")");
        editor.ShouldContain("action.includes(\"e\")");
        editor.ShouldContain("action.includes(\"n\")");
        editor.ShouldContain("action.includes(\"s\")");
        cues.ShouldNotContain("textarea");
        cues.ShouldContain("Add uploaded media");
        cues.ShouldNotContain("textarea");
        cues.ShouldNotContain("Cue-V1");

        var cueStyles = Source("CuesPage.razor.css");
        cueStyles.ShouldContain(".cue-workspace > .card");
        cueStyles.ShouldContain("box-shadow: none;");
        cueStyles.ShouldContain("align-items: stretch;");
        cueStyles.ShouldContain("grid-template-columns: minmax(18rem, 21rem) minmax(43rem, 1fr);");
        cueStyles.ShouldContain("border-left: 1px solid var(--app-border);");
        overlays.ShouldContain("Available selectors");
        overlays.ShouldNotContain("Stable selectors");
        overlays.ShouldNotContain("Cards are plain text");
    }

    [Test]
    public void DashboardDraft_IsCoalescedIdentityBoundAndUnavailableToTopLevelSources()
    {
        var editor = Source("OverlaysPage.razor.js");
        var browserSource = OverlayBrowserSourceAssets.JavaScript;

        editor.ShouldContain("requestAnimationFrame");
        editor.ShouldContain("dotNetBusy");
        editor.ShouldContain("pendingForDotNet");
        editor.ShouldContain("overlayId: activeFrame.dataset.overlayId");
        editor.ShouldNotContain("pointermove\", (event) => dotnet");

        browserSource.ShouldContain("credentials === \"same-origin\"");
        browserSource.ShouldContain("window.parent !== window");
        browserSource.ShouldContain("event.source !== window.parent");
        browserSource.ShouldContain("event.origin !== window.location.origin");
        browserSource.ShouldContain("endsWith(`/overlays/preview/${value.overlayId}`)");
        browserSource.ShouldContain("withDashboardDraft");
        browserSource.ShouldNotContain("unsafe-inline");
        browserSource.ShouldNotContain("utils.js");
        browserSource.ShouldContain("if (!fromDraft)");
        browserSource.ShouldContain("applyPresentationAnimation(");
        browserSource.Split("applyAnimation(").Length.ShouldBe(2);
        browserSource.ShouldContain("class: \"overlay\"");
        browserSource.ShouldContain("class: \"guessing-presentation\"");
        browserSource.ShouldContain("class: \"giveaway-presentation\"");
        browserSource.ShouldContain("class: \"event-feed-presentation\"");
        browserSource.ShouldNotContain("class: \"guessing-presentation overlay\"");
        browserSource.ShouldNotContain("class: \"giveaway-presentation overlay\"");
        browserSource.ShouldNotContain("class: \"event-feed-presentation overlay\"");
        OverlayBrowserSourceAssets.Stylesheet.ShouldContain(
            "#overlay-root[data-animation=\"entrance\"] .guessing-presentation"
        );
        OverlayBrowserSourceAssets.Stylesheet.ShouldContain(
            "#overlay-root[data-animation=\"winner\"] .giveaway-presentation"
        );
        OverlayBrowserSourceAssets.Stylesheet.ShouldContain(
            "#overlay-root[data-animation=\"card\"] .event-feed-presentation"
        );
        OverlayBrowserSourceAssets.Stylesheet.ShouldNotContain("[data-animation] .overlay");
        Source("OverlayBrowserSourceEndpoints.cs").ShouldContain("style-src 'self'");
        Source("OverlayBrowserSourceEndpoints.cs").ShouldNotContain("unsafe-inline");
    }

    [Test]
    public void DashboardDraft_RevalidatesSelectedAndResetCssAndResendsAfterEveryPreviewLoad()
    {
        var editor = Source("OverlaysPage.razor.js");

        editor.ShouldContain("activeFrame.dataset.overlayId");
        editor.ShouldContain("editor.dataset.renderedCss");
        editor.ShouldContain("input.value");
        editor.ShouldContain("validatedCssIdentity");
        editor.ShouldContain("const generation = ++cssGeneration");
        editor.ShouldContain("scopedCss = \"\";");
        editor.ShouldContain("validatedCssIdentity = null;");
        editor.ShouldContain("generation !== cssGeneration");
        editor.ShouldContain("cssIdentity(desiredCss) !== identity");
        editor.ShouldContain("nextFrame.addEventListener(\"load\"");
        editor.ShouldContain("blokebot-dashboard-draft-ready");
        editor.ShouldContain("value.requestId !== activeRequestId");
        editor.ShouldContain("event.source !== activeFrame.contentWindow");
        editor.ShouldNotContain("nextFrame.addEventListener(\"load\", sendDraft, { once: true })");
        editor.ShouldContain("resync: (nextFrame) => bindDraftSources(nextFrame)");
        editor.ShouldContain("(!dotNetBusy && pendingForDotNet === null)");
        editor.ShouldContain("paint(geometry, false)");
        editor.ShouldContain("if (!notifyDotNet) return;");
        Source("OverlaysPage.razor").ShouldContain("@key=\"_previewUrl\"");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "kind: \"blokebot-dashboard-draft-ready\""
        );
    }

    [Test]
    public async Task DashboardDraft_RevealsOnlyLoadedValidatedCurrentSelectionAfterAcknowledgement()
    {
        var editor = Source("OverlaysPage.razor.js");
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

    [Test]
    public void SpatialEditor_ExposesBodyMovementAllEdgesAndAllCornersWithoutSolidHandles()
    {
        var source = Source("OverlaysPage.razor");

        source.ShouldContain("data-selection-line");
        source.ShouldContain("overlay-selection-dashes");
        source.ShouldContain("prefers-reduced-motion: reduce");
        foreach (var action in new[] { "move", "n", "s", "e", "w", "ne", "nw", "se", "sw" })
        {
            source.ShouldContain($"data-appearance-action=\"{action}\"");
        }
        source.ShouldContain("[data-appearance-action=\"move\"] { cursor: move; }");
        source.ShouldContain(
            "[data-appearance-action=\"w\"], [data-appearance-action=\"e\"] { cursor: ew-resize; }"
        );
        source.ShouldContain(
            "[data-appearance-action=\"n\"], [data-appearance-action=\"s\"] { cursor: ns-resize; }"
        );
        source.ShouldContain(
            "[data-appearance-action=\"nw\"], [data-appearance-action=\"se\"] { cursor: nwse-resize; }"
        );
        source.ShouldContain(
            "[data-appearance-action=\"ne\"], [data-appearance-action=\"sw\"] { cursor: nesw-resize; }"
        );
        source.ShouldNotContain("fill=\"#2563eb\"");
    }

    [Test]
    public void DashboardPreviews_AvoidIneffectiveSandboxPairsAndRootLocalRoutes()
    {
        var sources = Source("OverlaysPage.razor");
        var cues = Source("CuesPage.razor");
        var tabs = Source("OverlaySectionTabs.razor");

        sources.ShouldNotContain("allow-scripts allow-same-origin");
        cues.ShouldNotContain("allow-scripts allow-same-origin");
        tabs.ShouldContain("\"/overlays/sources\"");
        tabs.ShouldContain("\"/overlays/cues\"");
        tabs.ShouldContain("\"/overlays/media\"");
        tabs.ShouldNotContain("\"file:");
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
