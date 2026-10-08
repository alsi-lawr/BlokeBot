import test from 'node:test';
import assert from 'node:assert/strict';
import { createEditorDocument } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/EditorDocument.js';
import { targetElement, planStyles } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/VisualStyles.js';
const fixture=(html,css='',widgets=[])=>({id:crypto.randomUUID(),html,css,widgets,diagnostics:[]});
const byLabel=(owner,label)=>owner.view().layers.find(layer=>layer.label===label).key;
const widget=id=>({id:{value:id},kind:{value:'giveaway'},configuration:{title:'Retained'},audio:{isMuted:true,volume:.3},authoring:{isLocked:true,x:'18px',y:'8px'}});
const native=(key,position='static',translate='none',movementBasis=[1,0,0,1])=>({key,visible:true,x:0,y:0,width:100,height:50,layoutX:0,layoutY:0,styles:{position,translate},movementBasis});
function move(owner,targets,dx=16,dy=8){const view=owner.view();return owner.planGesture({kind:'move',dx,dy,targets},view.selected,view.revision,view.selectionVersion);}
function relocate(owner,relation,target){const context=owner.context(),plan=owner.planHierarchy({relation,target},context);assert.equal(plan.kind,'planned',plan.code);return owner.commitGesture(plan);}

test('Lossless relocation maps moved and shifted ordinary identities, normalized selected roots, and preserves all authored and metadata bytes in one undo',()=>{
 const id=crypto.randomUUID(),a='<p aria-label="A">Same</p>',b='<p aria-label="B">Same</p>',w=`<section data-blokebot-widget="${id}" style="rotate:7deg"><b>Widget</b></section>`;
 const original=fixture(`<!-- keep --><main><div aria-label="From">${a}${w}</div>\n<div aria-label="To">${b}</div><script>const raw='<p>';</script></main>`,'/* keep */ @media (width > 30rem) { .x { unknown:future(x) } }',[widget(id)]),owner=createEditorDocument(original);
 const keys=[byLabel(owner,'A'),`widget:${id}`,byLabel(owner,'b')],destination=byLabel(owner,'To');owner.selectSet(keys,keys[0]);
 const before=owner.context(),plan=owner.planHierarchy({relation:'inside',target:destination},before);assert.equal(plan.kind,'planned',plan.code);assert.deepEqual(owner.candidate(),original);assert.equal(plan.selectionMap.length,owner.view().layers.length);
 owner.commitGesture(plan);assert.equal(owner.view().revision,1);assert.deepEqual(owner.candidate().widgets,original.widgets);assert.equal(owner.candidate().css,original.css);
 assert.equal(owner.candidate().html,original.html.replace(a+w,'').replace(b,b+a+w));
 assert.equal(owner.view().selected,byLabel(owner,'A'));assert.deepEqual(owner.view().members,[byLabel(owner,'A'),`widget:${id}`,byLabel(owner,'b')]);
 assert.equal(owner.view().layers.find(layer=>layer.label==='B').parent,byLabel(owner,'To'));assert.equal(owner.view().layers.find(layer=>layer.label==='A').parent,byLabel(owner,'To'));
 owner.history('undo');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);assert.deepEqual(owner.view().members,[`widget:${id}`]);
 owner.history('redo');assert.equal(owner.candidate().html,original.html.replace(a+w,'').replace(b,b+a+w));
});

test('Before, after, named root and authored fragment root preserve exact subtrees and immediately expose the resulting hierarchy',()=>{
 for(const relation of ['before','after','root']){
  const original=fixture('<main><div aria-label="From"><p aria-label="A">A</p></div><aside aria-label="To">To</aside></main>'),owner=createEditorDocument(original);owner.select(byLabel(owner,'A'));relocate(owner,relation,byLabel(owner,'To'));
  const layer=owner.view().layers.find(layer=>layer.label==='A');assert.equal(layer.parent,byLabel(owner,'main'));assert.equal(owner.view().members[0],layer.key);
  assert.equal(owner.candidate().html,relation==='before'?'<main><div aria-label="From"></div><p aria-label="A">A</p><aside aria-label="To">To</aside></main>':'<main><div aria-label="From"></div><aside aria-label="To">To</aside><p aria-label="A">A</p></main>');
  owner.history('undo');assert.deepEqual(owner.candidate(),original);
 }
 const original=fixture('<section aria-label="From"><p aria-label="A">A</p></section><aside>Last</aside>'),owner=createEditorDocument(original);owner.select(byLabel(owner,'A'));relocate(owner,'root');assert.equal(owner.candidate().html,'<section aria-label="From"></section><aside>Last</aside><p aria-label="A">A</p>');assert.equal(owner.view().layers.find(layer=>layer.label==='A').parent,null);owner.history('undo');assert.deepEqual(owner.candidate(),original);
});

