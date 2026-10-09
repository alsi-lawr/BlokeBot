import { htmlRanges } from './SourceRanges.js';
import { elementKey } from './VisualStyles.js';
import { selectionRoots } from './LayerCommands.js';

const htmlNamespace='http://www.w3.org/1999/xhtml';
const voidTags=new Set(['area','base','br','col','embed','hr','img','input','link','meta','param','source','track','wbr']);
const machinery=new Set(['html','head','body','base','link','meta','script','style','title','template']);
const rawText=new Set(['script','style','textarea','title','xmp','iframe','noembed','noframes','plaintext']);
const phrasingParents=new Set(['p','span','h1','h2','h3','h4','h5','h6','em','strong','small','label','button','pre']);
const blockTags=new Set(['address','article','aside','blockquote','details','dialog','div','dl','fieldset','figure','figcaption','footer','form','h1','h2','h3','h4','h5','h6','header','hgroup','hr','main','menu','nav','ol','p','section','table','ul']);
const childModels=new Map([['ul',['li']],['ol',['li']],['menu',['li']],['table',['caption','colgroup','thead','tbody','tfoot']],['thead',['tr']],['tbody',['tr']],['tfoot',['tr']],['tr',['td','th']],['colgroup',['col']],['select',['option','optgroup']],['optgroup',['option']]]);
const requiredParents=new Map([['li',['ul','ol','menu']],['caption',['table']],['colgroup',['table']],['thead',['table']],['tbody',['table']],['tfoot',['table']],['tr',['thead','tbody','tfoot']],['td',['tr']],['th',['tr']],['col',['colgroup']],['optgroup',['select']],['option',['select','optgroup','datalist']]]);
const failure=code=>({kind:'unmapped',code});
const authoredStart=node=>node?.sourceCodeLocation?.startTag?node.sourceCodeLocation.startOffset:null;
const attributes=node=>Object.fromEntries((node.attrs??[]).map(attribute=>[attribute.name,attribute.value]));
function nativeNodes(tree) {
    const nodes=new Map();
    function visit(node) {
        const start=authoredStart(node);
        if(start!==null){const matches=nodes.get(start)??[];matches.push(node);nodes.set(start,matches);}
        for(const child of node.childNodes??[])visit(child);
        if(node.content)visit(node.content);
    }
    visit(tree);return nodes;
}
function ancestors(node) {const result=[];for(let parent=node?.parentNode;parent;parent=parent.parentNode)result.push(parent);return result;}
function subtree(node) {return [node,...(node.childNodes??[]).flatMap(subtree),...(node.content?subtree(node.content):[])];}
function shape(node) {return [node.nodeName,node.namespaceURI??null,node.attrs??[],node.value??node.data??null,(node.childNodes??[]).map(shape),node.content?shape(node.content):null];}
function interactive(node) {return ['button','input','select','textarea','details','iframe'].includes(node.tagName)||node.tagName==='a'&&Object.hasOwn(attributes(node),'href');}
function localContent(parent,roots) {
    const chain=[parent,...ancestors(parent)],name=parent.tagName;
    if(!name||parent.namespaceURI!==htmlNamespace||voidTags.has(name)||rawText.has(name)||machinery.has(name)&&name!=='body')return false;
    const allowed=childModels.get(name);
    if(allowed&&roots.some(root=>!allowed.includes(root.tagName)))return false;
    if(roots.some(root=>requiredParents.has(root.tagName)&&!requiredParents.get(root.tagName).includes(name)))return false;
    const content=roots.flatMap(subtree);
    if(chain.some(node=>phrasingParents.has(node.tagName))&&content.some(node=>blockTags.has(node.tagName)))return false;
    if(chain.some(node=>node.tagName==='button'||node.tagName==='a'&&Object.hasOwn(attributes(node),'href'))&&content.some(interactive))return false;
    if(chain.some(node=>node.tagName==='form')&&content.some(node=>node.tagName==='form'))return false;
    if(chain.some(node=>node.tagName==='label')&&content.some(node=>node.tagName==='label'))return false;
    return true;
}
function sourceBoundary(node) {
    const location=node.sourceCodeLocation;
    return location?.startTag&&Number.isSafeInteger(location.endOffset)
        &&(location.endTag||voidTags.has(node.tagName));
}
function safeRegion(node) {
    return node.namespaceURI===htmlNamespace&&sourceBoundary(node)
        &&!ancestors(node).some(parent=>parent.tagName==='template'||parent.namespaceURI&&parent.namespaceURI!==htmlNamespace)
        &&!(node.parentNode?.tagName&&authoredStart(node.parentNode)===null&&!['body','html'].includes(node.parentNode.tagName));
}
function rootDestination(parsed,native) {
    const element=parsed.elements.find(node=>node.name==='main')??parsed.elements.find(node=>node.name==='body');
    if(element){const matches=native.get(element.start);return matches?.length===1?{node:matches[0],at:element.endTag?.start??null,label:`Overlay root · <${element.name}>`}:null;}
    const body=parsed.tree.childNodes?.find(node=>node.tagName==='html')?.childNodes?.find(node=>node.tagName==='body');
    return body?{node:body,at:null,label:'Overlay root · authored fragment',fragment:true}:null;
}
export function hierarchyRootLabel(parsed) {return rootDestination(parsed,nativeNodes(parsed.tree))?.label??'Overlay root';}
function coalesce(source,edits) {
    const groups=[];
    for(const edit of [...edits].sort((a,b)=>a.start-b.start||a.end-b.end)) {
        const last=groups.at(-1);
        if(last&&edit.start<=last.end){last.end=Math.max(last.end,edit.end);last.edits.push(edit);}
        else groups.push({start:edit.start,end:edit.end,edits:[edit]});
    }
    return groups.map(group=>{
        const before=source.slice(group.start,group.end);let after=before;
        for(const edit of group.edits.sort((a,b)=>b.start-a.start||b.end-a.end))after=after.slice(0,edit.start-group.start)+edit.after+after.slice(edit.end-group.start);
        return {buffer:'html',start:group.start,end:group.end,before,after};
    });
}
function prepareTarget(parsed,keys,relation,target,length) {
    if(!['inside','before','after','root'].includes(relation))return failure('invalid-relation');
    const native=nativeNodes(parsed.tree),unique=key=>{const matches=parsed.elements.filter(node=>elementKey(node)===key);return matches.length===1?matches[0]:null;};
    const selected=[...new Set(keys)].map(unique);
    if(!selected.length)return {kind:'unchanged'};
    if(selected.some(node=>!node))return failure('selection-changed');
    const roots=selectionRoots(selected).sort((a,b)=>a.start-b.start),rawRoots=roots.map(node=>native.get(node.start)?.length===1?native.get(node.start)[0]:null);
    if(rawRoots.some(node=>!node||!safeRegion(node)||machinery.has(node.tagName)))return failure('ambiguous-source-boundary');
    const destination=relation==='root'?rootDestination(parsed,native):null,chosen=relation==='root'?null:unique(target);
    const rawTarget=chosen&&native.get(chosen.start)?.length===1?native.get(chosen.start)[0]:null;
    if(relation!=='root'&&(!rawTarget||!safeRegion(rawTarget)))return failure('ambiguous-destination');
    const parent=relation==='root'?destination?.node:relation==='inside'?rawTarget:rawTarget.parentNode;
    const at=relation==='root'?destination?.fragment?length:destination?.at:relation==='inside'?chosen.endTag?.start:relation==='before'?chosen.start:chosen.end;
    if(!parent||!Number.isSafeInteger(at))return failure('missing-authored-boundary');
    if(rawRoots.some(node=>node===rawTarget||node===parent||ancestors(parent).includes(node)))return failure('hierarchy-cycle');
    if(!localContent(parent,rawRoots))return failure('invalid-local-content');
    const affected=[...roots,...(chosen?[chosen]:[])];
    if(parsed.diagnostics.some(d=>d.code==='duplicate-attribute'&&affected.some(node=>d.start>=node.start&&d.start<node.end)))return failure('ambiguous-attribute');
    return {kind:'target',native,roots,rawRoots,rawTarget,parent,at};
}
export function hierarchyTarget(parsed,keys,relation,target,length) {
    return prepareTarget(parsed,keys,relation,target,length);
}
export function planHierarchy(snapshot,keys,relation,target,parsed=htmlRanges(snapshot.html)) {
    const prepared=prepareTarget(parsed,keys,relation,target,snapshot.html.length);
    if(prepared.kind!=='target')return prepared;
    const {native,roots,rawRoots,rawTarget,parent,at}=prepared;
    const segments=[],output=[];let length=0;
    function copy(start,end){if(start===end)return;segments.push({start,end,next:length});const value=snapshot.html.slice(start,end);output.push(value);length+=value.length;}
    const points=[...new Set([0,snapshot.html.length,at,...roots.flatMap(node=>[node.start,node.end])])].sort((a,b)=>a-b);
    for(let i=0;i<points.length;i++) {
        const start=points[i],end=points[i+1];
        if(start===at)for(const root of roots)copy(root.start,root.end);
        if(end!==undefined&&!roots.some(node=>start>=node.start&&end<=node.end))copy(start,end);
    }
    const html=output.join('');
    if(html===snapshot.html)return {kind:'unchanged'};
    const mapOffset=offset=>{const matches=segments.filter(part=>offset>=part.start&&offset<part.end);return matches.length===1?matches[0].next+offset-matches[0].start:null;};
    const candidate=htmlRanges(html),nextNative=nativeNodes(candidate.tree),nodeMap=new Map(),selectionMap=[];
    if(candidate.elements.length!==parsed.elements.length)return failure('candidate-source-repair');
    for(const node of parsed.elements) {
        const nextStart=mapOffset(node.start),matches=candidate.elements.filter(item=>item.start===nextStart),oldRaw=native.get(node.start),newRaw=nextNative.get(nextStart);
        if(matches.length!==1||oldRaw?.length!==1||newRaw?.length!==1)return failure('incomplete-origin-map');
        const next=matches[0];
        if(next.name!==node.name||JSON.stringify(next.values)!==JSON.stringify(node.values)||oldRaw[0].namespaceURI!==newRaw[0].namespaceURI
            ||!!node.endTag!==!!next.endTag||node.endTag&&mapOffset(node.endTag.start)!==next.endTag.start)return failure('candidate-source-repair');
        nodeMap.set(oldRaw[0],newRaw[0]);
        const key=elementKey(node);
        if(parsed.elements.filter(item=>elementKey(item)===key).length===1)selectionMap.push([key,elementKey(next)]);
    }
    function parentSignature(node,converted) {
        const path=[];
        for(let current=node;current;current=current.parentNode) {
            if(authoredStart(current)!==null)return [converted?mapOffset(authoredStart(current)):authoredStart(current),...path];
            path.push(current.nodeName,current.namespaceURI??null);
        }
        return path;
    }
    const destinationSignature=parentSignature(parent,true);
    for(const [oldNode,newNode]of nodeMap) {
        const expected=rawRoots.includes(oldNode)?destinationSignature:parentSignature(oldNode.parentNode,true);
        if(JSON.stringify(expected)!==JSON.stringify(parentSignature(newNode.parentNode,false)))return failure('candidate-hierarchy-repair');
    }
    const newParent=nodeMap.get(parent)??nodeMap.get(rawRoots[0]).parentNode;
    const oldOrder=(parent.childNodes??[]).filter(node=>authoredStart(node)!==null&&!rawRoots.includes(node));
    const targetIndex=oldOrder.indexOf(rawTarget);
    if((relation==='before'||relation==='after')&&targetIndex<0)return failure('candidate-sibling-repair');
    const index=relation==='before'||relation==='after'?targetIndex+(relation==='after'?1:0):oldOrder.length;
    oldOrder.splice(index,0,...rawRoots);
    const expectedOrder=oldOrder.map(node=>nodeMap.get(node)),actualOrder=(newParent.childNodes??[]).filter(node=>authoredStart(node)!==null);
    if(expectedOrder.length!==actualOrder.length||expectedOrder.some((node,index)=>node!==actualOrder[index]))return failure('candidate-sibling-repair');
    if(rawRoots.some(node=>JSON.stringify(shape(node))!==JSON.stringify(shape(nodeMap.get(node)))))return failure('candidate-subtree-repair');
    const edits=coalesce(snapshot.html,[...roots.map(node=>({start:node.start,end:node.end,after:''})),{start:at,end:at,after:roots.map(node=>snapshot.html.slice(node.start,node.end)).join('')}]);
    let reconstructed=snapshot.html;
    for(const edit of [...edits].sort((a,b)=>b.start-a.start))reconstructed=reconstructed.slice(0,edit.start)+edit.after+reconstructed.slice(edit.end);
    return reconstructed===html?{kind:'planned',edits,metadata:[],selectionMap,ownedOrigins:true}:failure('incomplete-relocation');
}
