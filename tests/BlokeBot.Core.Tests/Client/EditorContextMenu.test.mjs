import { test } from "node:test";
import assert from "node:assert/strict";
import { createEditorMenu } from "../../../src/BlokeBot.Core/wwwroot/Components/EditorContextMenu.js";

test("shared menu clamps to usable client viewport, excluding classic scrollbars", async () => {
  class Node {
    listeners = new Map();
    style = {};
    dataset = {};
    isConnected = true;
    addEventListener(type, callback) { this.listeners.set(type, callback); }
    setAttribute() {}
    hasAttribute() { return false; }
    closest() { return null; }
    getClientRects() { return [{}]; }
    getBoundingClientRect() { return { left: 380, bottom: 850, width: 248, height: 250 }; }
    focus() {}
  }
  class Button extends Node {}
  const opener = new Button(), popup = new Node(), root = new Node();
  const buttons = ["undo", "redo", "delete"].map(action => {
    const button = new Button();
    button.dataset.editorMenuAction = action;
    return button;
  });
  const copy = new Node();
  popup.querySelectorAll = () => buttons;
  popup.querySelector = () => copy;
  root.querySelector = selector => selector === "[data-editor-menu-open]" ? opener : popup;
  const document = new Node(), window = new Node();
  document.documentElement = { clientWidth: 375, clientHeight: 844 };
  window.document = document;
  // These larger dimensions include scrollbar tracks and must not be used for placement.
  window.innerWidth = 390;
  window.innerHeight = 860;
  Object.assign(globalThis, { window, document, HTMLButtonElement: Button });
  const menu = createEditorMenu(root, {
    prepare: async () => ({ target: "Widget", history: "History", note: "", undo: true, redo: false, delete: true }),
    current: () => true,
  });
  try {
    opener.listeners.get("click")();
    await Promise.resolve();
    await Promise.resolve();
    assert.equal(popup.hidden, false);
    assert.equal(popup.style.left, "119px");
    assert.equal(popup.style.top, "586px");
    document.documentElement.clientWidth = 320;
    menu.refresh();
    assert.equal(popup.style.left, "64px");
  } finally {
    menu.dispose();
  }
});
