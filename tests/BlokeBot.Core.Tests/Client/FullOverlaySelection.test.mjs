import test from 'node:test';
import assert from 'node:assert/strict';
import { createEditorDocument } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/EditorDocument.js';
import { targetElement } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/VisualStyles.js';
import { insertedLayer } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/ToolboxInsertion.js';

const key=id=>`element:${id}`;
const fixture=(html,css='',widgets=[])=>({id:crypto.randomUUID(),html,css,widgets,diagnostics:[]});
const geometry=(key,left=0)=>({key,visible:true,x:left,y:0,width:100,height:50,layoutX:left,layoutY:0,styles:{left:`${left}px`,top:'0px',width:'100px',height:'50px'}});
const move=(owner,targets,dx=16)=>{const view=owner.view();return owner.planGesture({kind:'move',dx,dy:8,targets},view.selected,view.revision,view.selectionVersion);};
const widget=id=>({id:{value:id},kind:{value:'giveaway'},configuration:{title:id},authoring:{isVisible:true,isLocked:false,clipOverflow:false,x:'0px',y:'0px',width:'100px',height:'50px',rotationDegrees:0,scaleX:1,scaleY:1,horizontalAnchor:0,verticalAnchor:0},audio:{isMuted:false,volume:.3}});

test('Transient ordered membership keeps active-only styling and successful creation replaces the set without saving selection',()=>{
 const original=fixture('<main><div data-blokebot-element="a">A</div><div data-blokebot-element="b">B</div></main>');
 const owner=createEditorDocument(original);owner.select(key('a'));owner.select(key('b'),true);owner.select(key('a'),true);
 assert.deepEqual(owner.view().members,[key('b')]);assert.equal(owner.view().selected,key('b'));
 owner.select(key('a'),true);owner.select(key('a'),true);assert.equal(owner.view().selected,key('b'));
 assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);
 owner.select(key('a'),true);owner.command({kind:'style',property:'color',value:'red'});
 assert.deepEqual(owner.view().members,[key('b'),key('a')]);assert.equal(owner.view().selected,key('a'));
 owner.select(key('b'));assert.equal(owner.view().styles.color,undefined);owner.history('undo');assert.deepEqual(owner.candidate(),original);
 owner.select(key('a'),true);const before=owner.view(),after=owner.control({kind:'add'},before.selected,before.revision,before.selectionVersion);
 assert.equal(insertedLayer(before,after),after.selected);assert.deepEqual(after.members,[after.selected]);assert.equal(Object.hasOwn(owner.candidate(),'members'),false);
 owner.history('undo');assert.deepEqual(owner.candidate(),original);
});

test('Two widget roots combine placement patches and source/placement metadata commit as one recoverable Visual edit',()=>{
 const a=crypto.randomUUID(),b=crypto.randomUUID(),html=`<!-- keep --><section data-blokebot-widget="${a}"></section><section data-blokebot-widget="${b}"></section><script>const untouched='<section>';</script>`,original=fixture(html,'/* custom */ .shared { unknown:future(x) }',[widget(a),widget(b)]);
 const owner=createEditorDocument(original),keys=[`widget:${a}`,`widget:${b}`];owner.selectSet(keys);
 const planned=move(owner,keys.map(k=>geometry(k)));assert.equal(planned.kind,'planned');assert.deepEqual(owner.candidate(),original);
 owner.commitGesture(planned);assert.equal(owner.view().revision,1);assert.equal(owner.candidate().css,planned.presentation.css);
 assert.deepEqual(owner.candidate().widgets.map(w=>[w.authoring.x,w.authoring.y]),[['16px','8px'],['16px','8px']]);assert.equal(owner.candidate().css,original.css);assert(owner.candidate().html.includes("const untouched='<section>';"));
 owner.history('undo');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);owner.history('redo');assert.equal(owner.candidate().widgets[1].authoring.x,'16px');
});

