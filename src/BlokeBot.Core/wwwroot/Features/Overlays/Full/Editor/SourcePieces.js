let identity = 0;
const marker = () => ({ marker: ++identity });
const textPiece = (value) => ({ id: ++identity, value, from: 0, to: value.length });
const text = (piece) => piece.marker ? '' : piece.value.slice(piece.from, piece.to);
export const readPieces = (pieces) => pieces.map(text).join('');
export const createPieces = (value) => value ? [textPiece(value)] : [];

function boundary(pieces, offset, edge) {
    let position = 0;
    for (let index = 0; index < pieces.length; index++) {
        const piece = pieces[index];
        if (piece.marker) {
            if (edge === 'end' && position === offset) return index;
            continue;
        }
        const length = piece.to - piece.from;
        if (position === offset) return index;
        if (position + length > offset) {
            const split = piece.from + offset - position;
            pieces.splice(index, 1, { ...piece, to: split }, { ...piece, from: split });
            return index + 1;
        }
        position += length;
    }
    return pieces.length;
}

function contentIdentity(pieces) {
    const spans = [];
    for (const piece of pieces) {
        if (piece.marker || piece.from === piece.to) continue;
        const last = spans.at(-1);
        if (last?.id === piece.id && last.to === piece.from) last.to = piece.to;
        else spans.push({ id: piece.id, from: piece.from, to: piece.to });
    }
    return spans;
}

function sameContent(left, right) {
    const a = contentIdentity(left), b = contentIdentity(right);
    return a.length === b.length && a.every((span, index) =>
        span.id === b[index].id && span.from === b[index].from && span.to === b[index].to);
}

export function insertPatch(pieces, edit) {
    const from = boundary(pieces, edit.start, 'start');
    const to = edit.end === edit.start ? from : boundary(pieces, edit.end, 'end');
    const start = marker(), end = marker();
    const before = pieces.slice(from, to);
    const after = createPieces(edit.after);
    pieces.splice(from, to - from, start, ...after, end);
    return { start, end, before, after, applied: true };
}

export function locatePatch(pieces, patch) {
    const from = pieces.indexOf(patch.start), to = pieces.indexOf(patch.end);
    if (from < 0 || to < from) return null;
    const current = pieces.slice(from + 1, to);
    if (!sameContent(current, patch.applied ? patch.after : patch.before)) return null;
    return { from, to, current };
}

export function togglePatch(pieces, patch, location) {
    if (patch.applied) patch.after = location.current;
    else patch.before = location.current;
    pieces.splice(location.from + 1, location.to - location.from - 1,
        ...(patch.applied ? patch.before : patch.after));
    patch.applied = !patch.applied;
}

export function forgetPatch(pieces, patch) {
    for (const edge of [patch.start, patch.end]) {
        const index = pieces.indexOf(edge);
        if (index >= 0) pieces.splice(index, 1);
    }
}
