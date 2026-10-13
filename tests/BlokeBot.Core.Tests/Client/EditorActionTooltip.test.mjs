import test from 'node:test';
import assert from 'node:assert/strict';
import { bindEditorActionTooltips } from '../../../src/BlokeBot.Core/Components/EditorAction.razor.js';

function browser() {
    const observers = [];
    class Observer {
        constructor(callback) { this.callback = callback; observers.push(this); }
        observe() { this.connected = true; }
        disconnect() { this.connected = false; }
    }
    const window = Object.assign(new EventTarget(), {
        innerHeight: 600, MutationObserver: Observer, ResizeObserver: Observer,
        visualViewport: Object.assign(new EventTarget(), { width: 800, height: 600, offsetLeft: 0, offsetTop: 0 })
    });
    const document = Object.assign(new EventTarget(), { defaultView: window, documentElement: { clientWidth: 800 } });
    function action() {
        const tooltip = {
            isConnected: true, open: false, style: {},
            matches() { return this.open; },
            showPopover() { this.open = true; },
            hidePopover() { this.open = false; },
            getBoundingClientRect() { return { width: 160, height: 35 }; }
        };
        const owner = {
            isConnected: true, hovered: false, focused: false, dataset: {},
            closest() { return this; },
            querySelector() { return tooltip; },
            contains(target) { return target === this; },
            matches() { return this.hovered || this.focused; },
            removeAttribute() { delete this.dataset.tipDismissed; },
            getClientRects() { return [this.getBoundingClientRect()]; },
            getBoundingClientRect() { return { right: 795, top: 570, bottom: 595 }; }
        };
        return { owner, tooltip };
    }
    function dispatch(type, owner, relatedTarget = null, key = null) {
        const event = new Event(type);
        Object.defineProperties(event, {
            target: { value: owner }, relatedTarget: { value: relatedTarget }, key: { value: key }
        });
        document.dispatchEvent(event);
    }
    return { document, window, observers, action, dispatch };
}

test('Escape stays dismissed until a new discovery and hover/focus departure releases active listeners', () => {
    const b = browser(), action = b.action(), binding = bindEditorActionTooltips(b.document);
    action.owner.hovered = true;
    b.dispatch('pointerenter', action.owner);
    action.owner.focused = true;
    b.dispatch('focusin', action.owner);
    b.dispatch('keydown', action.owner, null, 'Escape');
    assert.equal(action.tooltip.open, false);
    assert.ok(b.observers.every(observer => !observer.connected));
    b.dispatch('pointerenter', { closest: () => action.owner }, action.owner);
    assert.equal(action.tooltip.open, false);
    action.owner.focused = false;
    b.dispatch('focusout', action.owner);
    action.owner.focused = true;
    b.dispatch('focusin', action.owner);
    assert.equal(action.tooltip.open, true);
    action.owner.hovered = false;
    b.dispatch('pointerleave', action.owner);
    assert.equal(action.tooltip.open, true);
    action.owner.focused = false;
    b.dispatch('focusout', action.owner);
    assert.equal(action.tooltip.open, false);
    assert.ok(b.observers.every(observer => !observer.connected));
    binding.dispose();
});

test('outgoing owner removal cannot dismiss its replacement and viewport changes release the current tooltip', () => {
    const b = browser(), outgoing = b.action(), incoming = b.action();
    const binding = bindEditorActionTooltips(b.document);
    b.dispatch('focusin', outgoing.owner);
    const outgoingObserver = b.observers[0];
    b.dispatch('focusin', incoming.owner);
    assert.equal(outgoing.tooltip.open, false);
    outgoing.owner.isConnected = false;
    outgoingObserver.callback();
    assert.equal(incoming.tooltip.open, true);
    incoming.owner.isConnected = false;
    b.observers.at(-2).callback();
    assert.equal(incoming.tooltip.open, false);
    assert.ok(b.observers.every(observer => !observer.connected));
    incoming.owner.isConnected = true;
    for (const [target, event] of [[b.document, 'scroll'], [b.window, 'resize'], [b.window.visualViewport, 'scroll']]) {
        b.dispatch('focusin', incoming.owner);
        assert.equal(incoming.tooltip.open, true);
        target.dispatchEvent(new Event(event));
        assert.equal(incoming.tooltip.open, false);
        assert.ok(b.observers.every(observer => !observer.connected));
    }
    binding.dispose();
    b.dispatch('focusin', incoming.owner);
    assert.equal(incoming.tooltip.open, false);
});