test('Affected cycles, phrasing/interactive content, repaired table boundaries and ambiguous identities refuse without source/history mutation; unrelated unfamiliar content is retained',()=>{
 const cases=[['<main><div aria-label="A"><span aria-label="To">X</span></div></main>','A','To'],['<main><div aria-label="A">A</div><p aria-label="To">Text</p></main>','A','To'],['<main><button aria-label="A">A</button><a href="/" aria-label="To">To</a></main>','A','To'],['<main><div aria-label="A">A</div><table aria-label="To"><tr><td>T</td></tr></table></main>','A','To'],['<main><p aria-label="A">A<p aria-label="To">To</main>','A','To'],['<main><p data-blokebot-element="a" aria-label="A">A</p><p data-blokebot-element="a">Duplicate</p><aside aria-label="To"></aside></main>','A','To']];
 for(const [html,label,target] of cases){const original=fixture(html),owner=createEditorDocument(original);owner.select(byLabel(owner,label));const plan=owner.planHierarchy({relation:'inside',target:byLabel(owner,target)},owner.context());assert.notEqual(plan.kind,'planned',html);assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);}
 const original=fixture('<main><p aria-label="A">A</p><aside aria-label="To"></aside><svg><path d="M 0 0"></path></svg><script>const raw="<aside>";</script></main>','[broken'),owner=createEditorDocument(original);owner.select(byLabel(owner,'A'));relocate(owner,'inside',byLabel(owner,'To'));assert.equal(owner.candidate().css,original.css);assert(owner.candidate().html.includes('<svg><path d="M 0 0"></path></svg><script>const raw="<aside>";</script>'));owner.history('undo');assert.deepEqual(owner.candidate(),original);
});

test('Hierarchy and Return require the exact captured member set, active member, source revision and focus; pending intents never overwrite newer work',()=>{
 for(const change of ['membership','active','source','focus']){
  const original=fixture('<main><p aria-label="A">A</p><p aria-label="B">B</p><aside aria-label="To"></aside></main>'),owner=createEditorDocument(original),a=byLabel(owner,'A'),b=byLabel(owner,'B');owner.selectSet([a,b],b);
  const captured=owner.context(),planned=owner.planHierarchy({relation:'inside',target:byLabel(owner,'To')},captured);
  if(change==='membership')owner.selectSet([b],b);if(change==='active')owner.selectSet([a,b],a);if(change==='source')owner.source('html','<!-- newer -->'+original.html);if(change==='focus')owner.focus('html');
  const before=owner.candidate();assert.equal(owner.planReturn([native(a),native(b)],captured).kind,'conflict');assert.equal(owner.hierarchyTarget({relation:'root'},captured).kind,'conflict');owner.commitGesture(planned);assert.deepEqual(owner.candidate(),before);assert.match(owner.view().feedback,/newer work/);
 }
});

