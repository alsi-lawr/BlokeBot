export function createCanvas(root, readView, command) {
    const area=root.querySelector('[data-canvas-area]'), scene=root.querySelector('[data-canvas-scene]');
    const outline=root.querySelector('[data-selection]'), stage=root.querySelector('[data-canvas-stage]');
    let items=[], viewport={width:1920,height:1080}, scale=1, zoom=0, pan={x:0,y:0}, gesture=null,panMode=false;
    function fit() {
        const bounds=area.getBoundingClientRect();
        scale=zoom||Math.max(.01,Math.min((bounds.width-40)/viewport.width,(bounds.height-40)/viewport.height));
        stage.style.width=`${viewport.width*scale}px`;stage.style.height=`${viewport.height*scale}px`;
        stage.style.transform=`translate(${pan.x}px,${pan.y}px)`;
        scene.style.width=`${viewport.width}px`;scene.style.height=`${viewport.height}px`;scene.style.transform=`scale(${scale})`;
        draw();
    }
    function draw() {
        const item=items.find(item=>item.key===readView().selected);
        outline.hidden=!item;
        if(!item)return;
        Object.assign(outline.style,{left:`${item.x*scale}px`,top:`${item.y*scale}px`,width:`${item.width*scale}px`,height:`${item.height*scale}px`});
        outline.dataset.locked=String(readView().locked);
    }
    const resize=new ResizeObserver(fit);resize.observe(area);
    const down=event=>{
        if(event.button!==0||!(event.target instanceof Element))return;
        if(event.target.closest('button')&&!event.target.closest('[data-resize]'))return;
        const bounds=stage.getBoundingClientRect(),view=readView();
        const x=(event.clientX-bounds.left)/scale,y=(event.clientY-bounds.top)/scale;
        if(event.altKey||panMode)gesture={mode:'pan',x:event.clientX,y:event.clientY,start:{...pan}};
        else {
            const handle=event.target.closest('[data-resize]');
            const item=handle?items.find(item=>item.key===view.selected):items.findLast(item=>item.width>0&&item.height>0&&x>=item.x&&x<=item.x+item.width&&y>=item.y&&y<=item.y+item.height);
            if(!item)return;
            command({kind:'select',key:item.key});
            if(readView().locked)return;
            gesture={mode:handle?'resize':'move',handle:handle?.dataset.resize??'',item,x:event.clientX,y:event.clientY,revision:readView().revision,dx:0,dy:0};
        }
        area.setPointerCapture(event.pointerId);event.preventDefault();
    };
    const move=event=>{
        if(!gesture)return;
        if(gesture.mode==='pan'){pan={x:gesture.start.x+event.clientX-gesture.x,y:gesture.start.y+event.clientY-gesture.y};fit();return;}
        const snap=value=>root.querySelector('[data-snap]').checked&&!event.shiftKey?Math.round(value/8)*8:value;
        gesture.dx=snap((event.clientX-gesture.x)/scale);gesture.dy=snap((event.clientY-gesture.y)/scale);
        const item=gesture.item;
        Object.assign(outline.style,gesture.mode==='move'?{left:`${(item.x+gesture.dx)*scale}px`,top:`${(item.y+gesture.dy)*scale}px`}
            :{left:`${(item.x+(gesture.handle.includes('w')?gesture.dx:0))*scale}px`,top:`${(item.y+(gesture.handle.includes('n')?gesture.dy:0))*scale}px`,width:`${Math.max(1,item.width+(gesture.handle.includes('w')?-gesture.dx:gesture.handle.includes('e')?gesture.dx:0))*scale}px`,height:`${Math.max(1,item.height+(gesture.handle.includes('n')?-gesture.dy:gesture.handle.includes('s')?gesture.dy:0))*scale}px`});
    };
    const up=()=>{
        const previous=gesture;gesture=null;
        if(!previous||previous.mode==='pan'||(!previous.dx&&!previous.dy))return;
        if(previous.revision!==readView().revision){draw();return;}
        const item=previous.item;
        if(previous.mode==='move') {
            command({kind:'move',dx:previous.dx,dy:previous.dy,computed:item.styles,layoutX:item.layoutX,layoutY:item.layoutY});
        } else {
            const width=`${Math.max(1,Math.round((parseFloat(item.styles.width)||item.width)+(previous.handle.includes('w')?-previous.dx:previous.handle.includes('e')?previous.dx:0)))}px`;
            const height=`${Math.max(1,Math.round((parseFloat(item.styles.height)||item.height)+(previous.handle.includes('n')?-previous.dy:previous.handle.includes('s')?previous.dy:0)))}px`;
            command({kind:'resize',width,height,handle:previous.handle,dx:previous.dx,dy:previous.dy,computed:item.styles,layoutX:item.layoutX,layoutY:item.layoutY});
        }
    };
    const cancel=()=>{gesture=null;draw();};
    area.addEventListener('pointerdown',down);area.addEventListener('pointermove',move);area.addEventListener('pointerup',up);area.addEventListener('pointercancel',cancel);
    return { observed(next,size){items=next;if(size)viewport=size;fit();},draw,
        pan(value){panMode=value;},
        viewport(width,height){viewport={width,height};fit();},
        zoom(value){zoom=value;pan={x:0,y:0};fit();},
        align(axis,edge,selection,revision){if(selection!==readView().selected)return;const item=items.find(i=>i.key===selection);if(!item)return;
            const size=axis==='x'?viewport.width:viewport.height,length=axis==='x'?item.width:item.height;
            const value=`${Math.round(edge===0?0:edge===1?(size-length)/2:size-length)}px`;
            const horizontal=axis==='x',otherTranslate=readView().styles.translate?.split(/\s+/)??['0px','0px'];
            command({kind:'layout',properties:{position:'absolute',[horizontal?'left':'top']:value,[horizontal?'right':'bottom']:'auto', [`--blokebot-${axis}`]:value,[`--blokebot-anchor-${axis}`]:'0',translate:horizontal?`0px ${otherTranslate[1]??'0px'}`:`${otherTranslate[0]??'0px'} 0px`},metadata:{[axis]:value,[horizontal?'horizontalAnchor':'verticalAnchor']:0}},selection,revision);},
        dispose(){resize.disconnect();area.removeEventListener('pointerdown',down);area.removeEventListener('pointermove',move);area.removeEventListener('pointerup',up);area.removeEventListener('pointercancel',cancel);}
    };
}
