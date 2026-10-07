import test from 'node:test';
import assert from 'node:assert/strict';
import { createEditorDocument } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/EditorDocument.js';
import { createSourceSession, EditorFocus } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/SourceSession.js';
import { targetElement } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/VisualStyles.js';
import { cssRanges } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/SourceRanges.js';
import { createPreviewBridge } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/EditorPreview.js';

const id='91c902f7-4020-416c-8b99-a5bdf008e55b';
const widget={id:{value:id},kind:{value:'giveaway'},configuration:{appearance:{css:'.card { opacity: .7; }'},secretSetting:'EDITOR ONLY'},authoring:{isVisible:true,isLocked:false,clipOverflow:false,x:'0px',y:'0px',width:'100px',height:'50px',rotationDegrees:0,scaleX:1,scaleY:1,horizontalAnchor:0,verticalAnchor:0},audio:{isMuted:false,volume:.3}};
const document={id:crypto.randomUUID(),html:`<!-- untouched --><main><section data-blokebot-widget="${id}"></section><p>Ordinary</p><script>const raw = "<section>";</script></main>`,css:`/* untouched */[data-blokebot-widget="${id}"] { color: red; unknown-prop: future(x); } @media (width > 30rem) { [data-blokebot-widget="${id}"] { opacity: .8; } }`,widgets:[widget],diagnostics:[]};

test('Local preview keeps authorized widgets only and rejects obsolete or nested-frame geometry',()=>{
 const previousWindow=globalThis.window,previousLocation=globalThis.location;
 const host=new EventTarget(),frame=new EventTarget(),sent=[],accepted=[];
 globalThis.window=host;globalThis.location={origin:'https://editor.example'};
 frame.contentWindow={postMessage:value=>sent.push(value)};
 frame.removeAttribute=()=>{};
 let view={revision:0,selected:'element:target',layers:[{key:'element:target',selector:'#target'}]};
 const bridge=createPreviewBridge(frame,()=>view,(...args)=>accepted.push(args),()=>{},()=>{},()=>{});
 const receive=(data,source=frame.contentWindow)=>{const event=new Event('message');Object.assign(event,{data,source,origin:location.origin});host.dispatchEvent(event);};
 try {
  const complete=message=>receive({kind:'blokebot-full-source-complete',previewId:'approved',lifetime:'renderer',requestId:message.requestId,revision:message.revision});
  bridge.set('approved',0,structuredClone(document));frame.dispatchEvent(new Event('load'));
  receive({kind:'blokebot-full-preview-status',previewId:'approved',state:'ready',diagnostics:[],lifetime:'renderer'});
  const first=sent.at(-1);assert.deepEqual(first.widgetIds,[id]);
  complete(first);
  const edited={...structuredClone(document),html:'<section id="target">new local source</section>'};
  view={...view,revision:1};assert.equal(bridge.render(edited,1),false);
  const current=sent.at(-1);assert.equal(current.html,edited.html);assert.deepEqual(current.widgetIds,[id]);
  bridge.set('late-reply',0,document);assert.equal(frame.src,'/full-overlays/preview/approved');
  const geometry=request=>({kind:'blokebot-full-observations',previewId:'approved',requestId:request.requestId,revision:request.revision,viewport:{width:1920,height:1080},items:[{key:'element:target',x:1,y:2,width:30,height:40,layoutX:1,layoutY:2,visible:true,styles:{left:'1px'}}]});
  const before=accepted.length;receive(geometry(first));receive(geometry(current),{});receive({...geometry(current),revision:0});assert.equal(accepted.length,before);
  receive(geometry(current));assert.equal(accepted.at(-1)[0][0].x,1);
  complete(current);
  edited.widgets[0].configuration={sourceId:'new binding'};view={...view,revision:2};assert.equal(bridge.render(edited,2),true);assert.deepEqual(sent.at(-1).widgetIds,[]);
  view={...view,revision:3};assert.equal(bridge.render(structuredClone(document),3),true);
  view={...view,revision:4};assert.equal(bridge.render({...edited,widgets:[]},4),true);assert.deepEqual(sent.at(-1).widgetIds,[]);
  bridge.dispose();const disposed=accepted.length;receive(geometry(sent.at(-1)));assert.equal(accepted.length,disposed);
 } finally {bridge.dispose();globalThis.window=previousWindow;globalThis.location=previousLocation;}
});

