const railPreferenceKey = "blokebot.shell.rail.v1";
const preferenceVersion = 1;

function readPreference() {
    try {
        const stored = window.localStorage.getItem(railPreferenceKey);
        if (stored === null)
            return null;

        const value = JSON.parse(stored);
        if (
            typeof value !== "object" ||
            value === null ||
            value.version !== preferenceVersion ||
            (value.presentation !== "expanded" && value.presentation !== "icon")
        ) {
            window.localStorage.removeItem(railPreferenceKey);
            return null;
        }

        return value.presentation;
    } catch {
        return null;
    }
}

export function readRailPresentation() {
    return readPreference() === "icon";
}

export function writeRailPresentation(iconRail) {
    try {
        if (window.localStorage.getItem("blokebot.preferences.disabled") === "true")
            return;

        window.localStorage.setItem(
            railPreferenceKey,
            JSON.stringify({
                version: preferenceVersion,
                presentation: iconRail ? "icon" : "expanded",
            }),
        );
    } catch {
    }
}
