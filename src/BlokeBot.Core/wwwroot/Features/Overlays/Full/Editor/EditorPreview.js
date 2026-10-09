export function createPreviewBridge(frame, readView, observed, status) {
    let current = null, request = null;
    const origin = location.origin;
    function query() {
        if (!current || current.revision !== readView().revision) return;
        request = { id:crypto.randomUUID(), revision:current.revision };
        frame.contentWindow?.postMessage({ kind:'blokebot-full-observe', requestId:request.id,
            selectors:readView().layers.map(layer=>({key:layer.key,selector:layer.selector})) },origin);
    }
    const receive = event => {
        if (!current || event.source !== frame.contentWindow || event.origin !== origin || current.revision !== readView().revision) return;
        const value = event.data;
        if (!value || value.previewId !== current.id) return;
        if (value.kind === 'blokebot-full-preview-status') {
            if(typeof value.state !== 'string'||!Array.isArray(value.diagnostics))return;
            status(value.state, value.diagnostics.filter(item=>typeof item?.code==='string'));
            if(value.state==='ready')query();
        } else if (value.kind === 'blokebot-full-observations' && request && value.requestId === request.id
            && request.revision === readView().revision && Array.isArray(value.items)) {
            const viewport=value.viewport;
            if(!viewport||![viewport.width,viewport.height].every(size=>Number.isFinite(size)&&size>0))return;
            const keys=new Set(readView().layers.map(layer=>layer.key));
            const items=value.items.filter(item=>keys.has(item?.key)&&[item.x,item.y,item.width,item.height,item.layoutX,item.layoutY].every(Number.isFinite)
                &&item.width>=0&&item.height>=0&&item.styles&&Object.values(item.styles).every(v=>typeof v==='string'));
            observed(items,viewport,request.revision);
        }
    };
    window.addEventListener('message',receive);
    frame.addEventListener('load',query);
    return {
        set(id,revision){current={id,revision};request=null;frame.src=`/full-overlays/preview/${encodeURIComponent(id)}`;},
        invalidate(){current=null;request=null;observed([],null,readView().revision);},
        query,
        dispose(){window.removeEventListener('message',receive);frame.removeEventListener('load',query);frame.removeAttribute('src');current=null;request=null;}
    };
}
