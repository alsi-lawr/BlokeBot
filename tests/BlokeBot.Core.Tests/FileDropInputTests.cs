using System.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class FileDropInputTests
{
    [Test]
    public async Task ProductionModule_SharesDocumentListenersAndDispatchesEachBrowseOrDropOnce()
    {
        var module = Source("FileDropInput.razor.js");
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
                    fileName switch
                    {
                        _ when fileName.EndsWith(".razor", StringComparison.Ordinal) =>
                            "Components",
                        _ when fileName.EndsWith(".razor.js", StringComparison.Ordinal) =>
                            "Components",
                        _ when fileName.EndsWith(".css", StringComparison.Ordinal) => Path.Combine(
                            "Styles",
                            "components"
                        ),
                        _ => "Features",
                    },
                    fileName.EndsWith(".cs", StringComparison.Ordinal)
                        ? Path.Combine("Overlays", fileName)
                        : fileName
                )
            )
        );
}
