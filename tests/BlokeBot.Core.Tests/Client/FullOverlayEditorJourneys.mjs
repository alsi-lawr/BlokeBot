function equal(actual, expected, guarantee) {
    if (actual !== expected) throw new Error(guarantee);
}
const edit = (buffer, source, before, after) => ({ buffer, start: source.indexOf(before), end: source.indexOf(before) + before.length, before, after });

export function sourcePreservation(core) {
    const html = '<!-- untouched -->\n<main><h1 class=old>Quest</h1><script>const raw = "<h1 class=hidden>";</script></main>';
    const css = '/* untouched */ @media (width > 40rem) { .card { --custom: future(x, y); color: red !important; strange-prop: future(1); } }';
    const document = core.createSourceSession({ html, css });
    const heading = core.htmlRanges(html).elements.find(node => node.name === 'h1');
    const colour = core.cssRanges(css).declarations.find(node => node.property === 'color');
    const attribute = core.attributeEdit(html, heading.start, 'class', 'hero & "gold"');
    const declaration = core.declarationEdit(css, colour.start, 'green');
    equal(document.apply(core.EditorFocus.Visual, 0, [attribute.edit, declaration.edit]).kind, 'applied', 'Visual spans apply atomically');
    equal(document.snapshot().html, html.replace('class=old', 'class="hero &amp; &quot;gold&quot;"'), 'Unrelated comments/script/raw HTML stay byte-exact');
    equal(document.snapshot().css, css.replace('color: red', 'color: green'), 'Only intended CSS value changes; important/unfamiliar rules remain');
    const newerHtml = '<!-- newer source -->' + document.snapshot().html;
    document.replaceSource(core.EditorFocus.Html, 1, newerHtml);
    equal(document.undo(core.EditorFocus.Visual).kind, 'applied', 'Focused visual undo survives preceding source insertion');
    equal(document.snapshot().html, '<!-- newer source -->' + html, 'Undo preserves unrelated newer HTML');
    equal(document.snapshot().css, css, 'Grouped CSS span is restored, not whole shared document');
    equal(document.redo(core.EditorFocus.Visual).kind, 'applied', 'Rebased visual redo succeeds');
    return { commentsScriptsAndUnknownCssPreserved: true, independentSourceEditPreserved: true };
}

export function focusedOverlap(core) {
    const document = core.createSourceSession({ html: '<main>kept</main>', css: '.card { color: red; }' });
    document.apply(core.EditorFocus.Visual, 0, [edit('css', document.snapshot().css, 'red', 'green')]);
    document.replaceSource(core.EditorFocus.Css, 1, '.card { color: purple; }');
    equal(document.undo(core.EditorFocus.Visual).code, 'newer-overlap', 'Newer CSS overlap blocks visual undo');
    equal(document.snapshot().css, '.card { color: purple; }', 'Conflict preserves purple');
    equal(document.undo(core.EditorFocus.Css).kind, 'applied', 'Other editor can undo overlap');
    equal(document.undo(core.EditorFocus.Visual).kind, 'applied', 'Original focused undo can retry');
    equal(document.snapshot().css, '.card { color: red; }', 'Retry restores intended earlier value');
    equal(document.redo(core.EditorFocus.Css).kind, 'conflict', 'Other redo cannot overwrite current visual undo');
    equal(document.redo(core.EditorFocus.Visual).kind, 'applied', 'Visual redo restores its provenance');
    equal(document.redo(core.EditorFocus.Css).kind, 'applied', 'Other redo can retry after overlap clears');
    equal(document.snapshot().css, '.card { color: purple; }', 'Independent redo order retains final selected value');
    return { newerOverlapKept: true, undoAndRedoRetry: true };
}

export function coveringAndPartialOverlap(core) {
    const original = '.card { color: red; padding: 2px; }';
    const document = core.createSourceSession({ html: '<p>original</p>', css: original });
    document.apply(core.EditorFocus.Visual, 0, [edit('css', original, 'red', 'green')]);
    document.replaceSource(core.EditorFocus.Css, 1, '@media print { .card { opacity: .3; } }');
    document.replaceSource(core.EditorFocus.Html, 2, '<p>newer unrelated</p>');
    equal(document.undo(core.EditorFocus.Visual).kind, 'conflict', 'Covering source rewrite blocks erased visual range');
    document.undo(core.EditorFocus.Css);
    equal(document.undo(core.EditorFocus.Visual).kind, 'applied', 'Undo of covering edit restores source identities');
    equal(document.snapshot().css, original, 'Covering retry restores original CSS');
    equal(document.snapshot().html, '<p>newer unrelated</p>', 'Other buffer not restored from a snapshot');
    document.redo(core.EditorFocus.Visual);
    document.replaceSource(core.EditorFocus.Css, document.snapshot().revision, original.replace('red', 'great'));
    equal(document.undo(core.EditorFocus.Visual).kind, 'conflict', 'Partial edit within earlier insertion blocks undo');
    document.undo(core.EditorFocus.Css);
    equal(document.undo(core.EditorFocus.Visual).kind, 'applied', 'Partial overlap undo restores split identities');
    return { coveringAndPartialRetry: true, unrelatedBufferKept: true };
}

