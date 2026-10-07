export function createCanvas(root, readView, command, manipulation) {
    const area=root.querySelector('[data-canvas-area]'),scene=root.querySelector('[data-canvas-scene]');
    const outline=root.querySelector('[data-selection]'),stage=root.querySelector('[data-canvas-stage]');
    const outlines=new Map(),marquee=window.document.createElement('div');marquee.className='full-editor-marquee';marquee.hidden=true;stage.append(marquee);
    let items=[],viewport={width:1920,height:1080},scale=1,zoom=0,pan={x:0,y:0},gesture=null,panMode=false,disposed=false;
    const context=()=>{const view=readView();return {members:[...view.members],selected:view.selected,selectionVersion:view.selectionVersion,revision:view.revision};};
    const current=value=>value.revision===readView().revision&&value.selectionVersion===readView().selectionVersion;
    const mapped=item=>item.visible===true&&item.width>0&&item.height>0&&readView().layers.filter(layer=>layer.key===item.key).length===1;
    function release(previous) {if(previous&&area.hasPointerCapture(previous.pointerId))area.releasePointerCapture(previous.pointerId);}
    function cancel() {
        const previous=gesture;gesture=null;marquee.hidden=true;release(previous);
        if(previous?.mode==='marquee'&&current(previous))command({kind:'selection',keys:previous.before.members,active:previous.before.selected});
        if(previous&&previous.mode!=='pan')manipulation.clear();draw();
    }
    function fit() {
        const bounds=area.getBoundingClientRect(),next=zoom||Math.max(.01,Math.min((bounds.width-40)/viewport.width,(bounds.height-40)/viewport.height));
        if(gesture&&gesture.mode!=='pan'&&scale!==next)cancel();scale=next;
        stage.style.width=`${viewport.width*scale}px`;stage.style.height=`${viewport.height*scale}px`;stage.style.transform=`translate(${pan.x}px,${pan.y}px)`;
        scene.style.width=`${viewport.width}px`;scene.style.height=`${viewport.height}px`;scene.style.transform=`scale(${scale})`;draw();
    }
    function bounds(node,item) {node.hidden=!item;if(item)Object.assign(node.style,{left:`${item.x*scale}px`,top:`${item.y*scale}px`,width:`${item.width*scale}px`,height:`${item.height*scale}px`});}
    function draw() {
        if(disposed)return;const view=readView();
        if(gesture&&gesture.mode!=='pan'&&!current(gesture)){cancel();return;}
        const geometry=gesture?.rendered??items;
        bounds(outline,geometry.find(item=>item.key===view.selected&&mapped(item)));outline.dataset.locked=String(view.locked);
        for(const[key,node]of outlines)if(!view.members.includes(key)||key===view.selected){node.remove();outlines.delete(key);}
        for(const key of view.members.filter(key=>key!==view.selected)) {
            let node=outlines.get(key);if(!node){node=window.document.createElement('div');node.className='full-editor-selection full-editor-selection-member';node.dataset.selectionMember=key;stage.append(node);outlines.set(key,node);}
            bounds(node,geometry.find(item=>item.key===key&&mapped(item)));
        }
    }
    const resize=new ResizeObserver(fit);resize.observe(area);
    const scenePoint=event=>{const rect=stage.getBoundingClientRect();return {x:(event.clientX-rect.left)/scale,y:(event.clientY-rect.top)/scale};};
    const hit=point=>items.findLast(item=>mapped(item)&&point.x>=item.x&&point.x<=item.x+item.width&&point.y>=item.y&&point.y<=item.y+item.height);
    const down=event=>{
        if(gesture||![0,1].includes(event.button)||!(event.target instanceof Element))return;
        const handle=event.target.closest('[data-resize]');if(event.target.closest('button,input,textarea,select,[contenteditable=true]')&&!handle)return;
        area.focus({preventScroll:true});const point=scenePoint(event),view=readView();
        if(event.button===0&&event.altKey) {
            command({kind:'selection',keys:view.members,active:view.selected});
            const before=context();gesture={...before,before,mode:'marquee',point,pointerId:event.pointerId};marquee.hidden=false;
            Object.assign(marquee.style,{left:`${point.x*scale}px`,top:`${point.y*scale}px`,width:'0px',height:'0px'});
        } else if(event.button===1||panMode)gesture={mode:'pan',x:event.clientX,y:event.clientY,start:{...pan},pointerId:event.pointerId};
        else {
            const item=handle?items.find(item=>item.key===view.selected&&mapped(item)):hit(point);
            if(event.ctrlKey){if(item)command({kind:'select',key:item.key,toggle:true});event.preventDefault();return;}
            if(!item){command({kind:'select',key:null});event.preventDefault();return;}
            const retained=view.members.includes(item.key);
            command(retained?{kind:'selection',keys:view.members,active:view.selected}:{kind:'select',key:item.key});
            const capture=context();
            if(handle&&readView().locked)return;
            gesture={...capture,id:crypto.randomUUID(),mode:handle?'resize':'move',handle:handle?.dataset.resize??'',item,
                targets:items.filter(item=>capture.members.includes(item.key)),x:event.clientX,y:event.clientY,scale,pointerId:event.pointerId,
                clicked:item.key,retained,dragged:false,dx:0,dy:0,planned:null,rendered:null};
        }
        area.setPointerCapture(event.pointerId);event.preventDefault();
    };
    const move=event=>{
        if(!gesture||event.pointerId!==gesture.pointerId)return;
        if(gesture.mode==='pan'){pan={x:gesture.start.x+event.clientX-gesture.x,y:gesture.start.y+event.clientY-gesture.y};fit();return;}
        if(!current(gesture)){cancel();return;}
        if(gesture.mode==='marquee') {
            const point=scenePoint(event),left=Math.min(point.x,gesture.point.x),top=Math.min(point.y,gesture.point.y),width=Math.abs(point.x-gesture.point.x),height=Math.abs(point.y-gesture.point.y);
            Object.assign(marquee.style,{left:`${left*scale}px`,top:`${top*scale}px`,width:`${width*scale}px`,height:`${height*scale}px`});
            const keys=width>0&&height>0?items.filter(item=>mapped(item)&&Math.min(left+width,item.x+item.width)>Math.max(left,item.x)&&Math.min(top+height,item.y+item.height)>Math.max(top,item.y)).map(item=>item.key):[];
            // Only this gesture's own selection transition may update its cancellation fence.
            const previous=gesture;gesture=null;command({kind:'selection',keys});gesture=previous;Object.assign(gesture,context());draw();return;
        }
        const rawX=(event.clientX-gesture.x)/gesture.scale,rawY=(event.clientY-gesture.y)/gesture.scale;
        if(Math.hypot(rawX,rawY)>2)gesture.dragged=true;
        if(!gesture.dragged)return;
        const snap=value=>root.querySelector('[data-snap]').checked&&!event.shiftKey?Math.round(value/8)*8:value;
        const dx=snap(rawX),dy=snap(rawY);if(dx===gesture.dx&&dy===gesture.dy)return;gesture.dx=dx;gesture.dy=dy;
        if(!dx&&!dy){gesture.planned=null;gesture.rendered=null;gesture.id=crypto.randomUUID();manipulation.clear();draw();return;}
        const item=gesture.item;
        const planned=manipulation.plan({kind:gesture.mode,handle:gesture.handle,dx,dy,targets:gesture.targets,computed:item.styles,layoutX:item.layoutX,layoutY:item.layoutY,observedWidth:item.width,observedHeight:item.height},gesture.selected,gesture.revision,gesture.selectionVersion);
        if(planned.kind==='unchanged'){gesture.planned=null;manipulation.clear();draw();return;}
        if(planned.kind!=='planned'){cancel();return;}
        gesture.planned=planned;if(!manipulation.present(gesture.id,planned.presentation,gesture.revision)){cancel();return;}
    };
    const up=event=>{
        if(!gesture||event.pointerId!==gesture.pointerId)return;move(event);
        const previous=gesture;gesture=null;marquee.hidden=true;release(previous);
        if(!previous||previous.mode==='pan'||previous.mode==='marquee')return;
        if(!previous.dragged){manipulation.clear();command({kind:'select',key:previous.clicked});draw();return;}
        if(!previous.planned||(!previous.dx&&!previous.dy)){manipulation.clear();draw();return;}
        manipulation.commit(previous.planned);
    };
    const interrupted=event=>{if(gesture&&event.pointerId===gesture.pointerId)cancel();};
    const key=event=>{if(event.key==='Escape'&&gesture){event.preventDefault();cancel();}};
    const aux=event=>{if(event.button===1)event.preventDefault();};
    area.addEventListener('pointerdown',down);area.addEventListener('pointermove',move);area.addEventListener('pointerup',up);area.addEventListener('pointercancel',interrupted);area.addEventListener('lostpointercapture',interrupted);area.addEventListener('auxclick',aux);
    root.addEventListener('keydown',key);window.addEventListener('blur',cancel);
    return {observed(next,size){items=next;if(size)viewport=size;fit();},draw,cancel,refresh:fit,
        contextTarget(point){return hit(scenePoint({clientX:point.x,clientY:point.y}))?.key??null;},
        presented(id,next){if(gesture?.id!==id||!current(gesture))return;gesture.rendered=next;draw();},
        nudge(dx,dy){command({kind:'move',dx,dy,targets:items});},
        pan(value){cancel();panMode=value;},viewport(width,height){cancel();viewport={width,height};fit();},zoom(value){cancel();zoom=value;pan={x:0,y:0};fit();},
        align(axis,edge,selection,revision){if(selection!==readView().selected)return;const item=items.find(i=>i.key===selection);if(!item)return;
            const size=axis==='x'?viewport.width:viewport.height,length=axis==='x'?item.width:item.height,value=`${Math.round(edge===0?0:edge===1?(size-length)/2:size-length)}px`;
            const horizontal=axis==='x',otherTranslate=readView().styles.translate?.split(/\s+/)??['0px','0px'];
            command({kind:'layout',properties:{position:'absolute',[horizontal?'left':'top']:value,[horizontal?'right':'bottom']:'auto',[`--blokebot-${axis}`]:value,[`--blokebot-anchor-${axis}`]:'0',translate:horizontal?`0px ${otherTranslate[1]??'0px'}`:`${otherTranslate[0]??'0px'} 0px`},metadata:{[axis]:value,[horizontal?'horizontalAnchor':'verticalAnchor']:0}},selection,revision);},
        dispose(){cancel();disposed=true;resize.disconnect();for(const node of outlines.values())node.remove();marquee.remove();area.removeEventListener('pointerdown',down);area.removeEventListener('pointermove',move);area.removeEventListener('pointerup',up);area.removeEventListener('pointercancel',interrupted);area.removeEventListener('lostpointercapture',interrupted);area.removeEventListener('auxclick',aux);root.removeEventListener('keydown',key);window.removeEventListener('blur',cancel);}
    };
}
