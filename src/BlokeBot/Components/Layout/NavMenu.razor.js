const attributesByKey = new Map([
    ["blokebot.sidebar.guessing.open", "navGuessingOpen"],
    ["blokebot.sidebar.points.open", "navPointsOpen"],
]);

function applyDocumentState(key, value) {
    const attribute = attributesByKey.get(key);
    if (attribute)
        document.documentElement.dataset[attribute] = value ? "true" : "false";
}

export function readBoolean(key, fallback) {
    const value = localStorage.getItem(key);
    const result = value === null ? fallback : value === "true";
    applyDocumentState(key, result);
    return result;
}

export function writeBoolean(key, value) {
    localStorage.setItem(key, value ? "true" : "false");
    applyDocumentState(key, value);
}
