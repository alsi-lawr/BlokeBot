using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class FileDropInputTests
{
    [Test]
    public async Task Selection_RecordsHumanMetadataAndInvokesTheSuppliedHandlerOnce()
    {
        var count = 0;
        InputFileChangeEventArgs? received = null;
        var component = new FileDropInput();
        typeof(FileDropInput)
            .GetProperty(nameof(FileDropInput.OnChange))!
            .SetValue(
                component,
                EventCallback.Factory.Create<InputFileChangeEventArgs>(
                    this,
                    args =>
                    {
                        count++;
                        received = args;
                    }
                )
            );
        var args = new InputFileChangeEventArgs([
            new TestBrowserFile("opening.mp4", 1_572_864, "video/mp4"),
        ]);

        await component.HandleSelectionAsync(args);

        count.ShouldBe(1);
        received.ShouldBeSameAs(args);
        component.SelectedFileLabel.ShouldBe("opening.mp4 · 1.5 MB");
    }

    [Test]
    public async Task Render_UsesOneAccessibleBrowseButtonAndNativeFileBoundary()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJSRuntime, NullJsRuntime>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(
            provider,
            provider.GetRequiredService<ILoggerFactory>()
        );
        var markup = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<FileDropInput>(
                ParameterView.FromDictionary(
                    new Dictionary<string, object?>
                    {
                        [nameof(FileDropInput.Label)] = "Media file",
                        [nameof(FileDropInput.Accept)] = "image/*,audio/*,video/*",
                        [nameof(FileDropInput.Disabled)] = true,
                    }
                )
            );
            return output.ToHtmlString();
        });

        markup.ShouldContain("data-file-drop-target");
        markup.ShouldContain("<strong>Drag and drop here</strong>");
        markup.ShouldContain("Browse files");
        markup.ShouldContain("<button");
        markup.ShouldContain("type=\"button\"");
        markup.ShouldContain("disabled");
        markup.ShouldContain("aria-disabled=\"true\"");
        markup.ShouldContain("aria-busy=\"false\"");
        markup.ShouldContain("type=\"file\"");
        markup.ShouldContain("accept=\"image/*,audio/*,video/*\"");
        markup.ShouldContain("Media file");
        markup.ShouldContain("aria-hidden=\"true\"");
    }

    [Test]
    public void GlobalDragHighlightsEveryEnabledTargetWithoutRestartingPageAnimation()
    {
        var styles = Source("file-drop-input.css");

        styles.ShouldContain(
            """
                body.blokebot-file-drag-active
                    .app-motion-stack
                    > *:has(.file-drop-input[data-disabled="false"]) {
                    animation-fill-mode: none;
                }
            """
        );
        styles.ShouldNotContain("animation: none;");
        styles.ShouldNotContain("transform: none !important;");
        styles.ShouldContain("body.blokebot-file-drag-active::after");
        styles.ShouldContain("z-index: 90;");
        styles.ShouldContain(
            "body.blokebot-file-drag-active .file-drop-input[data-disabled=\"false\"]"
        );
        styles.ShouldContain("background: #eff6ff;");
        styles.ShouldContain(
            """
                html[data-theme="dark"]
                    body.blokebot-file-drag-active
                    .file-drop-input[data-disabled="false"] {
                    background: var(--app-control-hover);
                }
            """
        );
        styles.ShouldNotContain(
            "body.blokebot-file-drag-active .file-drop-input[data-file-drop-hover]"
        );
        styles.ShouldContain("z-index: 91;");
    }

    [Test]
    public async Task ProductionModule_SharesDocumentListenersAndDispatchesEachBrowseOrDropOnce()
    {
        var module = Source("FileDropInput.razor.js");
        module.ShouldNotContain("data-file-drop-hover");
        var harness = $$"""
            import assert from "node:assert/strict";

            class ClassList {
              constructor() { this.values = new Set(); }
              add(value) { this.values.add(value); }
              remove(value) { this.values.delete(value); }
              contains(value) { return this.values.has(value); }
            }
            class FakeInput {
              constructor() {
                this.files = [];
                this.clicks = 0;
                this.changes = 0;
              }
              click() { this.clicks += 1; }
              dispatchEvent(event) {
                if (event.type === "change") this.changes += 1;
                return true;
              }
            }
            class FakeRoot {
              constructor(input) {
                this.input = input;
              }
              querySelector(selector) {
                assert.equal(selector, 'input[type="file"]');
                return this.input;
              }
            }
            class FakeDataTransfer {
              constructor() {
                this.files = [];
                this.items = { add: (file) => this.files.push(file) };
              }
            }
            class EventTargetHarness {
              constructor() {
                this.listeners = new Map();
                this.additions = new Map();
                this.removals = new Map();
              }
              addEventListener(type, listener) {
                this.listeners.set(type, listener);
                this.additions.set(type, (this.additions.get(type) ?? 0) + 1);
              }
              removeEventListener(type) {
                this.listeners.delete(type);
                this.removals.set(type, (this.removals.get(type) ?? 0) + 1);
              }
              emit(type, event = {}) { this.listeners.get(type)?.(event); }
            }

            globalThis.HTMLInputElement = FakeInput;
            globalThis.DataTransfer = FakeDataTransfer;
            const documentEvents = new EventTargetHarness();
            const windowEvents = new EventTargetHarness();
            globalThis.document = {
              body: { classList: new ClassList() },
              addEventListener: (...args) => documentEvents.addEventListener(...args),
              removeEventListener: (...args) => documentEvents.removeEventListener(...args),
            };
            globalThis.window = {
              addEventListener: (...args) => windowEvents.addEventListener(...args),
              removeEventListener: (...args) => windowEvents.removeEventListener(...args),
            };

            {{module}}

            const inputA = new FakeInput();
            const inputB = new FakeInput();
            const rootA = new FakeRoot(inputA);
            const rootB = new FakeRoot(inputB);
            const bindingA = bindFileDrop(rootA, false);
            const bindingB = bindFileDrop(rootB, false);
            for (const type of ["dragenter", "dragover", "dragleave", "drop", "dragend"]) {
              assert.equal(documentEvents.additions.get(type), 1);
            }
            assert.equal(windowEvents.additions.get("blur"), 1);

            const nonFile = {
              dataTransfer: { types: ["text/plain"], files: [] },
              composedPath: () => [rootA],
              preventDefault() { throw new Error("non-file drag was intercepted"); },
            };
            documentEvents.emit("dragenter", nonFile);
            assert.equal(document.body.classList.contains("blokebot-file-drag-active"), false);

            const files = [{ name: "opening.mp4" }];
            const fileEvent = (root) => ({
              dataTransfer: { types: ["Files"], files, dropEffect: "none" },
              composedPath: () => [root],
              preventDefault() {},
            });
            documentEvents.emit("dragenter", fileEvent(rootA));
            assert.equal(document.body.classList.contains("blokebot-file-drag-active"), true);

            documentEvents.emit("dragover", fileEvent(rootB));
            assert.equal(document.body.classList.contains("blokebot-file-drag-active"), true);
            documentEvents.emit("drop", fileEvent(rootB));
            assert.equal(inputA.changes, 0);
            assert.equal(inputB.changes, 1);
            assert.deepEqual(inputB.files, [files[0]]);
            assert.equal(document.body.classList.contains("blokebot-file-drag-active"), false);

            bindingA.browse();
            assert.equal(inputA.clicks, 1);
            inputA.dispatchEvent(new Event("change"));
            assert.equal(inputA.changes, 1);
            documentEvents.emit("dragenter", fileEvent(rootA));
            assert.equal(document.body.classList.contains("blokebot-file-drag-active"), true);
            bindingA.setDisabled(true);
            assert.equal(document.body.classList.contains("blokebot-file-drag-active"), true);
            bindingA.browse();
            assert.equal(inputA.clicks, 1);
            documentEvents.emit("dragleave", { ...fileEvent(rootA), relatedTarget: null });
            assert.equal(document.body.classList.contains("blokebot-file-drag-active"), false);
            documentEvents.emit("dragenter", fileEvent(rootA));
            assert.equal(document.body.classList.contains("blokebot-file-drag-active"), true);
            documentEvents.emit("drop", fileEvent(rootA));
            assert.equal(inputA.changes, 1);
            assert.equal(document.body.classList.contains("blokebot-file-drag-active"), false);
            documentEvents.emit("dragenter", fileEvent(rootB));
            windowEvents.emit("blur");
            assert.equal(document.body.classList.contains("blokebot-file-drag-active"), false);

            bindingA.dispose();
            assert.equal(documentEvents.listeners.has("drop"), true);
            bindingB.dispose();
            assert.equal(documentEvents.listeners.has("drop"), false);
            for (const type of ["dragenter", "dragover", "dragleave", "drop", "dragend"]) {
              assert.equal(documentEvents.removals.get(type), 1);
            }
            assert.equal(windowEvents.removals.get("blur"), 1);
            """;

        await RunNodeAsync(harness);
    }

    [Test]
    public async Task EventFeedProductionRenderer_UsesTheFullAcceptedCanvasHeight()
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
                this.scrollHeight = 64;
              }
              append(...children) { this.children.push(...children); }
              replaceChildren(...children) { this.children = [...children]; }
              setAttribute(name, value) {
                this.attributes.set(name, value);
                if (name === "class") this.className = value;
              }
              removeAttribute(name) { this.attributes.delete(name); }
              remove() {}
              querySelector() { return null; }
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
            globalThis.window = {
              parent: null,
              location: {
                origin: "https://example.test",
                pathname: "/overlays/preview/feed",
                href: "https://example.test/overlays/preview/feed",
              },
              addEventListener() {},
              setTimeout() { return 1; },
              clearTimeout() {},
              requestAnimationFrame(callback) { callback(); return 1; },
            };
            window.parent = window;
            globalThis.fetch = async () => ({
              ok: true,
              json: async () => ({
                schemaVersion: 1,
                overlayType: "eventFeed",
                animation: "none",
                appearance: { x: 0, y: 0, width: 1600, height: 1080 },
                state: {
                  active: {
                    id: 1,
                    kind: "pointAward",
                    priority: "normal",
                    title: "Points",
                    body: "A short event",
                    enqueuedAtUtc: "2026-08-01T00:00:00Z",
                    displayDeadlineUtc: null,
                  },
                  pending: [],
                },
                sequence: 1,
                serverEpoch: "epoch",
                generatedAtUtc: "2026-08-01T00:00:00Z",
              }),
            });

            {{browserSource}}
            for (let index = 0; index < 8; index += 1) await Promise.resolve();

            const geometry = canvas.children.at(-1);
            assert.equal(
              geometry.attributes.get("transform"),
              "translate(0 1080) scale(1 4) translate(0 -270)",
            );
            """;

        await RunNodeAsync(harness);
    }

    private static async Task RunNodeAsync(string harness)
    {
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
                    fileName.EndsWith(".razor", StringComparison.Ordinal) ? "Components"
                        : fileName.EndsWith(".razor.js", StringComparison.Ordinal) ? "Components"
                        : fileName.EndsWith(".css", StringComparison.Ordinal)
                            ? Path.Combine("Styles", "components")
                        : "Features",
                    fileName.EndsWith(".cs", StringComparison.Ordinal)
                        ? Path.Combine("Overlays", fileName)
                        : fileName
                )
            )
        );

    private sealed class TestBrowserFile(string name, long size, string contentType) : IBrowserFile
    {
        public string Name { get; } = name;
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UnixEpoch;
        public long Size { get; } = size;
        public string ContentType { get; } = contentType;

        public Stream OpenReadStream(
            long maxAllowedSize = 512000,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class NullJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult(default(TValue)!);
    }
}
