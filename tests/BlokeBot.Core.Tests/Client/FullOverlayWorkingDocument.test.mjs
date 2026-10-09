import test from 'node:test';
import assert from 'node:assert/strict';
import { createEditorDocument } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/EditorDocument.js';
import { createSourceSession, EditorFocus } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/SourceSession.js';

const id='91c902f7-4020-416c-8b99-a5bdf008e55b';
const widget={id:{value:id},kind:{value:'giveaway'},configuration:{appearance:{css:'.card { opacity: .7; }'},secretSetting:'EDITOR ONLY'},authoring:{isVisible:true,isLocked:false,clipOverflow:false,x:'0px',y:'0px',width:'100px',height:'50px',rotationDegrees:0,scaleX:1,scaleY:1,horizontalAnchor:0,verticalAnchor:0},audio:{isMuted:false,volume:.3}};
const document={id:crypto.randomUUID(),html:`<!-- untouched --><main><section data-blokebot-widget="${id}"></section><p>Ordinary</p><script>const raw = "<section>";</script></main>`,css:`/* untouched */[data-blokebot-widget="${id}"] { color: red; unknown-prop: future(x); } @media (width > 30rem) { [data-blokebot-widget="${id}"] { opacity: .8; } }`,widgets:[widget],diagnostics:[]};

test('Grouped source and audio metadata undo preserves newer CSS and retries atomically',()=>{
 const owner=createEditorDocument(structuredClone(document));owner.select(`widget:${id}`);
 owner.command({kind:'layout',properties:{color:'green'},metadata:{width:'200px'}});
 const after=owner.candidate();owner.source('css',after.css.replace('green','purple'));owner.focus(EditorFocus.Visual);
 assert.match(owner.history('undo').feedback,/newer overlapping/);
 assert.equal(owner.candidate().widgets[0].authoring.width,'200px');assert.match(owner.candidate().css,/purple/);
 owner.focus(EditorFocus.Css);owner.history('undo');owner.focus(EditorFocus.Visual);owner.history('undo');
 assert.equal(owner.candidate().widgets[0].authoring.width,'100px');assert.equal(owner.candidate().css,document.css);
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