test('Newest pending source drains once while authorization, reconnect and replacement stay independent',()=>{
 const previousWindow=globalThis.window,previousLocation=globalThis.location;
 const host=new EventTarget(),frame=new EventTarget(),sent=[];
 globalThis.window=host;globalThis.location={origin:'https://editor.example'};
 frame.contentWindow={postMessage:value=>sent.push(value)};frame.removeAttribute=()=>{};
 let view={revision:0,layers:[]};
 const bridge=createPreviewBridge(frame,()=>view,()=>{},()=>{},()=>{},()=>{});
 const receive=data=>{const event=new Event('message');Object.assign(event,{data,source:frame.contentWindow,origin:location.origin});host.dispatchEvent(event);};
 const ready=(previewId,lifetime,revision)=>receive({kind:'blokebot-full-preview-status',previewId,state:'ready',diagnostics:[],lifetime,revision});
 const complete=message=>receive({kind:'blokebot-full-source-complete',previewId:message.previewId,lifetime:message.lifetime,requestId:message.requestId,revision:message.revision});
 const renders=()=>sent.filter(value=>value.kind==='blokebot-full-source'&&!value.authorizationOnly);
 const edit=(revision,widgets=document.widgets)=>{view={...view,revision};bridge.render({...document,html:`<p>${revision}</p>`,widgets},revision);};
 try {
  bridge.set('first',0,document);ready('first','old',0);const initial=renders().at(-1);
  edit(1);edit(2);edit(3);
  assert.equal(renders().length,1);
  edit(4,[]);assert.equal(sent.at(-1).authorizationOnly,true);assert.deepEqual(sent.at(-1).widgetIds,[]);
  complete({...initial,lifetime:'foreign'});complete({...initial,requestId:'obsolete'});assert.equal(renders().length,1);
  complete(initial);const newest=renders().at(-1);assert.equal(newest.html,'<p>4</p>');assert.equal(newest.revision,4);assert.equal(renders().length,2);
  edit(5,[]);complete(initial);assert.equal(renders().length,2);
  receive({kind:'blokebot-full-preview-status',previewId:'first',state:'reconnecting',diagnostics:[],lifetime:null,revision:4});
  ready('first','reconnected',4);const reconnected=renders().at(-1);assert.equal(reconnected.html,'<p>5</p>');assert.equal(reconnected.lifetime,'reconnected');
  edit(6,[]);complete(newest);assert.equal(renders().at(-1),reconnected);
  complete(reconnected);assert.equal(renders().at(-1).html,'<p>6</p>');
  bridge.set('replacement',6,{...document,html:'<p>replacement</p>'});ready('replacement','new',6);const replacement=renders().at(-1);
  edit(7);complete(reconnected);assert.equal(renders().at(-1),replacement);
  complete(replacement);assert.equal(renders().at(-1).html,'<p>7</p>');
  edit(8);const count=sent.length;bridge.dispose();complete(renders().at(-1));assert.equal(sent.length,count);
 } finally {bridge.dispose();globalThis.window=previousWindow;globalThis.location=previousLocation;}
});

