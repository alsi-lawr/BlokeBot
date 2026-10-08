import { walk, string } from 'css-tree';
import { htmlRanges, attributeEdit, parsedCss } from './SourceRanges.js';

const placementProperty=property=>property.startsWith('--')?property:property.replace(/[A-Z]/g,letter=>letter.toLowerCase());

export function targetElement(snapshot, key) {
    const elements = htmlRanges(snapshot.html).elements;
    const matches = elements.filter(element => elementKey(element) === key);
    return matches.length === 1 ? matches[0] : null;
}
export function elementKey(element) {
    return element.values['data-blokebot-widget'] ? `widget:${element.values['data-blokebot-widget']}`
        : element.values['data-blokebot-element'] ? `element:${element.values['data-blokebot-element']}` : `source:${element.start}`;
}
export function prepareStyles(snapshot, key) {
    const element=targetElement(snapshot,key);
    return {element,stylesheet:element?parsedCss(snapshot.css):{tree:null,diagnostics:[]}};
}
export function planStyles(snapshot, key, properties, prepared=prepareStyles(snapshot,key), placement=null) {
    const element = prepared.element;
    if (!element) return { kind: 'unmapped', code: 'selection-changed' };
    const edits = [], attributes = {};
    let identity = element.values['data-blokebot-widget'], selector, selected = key;
    if (identity) selector = `[data-blokebot-widget=${string.encode(identity)}]`;
    else {
        identity = element.values['data-blokebot-element'] ?? crypto.randomUUID();
        selector = `[data-blokebot-element=${string.encode(identity)}]`;
        if (!element.values['data-blokebot-element']) {
            const patch = attributeEdit(snapshot.html, element.start, 'data-blokebot-element', identity);
            if (patch.kind !== 'patch') return patch;
            edits.push(patch.edit); selected = `element:${identity}`;
            attributes['data-blokebot-element'] = identity;
        }
    }
    const {tree,diagnostics}=prepared.stylesheet;
    if(!tree||diagnostics.length)return {kind:'unmapped',code:'incomplete-css'};
    properties={...properties};
    if(element.values.style||placement){
        const attribute=element.attributes.find(item=>item.name==='style');
        const original=element.values.style??'';
        if(attribute&&!snapshot.html.slice(attribute.start,attribute.end).includes(original))return {kind:'unmapped',code:'encoded-inline-css'};
        const inline=parsedCss(original,'declarationList');
        if(!inline.tree||inline.diagnostics.length)return {kind:'unmapped',code:'incomplete-inline-css'};
        const changes=[],appended=[];
        for(const [property,value] of Object.entries(properties)){
            const declarations=inline.tree.children.toArray();
            const matching=declarations.filter(node=>node.type==='Declaration'&&(placement?placementProperty(node.property)===placementProperty(property):node.property===property));
            const declaration=placement?matching.findLast(node=>node.important)??matching.at(-1):matching.at(-1);
            // A later inset (including logical placement) can win after an earlier longhand.
            // Explicit movement/Return overrides only the physical edge it actually edits.
            const laterInset=placement==='move'&&['left','right','top','bottom'].includes(property)
                &&declarations.some((node,index)=>node.type==='Declaration'&&index>declarations.indexOf(declaration)
                    &&/^inset(?:-(?:inline|block)(?:-(?:start|end))?)?$/.test(placementProperty(node.property)));
            if(laterInset){appended.push(`${property}: ${value} !important;`);delete properties[property];continue;}
            const shorthand=['background-color','background-image'].includes(property)?declarations.findLast(node=>node.type==='Declaration'&&node.property==='background'):null;
            if(shorthand&&(!declaration||declarations.indexOf(shorthand)>declarations.indexOf(declaration)||shorthand.important&&!declaration.important)) {
                appended.push(`${property}: ${value}${shorthand.important||declaration?.important?' !important':''};`);
                delete properties[property];continue;
            }
            if(!declaration?.value.loc){
                if(placement){appended.push(`${property}: ${value}${placement==='move'?' !important':''};`);delete properties[property];}
                continue;
            }
            const first=declaration.value.children?.first??declaration.value,last=declaration.value.children?.last??declaration.value;
            changes.push({start:first.loc.start.offset,end:last.loc.end.offset,after:String(value)+(placement==='move'&&!declaration.important?' !important':'')});
            delete properties[property];
        }
        if(changes.length||appended.length){
            let value=original;
            for(const change of changes.sort((a,b)=>b.start-a.start))value=value.slice(0,change.start)+change.after+value.slice(change.end);
            if(appended.length)value+=`; ${appended.join(' ')}`;
            const change=attributeEdit(snapshot.html,element.start,'style',value);
            if(change.kind!=='patch')return change;
            edits.push(change.edit);
            attributes.style = value;
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
        const declarations=rule?.block.children.toArray()??[];
        const declaration=declarations.findLast(node=>node.type==='Declaration'&&node.property===property);
        const shorthand=['background-color','background-image'].includes(property)?declarations.findLast(node=>node.type==='Declaration'&&node.property==='background'):null;
        if(shorthand&&(!declaration||declarations.indexOf(shorthand)>declarations.indexOf(declaration)||shorthand.important&&!declaration.important)) {
            additions.push(`${property}: ${value}${shorthand.important||declaration?.important?' !important':''};`);continue;
        }
        if (declaration?.value.loc) {
            const first=declaration.value.children?.first??declaration.value,last=declaration.value.children?.last??declaration.value;
            const start=first.loc.start.offset,end=last.loc.end.offset;
            edits.push({buffer:'css',start,end,before:snapshot.css.slice(start,end),after:String(value)});
        } else additions.push(`${property}: ${value};`);
    }
    if (additions.length) {
        const at = rule ? rule.block.loc.end.offset - 1 : snapshot.css.length;
        const after = rule ? `;\n  ${additions.join('\n  ')}\n` : `\n${selector} {\n  ${additions.join('\n  ')}\n}\n`;
        edits.push({ buffer: 'css', start: at, end: at, before: '', after });
    }
    let css = snapshot.css;
    for (const edit of edits.filter(edit=>edit.buffer==='css').sort((a,b)=>b.start-a.start))
        css = css.slice(0,edit.start)+edit.after+css.slice(edit.end);
    return { kind: 'planned', edits, selected, presentation:{patches:[{selector:element.selector,attributes}],css} };
}

// Multiple roots are planned against the same source, never applied one at a time.
export function combineStyles(snapshot, plans) {
    const failure=plans.find(plan=>plan.kind!=='planned');
    if(failure)return failure;
    if(!plans.length)return {kind:'unchanged'};
    const edits=[];
    for(const edit of plans.flatMap(plan=>plan.edits)) {
        const insertion=edit.start===edit.end&&edits.find(item=>item.buffer===edit.buffer&&item.start===edit.start&&item.end===edit.end);
        if(insertion)insertion.after+=edit.after;else edits.push({...edit});
    }
    const ordered=[...edits].sort((a,b)=>a.buffer.localeCompare(b.buffer)||b.start-a.start);
    for(let i=0;i<ordered.length;i++) {
        const edit=ordered[i],previous=ordered[i-1];
        if(snapshot[edit.buffer].slice(edit.start,edit.end)!==edit.before
            ||previous?.buffer===edit.buffer&&edit.end>previous.start)return {kind:'invalid',code:'overlapping-patches'};
    }
    let css=snapshot.css;
    for(const edit of ordered.filter(edit=>edit.buffer==='css'))css=css.slice(0,edit.start)+edit.after+css.slice(edit.end);
    return {kind:'planned',edits,metadata:plans.flatMap(plan=>plan.metadata??[]),
        selectionMap:plans.map(plan=>[plan.selection,plan.selected]),
        presentation:{css,patches:plans.flatMap(plan=>plan.presentation.patches)}};
}

export function styleValues(snapshot, key, prepared=prepareStyles(snapshot,key)) {
    const element = prepared.element;
    if (!element) return {};
    const selector = element.values['data-blokebot-widget']
        ? `[data-blokebot-widget=${string.encode(element.values['data-blokebot-widget'])}]`
        : element.values['data-blokebot-element'] ? `[data-blokebot-element=${string.encode(element.values['data-blokebot-element'])}]` : null;

    const {tree}=prepared.stylesheet;
    if(!tree)return {};
    const result = {};
    for (const rule of tree.children.toArray()) {
        if (rule.type !== 'Rule' || !rule.prelude.loc || snapshot.css.slice(rule.prelude.loc.start.offset, rule.prelude.loc.end.offset).trim() !== selector) continue;
        for (const declaration of rule.block.children.toArray()) if (declaration.type === 'Declaration' && declaration.value.loc)
            result[declaration.property] = snapshot.css.slice(declaration.value.loc.start.offset, declaration.value.loc.end.offset);
    }
    if(element.values.style){
        const {tree:inline}=parsedCss(element.values.style,'declarationList');
        const declarations=new Map();
        if(inline)for(const declaration of inline.children.toArray())if(declaration.type==='Declaration'&&declaration.value.loc
            &&(!declarations.get(declaration.property)?.important||declaration.important))declarations.set(declaration.property,declaration);
        for(const declaration of declarations.values())result[declaration.property]=element.values.style.slice(declaration.value.loc.start.offset,declaration.value.loc.end.offset);
    }
    return result;
}
