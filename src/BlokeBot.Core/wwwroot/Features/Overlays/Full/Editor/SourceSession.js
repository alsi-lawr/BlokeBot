import { createMetadata } from './MetadataCells.js';
import { createPieces, readPieces, insertPatch, locatePatch, togglePatch, forgetPatch } from './SourcePieces.js';

export const EditorFocus = Object.freeze({ Visual: 'visual', Html: 'html', Css: 'css' });

export function createSourceSession({ html, css, widgets = [] }) {
    const buffers = { html: createPieces(html), css: createPieces(css) };
    const metadata = createMetadata(widgets);
    const histories = new Map(Object.values(EditorFocus).map(focus => [focus, { undo: [], redo: [] }]));
    let revision = 0;
    const snapshot = () => Object.freeze({ revision, html: readPieces(buffers.html), css: readPieces(buffers.css), widgets: metadata.snapshot() });

    function apply(focus, expectedRevision, edits, changes = []) {
        const history = histories.get(focus);
        if (!history) throw new TypeError('Unknown editor focus');
        if (expectedRevision !== revision) return { kind: 'conflict', code: 'stale-source', revision };
        const current = snapshot();
        const ordered = [...edits].sort((a, b) => a.buffer.localeCompare(b.buffer) || b.start - a.start);
        let previous;
        for (const edit of ordered) {
            if (!Object.hasOwn(buffers, edit.buffer) || !Number.isSafeInteger(edit.start) || !Number.isSafeInteger(edit.end)
                || edit.start < 0 || edit.end < edit.start || edit.end > current[edit.buffer].length
                || typeof edit.before !== 'string' || typeof edit.after !== 'string')
                return { kind: 'invalid', code: 'invalid-range' };
            if (current[edit.buffer].slice(edit.start, edit.end) !== edit.before)
                return { kind: 'conflict', code: 'changed-range', revision };
            if (previous?.buffer === edit.buffer && (edit.end > previous.start
                || (edit.start === edit.end && previous.start === previous.end && edit.start === previous.start)))
                return { kind: 'invalid', code: 'overlapping-patches' };
            previous = edit;
        }
        const prepared = metadata.prepare(changes);
        if (prepared.kind !== 'prepared') return prepared;
        const sourceChanges = ordered.filter(edit => edit.before !== edit.after);
        if (!sourceChanges.length && !prepared.patches.length) return { kind: 'unchanged', revision };
        for (const operation of history.redo) for (const patch of operation.source)
            forgetPatch(buffers[patch.buffer], patch);
        history.redo.length = 0;
        const operation = { source: sourceChanges.map(edit => ({ ...insertPatch(buffers[edit.buffer], edit), buffer: edit.buffer })), metadata: prepared.patches };
        metadata.apply(operation.metadata);
        history.undo.push(operation);
        revision++;
        return { kind: 'applied', revision };
    }

    function moveHistory(focus, direction) {
        const history = histories.get(focus);
        if (!history) throw new TypeError('Unknown editor focus');
        const source = history[direction];
        const target = history[direction === 'undo' ? 'redo' : 'undo'];
        const operation = source.at(-1);
        if (!operation) return { kind: 'unchanged', revision };
        for (const patch of operation.source) if (!locatePatch(buffers[patch.buffer], patch))
            return { kind: 'conflict', code: 'newer-overlap', buffer: patch.buffer, revision };
        if (operation.metadata.some(patch => !metadata.current(patch))) return { kind: 'conflict', code: 'newer-overlap', buffer: 'configuration', revision };
        for (const patch of operation.source) {
            const pieces = buffers[patch.buffer];
            togglePatch(pieces, patch, locatePatch(pieces, patch));
        }
        for (const patch of operation.metadata) metadata.toggle(patch);
        source.pop(); target.push(operation);
        revision++;
        return { kind: 'applied', revision };
    }

    function replaceSource(focus, expectedRevision, value) {
        if (focus !== EditorFocus.Html && focus !== EditorFocus.Css) throw new TypeError('Source focus required');
        const before = snapshot()[focus];
        let start = 0, end = before.length, valueEnd = value.length;
        while (start < end && start < valueEnd && before[start] === value[start]) start++;
        while (end > start && valueEnd > start && before[end - 1] === value[valueEnd - 1]) { end--; valueEnd--; }
        return apply(focus, expectedRevision, [{ buffer: focus, start, end, before: before.slice(start, end), after: value.slice(start, valueEnd) }]);
    }

    return Object.freeze({ snapshot, apply, replaceSource,
        undo: focus => moveHistory(focus, 'undo'), redo: focus => moveHistory(focus, 'redo') });
}
