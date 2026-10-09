export function isEditable(target) {
    return target instanceof Element && (target.isContentEditable || target.closest('input,textarea,select,[contenteditable]:not([contenteditable=false])') !== null);
}

export function createEditorMenu(root, owner) {
    const opener = root.querySelector('[data-editor-menu-open]');
    const popup = root.querySelector('[data-editor-menu]');
    const buttons = [...popup.querySelectorAll('[role=menuitem]')];
    const abort = new AbortController(), options = { signal: abort.signal };
    let invocation = null, origin = null, anchor = null, generation = 0, opening = false, disposed = false;
    const visible = node => node?.isConnected && node.getClientRects().length > 0 && !node.closest('[hidden],[inert]');
    const fallback = () => owner.fallback?.() ?? opener;

    function restore() {
        const target = visible(origin) ? origin : fallback();
        if (visible(target)) target.focus({ preventScroll: true });
    }
    function close(returnFocus = true) {
        generation++;
        opening = false;
        invocation = null;
        popup.hidden = true;
        opener.setAttribute('aria-expanded', 'false');
        if (returnFocus) restore();
    }
    function position() {
        const bounds = popup.getBoundingClientRect();
        popup.style.left = `${Math.max(8, Math.min(anchor.x, window.innerWidth - bounds.width - 8))}px`;
        popup.style.top = `${Math.max(8, Math.min(anchor.y, window.innerHeight - bounds.height - 8))}px`;
    }
    function refresh() {
        if (!invocation && !opening) return;
        if (!root.isConnected) close(false);
        else if (invocation && !owner.current(invocation)) close();
        else if (invocation) position();
    }
    async function open(target, point) {
        close(false);
        owner.beforeOpen?.();
        const request = ++generation;
        origin = target.closest?.('[role=treeitem],[data-automation-node-select],[data-automation-edge],[data-canvas-area],[data-automation-canvas]') ?? target;
        if (!origin?.hasAttribute('tabindex') && !(origin instanceof HTMLButtonElement)) origin = fallback();
        anchor = point ?? (() => { const r = origin.getBoundingClientRect(); return { x: r.left, y: r.bottom }; })();
        opening = true;
        const prepared = await owner.prepare(target, point);
        if (disposed || request !== generation) return;
        opening = false;
        if (!root.isConnected || !prepared || !owner.current(prepared)) return;
        invocation = prepared;
        popup.querySelector('[data-editor-menu-target]').textContent = prepared.target;
        popup.querySelector('[data-editor-menu-history]').textContent = prepared.history;
        popup.querySelector('[data-editor-menu-note]').textContent = prepared.note;
        for (const button of buttons) button.setAttribute('aria-disabled', String(!prepared[button.dataset.editorMenuAction]));
        popup.hidden = false;
        opener.setAttribute('aria-expanded', 'true');
        position();
        buttons[0].focus({ preventScroll: true });
    }
    async function activate(button) {
        const captured = invocation;
        if (!captured || button.getAttribute('aria-disabled') === 'true') return;
        if (!owner.current(captured)) { close(); return; }
        const focused = window.document.activeElement;
        close(false);
        await owner.execute(button.dataset.editorMenuAction, captured);
        if (!disposed && popup.hidden && (window.document.activeElement === focused || window.document.activeElement === window.document.body)) restore();
    }
    root.addEventListener('pointerdown', event => {
        if (event.target.closest('[data-editor-menu-open]')) owner.beforeOpen?.();
    }, { capture: true, ...options });
    opener.addEventListener('click', () => { void open(opener); }, options);
    root.addEventListener('contextmenu', event => {
        if (isEditable(event.target) || !owner.accepts(event.target)) return;
        event.preventDefault();
        event.stopPropagation();
        void open(event.target, { x: event.clientX, y: event.clientY });
    }, options);
    root.addEventListener('keydown', event => {
        if (popup.contains(event.target) && invocation) {
            event.stopImmediatePropagation();
            if (event.key === 'Tab') { close(); return; }
            event.preventDefault();
            const index = buttons.indexOf(window.document.activeElement);
            if (event.key === 'Escape') close();
            else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') buttons[(index + (event.key === 'ArrowDown' ? 1 : buttons.length - 1)) % buttons.length].focus();
            else if (event.key === 'Home') buttons[0].focus();
            else if (event.key === 'End') buttons.at(-1).focus();
            else if (event.key === 'Enter' || event.key === ' ') void activate(buttons[index]);
            return;
        }
        if ((event.key === 'ContextMenu' || event.key === 'F10' && event.shiftKey) && !isEditable(event.target) && (event.target === opener || owner.accepts(event.target))) {
            event.preventDefault();
            event.stopImmediatePropagation();
            void open(event.target);
        }
    }, { capture: true, ...options });
    popup.addEventListener('click', event => {
        const button = event.target.closest('[data-editor-menu-action]');
        if (button) { event.stopPropagation(); void activate(button); }
    }, options);
    window.document.addEventListener('pointerdown', event => {
        if ((invocation || opening) && !popup.contains(event.target) && !opener.contains(event.target)) close(false);
    }, { capture: true, ...options });
    window.document.addEventListener('focusin', event => {
        if ((invocation || opening) && !popup.contains(event.target)) close(false);
    }, options);
    window.document.addEventListener('keydown', event => {
        if (opening && (event.key === 'Escape' || event.key === 'Tab')) close(false);
    }, { capture: true, ...options });
    window.addEventListener('resize', refresh, options);
    window.document.addEventListener('fullscreenchange', refresh, options);
    return { refresh, dispose() { disposed = true; close(false); abort.abort(); } };
}
