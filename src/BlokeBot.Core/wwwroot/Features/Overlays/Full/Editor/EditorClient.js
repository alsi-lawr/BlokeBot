import { createEditorDocument } from './EditorDocument.js';
import { createCanvas } from './EditorCanvas.js';
import { createPreviewBridge } from './EditorPreview.js';
import { initializeFullscreen, disposeFullscreen, toggleBrowserFullscreen } from '/Features/Automations/Page/AutomationEditorPage.razor.js';

export function createClient(root, document, dotnet) {
    const owner=createEditorDocument(document), abort=new AbortController(), options={signal:abort.signal};
    const html=root.querySelector('[data-source=html]'),css=root.querySelector('[data-source=css]');
    let view=owner.view(),disposed=false, previewTimer=null,measurements=[],measuredRevision=-1;
    const measured=value=>{const item=measuredRevision===value.revision?measurements.find(item=>item.key===value.selected):null;return item?{...value,styles:{...item.styles,...value.styles}}:value;};
    const publish=()=>{if(!disposed)void dotnet.invokeMethodAsync('EditorChangedAsync',view);};
    const changed=next=>{
        const previous=view.revision;view=measured(next);
        if(previous!==view.revision){preview.invalidate();clearTimeout(previewTimer);
            previewTimer=setTimeout(()=>{if(!disposed)void dotnet.invokeMethodAsync('RefreshPreviewAsync',view.revision);},200);}
        const snapshot=owner.session.snapshot();
        if(html.value!==snapshot.html)html.value=snapshot.html;
        if(css.value!==snapshot.css)css.value=snapshot.css;
        canvas.draw();publish();return view;
    };
    const command=(value,selection=view.selected,revision=view.revision)=>changed(owner.control(value.kind==='visible'&&!value.value?{...value,display:view.styles.display}:value.kind==='position'||value.kind==='anchor'||value.kind==='move'||value.kind==='resize'?{...value,computed:{...value.computed,...view.styles}}:value,selection,revision));
    const canvas=createCanvas(root,()=>view,command);
    const preview=createPreviewBridge(root.querySelector('[data-preview-frame]'),()=>view,
        (items,size,revision)=>{if(revision===view.revision){measurements=items;measuredRevision=revision;view=measured(owner.view());canvas.observed(items,size);publish();}},
        (state,diagnostics)=>{if(!disposed)void dotnet.invokeMethodAsync('PreviewStatusAsync',state,diagnostics.map(d=>d.code));});
    html.value=document.html;css.value=document.css;
    for(const input of [html,css]){
        input.addEventListener('focus',()=>{view=owner.focus(input.dataset.source);publish();},options);
        input.addEventListener('input',()=>changed(owner.source(input.dataset.source,input.value)),options);
    }
    root.addEventListener('keydown',event=>{
        if(!(event.target instanceof Element))return;
        const tree=event.target.closest('[role=tree]');if(tree&&['ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Home','End',' '].includes(event.key))event.preventDefault();
        const source=event.target.closest('[data-source]');
        const editable=event.target.closest('input,textarea,select,[contenteditable=true]');
        if((event.ctrlKey||event.metaKey)&&!event.altKey){
            const key=event.key.toLowerCase();
            if((key==='z'||key==='y')&&(!editable||source)){
                event.preventDefault();if(source)owner.focus(source.dataset.source);
                changed(owner.history(key==='y'||event.shiftKey?'redo':'undo'));return;
            }
            if(key==='s'){event.preventDefault();void dotnet.invokeMethodAsync('SaveShortcutAsync');return;}
        }
        if(editable)return;
        if(event.key==='Delete'&&view.selected){event.preventDefault();command({kind:'remove'});}
        if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key)&&event.target.closest('[data-canvas-area]')&&view.selected&&!view.locked){
            event.preventDefault();const axis=event.key==='ArrowLeft'||event.key==='ArrowRight'?'x':'y';
            const step=(event.shiftKey?10:1)*(event.key==='ArrowLeft'||event.key==='ArrowUp'?-1:1);
            const item=measurements.find(item=>item.key===view.selected);
            command({kind:'move',dx:axis==='x'?step:0,dy:axis==='y'?step:0,computed:item?.styles,layoutX:item?.layoutX,layoutY:item?.layoutY});
        }
    },options);
    const beforeUnload=event=>{if(view.dirty){event.preventDefault();event.returnValue='';}};
    window.addEventListener('beforeunload',beforeUnload,options);
    initializeFullscreen(dotnet);
    document = null;
    const height=()=>root.style.setProperty('--editor-height',`${Math.max(300,window.innerHeight-root.getBoundingClientRect().top)}px`);
    window.addEventListener('resize',height,options);height();publish();
    return {
        candidate:()=>owner.candidate(),view:()=>view,command,
        candidateStream(){return new Blob([JSON.stringify(owner.candidate())],{type:'application/json'});},
        export(form,name){form.elements.namedItem('document').value=JSON.stringify(owner.candidate());form.elements.namedItem('name').value=name;form.requestSubmit();},
        history:direction=>changed(owner.history(direction)),
        saved:value=>changed(owner.saved(value)),
        preview(id,revision){if(revision===view.revision)preview.set(id,revision);},
        pan:value=>canvas.pan(value),
        viewport(width,height){canvas.viewport(width,height);preview.query();},
        zoom:value=>canvas.zoom(value),align:(axis,edge,selection,revision)=>canvas.align(axis,edge,selection,revision),fullscreen:toggleBrowserFullscreen,
        find(selection){if(selection!==view.selected)return;const position=owner.command({kind:'find'}).sourcePosition;if(position!==null){html.focus();html.setSelectionRange(position,position);}},
        dispose(){disposed=true;clearTimeout(previewTimer);abort.abort();preview.dispose();canvas.dispose();disposeFullscreen();}
    };
}

const openers = new WeakMap();
export function openDialog(element) { openers.set(element,window.document.activeElement);element.showModal(); }
export function closeDialog(element) { element.close();openers.get(element)?.focus(); }

export function copyText(text) { return navigator.clipboard.writeText(text); }