export function staleAtomicAndAdjacent(core) {
    const document = core.createSourceSession({ html: 'abcd', css: 'red' });
    const candidate = document.snapshot();
    document.replaceSource(core.EditorFocus.Css, 0, 'purple');
    equal(document.apply(core.EditorFocus.Visual, candidate.revision, [edit('html', candidate.html, 'a', 'A')]).code, 'stale-source', 'Stale planned spans cannot overwrite current state');
    equal(document.apply(core.EditorFocus.Visual, 1, [edit('html', 'abcd', 'a', 'A'), { buffer: 'css', start: 0, end: 3, before: 'red', after: 'green' }]).kind, 'conflict', 'Later patch conflict rejects whole operation');
    equal(document.snapshot().html, 'abcd', 'No partial first-buffer change');
    const edits = [edit('html', 'abcd', 'ab', 'AB'), edit('html', 'abcd', 'cd', 'CD')];
    equal(document.apply(core.EditorFocus.Visual, 1, edits).kind, 'applied', 'Adjacent spans form one atomic edit');
    equal(document.snapshot().html, 'ABCD', 'Adjacent edits preserve intended positions');
    equal(document.undo(core.EditorFocus.Visual).kind, 'applied', 'Adjacent grouped undo is coherent');
    equal(document.snapshot().html, 'abcd', 'Adjacent undo restores only its spans');
    return { staleAndPartialFailuresPreserveCurrentSource: true, adjacentAtomicUndo: true };
}

export function deletionAndIdentity(core) {
    const document = core.createSourceSession({ html: '<p>keep</p><aside>remove</aside>', css: 'red' });
    document.apply(core.EditorFocus.Visual, 0, [edit('html', document.snapshot().html, '<aside>remove</aside>', '')]);
    document.replaceSource(core.EditorFocus.Html, 1, document.snapshot().html + '<footer>newer</footer>');
    equal(document.undo(core.EditorFocus.Visual).kind, 'applied', 'Deletion gap survives unrelated insertion');
    equal(document.snapshot().html, '<p>keep</p><aside>remove</aside><footer>newer</footer>', 'Undo deletion does not remove newer content');
    document.apply(core.EditorFocus.Visual, document.snapshot().revision, [edit('css', 'red', 'red', 'green')]);
    document.replaceSource(core.EditorFocus.Css, document.snapshot().revision, 'blue');
    document.replaceSource(core.EditorFocus.Css, document.snapshot().revision, 'green');
    equal(document.undo(core.EditorFocus.Visual).kind, 'conflict', 'Same bytes with newer provenance are still an overlap');
    document.undo(core.EditorFocus.Css); document.undo(core.EditorFocus.Css);
    equal(document.undo(core.EditorFocus.Visual).kind, 'applied', 'Undo newer writes restores original edit identity');
    return { deletionKeepsNewerInsertion: true, sameBytesDoNotHideNewerOverlap: true };
}

export function incompleteAndAmbiguous(core) {
    const html = '<h1 class=first class=second>Text</h1><!-- keep -->';
    const target = core.htmlRanges(html).elements.find(node => node.name === 'h1');
    equal(core.attributeEdit(html, target.start, 'class', 'new').code, 'ambiguous-attribute', 'Duplicate attribute is diagnosed, not guessed away');
    const document = core.createSourceSession({ html, css: '.card { color: red; }' });
    document.replaceSource(core.EditorFocus.Css, 0, '.card { color: red; broken');
    equal(document.snapshot().css, '.card { color: red; broken', 'Incomplete source remains authoritative');
    equal(document.snapshot().html, html, 'Incomplete CSS does not rewrite HTML');
    if (!core.cssRanges(document.snapshot().css).diagnostics.length) throw new Error('Incomplete CSS has source diagnostic');
    document.undo(core.EditorFocus.Css);
    equal(document.snapshot().css, '.card { color: red; }', 'Focused source undo restores complete CSS');
    return { duplicateAttributeDiagnosedWithoutMutation: true, incompleteSourcePreserved: true };
}