test('An invalid second root, missing native target or stale membership refuses the whole source/metadata operation',()=>{
 const a=crypto.randomUUID(),b=crypto.randomUUID(),original=fixture(`<section data-blokebot-widget="${a}"></section><section data-blokebot-widget="${b}" style="left:&#48;px"></section>`,'',[widget(a),widget(b)]),owner=createEditorDocument(original),keys=[`widget:${a}`,`widget:${b}`];owner.selectSet(keys);
 const rejected=move(owner,keys.map(k=>geometry(k)));assert.equal(rejected.kind,'unmapped');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);
 const source=owner.candidate().html.replace('left:&#48;px','left:0px');owner.source('html',source);owner.focus('visual');
 const before=owner.candidate();assert.equal(move(owner,[geometry(keys[0])]).kind,'unmapped');assert.deepEqual(owner.candidate(),before);
 const pending=move(owner,keys.map(k=>geometry(k))),captured=owner.context();owner.selectSet([keys[1]],keys[1]);
 assert.equal(owner.view().selected,captured.selected);assert.equal(owner.view().revision,captured.revision);assert.equal(owner.matches(captured),false);
 owner.commitGesture(pending);assert.deepEqual(owner.candidate(),before);
 const live=owner.view();const refused=owner.control({kind:'add'},captured.selected,captured.revision,captured.selectionVersion);
 assert.equal(insertedLayer(live,refused),null);assert.deepEqual(owner.candidate(),before);
 owner.contextualDelete(captured.selected,captured.revision,captured.selectionVersion);assert.deepEqual(owner.candidate(),before);
});

test('Placement locks precede ancestor normalization in either selection order; a moving unlocked ancestor does not edit a locked descendant',()=>{
 for(const order of [['parent','child'],['child','parent']]) {
  const original=fixture('<section data-blokebot-element="parent" data-blokebot-locked="true"><div data-blokebot-element="child">Child</div></section>'),owner=createEditorDocument(original);owner.selectSet(order.map(key));
  const planned=move(owner,order.map(k=>geometry(key(k))));owner.commitGesture(planned);
  assert.equal(owner.view().revision,1);assert.equal(targetElement(owner.candidate(),key('parent')).values['data-blokebot-locked'],'true');
  owner.select(key('parent'));assert.equal(owner.view().styles.position,undefined);owner.select(key('child'));assert.equal(owner.view().styles['--blokebot-x'].trim(),'16px');owner.history('undo');assert.deepEqual(owner.candidate(),original);
 }
 const original=fixture('<section data-blokebot-element="parent"><div data-blokebot-element="child" data-blokebot-locked="true">Child</div></section>'),owner=createEditorDocument(original);owner.selectSet([key('child'),key('parent')]);
 const planned=move(owner,[geometry(key('parent')),geometry(key('child'))]);owner.commitGesture(planned);owner.select(key('child'));assert.equal(owner.view().styles.position,undefined);
 owner.select(key('parent'));assert.equal(owner.view().styles['--blokebot-x'].trim(),'16px');assert.equal(targetElement(owner.candidate(),key('child')).values.style,undefined);assert.equal(owner.candidate().css,original.css);
 owner.history('undo');assert.deepEqual(owner.candidate(),original);
});

test('Delete unions selected subtrees regardless locks and restores exact widget order/configuration/audio in one undo',()=>{
 const a=crypto.randomUUID(),b=crypto.randomUUID(),c=crypto.randomUUID(),original=fixture(`<main><div data-blokebot-element="parent" data-blokebot-locked="true"><section data-blokebot-widget="${a}"></section></div><section data-blokebot-widget="${b}"></section><section data-blokebot-widget="${c}"></section></main>`,'/* untouched */',[widget(a),{...widget(b),authoring:{...widget(b).authoring,isLocked:true}},widget(c)]);
 const owner=createEditorDocument(original);owner.selectSet([`widget:${a}`,key('parent'),`widget:${b}`]);owner.command({kind:'remove'});
 assert.equal(owner.view().revision,1);assert.deepEqual(owner.candidate().widgets,[widget(c)]);assert.equal(owner.candidate().css,original.css);assert.deepEqual(owner.view().members,[]);
 owner.history('undo');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);owner.history('redo');assert.deepEqual(owner.candidate().widgets,[widget(c)]);
});