test('Flow displacement changes only translate, uses each native 2D ancestor basis, preserves third components and keeps source/metadata transient until one commit',()=>{
 const id=crypto.randomUUID(),html=`<main><p data-blokebot-element="a" style="position:relative;left:4px;translate:var(--offset) 2px 7px;--offset:10px;color:red!important;transform:skew(2deg);rotate:9deg;scale:.8">A</p><section data-blokebot-widget="${id}" style="position:sticky;top:8px"></section></main>`,original=fixture(html,'/* keep */',[{...widget(id),authoring:{...widget(id).authoring,isLocked:false}}]),owner=createEditorDocument(original);owner.selectSet(['element:a',`widget:${id}`]);
 const plan=move(owner,[native('element:a','relative','10px 2px 7px',[0,2,-2,0]),native(`widget:${id}`,'sticky','none')]);assert.equal(plan.kind,'planned',plan.code);assert.deepEqual(owner.candidate(),original);assert.deepEqual(plan.metadata,[]);owner.commitGesture(plan);
 assert.equal(owner.view().revision,1);const inline=targetElement(owner.candidate(),'element:a').values.style;assert(inline.includes('translate:14px -6px 7px !important'));assert(inline.includes('position:relative;left:4px'));assert(inline.includes('--offset:10px;color:red!important;transform:skew(2deg);rotate:9deg;scale:.8'));
 assert(!inline.includes('--blokebot-x'));assert.deepEqual(owner.candidate().widgets,original.widgets);assert.equal(owner.candidate().css,original.css);owner.history('undo');assert.deepEqual(owner.candidate(),original);
 const refused=move(owner,[native('element:a'),native(`widget:${id}`,'static','none',[0,0,0,0])]);assert.equal(refused.kind,'unmapped');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);
});

test('Return normalizes the current selected-root placement only, including explicit locked targets; flow edges and every unrelated field/metadata survive one undo',()=>{
 const id=crypto.randomUUID(),html=`<main><p data-blokebot-element="a" data-blokebot-locked="true" style="position:relative!important;left:4px!important;translate:8px 9px;rotate:9deg;--blokebot-x:98px">A</p><section data-blokebot-widget="${id}" style="position:fixed;left:20px;right:30px;top:4px;bottom:8px;translate:12px 4px 9px;/* keep */transform:skew(2deg);scale:.8"></section></main>`,original=fixture(html,'#unrelated { left:1px!important }',[widget(id)]),owner=createEditorDocument(original);owner.selectSet(['element:a',`widget:${id}`]);
 const plan=owner.planReturn([native('element:a','relative','8px 9px'),native(`widget:${id}`,'fixed','12px 4px 9px')],owner.context());assert.equal(plan.kind,'planned');assert.deepEqual(owner.candidate(),original);owner.commitGesture(plan);
 const flow=targetElement(owner.candidate(),'element:a').values.style;assert(flow.includes('position:relative!important;left:4px!important;translate:none !important;rotate:9deg;--blokebot-x:98px'));
 const fixed=targetElement(owner.candidate(),`widget:${id}`).values.style;for(const value of ['position:static !important','left:auto !important','right:auto !important','top:auto !important','bottom:auto !important','translate:none !important'])assert(fixed.includes(value));assert(fixed.includes('/* keep */transform:skew(2deg);scale:.8'));
 assert.deepEqual(owner.candidate().widgets,original.widgets);assert.equal(owner.candidate().css,original.css);assert.equal(owner.view().revision,1);owner.history('undo');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);
});

test('Return writes only its finite fields after later important inset without rewriting shorthand, losing duplicates or unrelated author priority',()=>{
 const html='<div data-blokebot-element="a" style="left:20px;left:40px!important;inset:10px 30px!important;inset-inline:15px!important;position:absolute;translate:8px 4px;/* keep */color:red!important;--blokebot-x:88px"></div>',original=fixture(html,'#a{position:fixed!important;translate:30px 8px!important}'),owner=createEditorDocument(original);owner.select('element:a');
 const planned=owner.planReturn([native('element:a','fixed','30px 8px')],owner.context());owner.commitGesture(planned);
 const style=targetElement(owner.candidate(),'element:a').values.style;assert(style.includes('left:20px;left:40px!important;inset:10px 30px!important;inset-inline:15px!important'));
 assert(style.indexOf('left: auto !important')>style.indexOf('inset-inline:15px!important'));assert(style.includes('/* keep */color:red!important;--blokebot-x:88px'));
 assert.equal(owner.candidate().css,original.css);assert.equal(owner.view().revision,1);owner.history('undo');assert.deepEqual(owner.candidate(),original);
});