test('Grouped placement and audio metadata undo preserves newer authored source and retries atomically',()=>{
 const owner=createEditorDocument(structuredClone(document));owner.select(`widget:${id}`);
 owner.command({kind:'layout',properties:{width:'200px'},metadata:{width:'200px'}});
 const after=owner.candidate();owner.source('html',after.html.replace('200px','300px'));owner.focus(EditorFocus.Visual);
 assert.match(owner.history('undo').feedback,/newer overlapping/);
 assert.equal(owner.candidate().widgets[0].authoring.width,'200px');assert.match(owner.candidate().html,/300px/);
 owner.focus(EditorFocus.Html);owner.history('undo');owner.focus(EditorFocus.Visual);owner.history('undo');
 assert.equal(owner.candidate().widgets[0].authoring.width,'100px');assert.equal(owner.candidate().html,document.html);assert.equal(owner.candidate().css,document.css);
 owner.command({kind:'audio',property:'volume',value:.8});owner.source('html','<!-- new -->'+owner.candidate().html);owner.focus(EditorFocus.Visual);owner.history('undo');
 assert.equal(owner.candidate().widgets[0].audio.volume,.3);assert.equal(owner.candidate().html,'<!-- new -->'+document.html);
});

test('Metadata provenance blocks same-value newer replacement and whole group partial undo',()=>{
 const session=createSourceSession(document);const path=['widgets',id,'configuration'];
 session.apply(EditorFocus.Visual,0,[{buffer:'html',start:0,end:0,before:'',after:'<!-- visual -->'}],[{path,value:{...widget.configuration,title:'first'}}]);
 const next=session.snapshot();session.apply(EditorFocus.Css,next.revision,[],[{path,value:{...widget.configuration,title:'newer'}}]);
 session.apply(EditorFocus.Css,session.snapshot().revision,[],[{path,value:{...widget.configuration,title:'first'}}]);
 assert.equal(session.undo(EditorFocus.Visual).kind,'conflict');assert.match(session.snapshot().html,/visual/);
 session.undo(EditorFocus.Css);session.undo(EditorFocus.Css);assert.equal(session.undo(EditorFocus.Visual).kind,'applied');
 assert.equal(session.snapshot().html,document.html);assert.deepEqual(session.snapshot().widgets[0].configuration,widget.configuration);
});

test('Duplicate, reorder and remove use source anchors with independent configuration/audio and lossless surrounding source',()=>{
 const owner=createEditorDocument(structuredClone(document));owner.select(`widget:${id}`);owner.command({kind:'duplicate'});
 const copy=owner.candidate().widgets[1];assert.notEqual(copy.id.value,id);assert.deepEqual(copy.audio,widget.audio);assert.deepEqual(copy.configuration,widget.configuration);
 assert.match(owner.candidate().html,/const raw = "<section>"/);assert.match(owner.candidate().css,/unknown-prop: future\(x\)/);assert.equal(owner.candidate().css.match(/@media/g).length,1);
 assert.match(owner.candidate().css,new RegExp(copy.id.value));
 owner.command({kind:'audio',property:'volume',value:.9});assert.equal(owner.candidate().widgets[0].audio.volume,.3);
 owner.command({kind:'reorder',direction:-1});assert.equal(owner.view().selected,`widget:${copy.id.value}`);
 assert(owner.candidate().html.indexOf(copy.id.value)<owner.candidate().html.indexOf(id));
 owner.command({kind:'remove'});assert.equal(owner.candidate().widgets.length,1);owner.history('undo');assert.equal(owner.candidate().widgets.length,2);
 assert.equal(owner.candidate().widgets.find(w=>w.id.value===copy.id.value).audio.volume,.9);
});

test('Ordinary element styles gain stable source identity without replacing author IDs or raw source',()=>{
 const owner=createEditorDocument({...structuredClone(document),html:'<!-- preserved --><main><p id=author>Ordinary</p><aside>Sibling</aside></main>'});
 owner.select(owner.view().layers.find(n=>n.tag==='p').key);owner.command({kind:'style',property:'color',value:'rgb(1, 2, 3)'});
 const key=owner.view().selected;assert.match(key,/element:/);assert.match(owner.candidate().html,/id=author/);assert.match(owner.candidate().html,/<!-- preserved -->/);
 owner.command({kind:'reorder',direction:1});assert.equal(owner.view().selected,key);owner.command({kind:'style',property:'opacity',value:'.5'});
 assert.match(owner.candidate().css,/opacity: .5/);assert.equal(owner.candidate().widgets.length,1);
 owner.source('html','<!-- newer -->'+owner.candidate().html);assert.equal(owner.view().selected,key);
});

