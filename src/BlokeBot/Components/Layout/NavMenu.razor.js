export function readBoolean(key, fallback) {
    const value = localStorage.getItem(key);
    return value === null ? fallback : value === "true";
}

export function writeBoolean(key, value) {
    localStorage.setItem(key, value ? "true" : "false");
}
