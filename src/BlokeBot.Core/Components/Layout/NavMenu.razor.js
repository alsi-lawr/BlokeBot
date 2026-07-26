const preferenceResetStorageKey = "blokebot.sidebar.chat-tools.version";
const preferenceResetVersion = "2026-07-dashboard-foundation";
const chatToolsPreferenceKeys = [
    "blokebot.sidebar.guessing.open",
    "blokebot.sidebar.points.open",
    "blokebot.sidebar.customcommands.open",
    "blokebot.sidebar.native-twitch.open",
];

const attributesByKey = new Map([
    ["blokebot.sidebar.guessing.open", "navGuessingOpen"],
    ["blokebot.sidebar.points.open", "navPointsOpen"],
    ["blokebot.sidebar.customcommands.open", "navCustomCommandsOpen"],
    ["blokebot.sidebar.native-twitch.open", "navNativeTwitchOpen"],
]);

function applyDocumentState(key, value) {
    const attribute = attributesByKey.get(key);
    if (attribute)
        document.documentElement.dataset[attribute] = value ? "true" : "false";
}

function resetChatToolsPreferences() {
    if (localStorage.getItem(preferenceResetStorageKey) === preferenceResetVersion)
        return;

    for (const key of chatToolsPreferenceKeys)
        localStorage.setItem(key, "false");

    localStorage.setItem(preferenceResetStorageKey, preferenceResetVersion);
}

export function readBoolean(key) {
    resetChatToolsPreferences();
    const value = localStorage.getItem(key);
    const result = value === "true";
    applyDocumentState(key, result);
    return result;
}

export function writeBoolean(key, value) {
    localStorage.setItem(key, value ? "true" : "false");
    applyDocumentState(key, value);
}
