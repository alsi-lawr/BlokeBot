import { walk, string } from 'css-tree';
import { escapeAttribute } from 'entities/escape';
import { htmlRanges, attributeEdit, parsedCss } from './SourceRanges.js';
import { targetElement, elementKey } from './VisualStyles.js';

const patch = (buffer, source, start, end, after) => ({ buffer, start, end, before: source.slice(start, end), after });
const widgetPath = id => ['widgets', id];
function insertion(snapshot) {
    const nodes = htmlRanges(snapshot.html).elements;
    return nodes.find(node => node.name === 'main' && node.endTag)?.endTag.start
        ?? nodes.find(node => node.name === 'body' && node.endTag)?.endTag.start ?? snapshot.html.length;
}
export function addLayer(snapshot, widget) {
    const at = insertion(snapshot);
    if (!widget) { const id=crypto.randomUUID(); return { kind: 'planned', edits: [patch('html', snapshot.html, at, at, `\n<div data-blokebot-element="${id}">New text</div>\n`)], metadata: [], selected: `element:${id}` }; }
    const id = widget.id.value;
    widget = { ...widget, authoring:{...widget.authoring,width:'640px',height:'240px'} };
    const html = `\n<section data-blokebot-widget="${escapeAttribute(id)}"></section>\n`;
    const css = `\n[data-blokebot-widget=${string.encode(id)}] { position: absolute; left: 0px; top: 0px; width: 640px; height: 240px; }\n`;
    return { kind: 'planned', edits: [patch('html', snapshot.html, at, at, html), patch('css', snapshot.css, snapshot.css.length, snapshot.css.length, css)],
        metadata: [{ path: widgetPath(id), value: widget }, { path: ['order'], value: [...snapshot.widgets.map(w => w.id.value), id] }], selected: `widget:${id}` };
}
export function removeLayer(snapshot, key) {
    const node = targetElement(snapshot, key);
    if (!node) return { kind: 'unmapped', code: 'selection-changed' };
    const removed = htmlRanges(snapshot.html).elements.filter(n => n.start >= node.start && n.end <= node.end)
        .map(n => n.values['data-blokebot-widget']).filter(Boolean);
    return { kind: 'planned', edits: [patch('html', snapshot.html, node.start, node.end, '')], selected: null,
        metadata: [...removed.filter(id => snapshot.widgets.some(w => w.id.value === id)).map(id => ({ path: widgetPath(id), remove: true })),
            { path: ['order'], value: snapshot.widgets.filter(w => !removed.includes(w.id.value)).map(w => w.id.value) }] };
}
export function duplicateLayer(snapshot, key) {
    const node = targetElement(snapshot, key);
    if (!node) return { kind: 'unmapped', code: 'selection-changed' };
    const descendants = htmlRanges(snapshot.html).elements.filter(n => n.start >= node.start && n.end <= node.end);
    const identities = new Map(), replacements = [], metadata = [], order = snapshot.widgets.map(w => w.id.value);
    for (const child of descendants) for (const attribute of ['data-blokebot-widget', 'data-blokebot-element']) {
        const old = child.values[attribute];
        if (!old) continue;
        const next = crypto.randomUUID(); identities.set(old, next);
        const change = attributeEdit(snapshot.html, child.start, attribute, next);
        if (change.kind !== 'patch') return change;
        replacements.push(change.edit);
        if (attribute === 'data-blokebot-widget') {
            const original = snapshot.widgets.find(w => w.id.value === old);
            if (original) { metadata.push({ path: widgetPath(next), value: { ...structuredClone(original), id: { value: next } } }); order.splice(order.indexOf(old) + 1, 0, next); }
        }
    }
    let html = snapshot.html.slice(node.start, node.end);
    let ordinaryId=null;
    if(!node.values['data-blokebot-widget']&&!node.values['data-blokebot-element']) {
        ordinaryId=crypto.randomUUID();
        const change=attributeEdit(snapshot.html,node.start,'data-blokebot-element',ordinaryId);
        if(change.kind!=='patch')return change;
        replacements.push(change.edit);
    }
    for (const change of replacements.sort((a,b) => b.start-a.start)) html = html.slice(0,change.start-node.start)+change.after+html.slice(change.end-node.start);
    const edits = [patch('html', snapshot.html, node.end, node.end, '\n'+html)];
    const {tree:css,diagnostics}=parsedCss(snapshot.css);
    if(!css||diagnostics.length)return {kind:'unmapped',code:'incomplete-css'};
    walk(css, { visit: 'Rule', enter(rule) {
        if (!rule.prelude) return;
        const values = [];
        walk(rule, { visit: 'AttributeSelector', enter(attribute) {
            if (['data-blokebot-widget','data-blokebot-element'].includes(attribute.name.name) && attribute.value?.type === 'String' && identities.has(attribute.value.value))
                values.push({ ...attribute.value.loc, value: string.encode(identities.get(attribute.value.value)) });
        } });
        if (!values.length) return;
        let raw = snapshot.css.slice(rule.loc.start.offset, rule.loc.end.offset);
        for (const value of values.sort((a,b) => b.start.offset-a.start.offset)) raw=raw.slice(0,value.start.offset-rule.loc.start.offset)+value.value+raw.slice(value.end.offset-rule.loc.start.offset);
        edits.push(patch('css',snapshot.css,rule.loc.end.offset,rule.loc.end.offset,'\n'+raw));
        return walk.skip;
    } });
    const id = identities.get(node.values['data-blokebot-widget']) ?? identities.get(node.values['data-blokebot-element']) ?? ordinaryId;
    metadata.push({ path: ['order'], value: order });
    return { kind: 'planned', edits, metadata, selected: id ? `${node.values['data-blokebot-widget']?'widget':'element'}:${id}` : null };
}
export function reorderLayer(snapshot, key, direction) {
    const node = targetElement(snapshot,key);
    if (!node) return { kind:'unmapped',code:'selection-changed' };
    const siblings=htmlRanges(snapshot.html).elements.filter(n=>n.parent===node.parent);
    const adjacent=siblings[siblings.findIndex(n=>n.start===node.start)+direction];
    if(!adjacent)return {kind:'unchanged'};
    const first=direction<0?adjacent:node,last=direction<0?node:adjacent;
    const after=snapshot.html.slice(last.start,last.end)+snapshot.html.slice(first.end,last.start)+snapshot.html.slice(first.start,first.end);
    const order=snapshot.widgets.map(w=>w.id.value), a=order.indexOf(node.values['data-blokebot-widget']),b=order.indexOf(adjacent.values['data-blokebot-widget']);
    if(a>=0&&b>=0)[order[a],order[b]]=[order[b],order[a]];
    return {kind:'planned',edits:[patch('html',snapshot.html,first.start,last.end,after)],metadata:[{path:['order'],value:order}],selected:key.startsWith('source:')?`source:${direction<0?first.start:first.start+snapshot.html.slice(last.start,last.end).length+snapshot.html.slice(first.end,last.start).length}`:key};
}
