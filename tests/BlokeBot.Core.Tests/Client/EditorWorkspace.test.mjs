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

test('native Promise refusal retains actual enter/exit state and both operations can be retried', async () => {
    globalThis.document = browserDocument();
    const state = owner(), workspace = createWorkspace(state.root, state.dotnet);
    const request = document.documentElement.requestFullscreen;
    document.documentElement.requestFullscreen = async () => { throw new TypeError('Denied by browser'); };
    assert.equal(await workspace.toggleFullscreen(), 1);
    assert.deepEqual(state.changes, [false]);
    assert.equal(document.fullscreenElement, null);
    document.documentElement.requestFullscreen = request;
    await workspace.toggleFullscreen();
    assert.deepEqual(state.changes, [false, true]);
    const exit = document.exitFullscreen;
    document.exitFullscreen = () => Promise.reject(new TypeError('Native exit refused'));
    assert.equal(await workspace.toggleFullscreen(), 2);
    assert.equal(document.fullscreenElement, document.documentElement);
    assert.deepEqual(state.changes, [false, true]);
    document.exitFullscreen = exit;
    assert.equal(await workspace.toggleFullscreen(), 0);
    assert.equal(document.fullscreenElement, null);
    assert.deepEqual(state.changes, [false, true, false]);
    workspace.dispose();
});

test('unexpected native faults and synchronous API invocation faults remain exceptional', async () => {
    globalThis.document = browserDocument();
    const state = owner(), workspace = createWorkspace(state.root, state.dotnet);
    document.documentElement.requestFullscreen = () => Promise.reject(new Error('Unexpected failure'));
    await assert.rejects(workspace.toggleFullscreen(), Error);
    document.documentElement.requestFullscreen = () => { throw new TypeError('Broken invocation'); };
    await assert.rejects(workspace.toggleFullscreen(), TypeError);
    assert.deepEqual(state.changes, [false]);
    workspace.dispose();
});
