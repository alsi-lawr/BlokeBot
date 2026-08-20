let dirtyNavigation = null;
let fullscreenState = null;
let historyKeyboard = null;
let toolboxKeyboard = null;

function fullNavigationTarget(event) {
    if (
        event.defaultPrevented
        || event.button !== 0
        || event.altKey
        || event.ctrlKey
        || event.metaKey
        || event.shiftKey
    ) {
        return null;
    }

    const anchor = event.target instanceof Element ? event.target.closest("a[href]") : null;
    if (!(anchor instanceof HTMLAnchorElement)) return null;
    if (anchor.dataset.enhanceNav !== "false" || anchor.hasAttribute("download")) return null;

    const target = anchor.getAttribute("target")?.trim().toLowerCase();
    if (target && target !== "_self") return null;

    const url = new URL(anchor.href, window.location.href);
    return url.origin === window.location.origin && ["http:", "https:"].includes(url.protocol)
        ? url.href
        : null;
}

export function initializeDirtyNavigation(dotnet, dirty) {
    disposeDirtyNavigation();
    const click = (event) => {
        if (!dirtyNavigation?.dirty) return;
        const target = fullNavigationTarget(event);
        if (target === null) return;

        event.preventDefault();
        event.stopImmediatePropagation();
        void dirtyNavigation.dotnet.invokeMethodAsync("RequestFullNavigationAsync", target);
    };
    dirtyNavigation = { click, dirty: Boolean(dirty), dotnet };
    document.addEventListener("click", click, true);
}

export function setDirtyNavigation(dirty) {
    if (dirtyNavigation !== null) dirtyNavigation.dirty = Boolean(dirty);
}

export function disposeDirtyNavigation() {
    if (dirtyNavigation === null) return;
    document.removeEventListener("click", dirtyNavigation.click, true);
    dirtyNavigation = null;
}

export function navigateDocument(target) {
    requestAnimationFrame(() => window.location.assign(target));
}

export function initializeFullscreen(dotnet) {
    disposeFullscreen();
    const change = () => {
        void dotnet.invokeMethodAsync(
            "BrowserFullscreenChangedAsync",
            document.fullscreenElement !== null,
        );
    };
    fullscreenState = { change };
    document.addEventListener("fullscreenchange", change);
    change();
}

export function disposeFullscreen() {
    if (fullscreenState === null) return;
    document.removeEventListener("fullscreenchange", fullscreenState.change);
    fullscreenState = null;
}

function isEditable(target) {
    return (
        target instanceof Element
        && (target.isContentEditable || target.closest("input, textarea, select") !== null)
    );
}

function historyAction(event) {
    if (
        !event.ctrlKey
        || event.altKey
        || event.metaKey
        || event.shiftKey
        || !(event.target instanceof Element)
        || isEditable(event.target)
        || event.target.closest("[data-automation-editor-history]") === null
    ) {
        return null;
    }

    const key = event.key.toLowerCase();
    if (key === "z") return "undo";
    if (key === "y") return "redo";
    return null;
}

export function initializeHistoryKeyboard(dotnet) {
    disposeHistoryKeyboard();
    const keydown = (event) => {
        const action = historyAction(event);
        if (action === null) return;

        event.preventDefault();
        void dotnet.invokeMethodAsync("ApplyEditorHistoryShortcutAsync", action);
    };
    historyKeyboard = { keydown, dotnet };
    document.addEventListener("keydown", keydown, true);
}

export function disposeHistoryKeyboard() {
    if (historyKeyboard === null) return;
    document.removeEventListener("keydown", historyKeyboard.keydown, true);
    historyKeyboard = null;
}

export function initializeToolboxKeyboard(dotnet) {
    disposeToolboxKeyboard();
    const keydown = (event) => {
        if (
            event.key !== "/"
            || event.altKey
            || event.ctrlKey
            || event.metaKey
            || !(event.target instanceof Element)
            || isEditable(event.target)
        ) {
            return;
        }

        event.preventDefault();
        void dotnet.invokeMethodAsync("OpenToolboxFromShortcutAsync");
    };
    toolboxKeyboard = { keydown, dotnet };
    document.addEventListener("keydown", keydown, true);
}

export function disposeToolboxKeyboard() {
    if (toolboxKeyboard === null) return;
    document.removeEventListener("keydown", toolboxKeyboard.keydown, true);
    toolboxKeyboard = null;
}

export async function toggleBrowserFullscreen() {
    if (document.fullscreenElement) {
        await document.exitFullscreen();
        return;
    }

    await document.documentElement.requestFullscreen();
}

export function focusInspector() {
    document.querySelector("[data-automation-inspector]")?.focus({ preventScroll: true });
}
