const preferenceVersionKey = "blokebot.rail.version";
const preferenceVersion = "2026-07-compact-signed-in-shell";
const railCollapsedKey = "blokebot.rail.collapsed";

function resetPreferences() {
    if (localStorage.getItem(preferenceVersionKey) === preferenceVersion)
        return;

    localStorage.setItem(railCollapsedKey, "false");
    localStorage.setItem(preferenceVersionKey, preferenceVersion);
}

export function readRailCollapsed() {
    resetPreferences();
    return localStorage.getItem(railCollapsedKey) === "true";
}

export function writeRailCollapsed(collapsed) {
    resetPreferences();
    localStorage.setItem(railCollapsedKey, collapsed ? "true" : "false");
}