test('Motion is editable authored CSS and incomplete source never becomes a regenerated document',()=>{
 const owner=createEditorDocument(structuredClone(document));owner.select(`widget:${id}`);
 owner.command({kind:'motion',phase:'state',preset:'rise',duration:780,easing:'cubic-bezier(.2,.5,.4,1)'});
 assert.match(owner.candidate().css,/@keyframes/);assert.match(owner.candidate().css,/780ms cubic-bezier/);assert.match(owner.candidate().css,/data-blokebot-phase="state"/);
 const css=owner.candidate().css+'\n.unfinished { color: red; broken';owner.source('css',css);const html=owner.candidate().html;
 assert.match(owner.command({kind:'style',property:'color',value:'blue'}).feedback,/cannot be mapped/);
 assert.equal(owner.candidate().css,css);assert.equal(owner.candidate().html,html);
});


test('Inline declarations and nested widget styles are patched without losing comments or clone binding',()=>{
 const owner=createEditorDocument({...structuredClone(document),html:`<main><section style='width:680px; /* kept */ height:340px' data-blokebot-widget="${id}"><b data-blokebot-element="nested-old">Text</b></section></main>`,css:`[data-blokebot-widget="${id}"] { --unknown: future(x); & [data-blokebot-element="nested-old"] { color:red } }`});
 owner.select(`widget:${id}`);owner.command({kind:'layout',properties:{width:'480px',height:'200px'},metadata:{width:'480px',height:'200px'}});
 assert.match(owner.candidate().html,/width:480px; \/\* kept \*\/ height:200px/);assert.equal(owner.candidate().widgets[0].authoring.width,'480px');
 owner.command({kind:'duplicate'});const copy=owner.candidate().widgets[1];
 assert.match(owner.candidate().css,new RegExp(copy.id.value));assert.equal(owner.candidate().css.match(/nested-old/g).length,1);
 assert.equal(owner.candidate().html.match(/nested-old/g).length,1);assert.equal(owner.candidate().css.match(/--unknown: future/g).length,2);
 owner.history('undo');owner.history('undo');assert.match(owner.candidate().html,/width:680px/);
 const encoded=createEditorDocument({...structuredClone(document),html:`<section data-blokebot-widget="${id}" style='width:&#54;80px'></section>`});encoded.select(`widget:${id}`);const before=encoded.candidate();assert.match(encoded.command({kind:'style',property:'width',value:'480px'}).feedback,/cannot be mapped/);assert.deepEqual(encoded.candidate(),before);
});

test('Inspector latency accepts sequential visual intent but fences newer source, selection and configuration snapshots',()=>{
 const owner=createEditorDocument(structuredClone(document)),key=`widget:${id}`;owner.select(key);
 owner.control({kind:'position',axis:'x',value:'20px'},key,0);
 owner.control({kind:'position',axis:'y',value:'40px'},key,0);
 assert.equal(owner.candidate().widgets[0].authoring.x,'20px');assert.equal(owner.candidate().widgets[0].authoring.y,'40px');
 owner.history('undo');owner.control({kind:'configuration-field',path:['title'],value:'after undo'},key,2);
 assert.equal(owner.candidate().widgets[0].configuration.title,'after undo');
 const oldConfiguration=structuredClone(widget.configuration),beforeAudio=owner.view().revision;
 owner.control({kind:'audio',property:'volume',value:.8},key,beforeAudio);
 const newer=owner.candidate();assert.match(owner.control({kind:'configuration',value:oldConfiguration},key,beforeAudio).feedback,/newer work/);assert.deepEqual(owner.candidate(),newer);
 const beforeSource=owner.view().revision;owner.source('css',owner.candidate().css.replace('red','purple'));
 const source=owner.candidate();assert.match(owner.control({kind:'layout',properties:{color:'green'},metadata:{width:'999px'}},key,beforeSource).feedback,/newer work/);assert.deepEqual(owner.candidate(),source);
 const ordinary=owner.view().layers.find(n=>n.tag==='p').key;owner.select(ordinary);assert.match(owner.control({kind:'audio',property:'volume',value:0},key,owner.view().revision).feedback,/newer work/);assert.deepEqual(owner.candidate(),source);
});

