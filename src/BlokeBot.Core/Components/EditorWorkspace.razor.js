export function createWorkspace(root, dotnet) {
    const abort = new AbortController(), options = { signal: abort.signal };
    const change = () => { void dotnet.invokeMethodAsync('BrowserFullscreenChangedAsync', document.fullscreenElement !== null); };
    document.addEventListener('fullscreenchange', change, options);
    const reveal = event => event.target.closest('.editor-action')?.removeAttribute('data-tip-dismissed');
    root.addEventListener('pointerenter', reveal, { ...options, capture: true });
    root.addEventListener('focusin', reveal, options);
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        for (const action of root.querySelectorAll('.editor-action:hover, .editor-action:focus-within')) action.dataset.tipDismissed = 'true';
    }, options);
    change();
    return {
        async toggleFullscreen() {
            if (document.fullscreenElement) await document.exitFullscreen();
            else await document.documentElement.requestFullscreen();
        },
        dispose() { abort.abort(); }
    };
}
