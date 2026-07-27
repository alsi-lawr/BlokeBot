const preferenceVersion = "2026-07-compact-signed-in-shell";
const versionKey = "blokebot.disclosure.version";

function resetPreferences() {
    if (localStorage.getItem(versionKey) === preferenceVersion)
        return;

    for (const key of Object.keys(localStorage)) {
        if (key.startsWith("blokebot.disclosure."))
            localStorage.removeItem(key);
    }

    localStorage.setItem(versionKey, preferenceVersion);
}

export function readBoolean(key) {
    resetPreferences();
    const value = localStorage.getItem(key);
    return value === null ? null : value === "true";
}

export function writeBoolean(key, value) {
    resetPreferences();
    localStorage.setItem(key, value ? "true" : "false");
}

export function readString(key) {
    return window.localStorage.getItem(key);
}

export function writeString(key, value) {
    window.localStorage.setItem(key, value);
}

export function focusElement(id) {
    document.getElementById(id)?.focus();
}