test('Mixed-case standard placement edits its actual effective duplicate while custom-variable case, losing bytes, ordinary priority and one Undo stay independent',()=>{
 const id=crypto.randomUUID(),style='position:relative;translate:10px!important; /* losing */ TRANSLATE:20px 9px 7px!important;--X:11px;--x:22px;color:red!important;rotate:9deg',original=fixture(`<main><p data-blokebot-element="a" style="${style}">A</p><section data-blokebot-widget="${id}" style="translate:3px!important; TrAnSlAtE:8px 4px!important"></section></main>`,'/* unchanged */',[{...widget(id),authoring:{...widget(id).authoring,isLocked:false}}]),owner=createEditorDocument(original);owner.selectSet(['element:a',`widget:${id}`]);
 const plan=move(owner,[native('element:a','relative','20px 9px 7px'),native(`widget:${id}`,'static','8px 4px')]);assert.equal(plan.kind,'planned');assert.deepEqual(owner.candidate(),original);owner.commitGesture(plan);
 const changed=targetElement(owner.candidate(),'element:a').values.style;assert.equal(changed,style.replace('TRANSLATE:20px 9px 7px','TRANSLATE:36px 17px 7px'));assert.equal(targetElement(owner.candidate(),`widget:${id}`).values.style,'translate:3px!important; TrAnSlAtE:24px 12px!important');assert.deepEqual(owner.candidate().widgets,original.widgets);assert.equal(owner.candidate().css,original.css);owner.history('undo');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);
 owner.selectSet(['element:a',`widget:${id}`]);owner.commitGesture(owner.planReturn([native('element:a','relative','20px 9px 7px'),native(`widget:${id}`,'static','8px 4px')],owner.context()));assert.equal(targetElement(owner.candidate(),'element:a').values.style,style.replace('TRANSLATE:20px 9px 7px','TRANSLATE:none'));assert.deepEqual(owner.candidate().widgets,original.widgets);owner.history('undo');assert.deepEqual(owner.candidate(),original);
 const positioned=fixture('<p data-blokebot-element="a" style="position:absolute;left:100px;top:50px;--BLOKEBOT-X:91px;--blokebot-x:98px">A</p>'),positionedOwner=createEditorDocument(positioned);positionedOwner.select('element:a');const item={...native('element:a','absolute'),styles:{position:'absolute',translate:'none',left:'100px',top:'50px'}};positionedOwner.commitGesture(move(positionedOwner,[item],16,0));const placed=targetElement(positionedOwner.candidate(),'element:a').values.style;assert(placed.includes('--BLOKEBOT-X:91px;--blokebot-x:116px !important'));positionedOwner.history('undo');assert.deepEqual(positionedOwner.candidate(),positioned);
 const ordinary=fixture('<p data-blokebot-element="a" style="WIDTH:100px!important;color:red!important">A</p>'),resize=planStyles(ordinary,'element:a',{width:'116px'},undefined,'layout');assert.equal(resize.presentation.patches[0].attributes.style,'WIDTH:116px!important;color:red!important');
 const normal=planStyles(ordinary,'element:a',{color:'blue'});assert.equal(normal.presentation.patches[0].attributes.style,'WIDTH:100px!important;color:blue!important');
});

test('Return resolves later uppercase inset shorthand and logical longhand without rewriting raw names, source or metadata beyond its finite fields',()=>{
 for(const inset of ['INSET:10px 30px!important','INSET-INLINE:15px!important','INSET-INLINE-START:18px!important']){
  const id=crypto.randomUUID(),style=`left:20px;LEFT:40px!important;${inset};POSITION:absolute;TRANSLATE:8px 4px;--X:11px;--x:22px;/* keep */color:red!important`,original=fixture(`<section data-blokebot-widget="${id}" style="${style}"></section>`,'/* unchanged */',[widget(id)]),owner=createEditorDocument(original);owner.select(`widget:${id}`);const planned=owner.planReturn([native(`widget:${id}`,'absolute','8px 4px')],owner.context());assert.equal(planned.kind,'planned');owner.commitGesture(planned);const changed=targetElement(owner.candidate(),`widget:${id}`).values.style;
  assert(changed.includes(`left:20px;LEFT:40px!important;${inset}`));assert(changed.indexOf('left: auto !important')>changed.indexOf(inset));assert(changed.includes('POSITION:static !important;TRANSLATE:none !important;--X:11px;--x:22px;/* keep */color:red!important'));assert.deepEqual(owner.candidate().widgets,original.widgets);assert.equal(owner.candidate().css,original.css);assert.equal(owner.view().revision,1);owner.history('undo');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);
 }
});
