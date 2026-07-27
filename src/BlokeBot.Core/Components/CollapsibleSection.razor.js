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
    return localStorage.getItem(key) === "true";
}

export function writeBoolean(key, value) {
    resetPreferences();
    localStorage.setItem(key, value ? "true" : "false");
}
