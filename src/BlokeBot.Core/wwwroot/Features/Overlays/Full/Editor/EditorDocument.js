import { createSourceSession, EditorFocus, htmlRanges, cssRanges, attributeEdit } from './FullOverlayEditorCore.js';
import { planStyles, prepareStyles, styleValues, elementKey, targetElement } from './VisualStyles.js';
import { parsedCss } from './SourceRanges.js';
import { motionPreset } from './MotionCommands.js';
import { addLayer, removeLayer, duplicateLayer, reorderLayer } from './LayerCommands.js';

export function createEditorDocument(document) {
    const session = createSourceSession(document);
    let selected = null, saved = JSON.stringify({...document,diagnostics:[]}), feedback = '', focus = EditorFocus.Visual, externalRevision=0, gestureStyles=null;
    const candidate = () => { const value = session.snapshot(); return { ...document, html:value.html, css:value.css, widgets:value.widgets, diagnostics:[] }; };
    function view() {
        const snapshot = session.snapshot(), parsed = htmlRanges(snapshot.html);
        const keys = new Map(parsed.elements.map(node=>[node.start,elementKey(node)]));
        const layers = parsed.elements.map(node=>({ key:elementKey(node), parent:keys.get(node.parent)??null,
            label:node.values['aria-label']??node.values.id??node.name,
            tag:node.name, selector:node.selector, widgetId:node.values['data-blokebot-widget']??null }));
        const widget = snapshot.widgets.find(widget=>`widget:${widget.id.value}`===selected)??null;
        return { revision:snapshot.revision,dirty:JSON.stringify(candidate())!==saved, selected,
            layers,widget,locked:widget?.authoring.isLocked??targetElement(snapshot,selected)?.values['data-blokebot-locked']==='true',styles:styleValues(snapshot,selected),feedback,focus,
            diagnostics:[...parsed.diagnostics.filter(d=>d.code!=='missing-doctype').map(d=>({code:d.code,buffer:'html',offset:d.start})),
                ...cssRanges(snapshot.css).diagnostics.map(d=>({code:d.code,buffer:'css',offset:d.start}))] };
    }
    function finish(result, nextSelection=selected) {
        if(result.kind==='applied') {selected=nextSelection;feedback='';}
        else if(result.kind==='conflict')feedback='Undo kept a newer overlapping edit. Undo that edit in its editor, then retry.';
        else if(result.kind==='unmapped'||result.kind==='invalid')feedback='This source region cannot be mapped safely. Keep editing the source, then retry.';
        return view();
    }
    function plan(planned) {
        if(planned.kind!=='planned')return finish(planned);
        return finish(session.apply(EditorFocus.Visual,session.snapshot().revision,planned.edits,planned.metadata??[]),planned.selected);
    }
    function style(properties, metadata=[]) {
        const snapshot=session.snapshot(),planned=planStyles(snapshot,selected,properties);
        if(planned.kind!=='planned')return finish(planned);
        return finish(session.apply(EditorFocus.Visual,snapshot.revision,planned.edits,metadata),planned.selected);
    }
    function widgetValue(path,value) {
        const id=selected?.startsWith('widget:')?selected.slice(7):null;
        return id ? finish(session.apply(EditorFocus.Visual,session.snapshot().revision,[],[{path:['widgets',id,...path],value}])) : view();
    }
    function layoutPlan(snapshot,command,prepared=prepareStyles(snapshot,selected)) {
        const id=selected?.startsWith('widget:')?selected.slice(7):null;
        const values={...command.computed,...styleValues(snapshot,selected,prepared)},widget=snapshot.widgets.find(w=>w.id.value===id);
        const properties={position:'absolute'},metadata=[];
        const axes=command.kind==='move'||command.kind==='resize'?['x','y']:[command.axis];
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
            const anchor=command.kind==='anchor'?command.value:Number(values[`--blokebot-anchor-${axis}`]??widget?.authoring[horizontal?'horizontalAnchor':'verticalAnchor']??0);
            let value=command.kind==='position'?command.value:values[`--blokebot-${axis}`]??values[horizontal?(anchor===2?'right':'left'):(anchor===2?'bottom':'top')]??widget?.authoring[axis]??'0px';
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
                [`--blokebot-translate-${axis}`]:anchor===1?'-50%':'0px',translate:'var(--blokebot-translate-x, 0px) var(--blokebot-translate-y, 0px)'});
            if(id)metadata.push({path:['widgets',id,'authoring',axis],value},{path:['widgets',id,'authoring',horizontal?'horizontalAnchor':'verticalAnchor'],value:anchor});
        }
        const planned=planStyles(snapshot,selected,properties,prepared);
        return planned.kind==='planned'?{...planned,metadata,revision:snapshot.revision,selection:selected}:planned;
    }
    return {
        candidate,view,session,
        planGesture(command,selection,revision){
            if(selection!==selected||revision!==session.snapshot().revision)return {kind:'conflict'};
            const snapshot=session.snapshot();
            if(gestureStyles?.revision!==revision||gestureStyles.selection!==selection)
                gestureStyles={revision,selection,prepared:prepareStyles(snapshot,selection)};
            return layoutPlan(snapshot,command,gestureStyles.prepared);
        },
        commitGesture(planned){
            if(planned.selection!==selected||planned.revision!==session.snapshot().revision)return this.stale();
            focus=EditorFocus.Visual;
            return plan(planned);
        },
        stale(){feedback='The source or selection changed while this control was pending. Your newer work is kept; retry.';return view();},
        select(key){selected=key;focus=EditorFocus.Visual;feedback='';return view();},
        focus(value){focus=value;return view();},
        source(buffer,value){focus=buffer;if(buffer==='html'&&selected?.startsWith('source:'))selected=null;const result=session.replaceSource(buffer,session.snapshot().revision,value);if(result.kind==='applied')externalRevision=result.revision;return finish(result);},
        saved(value){saved=JSON.stringify(value);return view();},
        history(direction){const result=session[direction](focus);if(focus!==EditorFocus.Visual&&result.kind==='applied')externalRevision=result.revision;return finish(result);},
        control(command,selection,revision){
            if(selection!==selected||revision<externalRevision||revision>session.snapshot().revision||command.kind==='configuration'&&revision!==session.snapshot().revision)return this.stale();
            return this.command(command);
        },
        command(command){
            focus=EditorFocus.Visual;
            const snapshot=session.snapshot(),id=selected?.startsWith('widget:')?selected.slice(7):null;
            if(command.kind==='select')return this.select(command.key);
            if(command.kind==='add')return plan(addLayer(snapshot,command.widget??null));
            if(command.kind==='motion')return plan(motionPreset(snapshot,selected,command));
            if(command.kind==='remove')return plan(removeLayer(snapshot,selected));
            if(command.kind==='duplicate')return plan(duplicateLayer(snapshot,selected));
            if(command.kind==='reorder')return plan(reorderLayer(snapshot,selected,command.direction));
            if(command.kind==='position'||command.kind==='anchor'||command.kind==='move'||command.kind==='resize') {
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
                id?Object.entries(command.metadata??{}).map(([key,value])=>({path:['widgets',id,'authoring',key],value})):[]);
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
