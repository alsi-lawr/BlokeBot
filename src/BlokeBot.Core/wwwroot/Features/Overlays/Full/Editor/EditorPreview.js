export function createPreviewBridge(frame, readView, observed, status, presented, cancelled) {
    let current = null, source = null, approved = new Map(), authorized = false, request = null, active = null, pending = null, transient = null, lifetime = null, sequence = 0, renderedSequence = 0, disposed = false;
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
        sendSource();
    }
    function sendSource() {
        if (!source || !current || !request) return;
        pending = { kind:'blokebot-full-source', previewId:current.id, revision:request.revision,requestId:request.id,
            html:source.html,css:source.css,
            widgetIds:source.widgets.filter(widget=>approved.get(widget.id.value)===JSON.stringify(widget)).map(widget=>widget.id.value),
            selectors:readView().layers.map(layer=>({key:layer.key,selector:layer.selector})) };
        if (!authorized && lifetime) frame.contentWindow?.postMessage({kind:'blokebot-full-source',authorizationOnly:true,
            previewId:current.id,lifetime,revision:request.revision,requestId:request.id,widgetIds:pending.widgetIds},origin);
        drain();
    }
    function drain() {
        if (disposed || active || !pending || !lifetime || !frame.contentWindow) return;
        active = {...pending,lifetime};pending = null;
        frame.contentWindow.postMessage(active,origin);
    }
    function render(document, revision, retain = false) {
        source = document;
        if (document.widgets.length !== approved.size || document.widgets.some(widget=>approved.get(widget.id.value)!==JSON.stringify(widget))) authorized=false;
        const authorize = !authorized;
        if (disposed || !current) return true;
        if (!retain) clear();
        transient=null;renderedSequence=0;cancelled();
        current={id:current.id,revision};request={id:crypto.randomUUID(),revision};
        observed([],null,revision);sendSource();
        return authorize;
    }
    const receive = event => {
        if (disposed || !current || event.source !== frame.contentWindow || event.origin !== origin || current.revision !== readView().revision) return;
        const value = event.data;
        if (!value || value.previewId !== current.id) return;
        if (value.kind === 'blokebot-full-source-complete') {
            if (!active || value.lifetime !== lifetime || value.lifetime !== active.lifetime
                || value.requestId !== active.requestId || value.revision !== active.revision) return;
            active=null;drain();
        } else if (value.kind === 'blokebot-full-preview-status') {
            if(typeof value.state !== 'string'||!Array.isArray(value.diagnostics))return;
            if(value.state==='ready'&&typeof value.lifetime==='string'&&lifetime!==value.lifetime){
                active=null;pending=null;lifetime=value.lifetime;query();
            } else if(value.state!=='ready'){clear();cancelled();request=null;active=null;pending=null;lifetime=null;}
            if(value.revision!==undefined&&value.revision!==null&&value.revision!==current.revision)return;
            status(value.state, value.diagnostics.filter(item=>typeof item?.code==='string'));
        } else if (value.kind === 'blokebot-full-observations' && request && value.requestId === request.id
            && request.revision === readView().revision && value.revision === request.revision && Array.isArray(value.items)) {
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
        set(id,revision,document){if(disposed||revision!==readView().revision)return false;clear();cancelled();source=document;approved=new Map(document.widgets.map(widget=>[widget.id.value,JSON.stringify(widget)]));authorized=true;current={id,revision};request=null;active=null;pending=null;lifetime=null;frame.src=`/full-overlays/preview/${encodeURIComponent(id)}`;return true;},
        render,
        present(gestureId,presentation,revision){
            if(disposed||!current||!request||revision!==current.revision||revision!==readView().revision)return false;
            if(transient?.gestureId!==gestureId)renderedSequence=0;
            transient={previewId:current.id,requestId:request.id,revision,gestureId,sequence:++sequence};
            frame.contentWindow?.postMessage({kind:'blokebot-full-present',...transient,presentation},origin);return true;
        },
        clear,query,
        dispose(){clear();disposed=true;cancelled();window.removeEventListener('message',receive);frame.removeEventListener('load',query);frame.removeAttribute('src');current=null;request=null;active=null;pending=null;lifetime=null;}
    };
}