test('Responsive direct movement and grouped resize preserve independent transforms and layout metadata',()=>{
 const owner=createEditorDocument(structuredClone(document)),key=`widget:${id}`;owner.select(key);
 owner.command({kind:'anchor',axis:'x',value:2,computed:{left:'96px',right:'48px'}});
 owner.command({kind:'style',property:'rotate',value:'25deg'});
 owner.command({kind:'move',dx:16,dy:0,computed:{right:'96px'}});
 assert.equal(owner.candidate().widgets[0].authoring.x,'32px');assert.match(owner.candidate().css,/rotate: 25deg/);
 const before=owner.candidate();owner.command({kind:'resize',width:'180px',height:'50px',handle:'e',dx:80,dy:0,computed:{right:'80px'}});
 assert.equal(owner.candidate().widgets[0].authoring.width,'180px');assert.equal(owner.candidate().widgets[0].authoring.x,'-48px');
 owner.history('undo');assert.deepEqual(owner.candidate(),before);
});


test('Destination setup confirmation has focused metadata history and preserves newer source/audio without implicit resolution',()=>{
 const imported={...structuredClone(document),widgets:[{...structuredClone(widget),requiresSetup:true}]};
 const owner=createEditorDocument(imported);owner.select(`widget:${id}`);
 owner.command({kind:'audio',property:'volume',value:.7});
 owner.command({kind:'style',property:'opacity',value:'.5'});
 assert.equal(owner.candidate().widgets[0].requiresSetup,true);
 owner.command({kind:'setup',value:false});
 owner.source('html','<!-- newer -->'+owner.candidate().html);
 owner.focus(EditorFocus.Visual);owner.history('undo');
 assert.equal(owner.candidate().widgets[0].requiresSetup,true);
 assert.equal(owner.candidate().widgets[0].audio.volume,.7);
 assert.match(owner.candidate().html,/newer/);assert.match(owner.candidate().css,/opacity: .5/);
 owner.history('redo');assert.equal(owner.candidate().widgets[0].requiresSetup,false);
 const pending=owner.view();owner.source('css',owner.candidate().css+'\n/* newer CSS */');
 owner.control({kind:'setup',value:true},pending.selected,pending.revision);
 assert.equal(owner.candidate().widgets[0].requiresSetup,false);
});

test('Held manipulation plans stay transient and one final apply preserves newer unrelated source on undo',()=>{
 const html=`<!-- kept --><section data-blokebot-widget="${id}" style='left:40px; /* inline kept */ top:20px; width:100px; height:50px'></section><script>const raw = '<section>';</script>`;
 const owner=createEditorDocument({...structuredClone(document),html});const key=`widget:${id}`;owner.select(key);
 const before=owner.candidate(),revision=owner.view().revision;
 const move=dx=>owner.planGesture({kind:'move',dx,dy:8,computed:{left:'40px',top:'20px',width:'100px',height:'50px'},layoutX:40,layoutY:20},key,revision);
 move(8);move(16);const last=move(24);
 assert.deepEqual(owner.candidate(),before);assert.equal(owner.view().revision,revision);
 owner.commitGesture(last);
 assert.equal(owner.view().revision,revision+1);assert.equal(owner.candidate().css,last.presentation.css);
 assert.equal(owner.candidate().widgets[0].authoring.x,'64px');assert.equal(owner.candidate().widgets[0].authoring.y,'28px');
 assert.equal(targetElement(owner.session.snapshot(),key).values.style,last.presentation.patches[0].attributes.style);
 assert.match(owner.candidate().html,/inline kept/);
 assert.match(owner.candidate().html,/const raw = '<section>'/);
 owner.source('css',owner.candidate().css+'\n/* unrelated newer edit */');owner.focus(EditorFocus.Visual);owner.history('undo');
 assert.equal(owner.candidate().html,before.html);assert.equal(owner.candidate().css,before.css+'\n/* unrelated newer edit */');
 assert.deepEqual(owner.candidate().widgets,before.widgets);
});

