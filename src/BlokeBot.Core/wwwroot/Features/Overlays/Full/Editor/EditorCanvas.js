export function createCanvas(root, readView, command, manipulation) {
    const area=root.querySelector('[data-canvas-area]'), scene=root.querySelector('[data-canvas-scene]');
    const outline=root.querySelector('[data-selection]'), stage=root.querySelector('[data-canvas-stage]');
    let items=[], viewport={width:1920,height:1080}, scale=1, zoom=0, pan={x:0,y:0}, gesture=null,panMode=false,disposed=false;
    function release(previous) {
        if(previous&&area.hasPointerCapture(previous.pointerId))area.releasePointerCapture(previous.pointerId);
    }
    function cancel() {
        const previous=gesture;gesture=null;
        release(previous);
        if(previous&&previous.mode!=='pan')manipulation.clear();
        draw();
    }
    function fit() {
        const bounds=area.getBoundingClientRect();
        const nextScale=zoom||Math.max(.01,Math.min((bounds.width-40)/viewport.width,(bounds.height-40)/viewport.height));
        if(gesture&&gesture.mode!=='pan'&&scale!==nextScale)cancel();
        scale=nextScale;
        stage.style.width=`${viewport.width*scale}px`;stage.style.height=`${viewport.height*scale}px`;
        stage.style.transform=`translate(${pan.x}px,${pan.y}px)`;
        scene.style.width=`${viewport.width}px`;scene.style.height=`${viewport.height}px`;scene.style.transform=`scale(${scale})`;
        draw();
    }
    function draw() {
        if(disposed)return;
        const view=readView();
        if(gesture&&gesture.mode!=='pan'&&(gesture.revision!==view.revision||gesture.selection!==view.selected||view.locked)){cancel();return;}
        const item=gesture?.rendered??items.find(item=>item.key===view.selected);
        outline.hidden=!item;
        if(!item)return;
        Object.assign(outline.style,{left:`${item.x*scale}px`,top:`${item.y*scale}px`,width:`${item.width*scale}px`,height:`${item.height*scale}px`});
        outline.dataset.locked=String(view.locked);
    }
    const resize=new ResizeObserver(fit);resize.observe(area);
    const down=event=>{
        if(gesture||event.button!==0||!(event.target instanceof Element))return;
        if(event.target.closest('button')&&!event.target.closest('[data-resize]'))return;
        const bounds=stage.getBoundingClientRect(),view=readView();
        const x=(event.clientX-bounds.left)/scale,y=(event.clientY-bounds.top)/scale;
        if(event.altKey||panMode)gesture={mode:'pan',x:event.clientX,y:event.clientY,start:{...pan},pointerId:event.pointerId};
        else {
            const handle=event.target.closest('[data-resize]');
            const item=handle?items.find(item=>item.key===view.selected):items.findLast(item=>item.width>0&&item.height>0&&x>=item.x&&x<=item.x+item.width&&y>=item.y&&y<=item.y+item.height);
            if(!item)return;
            command({kind:'select',key:item.key});
            if(readView().locked)return;
            gesture={id:crypto.randomUUID(),mode:handle?'resize':'move',handle:handle?.dataset.resize??'',item,x:event.clientX,y:event.clientY,scale,pointerId:event.pointerId,selection:item.key,revision:readView().revision,dx:0,dy:0,planned:null,rendered:null};
        }
        area.setPointerCapture(event.pointerId);event.preventDefault();
    };
    const move=event=>{
        if(!gesture||event.pointerId!==gesture.pointerId)return;
        if(gesture.mode==='pan'){pan={x:gesture.start.x+event.clientX-gesture.x,y:gesture.start.y+event.clientY-gesture.y};fit();return;}
        if(gesture.revision!==readView().revision||gesture.selection!==readView().selected||readView().locked){cancel();return;}
        const snap=value=>root.querySelector('[data-snap]').checked&&!event.shiftKey?Math.round(value/8)*8:value;
        const dx=snap((event.clientX-gesture.x)/gesture.scale),dy=snap((event.clientY-gesture.y)/gesture.scale);
        if(dx===gesture.dx&&dy===gesture.dy)return;
        gesture.dx=dx;gesture.dy=dy;
        if(!dx&&!dy){
            gesture.planned=null;gesture.rendered=null;gesture.id=crypto.randomUUID();
            manipulation.clear();draw();return;
        }
        const item=gesture.item;
        const planned=manipulation.plan({kind:gesture.mode,handle:gesture.handle,dx,dy,computed:item.styles,layoutX:item.layoutX,layoutY:item.layoutY,observedWidth:item.width,observedHeight:item.height},gesture.selection,gesture.revision);
        if(planned.kind!=='planned'){cancel();return;}
        gesture.planned=planned;
        if(!manipulation.present(gesture.id,planned.presentation,gesture.revision)){cancel();return;}
    };
    const up=event=>{
        if(!gesture||event.pointerId!==gesture.pointerId)return;
        move(event);
        const previous=gesture;gesture=null;release(previous);
        if(!previous||previous.mode==='pan')return;
        if(!previous.planned||(!previous.dx&&!previous.dy)){manipulation.clear();draw();return;}
        manipulation.commit(previous.planned);
    };
    const interrupted=event=>{if(gesture&&event.pointerId===gesture.pointerId)cancel();};
    const key=event=>{if(event.key==='Escape'&&gesture){event.preventDefault();cancel();}};
    area.addEventListener('pointerdown',down);area.addEventListener('pointermove',move);area.addEventListener('pointerup',up);area.addEventListener('pointercancel',interrupted);area.addEventListener('lostpointercapture',interrupted);
    root.addEventListener('keydown',key);window.addEventListener('blur',cancel);
    return { observed(next,size){items=next;if(size)viewport=size;fit();},draw,cancel,refresh:fit,
        presented(id,item){if(gesture?.id!==id||gesture.revision!==readView().revision)return;gesture.rendered=item;draw();},
        pan(value){cancel();panMode=value;},
        viewport(width,height){cancel();viewport={width,height};fit();},
        zoom(value){cancel();zoom=value;pan={x:0,y:0};fit();},
        align(axis,edge,selection,revision){if(selection!==readView().selected)return;const item=items.find(i=>i.key===selection);if(!item)return;
            const size=axis==='x'?viewport.width:viewport.height,length=axis==='x'?item.width:item.height;
            const value=`${Math.round(edge===0?0:edge===1?(size-length)/2:size-length)}px`;
            const horizontal=axis==='x',otherTranslate=readView().styles.translate?.split(/\s+/)??['0px','0px'];
            command({kind:'layout',properties:{position:'absolute',[horizontal?'left':'top']:value,[horizontal?'right':'bottom']:'auto', [`--blokebot-${axis}`]:value,[`--blokebot-anchor-${axis}`]:'0',translate:horizontal?`0px ${otherTranslate[1]??'0px'}`:`${otherTranslate[0]??'0px'} 0px`},metadata:{[axis]:value,[horizontal?'horizontalAnchor':'verticalAnchor']:0}},selection,revision);},
        dispose(){cancel();disposed=true;resize.disconnect();area.removeEventListener('pointerdown',down);area.removeEventListener('pointermove',move);area.removeEventListener('pointerup',up);area.removeEventListener('pointercancel',interrupted);area.removeEventListener('lostpointercapture',interrupted);root.removeEventListener('keydown',key);window.removeEventListener('blur',cancel);}
    };
}
