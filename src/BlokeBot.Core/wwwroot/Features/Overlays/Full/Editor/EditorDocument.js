import { createSourceSession, EditorFocus, htmlRanges, attributeEdit } from './FullOverlayEditorCore.js';
import { planStyles, combineStyles, styleValues, elementKey, targetElement } from './VisualStyles.js';
import { parsedCss } from './SourceRanges.js';
import { motionPreset } from './MotionCommands.js';
import { addLayer, removeLayers, selectionRoots, duplicateLayer, reorderLayer } from './LayerCommands.js';

export function createEditorDocument(document) {
    const session = createSourceSession(document);
    let members=[],selected=null,selectionVersion=0,saved=JSON.stringify({...document,diagnostics:[]}),feedback='',focus=EditorFocus.Visual,externalRevision=0;
    const candidate=()=>{const value=session.snapshot();return {...document,html:value.html,css:value.css,widgets:value.widgets,diagnostics:[]};};
    let derived=null;
    function sourceData() {
        const snapshot=session.snapshot();
        if(derived?.revision===snapshot.revision)return derived;
        const parsed=htmlRanges(snapshot.html),stylesheet=parsedCss(snapshot.css),keys=new Map(parsed.elements.map(node=>[node.start,elementKey(node)]));
        const layers=parsed.elements.map(node=>({key:elementKey(node),parent:keys.get(node.parent)??null,label:node.values['aria-label']??node.values.id??node.name,
            tag:node.name,selector:node.selector,widgetId:node.values['data-blokebot-widget']??null}));
        derived={revision:snapshot.revision,snapshot,parsed,stylesheet,layers};return derived;
    }
    function element(key) {const nodes=sourceData().parsed.elements.filter(node=>elementKey(node)===key);return nodes.length===1?nodes[0]:null;}
    function locked(node) {const id=node.values['data-blokebot-widget'];return id?sourceData().snapshot.widgets.find(w=>w.id.value===id)?.authoring.isLocked??false:node.values['data-blokebot-locked']==='true';}
    function setSelection(keys,active=keys.at(-1)??null) {
        const next=[...new Set(keys)].filter(key=>element(key));
        active=next.includes(active)?active:next.at(-1)??null;
        if(active!==selected||JSON.stringify(next)!==JSON.stringify(members)){members=next;selected=active;selectionVersion++;}
    }
    function context() {return {members:[...members],selected,selectionVersion,revision:session.snapshot().revision,focus};}
    function matches(value) {return value&&value.selected===selected&&value.selectionVersion===selectionVersion&&value.revision===session.snapshot().revision&&value.focus===focus
        &&JSON.stringify(value.members)===JSON.stringify(members);}
    function view() {
        const {snapshot,parsed,stylesheet,layers}=sourceData(),node=element(selected),widget=snapshot.widgets.find(w=>`widget:${w.id.value}`===selected)??null;
        return {revision:snapshot.revision,dirty:JSON.stringify({...document,html:snapshot.html,css:snapshot.css,widgets:snapshot.widgets,diagnostics:[]})!==saved,
            selected,members:[...members],selectionVersion,layers,widget,locked:!!node&&locked(node),styles:styleValues(snapshot,selected,{element:node,stylesheet}),
            diagnostics:[...parsed.diagnostics.filter(d=>d.code!=='missing-doctype').map(d=>({code:d.code,buffer:'html',offset:d.start})),
                ...stylesheet.diagnostics.map(d=>({code:d.code,buffer:'css',offset:d.start}))],feedback,focus,history:session.availability(focus)};
    }
    function finish(result,previousHtml=session.snapshot().html,mapping=[],singleton=undefined) {
        if(result.kind==='applied') {
            const identities=new Map(mapping),htmlChanged=previousHtml!==session.snapshot().html;
            const retained=members.map(key=>identities.get(key)??key).filter(key=>!htmlChanged||!key.startsWith('source:'));
            const active=identities.get(selected)??selected;
            setSelection(singleton===undefined?retained:singleton?[singleton]:[],singleton===undefined?active:singleton);feedback='';
        } else if(result.kind==='conflict')feedback='Undo kept a newer overlapping edit. Undo that edit in its editor, then retry.';
        else if(result.kind==='unmapped'||result.kind==='invalid')feedback='This source region cannot be mapped safely. Keep editing the source, then retry.';
        return view();
    }
    function plan(planned,singleton=false) {
        if(planned.kind!=='planned')return finish(planned);
        const snapshot=session.snapshot(),mapping=planned.selectionMap??[[selected,planned.selected??selected]];
        return finish(session.apply(EditorFocus.Visual,snapshot.revision,planned.edits,planned.metadata??[]),snapshot.html,mapping,singleton?planned.selected:undefined);
    }
    function style(properties,metadata=[],placement=null) {
        const snapshot=session.snapshot(),planned=planStyles(snapshot,selected,properties,{element:element(selected),stylesheet:sourceData().stylesheet},placement);
        return plan({...planned,metadata});
    }
    function widgetValue(path,value) {
        const id=selected?.startsWith('widget:')?selected.slice(7):null;
        return id ? finish(session.apply(EditorFocus.Visual,session.snapshot().revision,[],[{path:['widgets',id,...path],value}])) : view();
    }
    function layoutPlan(snapshot,command,key=selected,prepared={element:element(key),stylesheet:sourceData().stylesheet}) {
        if(command.kind==='move'&&!command.dx&&!command.dy)return {kind:'unchanged'};
        const id=key?.startsWith('widget:')?key.slice(7):null;
        const values={...command.computed,...styleValues(snapshot,key,prepared)},widget=snapshot.widgets.find(w=>w.id.value===id);
        const properties={position:'absolute'},metadata=[];
        const axes=command.kind==='move'||command.kind==='resize'?['x','y']:[command.axis];
        const nativeTranslate=command.kind==='move'&&command.computed?.translate!==undefined
            ? parsedCss(command.computed.translate,'value').tree?.children.toArray()??[]:null;
        const translateComponent=index=>{
            const part=nativeTranslate?.[index];
            return part?.loc&&part.type!=='Identifier'?command.computed.translate.slice(part.loc.start.offset,part.loc.end.offset):'0px';
        };
        if(command.kind==='resize') {
            const size=(axis,negative,positive,fallback,movement)=>{
                if(command[axis])return {value:command[axis],movement};
                const base=parseFloat(command.computed?.[axis])||fallback;
                const value=`${Math.max(1,Math.round(base+(negative?-movement:positive?movement:0)))}px`;
                return {value,movement:negative?base-parseFloat(value):positive?parseFloat(value)-base:0};
            };
            const width=size('width',command.handle.includes('w'),command.handle.includes('e'),command.observedWidth,command.dx);
            const height=size('height',command.handle.includes('n'),command.handle.includes('s'),command.observedHeight,command.dy);
            command={...command,width:width.value,height:height.value,dx:width.movement,dy:height.movement};
            Object.assign(properties,{width:command.width,height:command.height});
            if(id)metadata.push(...['width','height'].map(axis=>({path:['widgets',id,'authoring',axis],value:command[axis]})));
        }
        for(const axis of axes) {
            const horizontal=axis==='x';if(!horizontal&&axis!=='y')return {kind:'invalid'};
            if(command.kind==='move'&&!(horizontal?command.dx:command.dy))continue;
            const nativeAnchor=command.kind==='move'?command.computed?.[`--blokebot-anchor-${axis}`]?.trim():null;
            const anchor=command.kind==='anchor'?command.value:Number(nativeAnchor||(values[`--blokebot-anchor-${axis}`]??widget?.authoring[horizontal?'horizontalAnchor':'verticalAnchor']??0));
            let value=command.kind==='position'?command.value:values[`--blokebot-${axis}`]??values[horizontal?(anchor===2?'right':'left'):(anchor===2?'bottom':'top')]??widget?.authoring[axis]??'0px';
            if(command.kind==='move') {
                const edge=command.computed?.[horizontal?(anchor===2?'right':'left'):(anchor===2?'bottom':'top')];
                const parsed=parsedCss(edge??'','value'),dimension=parsed.tree?.children.first;
                let base=dimension?.type==='Dimension'&&dimension.unit==='px'&&parsed.tree.children.size===1?Number(dimension.value):edge==='auto'&&anchor===0?horizontal?command.layoutX:command.layoutY:null;
                if(anchor===1){const size=horizontal?command.containingWidth:command.containingHeight;if(!Number.isFinite(size)||size<0)return {kind:'unmapped',code:'missing-native-geometry'};base=base===null?null:base-size/2;}
                if(![0,1,2].includes(anchor)||!Number.isFinite(base))return {kind:'unmapped',code:'missing-native-geometry'};
                value=`${base}px`;
            }
            if(command.kind==='move'||command.kind==='resize') {
                const negative=command.handle?.includes(horizontal?'w':'n'),positive=command.handle?.includes(horizontal?'e':'s');
                const movement=horizontal?command.dx:command.dy;
                const delta=(command.kind==='resize'?anchor===1?(negative||positive?movement/2:0):anchor===2?(positive?movement:0):(negative?movement:0):movement)*(anchor===2?-1:1);if(!delta)continue;
                if(value==='auto')value=`${horizontal?command.layoutX??0:command.layoutY??0}px`;
                const parsed=parsedCss(value,'value'),dimension=parsed.tree?.children.first;
                value=dimension?.type==='Dimension'&&dimension.unit==='px'&&parsed.tree.children.size===1?`${Number(dimension.value)+delta}px`:`calc(${value} + ${delta}px)`;
            }
            Object.assign(properties,{[`--blokebot-${axis}`]:value,[`--blokebot-anchor-${axis}`]:String(anchor),
                [horizontal?'left':'top']:anchor===2?'auto':anchor===1?`calc(50% + var(--blokebot-${axis}))`:`var(--blokebot-${axis})`,
                [horizontal?'right':'bottom']:anchor===2?`var(--blokebot-${axis})`:'auto',
                [`--blokebot-translate-${axis}`]:nativeTranslate?translateComponent(horizontal?0:1):anchor===1?'-50%':'0px',
                translate:nativeTranslate?command.computed.translate:'var(--blokebot-translate-x, 0px) var(--blokebot-translate-y, 0px)'});
            if(id)metadata.push({path:['widgets',id,'authoring',axis],value},{path:['widgets',id,'authoring',horizontal?'horizontalAnchor':'verticalAnchor'],value:anchor});
        }
        const planned=planStyles(snapshot,key,properties,prepared,command.kind==='move'?'move':'layout');
        return planned.kind==='planned'?{...planned,metadata,revision:snapshot.revision,selection:key}:planned;
    }
    return {
        candidate,view,session,context,matches,
        planGesture(command,selection,revision,version=selectionVersion){
            if(selection!==selected||revision!==session.snapshot().revision||version!==selectionVersion)return {kind:'conflict'};
            const snapshot=session.snapshot(),captured=context();
            if(command.kind!=='move')return {...layoutPlan(snapshot,command),context:captured};
            const eligible=members.map(element).filter(node=>node&&!locked(node));
            if(eligible.length!==members.filter(key=>{const node=element(key);return !node||!locked(node);}).length)return {kind:'unmapped'};
            const targets=command.targets??(members.length===1?[{key:selected,visible:true,styles:command.computed??{},layoutX:command.layoutX??0,layoutY:command.layoutY??0}]:[]);
            if(eligible.some(node=>targets.filter(item=>item.key===elementKey(node)).length!==1))return {kind:'unmapped',code:'missing-native-geometry'};
            const observations=new Map(targets.map(item=>[item.key,item]));
            if(eligible.some(node=>{const item=observations.get(elementKey(node));return item.visible!==true||![item.layoutX,item.layoutY].every(Number.isFinite)
                ||command.targets&&(!item.styles||![item.width,item.height].every(value=>Number.isFinite(value)&&value>0));}))return {kind:'unmapped',code:'missing-native-geometry'};
            const plans=selectionRoots(eligible).map(node=>{
                const item=observations.get(elementKey(node));
                return layoutPlan(snapshot,{...command,computed:item.styles??item.computed,layoutX:item.layoutX,layoutY:item.layoutY,containingWidth:item.containingWidth,containingHeight:item.containingHeight},elementKey(node));
            });
            if(plans.length&&plans.every(plan=>plan.kind==='unchanged'))return {kind:'unchanged'};
            return {...combineStyles(snapshot,plans),context:captured};
        },
        planStyle(properties,selection,revision){
            if(selection!==selected||revision!==session.snapshot().revision)return {kind:'conflict'};
            const planned=planStyles(session.snapshot(),selection,properties,{element:element(selection),stylesheet:sourceData().stylesheet});
            return {...planned,revision,selection,context:context()};
        },
        commitGesture(planned){
            if(!matches(planned.context))return this.stale();
            focus=EditorFocus.Visual;return plan(planned);
        },
        stale(){feedback='The source or selection changed while this control was pending. Your newer work is kept; retry.';return view();},
        select(key,toggle=false){
            if(toggle&&key){const next=members.includes(key)?members.filter(item=>item!==key):[...members,key];setSelection(next,members.includes(key)?selected===key?next.at(-1):selected:key);}
            else setSelection(key?[key]:[],key);
            focus=EditorFocus.Visual;feedback='';return view();
        },
        selectSet(keys,active){setSelection(keys,active);focus=EditorFocus.Visual;feedback='';return view();},
        focus(value){focus=value;return view();},
        source(buffer,value){focus=buffer;const before=session.snapshot().html,result=session.replaceSource(buffer,session.snapshot().revision,value);if(result.kind==='applied')externalRevision=result.revision;return finish(result,before);},
        saved(value){saved=JSON.stringify(value);return view();},
        history(direction){const before=session.snapshot().html,result=session[direction](focus);if(focus!==EditorFocus.Visual&&result.kind==='applied')externalRevision=result.revision;return finish(result,before);},
        contextualDelete(selection,revision,version=selectionVersion){
            if(selection!==selected||revision!==session.snapshot().revision||version!==selectionVersion)return this.stale();
            return this.command({kind:'remove'});
        },
        control(command,selection,revision,version=selectionVersion){
            if(selection!==selected||version!==selectionVersion||revision<externalRevision||revision>session.snapshot().revision||command.kind==='configuration'&&revision!==session.snapshot().revision)return this.stale();
            return this.command(command);
        },
        command(command){
            focus=EditorFocus.Visual;
            const snapshot=session.snapshot(),id=selected?.startsWith('widget:')?selected.slice(7):null;
            if(command.kind==='select')return this.select(command.key,command.toggle);
            if(command.kind==='selection')return this.selectSet(command.keys,command.active);
            if(command.kind==='add')return plan(addLayer(snapshot,command.widget??null),true);
            if(command.kind==='motion')return plan(motionPreset(snapshot,selected,command));
            if(command.kind==='remove')return plan(removeLayers(snapshot,members),true);
            if(command.kind==='duplicate')return plan(duplicateLayer(snapshot,selected),true);
            if(command.kind==='reorder')return plan(reorderLayer(snapshot,selected,command.direction));
            if(command.kind==='move')return plan(this.planGesture(command,selected,snapshot.revision));
            if(command.kind==='position'||command.kind==='anchor'||command.kind==='resize') {
                return plan(layoutPlan(snapshot,command));
            }
            if(command.kind==='configuration-field')return widgetValue(['configuration',...command.path],command.value);
            if(command.kind==='configuration')return widgetValue(['configuration'],command.value);
            if(command.kind==='setup')return widgetValue(['requiresSetup'],command.value);
            if(command.kind==='audio')return widgetValue(['audio',command.property],command.value);
            if(command.kind==='lock') {
                if(id)return widgetValue(['authoring','isLocked'],command.value);
                const node=targetElement(snapshot,selected);if(!node)return finish({kind:'unmapped'});
                const change=attributeEdit(snapshot.html,node.start,'data-blokebot-locked',String(command.value));
                return change.kind==='patch'?plan({kind:'planned',edits:[change.edit],selected}):finish(change);
            }
            if(command.kind==='style')return style({[command.property]:command.value});
            if(command.kind==='styles')return style(command.properties);
            if(command.kind==='layout')return style(command.properties,
                id?Object.entries(command.metadata??{}).map(([key,value])=>({path:['widgets',id,'authoring',key],value})):[],'layout');
            if(command.kind==='visible') {
                const properties=command.value?{display:'var(--blokebot-display, block)'}:{'--blokebot-display':command.display??styleValues(snapshot,selected).display??'block',display:'none'};
                return style(properties,id?[{path:['widgets',id,'authoring','isVisible'],value:command.value}]:[]);
            }
            if(command.kind==='clip')return style({overflow:command.value?'hidden':'visible'},id?[{path:['widgets',id,'authoring','clipOverflow'],value:command.value}]:[]);
            if(command.kind==='find') {const node=targetElement(snapshot,selected);return { ...view(), sourcePosition:node?.start??null };}
            return view();
        }
    };
}
