import { createEditorDocument } from './EditorDocument.js';
import { createCanvas } from './EditorCanvas.js';
import { createPreviewBridge } from './EditorPreview.js';
import { createGuidedStyling } from './GuidedStyling.js';
import { createPresentation } from './EditorPresentation.js';
import { containToolboxKeyboard } from '../../../../Components/EditorToolboxKeyboard.js';
import { insertedLayer } from './ToolboxInsertion.js';
import { createEditorMenu } from '../../../../Components/EditorContextMenu.js';

export function createClient(root, document, dotnet) {
    const owner=createEditorDocument(document), abort=new AbortController(), options={signal:abort.signal};
    const html=root.querySelector('[data-source=html]'),css=root.querySelector('[data-source=css]');
    let view=owner.view(),disposed=false,measurements=[],measuredRevision=-1,notification=0,previewRequest=0,styling,menu,hierarchyDrag=null;
    const measured=value=>{const item=measuredRevision===value.revision?measurements.find(item=>item.key===value.selected):null;return {...value,nativeStyles:item?.styles??{},styles:item?{...item.styles,...value.styles}:value.styles};};
    const publish=()=>disposed?Promise.resolve():dotnet.invokeMethodAsync('EditorChangedAsync',view,++notification);
    const changed=(next,retainPresentation=false)=>{
        if(next.revision!==view.revision||next.selected!==view.selected||next.selectionVersion!==view.selectionVersion){styling?.cancel();clearHierarchyDrop();}
        const previous=view.revision;view=measured(next);
        const authorize=previous!==view.revision&&preview.render(owner.candidate(),view.revision,retainPresentation);
        const snapshot=owner.session.snapshot();
        if(html.value!==snapshot.html)html.value=snapshot.html;
        if(css.value!==snapshot.css)css.value=snapshot.css;
        canvas.draw();styling?.refresh();menu?.refresh();const revision=view.revision;
        void publish().then(()=>{if(authorize&&!disposed&&revision===view.revision)void dotnet.invokeMethodAsync('RefreshPreviewAsync',revision,true);});return view;
    };
    const command=(value,selection=view.selected,revision=view.revision,version=view.selectionVersion,captured=null)=>{
        if(disposed)return view;
        styling?.cancel();
        if(value.kind==='return-layout'||value.kind==='hierarchy'){
            canvas.cancel();clearHierarchyDrop();
            if(!owner.matches(captured))return changed(owner.stale());
            const planned=value.kind==='hierarchy'?owner.planHierarchy(value,captured):measuredRevision===view.revision?owner.planReturn(measurements,captured):{kind:'unmapped',context:captured};
            return changed(owner.commitGesture(planned));
        }
        return changed(owner.control(value.kind==='visible'&&!value.value?{...value,display:view.styles.display}:value.kind==='position'||value.kind==='anchor'||value.kind==='move'||value.kind==='resize'?{...value,computed:{...value.computed,...view.styles}}:value,selection,revision,version));
    };
    function hierarchyPreview(value,captured){
        if(disposed||!owner.matches(captured))return {valid:false,message:'The source or selection changed. Choose the destination again.'};
        const result=owner.hierarchyTarget(value,captured);
        const messages={
            'hierarchy-cycle':'A layer cannot be moved into itself or its descendants.',
            'invalid-local-content':'This destination cannot contain the selected HTML.',
            'ambiguous-source-boundary':'The selection has an ambiguous or implicit HTML boundary. Edit its source first.',
            'ambiguous-destination':'This destination has an ambiguous HTML boundary.',
            'missing-authored-boundary':'This destination needs an explicit closing tag in the source.',
            'ambiguous-attribute':'The affected HTML contains duplicate attributes.',
            'selection-changed':'Choose a uniquely identified layer.'
        };
        const target=value.relation==='root'?view.hierarchyRoot:view.layers.find(layer=>layer.key===value.target)?.label??'destination';
        return {valid:result.kind==='target',message:result.kind==='target'?`${value.relation==='root'?'At':value.relation.charAt(0).toUpperCase()+value.relation.slice(1)} ${target} · selected roots only`:messages[result.code]??'Choose a safe destination.'};
    }
    function clearHierarchyDrop(){
        hierarchyDrag=null;
        for(const node of root.querySelectorAll('[data-drop-relation]')){delete node.dataset.dropRelation;delete node.dataset.dropLabel;delete node.dataset.dropValid;}
    }
    function dropTarget(event){
        const node=event.target.closest('[data-layer-key],[data-hierarchy-root]');
        if(!node)return null;
        if(node.hasAttribute('data-hierarchy-root'))return {node,relation:'root',target:null};
        const bounds=node.getBoundingClientRect(),fraction=(event.clientY-bounds.top)/bounds.height;
        return {node,relation:fraction<.25?'before':fraction>.75?'after':'inside',target:node.dataset.layerKey};
    }

    const canvas=createCanvas(root,()=>view,command,{
        plan:(value,selection,revision,version)=>owner.planGesture(value,selection,revision,version),
        present:(id,presentation,revision)=>preview.present(id,presentation,revision),
        clear:()=>preview.clear(),
        commit(planned){const next=owner.commitGesture(planned);if(next.revision===view.revision)preview.clear();changed(next,next.revision!==view.revision);}
    });
    const preview=createPreviewBridge(root.querySelector('[data-preview-frame]'),()=>view,
        (items,size,revision)=>{if(revision===view.revision){measurements=items;measuredRevision=revision;view=measured(owner.view());canvas.observed(items,size);styling?.refresh();publish();}},
        (state,diagnostics)=>{if(!disposed)void dotnet.invokeMethodAsync('PreviewStatusAsync',state,diagnostics.map(d=>d.code));},
        (id,items)=>canvas.presented(id,items),()=>{canvas.cancel();styling?.cancel();clearHierarchyDrop();});
    const focusEditor=(buffer,moveFocus=true)=>{if(disposed)return;view=owner.focus(buffer);publish();if(moveFocus)(buffer==='visual'?root.querySelector('[data-canvas-area]'):buffer==='html'?html:css).focus({preventScroll:true});};
    const presentation=createPresentation(root,focusEditor,()=>{canvas.cancel();styling?.cancel();clearHierarchyDrop();void dotnet.invokeMethodAsync('CloseToolboxForModeAsync');},()=>canvas.refresh());
    styling=createGuidedStyling(root,()=>view,()=>owner.view().styles,{
        plan:(properties,selection,revision)=>owner.planStyle(properties,selection,revision),
        present:(id,presentation,revision)=>preview.present(id,presentation,revision),
        clear:()=>preview.clear(),
        commit(planned){const next=owner.commitGesture(planned);if(next.revision===view.revision)preview.clear();changed(next,next.revision!==view.revision);}
    });
    const visibleCanvas=()=>{const node=root.querySelector('[data-canvas-area]');return node.getClientRects().length?node:root.querySelector('[data-editor-menu-open]');};
    menu=createEditorMenu(root,{
        beforeOpen:()=>{canvas.cancel();styling.handoff();},
        accepts:target=>!!target.closest('[data-canvas-area],[data-layer-key]'),
        fallback:visibleCanvas,
        prepare(target,point){
            const layer=target.closest('[data-layer-key]');
            const key=layer?layer.dataset.layerKey:point&&target.closest('[data-canvas-area]')?canvas.contextTarget(point):view.selected;
            if(!view.members.includes(key))changed(owner.select(key));else changed(owner.focus('visual'));
            const current=owner.view();
            return {...owner.context(),selection:current.selected,
                target:current.members.length>1?`${current.members.length} selected · Active: ${current.layers.find(layer=>layer.key===current.selected)?.label??'layer'}`:current.layers.find(layer=>layer.key===current.selected)?.label??'No selection',
                history:`${current.focus==='html'?'HTML':current.focus==='css'?'CSS':'Visual'} history`,
                ...current.history,delete:!!current.selected,
                note:current.history.undo||current.history.redo?'Selection Delete is undoable.':'No edits in this history.'};
        },
        current:captured=>owner.matches(captured),
        execute(action,captured){
            styling.handoff();
            changed(action==='delete'?owner.contextualDelete(captured.selection,captured.revision,captured.selectionVersion):owner.history(action));
        }
    });
    html.value=document.html;css.value=document.css;
    for(const input of [html,css]){
        input.addEventListener('focus',()=>{view=owner.focus(input.dataset.source);menu.refresh();publish();},options);
        input.addEventListener('input',()=>changed(owner.source(input.dataset.source,input.value)),options);
    }
    root.addEventListener('keydown',event=>{
        if(!(event.target instanceof Element))return;
        if((event.ctrlKey||event.metaKey)&&!event.altKey&&event.key.toLowerCase()==='s'){event.preventDefault();void dotnet.invokeMethodAsync('SaveShortcutAsync');return;}
        if(containToolboxKeyboard(event))return;
        const tree=event.target.closest('[role=tree]');if(tree&&['ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Home','End',' '].includes(event.key))event.preventDefault();
        const source=event.target.closest('[data-source]');
        const editable=event.target.closest('input,textarea,select,[contenteditable=true]');
        if((event.ctrlKey||event.metaKey)&&!event.altKey){
            const key=event.key.toLowerCase();
            if((key==='z'||key==='y')&&(!editable||source)){
                event.preventDefault();if(source)owner.focus(source.dataset.source);
                changed(owner.history(key==='y'||event.shiftKey?'redo':'undo'));return;
            }
        }
        if(editable)return;
        if(event.key==='Delete'&&view.selected){event.preventDefault();command({kind:'remove'});}
        if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key)&&event.target.closest('[data-canvas-area]')&&view.selected){
            event.preventDefault();const axis=event.key==='ArrowLeft'||event.key==='ArrowRight'?'x':'y';
            const step=(event.shiftKey?10:1)*(event.key==='ArrowLeft'||event.key==='ArrowUp'?-1:1);
            canvas.nudge(axis==='x'?step:0,axis==='y'?step:0);
        }
    },options);
    root.addEventListener('dragstart',event=>{
        const row=event.target.closest('[data-layer-key]');
        if(!row||event.target.closest('button'))return;
        canvas.cancel();styling.cancel();clearHierarchyDrop();
        const key=row.dataset.layerKey;
        if(!view.members.includes(key))changed(owner.select(key));else changed(owner.focus('visual'));
        hierarchyDrag=owner.context();event.dataTransfer.effectAllowed='move';event.dataTransfer.setData('text/plain','BlokeBot HTML layers');
    },options);
    root.addEventListener('dragover',event=>{
        if(!hierarchyDrag)return;
        if(!owner.matches(hierarchyDrag)){clearHierarchyDrop();return;}
        const target=dropTarget(event);if(!target)return;
        event.preventDefault();
        for(const node of root.querySelectorAll('[data-drop-relation]')){delete node.dataset.dropRelation;delete node.dataset.dropLabel;delete node.dataset.dropValid;}
        const result=hierarchyPreview(target,hierarchyDrag);
        target.node.dataset.dropRelation=target.relation;target.node.dataset.dropValid=String(result.valid);target.node.dataset.dropLabel=result.valid?target.relation==='root'?'At overlay root':target.relation.charAt(0).toUpperCase()+target.relation.slice(1):'Not a valid destination';
        event.dataTransfer.dropEffect=result.valid?'move':'none';
    },options);
    root.addEventListener('dragleave',event=>{
        const node=event.target.closest('[data-drop-relation]');
        if(node&&!node.contains(event.relatedTarget)){delete node.dataset.dropRelation;delete node.dataset.dropLabel;delete node.dataset.dropValid;}
    },options);
    root.addEventListener('drop',event=>{
        if(!hierarchyDrag)return;
        event.preventDefault();const captured=hierarchyDrag,target=dropTarget(event);clearHierarchyDrop();
        if(target)command({kind:'hierarchy',relation:target.relation,target:target.target},captured.selected,captured.revision,captured.selectionVersion,captured);
    },options);
    root.addEventListener('dragend',clearHierarchyDrop,options);
    root.addEventListener('keydown',event=>{if(event.key==='Escape')clearHierarchyDrop();},options);
    const beforeUnload=event=>{if(view.dirty){event.preventDefault();event.returnValue='';}};
    window.addEventListener('beforeunload',beforeUnload,options);
    document = null;
    const height=()=>root.style.setProperty('--editor-height',`${Math.max(300,window.innerHeight-root.getBoundingClientRect().top)}px`);
    const layout=new MutationObserver(height);layout.observe(root,{attributes:true,attributeFilter:['class']});
    window.addEventListener('resize',height,options);height();publish();
    return {
        candidate:()=>owner.candidate(),view:()=>view,command,hierarchyPreview,
        revealHierarchy(){if(!disposed){clearHierarchyDrop();canvas.cancel();styling.cancel();presentation.revealInspector();}},
        insert(value,selection,revision,version){const before=owner.view(),after=command(value,selection,revision,version);return {inserted:insertedLayer(before,after),revision:after.revision,selectionVersion:after.selectionVersion,feedback:after.feedback};},
        candidateStream(){return new Blob([JSON.stringify(owner.candidate())],{type:'application/json'});},
        export(form,name){form.elements.namedItem('document').value=JSON.stringify(owner.candidate());form.elements.namedItem('name').value=name;form.requestSubmit();},
        history:direction=>changed(owner.history(direction)),
        saved:value=>changed(owner.saved(value)),
        preview(id,revision,request){if(disposed||request<=previewRequest)return false;previewRequest=request;return revision===view.revision&&preview.set(id,revision,owner.candidate());},
        focusInsertedLayer(selection,revision,version){if(disposed||selection!==view.selected||revision!==view.revision||version!==view.selectionVersion)return;presentation.revealInspector();},
        pan:value=>canvas.pan(value),
        viewport(width,height){canvas.viewport(width,height);preview.query();},
        zoom:value=>canvas.zoom(value),align:(axis,edge,selection,revision)=>canvas.align(axis,edge,selection,revision),
        find(selection){if(disposed||selection!==view.selected)return;const position=owner.command({kind:'find'}).sourcePosition;if(position!==null){presentation.revealHtml();html.setSelectionRange(position,position);}},
        dispose(){clearHierarchyDrop();disposed=true;abort.abort();menu.dispose();styling.dispose();presentation.dispose();layout.disconnect();preview.dispose();canvas.dispose();}
    };
}

const openers = new WeakMap();
export function openDialog(element) { openers.set(element,window.document.activeElement);element.showModal(); }
export function closeDialog(element) { element.close();openers.get(element)?.focus(); }

export function copyText(text) { return navigator.clipboard.writeText(text); }
