function cell(value) {
    return value && typeof value === 'object' && !Array.isArray(value)
        ? { children: new Map(Object.entries(value).map(([key, child]) => [key, cell(child)])) }
        : { value: structuredClone(value) };
}
function value(node) {
    if (!node) return undefined;
    return node.children ? Object.fromEntries([...node.children].map(([key, child]) => [key, value(child)])) : structuredClone(node.value);
}
function find(root, path) {
    let node = root;
    for (const key of path) node = node?.children?.get(key);
    return node;
}
function replace(root, path, next) {
    if (!path.length) return next;
    const [key, ...rest] = path;
    const children = new Map(root.children);
    const child = replace(children.get(key), rest, next);
    if (child) children.set(key, child); else children.delete(key);
    return { children };
}

export function createMetadata(widgets) {
    let root = cell({ order: widgets.map(widget => widget.id.value),
        widgets: Object.fromEntries(widgets.map(widget => [widget.id.value, widget])) });
    function prepare(changes) {
        const patches = [];
        for (const change of changes) {
            if (!Array.isArray(change.path) || !change.path.length || change.path.some(key => typeof key !== 'string')
                || !find(root, change.path.slice(0, -1))?.children)
                return { kind: 'invalid', code: 'invalid-metadata-path' };
            if (patches.some(patch => [patch.path, change.path].every(path =>
                path.slice(0, Math.min(patch.path.length, change.path.length)).every((key, i) => key === change.path[i] && key === patch.path[i]))))
                return { kind: 'invalid', code: 'overlapping-metadata' };
            const before = find(root, change.path);
            const after = change.remove ? undefined : cell(change.value);
            if (JSON.stringify(value(before)) !== JSON.stringify(value(after))) patches.push({ path: change.path, before, after, active: true });
        }
        return { kind: 'prepared', patches };
    }
    const current = patch => !!find(root, patch.path.slice(0, -1))?.children
        && find(root, patch.path) === (patch.active ? patch.after : patch.before);
    function apply(patches) { for (const patch of patches) root = replace(root, patch.path, patch.after); }
    function toggle(patch) {
        root = replace(root, patch.path, patch.active ? patch.before : patch.after);
        patch.active = !patch.active;
    }
    const snapshot = () => { const result = value(root); return result.order.map(id => result.widgets[id]); };
    return { prepare, current, apply, toggle, snapshot };
}