test('Source reconciliation retains only uniquely mapped stable identities; unsafe offsets never survive source replacement or history',()=>{
 const owner=createEditorDocument(fixture('<main><p>A</p><div data-blokebot-element="stable">Stable</div></main>'));const unsafe=owner.view().layers.find(layer=>layer.tag==='p').key;
 owner.selectSet([unsafe,key('stable')],unsafe);owner.command({kind:'style',property:'color',value:'blue'});const promoted=owner.view().selected;assert(promoted.startsWith('element:'));assert.deepEqual(owner.view().members,[promoted,key('stable')]);
 owner.source('html','<!-- prefix -->'+owner.candidate().html);assert.deepEqual(owner.view().members,[promoted,key('stable')]);
 owner.source('html',owner.candidate().html+`<div data-blokebot-element="${promoted.slice(8)}">Ambiguous</div>`);assert.deepEqual(owner.view().members,[key('stable')]);
 owner.select(owner.view().layers.find(layer=>layer.tag==='main').key);owner.source('html','<!-- another -->'+owner.candidate().html);assert.equal(owner.view().selected,null);
 owner.history('undo');assert.equal(owner.view().selected,null);
});

test('Zero displacement and an all-locked set do not create source identities, history or dirty state',()=>{
 const original=fixture('<section data-blokebot-element="locked" data-blokebot-locked="true"></section>'),owner=createEditorDocument(original);owner.select(key('locked'));
 assert.equal(move(owner,[geometry(key('locked'))]).kind,'unchanged');owner.command({kind:'move',dx:0,dy:0,targets:[geometry(key('locked'))]});assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);assert.equal(owner.view().dirty,false);
});


test('Ordinary authored ID placement uses only lossless inline patches for every root and one exact undo',()=>{
 const original=fixture('<div id="a" data-blokebot-element="a" style="color: red; /* keep */ --custom:future(x)">A</div><div id="b" data-blokebot-element="b">B</div>','#a,#b { position:absolute;left:100px;top:50px;width:100px;height:50px }');
 const owner=createEditorDocument(original);owner.selectSet([key('a'),key('b')]);
 const planned=move(owner,[geometry(key('a'),100),geometry(key('b'),100)]);
 assert.equal(planned.kind,'planned');assert.equal(planned.presentation.css,original.css);assert(planned.edits.every(edit=>edit.buffer==='html'));
 assert.deepEqual(owner.candidate(),original);owner.commitGesture(planned);
 assert.equal(owner.view().revision,1);assert.equal(owner.candidate().css,original.css);
 for(const id of ['a','b'])assert(targetElement(owner.candidate(),key(id)).values.style.includes('--blokebot-x: 116px'));
 assert(targetElement(owner.candidate(),key('a')).values.style.startsWith('color: red; /* keep */ --custom:future(x)'));
 owner.history('undo');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);
});

test('Explicit movement scopes inline importance to edited placement, preserves unrelated authored priority, and keeps resize ordinary',async()=>{
 const {parse}=await import('../../../src/BlokeBot.Core/node_modules/css-tree/lib/index.js');
 const original=fixture('<div id="a" data-blokebot-element="a" style="left: 100px; color: red !important; /* retained */ opacity:.5">A</div><div id="b" data-blokebot-element="b">B</div>','#a,#b{position:absolute;left:100px!important;top:0px!important;width:100px;height:50px}');
 const owner=createEditorDocument(original);owner.selectSet([key('a'),key('b')]);
 const planned=move(owner,[geometry(key('a'),100),geometry(key('b'),100)],16);owner.commitGesture(planned);assert.equal(owner.view().revision,1);assert.equal(owner.candidate().css,original.css);
 for(const id of ['a','b']){
  const declarations=parse(targetElement(owner.candidate(),key(id)).values.style,{context:'declarationList'}).children.toArray();
  for(const property of ['position','left','right','top','bottom','translate','--blokebot-x','--blokebot-y'])assert(declarations.findLast(node=>node.property===property).important);
  assert.equal(declarations.some(node=>node.property==='width'),false);
 }
 const author=targetElement(owner.candidate(),key('a')).values.style;assert(author.includes('color: red !important; /* retained */ opacity:.5'));
 owner.history('undo');assert.deepEqual(owner.candidate(),original);
 owner.select(key('b'));owner.command({kind:'resize',handle:'se',dx:16,dy:8,observedWidth:100,observedHeight:50,computed:{width:'100px',height:'50px'}});
 const resized=parse(targetElement(owner.candidate(),key('b')).values.style,{context:'declarationList'}).children.toArray();assert(resized.every(node=>!node.important));owner.history('undo');assert.deepEqual(owner.candidate(),original);
});