test('Held west resize clamps displacement with size and commits its exact anchored transform-preserving plan',()=>{
 const owner=createEditorDocument(structuredClone(document)),key=`widget:${id}`;owner.select(key);
 owner.command({kind:'anchor',axis:'x',value:1});owner.command({kind:'style',property:'rotate',value:'25deg'});
 const before=owner.candidate(),revision=owner.view().revision;
 const planned=owner.planGesture({kind:'resize',handle:'w',dx:180,dy:0,computed:{width:'100px',height:'50px'},observedWidth:112,observedHeight:87},key,revision);
 assert.deepEqual(owner.candidate(),before);
 owner.commitGesture(planned);
 assert.equal(owner.candidate().widgets[0].authoring.width,'1px');assert.equal(owner.candidate().widgets[0].authoring.x,'49.5px');
 assert.equal(owner.candidate().css,planned.presentation.css);assert.match(owner.candidate().css,/rotate: 25deg/);
 owner.history('undo');assert.deepEqual(owner.candidate(),before);
});

test('Pending manipulation cannot commit over a newer source revision or a changed selection',()=>{
 const owner=createEditorDocument(structuredClone(document)),key=`widget:${id}`;owner.select(key);
 const command={kind:'move',dx:16,dy:8,computed:{left:'40px',top:'20px'}};
 const sourcePending=owner.planGesture(command,key,owner.view().revision);
 owner.source('css',owner.candidate().css+'\n/* newer source */');const newer=owner.candidate(),sourceView=owner.view();
 owner.commitGesture(sourcePending);assert.deepEqual(owner.candidate(),newer);
 assert.equal(owner.view().revision,sourceView.revision);assert.equal(owner.view().selected,sourceView.selected);
 const selectionPending=owner.planGesture(command,key,owner.view().revision);
 owner.select(owner.view().layers.find(layer=>layer.tag==='p').key);const selectedView=owner.view();
 owner.commitGesture(selectionPending);assert.deepEqual(owner.candidate(),newer);
 assert.equal(owner.view().revision,selectedView.revision);assert.equal(owner.view().selected,selectedView.selected);
});


test('Placement keeps authored stylesheet priority and losslessly updates effective inline declarations',()=>{
 const css=`/* retained */#panel { left:40px !important; top:20px; --unknown:future(x) } @supports (display:grid) { .unfamiliar { unknown:future(y) } }`;
 const html=`<section id="panel" data-blokebot-widget="${id}" style='left: /* position */ 40px !important; left:90px; top:20px; --unknown:future(x) '></section>`;
 const owner=createEditorDocument({...structuredClone(document),html,css}),key=`widget:${id}`;owner.select(key);
 const original=owner.candidate(),first=owner.planGesture({kind:'move',dx:24,dy:8,computed:{left:'40px',top:'20px'}},key,owner.view().revision);
 assert.deepEqual(owner.candidate(),original);owner.commitGesture(first);
 assert.equal(owner.candidate().css,css);
 const inline=targetElement(owner.candidate(),key).values.style;
 assert.match(inline,/left: \/\* position \*\/ var\(--blokebot-x\) !important; left:90px/);
 assert.equal(owner.candidate().widgets[0].authoring.x,'64px');
 assert(inline.includes('--unknown:future(x) '));assert.equal(first.presentation.patches[0].attributes.style,inline);
 owner.history('undo');assert.deepEqual(owner.candidate(),original);
});

