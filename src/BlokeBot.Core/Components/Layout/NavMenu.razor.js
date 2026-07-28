const attributesByKey = new Map([
    ["blokebot.sidebar.guessing.open", "navGuessingOpen"],
    ["blokebot.sidebar.points.open", "navPointsOpen"],
    ["blokebot.sidebar.customcommands.open", "navCustomCommandsOpen"],
]);
const routeHelpCleanups = new Map();

function applyDocumentState(key, value) {
    const attribute = attributesByKey.get(key);
    if (attribute)
        document.documentElement.dataset[attribute] = value ? "true" : "false";
}

export function readBoolean(key, fallback) {
    try {
        const value = window.localStorage.getItem(key);
        const result = value === null ? fallback : value === "true";
        applyDocumentState(key, result);
        return result;
    } catch {
        return fallback;
    }
}

export function writeBoolean(key, value) {
    try {
        window.localStorage.setItem(key, value ? "true" : "false");
    } catch {
    }

    applyDocumentState(key, value);
}

export function activateRouteHelp(rootId) {
    deactivateRouteHelp(rootId);
    const root = document.getElementById(rootId);
    if (!root)
        return;

    const clearDismissal = event => {
        event.target.closest(".nav-menu__route-item, .nav-menu__group")
            ?.removeAttribute("data-route-help-dismissed");
    };
    const dismiss = event => {
        if (event.key !== "Escape")
            return;

        for (const item of root.querySelectorAll(".nav-menu__route-item:hover, .nav-menu__route-item:focus-within, .nav-menu__group:hover, .nav-menu__group:focus-within"))
            item.dataset.routeHelpDismissed = "true";
    };

    root.addEventListener("pointerenter", clearDismissal, true);
    root.addEventListener("focusin", clearDismissal);
    document.addEventListener("keydown", dismiss);
    routeHelpCleanups.set(rootId, () => {
        root.removeEventListener("pointerenter", clearDismissal, true);
        root.removeEventListener("focusin", clearDismissal);
        document.removeEventListener("keydown", dismiss);
    });
}

export function deactivateRouteHelp(rootId) {
    routeHelpCleanups.get(rootId)?.();
    routeHelpCleanups.delete(rootId);
}
