import Coloris from '@melloware/coloris';
import { gradient, fillValue, editStop, stopPosition, individualNumber, solidColor } from './GuidedFill.js';

const swatches=['#ffffff','#111827','#8b5cf6','#3b82f6','#14b8a6','#f59e0b','#ef4444'];
export function createGuidedStyling(root,readView,authored,actions) {
    const abort=new AbortController(),options={signal:abort.signal},bound=new WeakSet(),pending=new WeakMap();
    let active=null,stop=0,selected=null,disposed=false,focusEpoch=0;
    Coloris.init();
    const input=name=>root.querySelector(`[data-guided-color=${name}]`);
    const image=()=>(readView().styles['background-image']??'none').trim();
    const fill=()=>gradient(image());
    const custom=()=>!!authored().background&&!('background-image' in authored())&&!solidColor(authored().background)&&!gradient(authored().background)||(image()!=='none'&&!fill());
    const set=(selector,value)=>{const node=root.querySelector(selector);if(node&&node!==window.document.activeElement&&node.value!==String(value))node.value=value;};
    const paint=(name,value)=>{const node=input(name);if(!node)return;if(active?.input!==node)node.value=value;root.querySelector(`[data-color-open=${name}]`)?.style.setProperty('--fill-color',value);};
    function returnFocus(opener) {
        const epoch=focusEpoch;
        queueMicrotask(()=>epoch===focusEpoch&&!disposed&&opener?.isConnected&&opener.focus({preventScroll:true}));
    }
    function cancel(restoreFocus=true) {
        const previous=active;active=null;
        if(previous){actions.clear();Coloris.close(true);if(restoreFocus)returnFocus(previous.opener);}
    }
    function refresh() {
        if(disposed)return;
        const view=readView();if(active&&(active.selection!==view.selected||active.revision!==view.revision))cancel();
        if(selected!==view.selected){selected=view.selected;stop=0;}
        for(const field of root.querySelectorAll('[data-guided-color]'))if(!bound.has(field)) {
            bound.add(field);Coloris({el:field,wrap:false});
            field.addEventListener('open',()=>{
                const current=readView(),name=field.dataset.guidedColor,g=fill();
                active={input:field,opener:root.querySelector(`[data-color-open=${name}]`),selection:current.selected,revision:current.revision,id:crypto.randomUUID(),name,fill:g,stop,planned:null};
            },options);
            field.addEventListener('input',()=>{
                const current=active;if(!current||current.input!==field||current.selection!==readView().selected||current.revision!==readView().revision)return;
                const properties=current.name==='stop'?{'background-image':fillValue(editStop(current.fill,current.stop,'color',field.value))}:{[current.name==='text'?'color':'background-color']:field.value};
                current.opener?.style.setProperty('--fill-color',field.value);
                const planned=actions.plan(properties,current.selection,current.revision);
                if(planned.kind!=='planned'||actions.present(current.id,planned.presentation,current.revision))current.planned=planned;
            },options);
            field.addEventListener('close',()=>{
                const current=active;if(!current||current.input!==field)return;active=null;
                if(current.planned&&current.selection===readView().selected&&current.revision===readView().revision)actions.commit(current.planned);else actions.clear();
                returnFocus(current.opener);
            },options);
        }
        paint('text',view.styles.color??'#ffffff');paint('solid',view.styles['background-color']??'transparent');
        const g=fill(),isCustom=custom();
        for(const node of root.querySelectorAll('[data-fill-kind]')){node.disabled=isCustom;node.setAttribute('aria-pressed',String(!isCustom&&(g?'gradient':'solid')===node.dataset.fillKind));}
        const notice=root.querySelector('[data-fill-custom]');if(notice)notice.hidden=!isCustom;
        const editor=root.querySelector('[data-gradient-controls]');if(editor)editor.hidden=isCustom||!g;
        const solid=root.querySelector('[data-solid-controls]');if(solid)solid.hidden=isCustom||!!g;
        const align=view.styles['text-align']??'start';set('[data-text-align]',['start','left','center','right','end','justify'].includes(align)?align:'custom');
        for(const property of ['rotate','scale']){
            const value=individualNumber(view.styles[property],property);set(`[data-transform=${property}]`,value??'');
            const context=root.querySelector(`[data-transform-custom=${property}]`);if(context)context.hidden=value!==null;
        }
        if(!g||!editor)return;
        stop=Math.min(stop,g.stops.length-1);paint('stop',g.stops[stop].color);
        set('[data-gradient-position]',stopPosition(g,stop));set('[data-gradient-kind]',g.kind);set('[data-gradient-angle]',g.angle);set('[data-gradient-centre]',g.centre);
        root.querySelector('[data-linear-controls]').hidden=g.kind!=='linear';root.querySelector('[data-radial-controls]').hidden=g.kind!=='radial';
        root.querySelector('[data-gradient-remove]').disabled=g.stops.length<=2;
        const strip=root.querySelector('[data-gradient-stops]'),signature=fillValue(g)+'|'+stop;
        if(strip&&strip.dataset.value!==signature){
            strip.dataset.value=signature;strip.style.background=fillValue(g);strip.replaceChildren(...g.stops.map((item,index)=>{
                const button=window.document.createElement('button');button.type='button';button.className='btn-secondary';button.dataset.gradientStop=String(index);button.setAttribute('aria-label',`Select gradient stop ${index+1}`);button.setAttribute('aria-pressed',String(stop===index));button.style.setProperty('--fill-color',item.color);button.textContent=String(index+1);return button;
            }));
        }
    }
    function apply(properties,metadata) {
        cancel();const view=readView();const planned=actions.plan(properties,view.selected,view.revision);
        actions.commit({...planned,metadata:metadata&&view.widget?Object.entries(metadata).map(([key,value])=>({path:['widgets',view.widget.id.value,'authoring',key],value})):[]});
        refresh();
    }
    root.addEventListener('pointerdown',event=>{if(active&&event.target.closest('[role=treeitem],[data-tab-key],[data-editor-pane]'))cancel();},{capture:true,...options});
    root.addEventListener('click',event=>{
        const opener=event.target.closest('[data-color-open]');
        if(opener){
            cancel();const field=input(opener.dataset.colorOpen);if(!field)return;
            Coloris({parent:root,themeMode:window.document.documentElement.dataset.theme==='dark'?'dark':'light',format:'rgb',alpha:true,swatches,closeButton:true,closeLabel:'Done',focusInput:false});
            field.click();window.document.querySelector('#clr-hue-slider')?.focus({preventScroll:true});return;
        }
        const target=event.target.closest('[data-fill-kind],[data-gradient-stop],[data-gradient-add],[data-gradient-remove],[data-fill-replace]');if(!target)return;
        if(target.hasAttribute('data-gradient-stop')){cancel();stop=Number(target.dataset.gradientStop);refresh();return;}
        const g=fill();
        if(target.hasAttribute('data-fill-replace')){
            if(window.confirm('Replace the custom background image with a solid fill? Other authored background properties stay in source.'))apply({'background-image':'none'});return;
        }
        if(target.dataset.fillKind){
            if(custom())return;
            if(target.dataset.fillKind==='solid'&&g)apply({'background-image':'none'});
            else if(target.dataset.fillKind==='gradient'&&!g)apply({'background-image':`linear-gradient(90deg, ${readView().styles['background-color']??'#8b5cf6'} 0%, #3b82f6 100%)`});return;
        }
        if(!g)return;
        if(target.hasAttribute('data-gradient-add')){const position=(stopPosition(g,stop)+100)/2;g.stops.splice(stop+1,0,{color:g.stops[stop].color,position,raw:`${g.stops[stop].color} ${position}%`});stop++;}
        else if(g.stops.length>2){g.stops.splice(stop,1);stop=Math.min(stop,g.stops.length-1);}
        apply({'background-image':fillValue(g)});
    },options);
    root.addEventListener('input',event=>{
        const target=event.target;if(!target.matches('[data-text-align],[data-transform],[data-gradient-position],[data-gradient-angle],[data-gradient-centre],[data-gradient-kind]'))return;
        const view=readView();pending.set(target,{selection:view.selected,revision:view.revision,stop,value:target.value});
    },options);
    root.addEventListener('change',event=>{
        const target=event.target,intent=pending.get(target);pending.delete(target);
        if(!intent||intent.selection!==readView().selected||intent.revision!==readView().revision||intent.value!==target.value)return;
        if(target.matches('[data-gradient-position]')&&intent.stop!==stop)return;
        if(target.matches('[data-text-align]')){apply({'text-align':target.value});return;}
        if(target.matches('[data-transform]')){
            if(target.value===''||!Number.isFinite(target.valueAsNumber))return;
            const value=target.valueAsNumber,property=target.dataset.transform;
            apply({[property]:property==='rotate'?`${value}deg`:String(value)},property==='rotate'?{rotationDegrees:value}:{scaleX:value,scaleY:value});return;
        }
        const g=fill();if(!g)return;
        if(target.matches('[data-gradient-position]')){if(target.value===''||!Number.isFinite(target.valueAsNumber))return;apply({'background-image':fillValue(editStop(g,stop,'position',target.valueAsNumber))});}
        else if(target.matches('[data-gradient-angle]')&&g.kind==='linear'){if(!Number.isFinite(target.valueAsNumber))return;apply({'background-image':fillValue({...g,direction:`${target.valueAsNumber}deg`})});}
        else if(target.matches('[data-gradient-centre]')&&g.kind==='radial')apply({'background-image':fillValue({...g,direction:`${g.direction.startsWith('circle')?'circle':'ellipse'} at ${target.value}`})});
        else if(target.matches('[data-gradient-kind]'))apply({'background-image':fillValue({...g,kind:target.value,direction:target.value==='linear'?'90deg':'ellipse at center'})});
    },options);
    window.document.addEventListener('keydown',event=>{if(event.key==='Escape'&&active){event.preventDefault();event.stopImmediatePropagation();cancel();refresh();}}, {capture:true,...options});
    window.document.addEventListener('fullscreenchange',cancel,options);
    window.addEventListener('resize',cancel,options);
    const observer=new MutationObserver(()=>{if(root.dataset.mode!=='visual'||!root.isConnected)cancel();refresh();});
    observer.observe(root,{childList:true,subtree:true,attributes:true,attributeFilter:['data-mode','class']});
    refresh();
    return {refresh,cancel,handoff(){focusEpoch++;cancel(false);refresh();},dispose(){focusEpoch++;cancel(false);disposed=true;abort.abort();observer.disconnect();Coloris({parent:window.document.body});}};
}