test('Current view follows metadata-only edits, source diagnostics, selection and saved baseline without losing focused history',()=>{
 const owner=createEditorDocument(structuredClone(document)),key=`widget:${id}`;
 owner.select(key);assert.equal(owner.view().dirty,false);assert.equal(owner.view().styles.color,'red');
 owner.command({kind:'audio',property:'volume',value:.8});owner.command({kind:'lock',value:true});
 assert.equal(owner.view().widget.audio.volume,.8);assert.equal(owner.view().locked,true);assert.equal(owner.view().dirty,true);
 const saved=owner.candidate();owner.saved(saved);assert.equal(owner.view().dirty,false);
 owner.source('css','[broken');assert(owner.view().diagnostics.some(d=>d.buffer==='css'));assert.equal(owner.view().focus,'css');
 owner.stale();const revision=owner.view().revision;owner.focus('html');
 assert.equal(owner.view().revision,revision);assert.equal(owner.view().focus,'html');assert.match(owner.view().feedback,/newer work/);
 owner.focus('css');owner.history('undo');assert.equal(owner.candidate().css,saved.css);assert.equal(owner.view().dirty,false);
 assert.deepEqual(owner.view().diagnostics.filter(d=>d.buffer==='css'),[]);assert.equal(owner.view().styles.color,'red');
 owner.focus('visual');owner.history('undo');assert.equal(owner.view().locked,false);assert.equal(owner.view().dirty,true);
 owner.history('redo');assert.equal(owner.view().locked,true);assert.equal(owner.view().dirty,false);
 owner.source('html',owner.candidate().html.replace('<p>Ordinary','<p data-blokebot-locked="true">Updated'));
 owner.select(owner.view().layers.find(layer=>layer.tag==='p').key);
 assert.equal(owner.view().widget,null);assert.equal(owner.view().locked,true);assert.deepEqual(owner.view().styles,{});
 assert.match(owner.candidate().html,/Updated/);
 owner.select(key);assert.equal(owner.view().styles.color,'red');assert.equal(owner.view().widget.audio.volume,.8);
});

test('A held styling plan preserves source until completion and cannot overwrite newer Visual work',()=>{
 const owner=createEditorDocument(structuredClone(document));owner.select(`widget:${id}`);
 const original=owner.candidate(),revision=owner.view().revision;
 const first=owner.planStyle({color:'rgba(20, 40, 60, .5)'},`widget:${id}`,revision);
 const last=owner.planStyle({color:'rgba(80, 40, 60, .8)'},`widget:${id}`,revision);
 assert.deepEqual(owner.candidate(),original);
 owner.commitGesture(last);assert.equal(owner.view().revision,revision+1);
 assert.equal(owner.view().styles.color,'rgba(80, 40, 60, .8)');
 owner.commitGesture(first);assert.equal(owner.view().styles.color,'rgba(80, 40, 60, .8)');
 owner.history('undo');assert.deepEqual(owner.candidate(),original);
});

test('Background longhands override inline important shorthand without rewriting its other components',()=>{
 const html='<section data-blokebot-element="paint" style="background: url(image.png) 20% 30% / cover no-repeat red !important; /* keep */ transform:rotateX(20deg)">Paint</section>';
 const original={...structuredClone(document),html,widgets:[]},owner=createEditorDocument(original);owner.select('element:paint');
 const plan=owner.planStyle({'background-color':'rgba(30, 60, 90, .5)'},'element:paint',0);owner.commitGesture(plan);
 const result=targetElement(owner.candidate(),'element:paint');
 assert.equal(result.values.style,'background: url(image.png) 20% 30% / cover no-repeat red !important; /* keep */ transform:rotateX(20deg); background-color: rgba(30, 60, 90, .5) !important;');
 assert.equal(owner.candidate().css,original.css);
 owner.history('undo');assert.deepEqual(owner.candidate(),original);
});

