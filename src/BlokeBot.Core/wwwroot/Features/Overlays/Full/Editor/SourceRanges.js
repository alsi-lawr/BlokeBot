import { parse as parseHtml } from 'parse5';
import { parse as parseCss, walk } from 'css-tree';
import { escapeAttribute } from 'entities/escape';

export function htmlRanges(source) {
    const diagnostics = [], elements = [];
    const tree = parseHtml(source, { sourceCodeLocationInfo: true,
        onParseError: error => diagnostics.push({ code: error.code, start: error.startOffset, end: error.endOffset }) });
    function visit(node, parent = null, path = "", ordinal = 1) {
        const location = node.sourceCodeLocation;
        const currentPath = node.tagName ? `${path}${path ? ' > ' : ''}${node.tagName}:nth-of-type(${ordinal})` : path;
        const authored = node.tagName && location?.startTag ? location.startOffset : parent;
        if (node.tagName && location?.startTag) elements.push({
            name: node.tagName, parent, selector: currentPath, values: Object.fromEntries((node.attrs ?? []).map(attr => [attr.name, attr.value])), start: location.startOffset, end: location.endOffset,
            endTag: location.endTag ? { start: location.endTag.startOffset, end: location.endTag.endOffset } : null,
            startTag: { start: location.startTag.startOffset, end: location.startTag.endOffset },
            attributes: Object.entries(location.attrs ?? {}).map(([name, span]) => ({ name, start: span.startOffset, end: span.endOffset })),
        });
        const siblings = new Map();
        for (const child of node.childNodes ?? []) {
            if (child.tagName) siblings.set(child.tagName, (siblings.get(child.tagName) ?? 0) + 1);
            visit(child, authored, currentPath, siblings.get(child.tagName) ?? 1);
        }
        if (node.content) visit(node.content, authored, currentPath);
    }
    visit(tree);
    return { elements, diagnostics, tree };
}

export function parsedCss(source, context = "stylesheet") {
    const diagnostics=[];
    try {
        const tree=parseCss(source,{context,positions:true,parseCustomProperty:false,
            onParseError:error=>diagnostics.push({code:'css-parse-error',start:error.offset})});
        return {tree,diagnostics};
    } catch(error) {
        if(error.name!=='SyntaxError')throw error;
        return {tree:null,diagnostics:[{code:'css-parse-error',start:error.offset}]};
    }
}
export function cssRanges(source) {
    const {tree,diagnostics}=parsedCss(source),declarations=[];
    if(tree)walk(tree,{visit:'Declaration',enter(node){
        const first=node.value.children?.first??node.value,last=node.value.children?.last??node.value;
        if(node.loc&&node.value.loc)declarations.push({property:node.property,start:node.loc.start.offset,end:node.loc.end.offset,
            value:{start:first.loc.start.offset,end:last.loc.end.offset},important:node.important});
    }});
    return {declarations,diagnostics};
}

export function attributeEdit(source, elementStart, name, value) {
    const parsed = htmlRanges(source);
    const element = parsed.elements.find(node => node.start === elementStart);
    if (!element) return { kind: 'unmapped', code: 'missing-source-element' };
    if (parsed.diagnostics.some(error => error.code === 'duplicate-attribute'
        && error.start >= element.startTag.start && error.start < element.startTag.end))
        return { kind: 'unmapped', code: 'ambiguous-attribute' };
    const attribute = element.attributes.find(item => item.name === name);
    if (!attribute) {
        const start = element.startTag.end - (source.slice(element.startTag.start, element.startTag.end).endsWith('/>') ? 2 : 1);
        return { kind: 'patch', edit: { buffer: 'html', start, end: start, before: '', after: ` ${name}="${escapeAttribute(value)}"` } };
    }
    return { kind: 'patch', edit: { buffer: 'html', start: attribute.start, end: attribute.end,
        before: source.slice(attribute.start, attribute.end), after: `${name}="${escapeAttribute(value)}"` } };
}

export function declarationEdit(source, declarationStart, value) {
    const declaration = cssRanges(source).declarations.find(node => node.start === declarationStart);
    if (!declaration) return { kind: 'unmapped', code: 'missing-source-declaration' };
    return { kind: 'patch', edit: { buffer: 'css', start: declaration.value.start, end: declaration.value.end,
        before: source.slice(declaration.value.start, declaration.value.end), after: value } };
}
