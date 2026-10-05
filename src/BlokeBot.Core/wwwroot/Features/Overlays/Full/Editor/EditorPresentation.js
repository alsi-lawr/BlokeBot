export function createPresentation(root, focusEditor, cancelGesture) {
    const abort = new AbortController(), options = {signal:abort.signal};
    const modeTabs = root.querySelector('[data-editor-tabs=mode]');
    const sourceTabs = root.querySelector('[data-editor-tabs=source]');
    let mode = 'visual', source = 'html', pane = 'preview', disposed = false;

    function tabs(strip, selected) {
        const buttons = [...strip.querySelectorAll('[data-tab-key]')];
        strip.style.setProperty('--segmented-active-index', buttons.findIndex(button=>button.dataset.tabKey===selected));
        for (const button of buttons) {
            const active = button.dataset.tabKey === selected;
            button.classList.toggle('segmented-motion__tab--active', active);
            button.setAttribute('aria-selected', String(active));
            button.tabIndex = active ? 0 : -1;
        }
    }
    function render() {
        root.dataset.mode = mode;
        root.dataset.pane = pane;
        for (const region of root.querySelectorAll('[data-visual-region]')) region.hidden = mode !== 'visual';
        root.querySelector('[data-source-region]').hidden = mode !== 'source';
        for (const region of root.querySelectorAll('[data-source-pane]')) region.hidden = region.dataset.sourcePane !== source;
        for (const button of root.querySelectorAll('[data-editor-pane]')) button.setAttribute('aria-pressed', String(button.dataset.editorPane===pane));
        tabs(modeTabs, mode);
        tabs(sourceTabs, source);
    }
    function select(strip, key, moveFocus) {
        if (disposed || (strip === modeTabs ? key === mode : key === source)) return;
        if (strip === modeTabs) { cancelGesture(); mode = key; pane = 'preview'; }
        else source = key;
        render();
        focusEditor(mode === 'visual' ? 'visual' : source, moveFocus);
    }
    function tab(event) {
        const button = event.target.closest('[data-tab-key]');
        const strip = button?.closest('[data-editor-tabs]');
        return strip === modeTabs || strip === sourceTabs ? {button, strip} : null;
    }
    root.addEventListener('click', event=>{
        const target = tab(event);
        if (target) select(target.strip, target.button.dataset.tabKey, true);
        else {
            const button = event.target.closest('[data-editor-pane]');
            if (button) { pane = button.dataset.editorPane; render(); }
        }
    }, options);
    root.addEventListener('keydown', event=>{
        const target = tab(event);
        if (!target) return;
        const buttons = [...target.strip.querySelectorAll('[data-tab-key]')], index = buttons.indexOf(target.button);
        let next;
        switch (event.key) {
            case 'ArrowLeft': case 'ArrowUp': next = buttons[(index-1+buttons.length)%buttons.length]; break;
            case 'ArrowRight': case 'ArrowDown': next = buttons[(index+1)%buttons.length]; break;
            case 'Home': next = buttons[0]; break;
            case 'End': next = buttons.at(-1); break;
            default: return;
        }
        event.preventDefault();
        event.stopPropagation();
        select(target.strip, next.dataset.tabKey, false);
        next.focus({preventScroll:true});
    }, options);
    return {
        revealHtml() {
            if (disposed) return;
            cancelGesture(); mode = 'source'; source = 'html'; pane = 'preview';
            render(); focusEditor('html');
        },
        dispose() { disposed = true; abort.abort(); }
    };
}