test('Completed individual transform styling keeps unrelated transforms and atomic widget authoring history',()=>{
 const owner=createEditorDocument({...structuredClone(document),css:document.css+`[data-blokebot-widget="${id}"] { transform: rotateX(25deg) skewY(5deg); }`});owner.select(`widget:${id}`);
 const before=owner.candidate(),planned=owner.planStyle({rotate:'-40deg'},`widget:${id}`,owner.view().revision);
 owner.commitGesture({...planned,metadata:[{path:['widgets',id,'authoring','rotationDegrees'],value:-40}]});
 assert.equal(owner.view().styles.transform,'rotateX(25deg) skewY(5deg)');assert.equal(owner.view().widget.authoring.rotationDegrees,-40);
 owner.history('undo');assert.deepEqual(owner.candidate(),before);
 owner.history('redo');assert.equal(owner.view().styles.rotate,'-40deg');assert.equal(owner.view().widget.authoring.rotationDegrees,-40);
});

test('Background longhand edits remain effective after later or important authored stylesheet shorthand',()=>{
 for(const declarations of ['background-image: url(old.png); background: red;', 'background: red !important; background-image: url(old.png);']){
  const original={...structuredClone(document),html:'<section data-blokebot-element="paint">Paint</section>',css:`/* keep */ [data-blokebot-element="paint"] { ${declarations} transform: skewY(5deg); }`,widgets:[]};
  const owner=createEditorDocument(original);owner.select('element:paint');
  owner.commitGesture(owner.planStyle({'background-image':'linear-gradient(red, blue)'},'element:paint',0));
  assert(owner.candidate().css.includes(declarations));assert.match(owner.candidate().css,/transform: skewY\(5deg\);/);
  assert.match(owner.candidate().css,new RegExp(`background-image: linear-gradient\\(red, blue\\)${declarations.includes('!important')?' !important':''};`));
  owner.history('undo');assert.deepEqual(owner.candidate(),original);
 }
});

test('Contextual subtree deletion rejects a moved source offset and preserves exact widget metadata on undo',()=>{
 const owner=createEditorDocument(structuredClone(document));
 const selected=owner.view().layers.find(layer=>layer.tag==='main').key;
 owner.select(selected);
 const captured=owner.view();
 owner.source(EditorFocus.Html,'<!-- newer -->'+document.html);
 const newer=owner.candidate();
 owner.contextualDelete(selected,captured.revision);
 assert.deepEqual(owner.candidate(),newer);
 assert(owner.view().feedback);
 const fresh=owner.view().layers.find(layer=>layer.tag==='main').key;
 owner.select(fresh);
 const current=owner.view();
 owner.contextualDelete(fresh,current.revision);
 assert.equal(owner.candidate().widgets.length,0);
 assert.equal(owner.candidate().css,document.css);
 owner.history('undo');
 assert.deepEqual(owner.candidate(),newer);
 owner.history('redo');
 assert.equal(owner.candidate().widgets.length,0);
});

test('Focused availability survives menu observation and a newer overlap stays recoverable in its owner',()=>{
 const owner=createEditorDocument(structuredClone(document));
 owner.select(`widget:${id}`);
 owner.command({kind:'style',property:'color',value:'green'});
 const visual=owner.view();
 assert.equal(visual.history.undo,true);
 const revision=visual.revision;
 assert.equal(owner.view().revision,revision);
 owner.source(EditorFocus.Css,owner.candidate().css.replace('green','purple'));
 owner.focus(EditorFocus.Visual);
 assert.equal(owner.view().history.undo,true);
 const newer=owner.candidate();
 owner.history('undo');
 assert.deepEqual(owner.candidate(),newer);
 assert.equal(owner.view().history.undo,true);
 owner.focus(EditorFocus.Css);
 owner.history('undo');
 assert.equal(owner.view().history.redo,true);
 owner.focus(EditorFocus.Visual);
 owner.history('undo');
 assert.equal(owner.candidate().css,document.css);
 assert.equal(owner.view().history.undo,false);
 assert.equal(owner.view().history.redo,true);
});