test('Move and nudge displace from winning native positions, not losing inline values, in singleton and atomic mixed selections',()=>{
 for(const plural of [false,true])for(const [dx,dy] of [[16,8],[1,0]]){
  const id=crypto.randomUUID(),other=`widget:${id}`,original=fixture(`<div id="a" data-blokebot-element="a" style="left:20px;top:9px;color:red!important; /* retained */ opacity:.5">A</div><section data-blokebot-widget="${id}"></section>`,'#a{position:absolute;left:100px!important;top:50px!important;width:100px;height:50px}',[widget(id)]);
  const owner=createEditorDocument(original),keys=plural?[key('a'),other]:[key('a')];owner.selectSet(keys);
  const targets=keys.map(k=>({...geometry(k,k===key('a')?100:300),layoutY:50,styles:{left:k===key('a')?'100px':'300px',top:'50px',translate:'none'}}));
  const view=owner.view(),planned=owner.planGesture({kind:'move',dx,dy,targets},view.selected,view.revision,view.selectionVersion);
  assert.equal(planned.kind,'planned');assert.deepEqual(owner.candidate(),original);owner.commitGesture(planned);assert.equal(owner.view().revision,1);
  owner.select(key('a'));assert.equal(owner.view().styles['--blokebot-x'].trim(),`${100+dx}px`);
  if(dy)assert.equal(owner.view().styles['--blokebot-y'].trim(),`${50+dy}px`);
  assert.equal(owner.candidate().css,original.css);assert(targetElement(owner.candidate(),key('a')).values.style.includes('color:red!important; /* retained */ opacity:.5'));
  if(plural){assert.equal(owner.candidate().widgets[0].authoring.x,`${300+dx}px`);assert.equal(owner.candidate().widgets[0].authoring.y,dy?`${50+dy}px`:'0px');assert.deepEqual(owner.candidate().widgets[0].configuration,original.widgets[0].configuration);assert.deepEqual(owner.candidate().widgets[0].audio,original.widgets[0].audio);}
  owner.history('undo');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);
 }
});

test('Native effective center and end anchors retain offsets and independent translation while rejecting an incomplete later root',()=>{
 const original=fixture('<div data-blokebot-element="center" style="--blokebot-anchor-x:1;--blokebot-anchor-y:1;--blokebot-x:5px;--blokebot-y:2px;rotate:15deg;color:red!important"></div><div data-blokebot-element="end" style="--blokebot-anchor-x:2;--blokebot-anchor-y:2;--blokebot-x:8px;--blokebot-y:4px;transform:scale(.8)"></div>'),owner=createEditorDocument(original),keys=[key('center'),key('end')];owner.selectSet(keys);
 const targets=[{...geometry(keys[0],650),containingWidth:1200,containingHeight:800,styles:{left:'650px',top:'410px','--blokebot-anchor-x':'1','--blokebot-anchor-y':'1',translate:'-50% -50% 7px'}},{...geometry(keys[1],100),styles:{right:'120px',bottom:'80px','--blokebot-anchor-x':'2','--blokebot-anchor-y':'2',translate:'12px -6px 9px'}}];
 const incomplete=[targets[1],{...targets[0],containingWidth:undefined}];assert.equal(move(owner,incomplete).kind,'unmapped');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);
 const planned=move(owner,targets);assert.equal(planned.kind,'planned');owner.commitGesture(planned);assert.equal(owner.view().revision,1);
 owner.select(keys[0]);assert.equal(owner.view().styles['--blokebot-x'].trim(),'66px');assert.equal(owner.view().styles['--blokebot-y'].trim(),'18px');assert.equal(owner.view().styles.translate.trim(),'-50% -50% 7px');assert.equal(owner.view().styles.rotate,'15deg');
 owner.select(keys[1]);assert.equal(owner.view().styles['--blokebot-x'].trim(),'104px');assert.equal(owner.view().styles['--blokebot-y'].trim(),'72px');assert.equal(owner.view().styles.translate.trim(),'12px -6px 9px');assert.equal(owner.view().styles.transform,'scale(.8)');
 owner.history('undo');assert.deepEqual(owner.candidate(),original);assert.equal(owner.view().history.undo,false);
});
