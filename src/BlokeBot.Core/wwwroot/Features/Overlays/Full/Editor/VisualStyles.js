import { walk, string } from 'css-tree';
import { htmlRanges, attributeEdit, declarationEdit, parsedCss } from './SourceRanges.js';

export function targetElement(snapshot, key) {
    const elements = htmlRanges(snapshot.html).elements;
    return elements.find(element => elementKey(element) === key);
}
export function elementKey(element) {
    return element.values['data-blokebot-widget'] ? `widget:${element.values['data-blokebot-widget']}`
        : element.values['data-blokebot-element'] ? `element:${element.values['data-blokebot-element']}` : `source:${element.start}`;
}
export function planStyles(snapshot, key, properties) {
    const element = targetElement(snapshot, key);
    if (!element) return { kind: 'unmapped', code: 'selection-changed' };
    const edits = [];
    let identity = element.values['data-blokebot-widget'], selector, selected = key;
    if (identity) selector = `[data-blokebot-widget=${string.encode(identity)}]`;
    else {
        identity = element.values['data-blokebot-element'] ?? crypto.randomUUID();
        selector = `[data-blokebot-element=${string.encode(identity)}]`;
        if (!element.values['data-blokebot-element']) {
            const patch = attributeEdit(snapshot.html, element.start, 'data-blokebot-element', identity);
            if (patch.kind !== 'patch') return patch;
            edits.push(patch.edit); selected = `element:${identity}`;
        }
    }
    const {tree,diagnostics}=parsedCss(snapshot.css);
    if(!tree||diagnostics.length)return {kind:'unmapped',code:'incomplete-css'};
    properties={...properties};
    if(element.values.style){
        const attribute=element.attributes.find(item=>item.name==='style');
        if(!attribute||!snapshot.html.slice(attribute.start,attribute.end).includes(element.values.style))return {kind:'unmapped',code:'encoded-inline-css'};
        const inline=parsedCss(element.values.style,'declarationList');
        if(!inline.tree||inline.diagnostics.length)return {kind:'unmapped',code:'incomplete-inline-css'};
        const changes=[];
        for(const [property,value] of Object.entries(properties)){
            const declaration=inline.tree.children.toArray().findLast(node=>node.type==='Declaration'&&node.property===property);
            if(!declaration?.value.loc)continue;
            const first=declaration.value.children?.first??declaration.value,last=declaration.value.children?.last??declaration.value;
            changes.push({start:first.loc.start.offset,end:last.loc.end.offset,after:String(value)});
            delete properties[property];
        }
        if(changes.length){
            let value=element.values.style;
            for(const change of changes.sort((a,b)=>b.start-a.start))value=value.slice(0,change.start)+change.after+value.slice(change.end);
            const change=attributeEdit(snapshot.html,element.start,'style',value);
            if(change.kind!=='patch')return change;
            edits.push(change.edit);
        }
    }
    const rules = [];
    walk(tree, { visit: 'Rule', enter(rule) {
        if (rule.prelude?.loc && snapshot.css.slice(rule.prelude.loc.start.offset, rule.prelude.loc.end.offset).trim() === selector)
            rules.push(rule);
    } });
    // Only a standalone authored selector is patched; conditional/unfamiliar rules remain intact.
    const rule = rules.findLast(rule => tree.children.toArray().includes(rule));
    const additions = [];
    for (const [property, value] of Object.entries(properties)) {
        const declaration = rule?.block.children.toArray().findLast(node => node.type === 'Declaration' && node.property === property);
        if (declaration?.value.loc) edits.push(declarationEdit(snapshot.css, declaration.loc.start.offset, String(value)).edit);
        else additions.push(`${property}: ${value};`);
    }
    if (additions.length) {
        const at = rule ? rule.block.loc.end.offset - 1 : snapshot.css.length;
        const after = rule ? `\n  ${additions.join('\n  ')}\n` : `\n${selector} {\n  ${additions.join('\n  ')}\n}\n`;
        edits.push({ buffer: 'css', start: at, end: at, before: '', after });
    }
    return { kind: 'planned', edits, selected };
}

export function styleValues(snapshot, key) {
    const element = targetElement(snapshot, key);
    if (!element) return {};
    const selector = element.values['data-blokebot-widget']
        ? `[data-blokebot-widget=${string.encode(element.values['data-blokebot-widget'])}]`
        : element.values['data-blokebot-element'] ? `[data-blokebot-element=${string.encode(element.values['data-blokebot-element'])}]` : null;

    const {tree}=parsedCss(snapshot.css);
    if(!tree)return {};
    const result = {};
    for (const rule of tree.children.toArray()) {
        if (rule.type !== 'Rule' || !rule.prelude.loc || snapshot.css.slice(rule.prelude.loc.start.offset, rule.prelude.loc.end.offset).trim() !== selector) continue;
        for (const declaration of rule.block.children.toArray()) if (declaration.type === 'Declaration' && declaration.value.loc)
            result[declaration.property] = snapshot.css.slice(declaration.value.loc.start.offset, declaration.value.loc.end.offset);
    }
    if(element.values.style){
        const {tree:inline}=parsedCss(element.values.style,'declarationList');
        if(inline)for(const declaration of inline.children.toArray())if(declaration.type==='Declaration'&&declaration.value.loc)
            result[declaration.property]=element.values.style.slice(declaration.value.loc.start.offset,declaration.value.loc.end.offset);
    }
    return result;
}
