export function createPreviewBridge(frame, readView, observed, status, presented, cancelled) {
    let current = null, request = null, transient = null, lifetime = null, sequence = 0, renderedSequence = 0, disposed = false;
    const origin = location.origin;
    function clear() {
        if(transient)frame.contentWindow?.postMessage({kind:'blokebot-full-present',previewId:transient.previewId,
            requestId:transient.requestId,revision:transient.revision,gestureId:transient.gestureId,sequence:++sequence,presentation:null},origin);
        transient=null;renderedSequence=0;
    }
    function query() {
        if (disposed || !current || current.revision !== readView().revision) return;
        clear();cancelled();
        request = { id:crypto.randomUUID(), revision:current.revision };
        frame.contentWindow?.postMessage({ kind:'blokebot-full-observe', previewId:current.id, revision:request.revision,requestId:request.id,
            selectors:readView().layers.map(layer=>({key:layer.key,selector:layer.selector})) },origin);
    }
    const receive = event => {
        if (disposed || !current || event.source !== frame.contentWindow || event.origin !== origin || current.revision !== readView().revision) return;
        const value = event.data;
        if (!value || value.previewId !== current.id) return;
        if (value.kind === 'blokebot-full-preview-status') {
            if(typeof value.state !== 'string'||!Array.isArray(value.diagnostics))return;
            status(value.state, value.diagnostics.filter(item=>typeof item?.code==='string'));
            if(value.state==='ready'){
                if(!request||lifetime!==value.lifetime){lifetime=value.lifetime;query();}
            } else {clear();cancelled();request=null;lifetime=null;}
        } else if (value.kind === 'blokebot-full-observations' && request && value.requestId === request.id
            && request.revision === readView().revision && Array.isArray(value.items)) {
            const viewport=value.viewport;
            if(!viewport||![viewport.width,viewport.height].every(size=>Number.isFinite(size)&&size>0))return;
            const keys=new Set(readView().layers.map(layer=>layer.key));
            const items=value.items.filter(item=>keys.has(item?.key)&&[item.x,item.y,item.width,item.height,item.layoutX,item.layoutY].every(Number.isFinite)
                &&item.width>=0&&item.height>=0&&item.styles&&Object.values(item.styles).every(v=>typeof v==='string'));
            if(value.gestureId!==undefined){
                if(!transient||value.gestureId!==transient.gestureId||!Number.isSafeInteger(value.sequence)
                    ||value.sequence<=renderedSequence||value.sequence>transient.sequence)return;
                renderedSequence=value.sequence;
                const item=items.find(item=>item.key===readView().selected);
                if(item)presented(value.gestureId,item);
            } else if(!transient)observed(items,viewport,request.revision);
        }
    };
    window.addEventListener('message',receive);
    frame.addEventListener('load',query);
    return {
        set(id,revision){clear();cancelled();current={id,revision};request=null;lifetime=null;frame.src=`/full-overlays/preview/${encodeURIComponent(id)}`;},
        invalidate(retain=false){if(!retain)clear();current=null;request=null;lifetime=null;cancelled();observed([],null,readView().revision);},
        present(gestureId,presentation,revision){
            if(disposed||!current||!request||revision!==current.revision||revision!==readView().revision)return false;
            if(transient?.gestureId!==gestureId)renderedSequence=0;
            transient={previewId:current.id,requestId:request.id,revision,gestureId,sequence:++sequence};
            frame.contentWindow?.postMessage({kind:'blokebot-full-present',...transient,presentation},origin);return true;
        },
        clear,query,
        dispose(){clear();disposed=true;cancelled();window.removeEventListener('message',receive);frame.removeEventListener('load',query);frame.removeAttribute('src');current=null;request=null;lifetime=null;}
    };
}
