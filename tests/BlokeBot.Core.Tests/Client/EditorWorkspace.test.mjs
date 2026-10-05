import test from 'node:test';
import assert from 'node:assert/strict';
import { createWorkspace } from '../../../src/BlokeBot.Core/Components/EditorWorkspace.razor.js';

function browserDocument() {
    const document = new EventTarget();
    document.fullscreenElement = null;
    document.documentElement = { async requestFullscreen() { document.fullscreenElement = this; document.dispatchEvent(new Event('fullscreenchange')); } };
    document.exitFullscreen = async () => { document.fullscreenElement = null; document.dispatchEvent(new Event('fullscreenchange')); };
    return document;
}

function owner() {
    const root = new EventTarget();
    root.querySelectorAll = () => [];
    const changes = [];
    return { root, changes, dotnet: { invokeMethodAsync(method, active) { changes.push(active); return Promise.resolve(); } } };
}

test('incoming workspace observes actual fullscreen and outgoing disposal cannot remove its observer', async () => {
    globalThis.document = browserDocument();
    const outgoing = owner(), incoming = owner();
    const first = createWorkspace(outgoing.root, outgoing.dotnet);
    assert.deepEqual(outgoing.changes, [false]);
    await first.toggleFullscreen();
    const second = createWorkspace(incoming.root, incoming.dotnet);
    assert.deepEqual(incoming.changes, [true]);
    first.dispose();
    await document.exitFullscreen();
    assert.deepEqual(outgoing.changes, [false, true]);
    assert.deepEqual(incoming.changes, [true, false]);
    await second.toggleFullscreen();
    assert.equal(document.fullscreenElement, document.documentElement);
    second.dispose();
    await document.exitFullscreen();
    assert.deepEqual(incoming.changes, [true, false, true]);
});

test('denied fullscreen request rejects without inventing an active state and can be retried', async () => {
    globalThis.document = browserDocument();
    const state = owner(), workspace = createWorkspace(state.root, state.dotnet);
    const request = document.documentElement.requestFullscreen;
    document.documentElement.requestFullscreen = async () => { throw new TypeError('Denied by browser'); };
    await assert.rejects(workspace.toggleFullscreen(), TypeError);
    assert.deepEqual(state.changes, [false]);
    assert.equal(document.fullscreenElement, null);
    document.documentElement.requestFullscreen = request;
    await workspace.toggleFullscreen();
    assert.deepEqual(state.changes, [false, true]);
    workspace.dispose();
});
