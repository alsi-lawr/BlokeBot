using System.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationEditorKeyboardTests
{
    [Test]
    public async Task HistoryShortcuts_PreserveEditableHistoryAndStayInsideTheEditor()
    {
        var source = string.Join(
            "\n",
            "import assert from \"node:assert/strict\";",
            _dom,
            Source(),
            _scenario
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);

        process.ExitCode.ShouldBe(0, $"{await output}\n{await error}");
    }

    private static string Source() =>
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
                    "AutomationEditorPage.razor.js"
                )
            )
        );

    private const string _dom = """
class FakeElement {
  constructor(tag, parent = null, options = {}) {
    this.tagName = tag.toLowerCase();
    this.parentElement = parent;
    this.isContentEditable = options.isContentEditable ?? false;
    this.historyRoot = options.historyRoot ?? false;
  }

  closest(selector) {
    for (let current = this; current !== null; current = current.parentElement) {
      if (selector === "input, textarea, select"
        && ["input", "textarea", "select"].includes(current.tagName)) return current;
      if (selector === "[data-automation-editor-history]" && current.historyRoot) return current;
    }
    return null;
  }
}

globalThis.Element = FakeElement;
const listeners = new Map();
globalThis.document = {
  addEventListener(name, listener) {
    const registered = listeners.get(name) ?? [];
    registered.push(listener);
    listeners.set(name, registered);
  },
  removeEventListener(name, listener) {
    const registered = listeners.get(name) ?? [];
    const index = registered.indexOf(listener);
    if (index >= 0) registered.splice(index, 1);
  },
};

function keydown(target, key, modifiers = {}) {
  let prevented = 0;
  const event = {
    target,
    key,
    ctrlKey: true,
    altKey: false,
    metaKey: false,
    shiftKey: false,
    preventDefault() { prevented += 1; },
    ...modifiers,
  };
  for (const listener of listeners.get("keydown") ?? []) listener(event);
  return prevented;
}
""";

    private const string _scenario = """
const calls = [];
const dotnet = {
  invokeMethodAsync(method, action) {
    calls.push({ method, action });
    return Promise.resolve();
  },
};
const editor = new FakeElement("section", null, { historyRoot: true });
const background = new FakeElement("span", editor);
const outside = new FakeElement("span");
const editableTargets = [
  new FakeElement("input", editor),
  new FakeElement("textarea", editor),
  new FakeElement("select", editor),
  new FakeElement("span", editor, { isContentEditable: true }),
];

initializeHistoryKeyboard(dotnet);
for (const target of editableTargets) {
  assert.equal(keydown(target, "z"), 0, "native undo remains available in editable controls");
  assert.equal(keydown(target, "y"), 0, "native redo remains available in editable controls");
}
assert.equal(calls.length, 0, "editable shortcuts are not forwarded");

assert.equal(keydown(background, "z"), 1, "editor undo is intercepted");
assert.equal(keydown(background, "Y"), 1, "editor redo is intercepted");
assert.deepEqual(calls, [
  { method: "ApplyEditorHistoryShortcutAsync", action: "undo" },
  { method: "ApplyEditorHistoryShortcutAsync", action: "redo" },
]);

assert.equal(keydown(outside, "z"), 0, "outside targets are ignored");
assert.equal(keydown(background, "z", { shiftKey: true }), 0, "modified shortcuts are ignored");
assert.equal(keydown(background, "x"), 0, "unrelated keys are ignored");
assert.equal(calls.length, 2, "ignored keys are not forwarded");

disposeHistoryKeyboard();
assert.equal(keydown(background, "z"), 0, "disposal removes the listener");
assert.equal(calls.length, 2);

initializeToolboxKeyboard(dotnet);
assert.equal(keydown(background, "/", { ctrlKey: false }), 1, "slash is intercepted outside an editor field");
assert.deepEqual(calls.at(-1), {
  method: "OpenToolboxFromShortcutAsync",
  action: undefined,
});
for (const target of editableTargets) {
  assert.equal(keydown(target, "/", { ctrlKey: false }), 0, "slash remains available in editable controls");
}
assert.equal(keydown(background, "/", { altKey: true, ctrlKey: false }), 0, "modified slash is ignored");
assert.equal(calls.length, 3);
disposeToolboxKeyboard();
assert.equal(keydown(background, "/", { ctrlKey: false }), 0, "toolbox shortcut disposal removes the listener");
""";
}
