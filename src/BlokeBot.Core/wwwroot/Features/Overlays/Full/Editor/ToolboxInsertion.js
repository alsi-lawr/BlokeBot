// Presentation acknowledgement only: the existing document owner performs the command.
export function insertedLayer(before, after) {
    return after.revision > before.revision && after.selected
        && after.layers.some(layer => layer.key === after.selected)
        && !before.layers.some(layer => layer.key === after.selected) ? after.selected : null;
}
