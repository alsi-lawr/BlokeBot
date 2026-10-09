import test from 'node:test';
import assert from 'node:assert/strict';
import { createEditorDocument } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/EditorDocument.js';
import { insertedLayer } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/ToolboxInsertion.js';

test('Toolbox acknowledges the actual inserted identity, not a newer refused control result', () => {
    const owner=createEditorDocument({id:crypto.randomUUID(),html:'<main><p>Existing</p></main>',css:'',widgets:[],diagnostics:[]});
    const original=owner.view();
    const added=owner.control({kind:'add'},original.selected,original.revision);
    assert.equal(insertedLayer(original,added),added.selected);
    assert.match(added.selected,/^element:/);
    const beforeRefusal=owner.view();
    const refused=owner.control({kind:'add'},original.selected,original.revision);
    assert.equal(insertedLayer(beforeRefusal,refused),null);
    assert.equal(owner.candidate().html.match(/New text/g).length,1);
    const undone=owner.history('undo');
    assert.equal(insertedLayer(added,undone),null);
});

import { containToolboxKeyboard } from '../../../src/BlokeBot.Core/wwwroot/Components/EditorToolboxKeyboard.js';
test('Shared Toolbox keyboard boundary consumes row Delete/Undo but retains native input history and Tab/Enter', () => {
    function key(key, editable, inside=true, ctrlKey=false) {
        const event={key,ctrlKey,altKey:false,metaKey:false,defaultPrevented:false,target:{closest:selector => selector==='[data-editor-toolbox]' ? inside : editable},preventDefault(){this.defaultPrevented=true;}};
        const contained=containToolboxKeyboard(event);
        return {contained,prevented:event.defaultPrevented};
    }
    assert.deepEqual(key('Delete',false),{contained:true,prevented:true});
    assert.deepEqual(key('z',false,true,true),{contained:true,prevented:true});
    assert.deepEqual(key('z',true,true,true),{contained:true,prevented:false});
    assert.deepEqual(key('Tab',false),{contained:true,prevented:false});
    assert.deepEqual(key('Enter',false),{contained:true,prevented:false});
    assert.deepEqual(key('z',false,false,true),{contained:false,prevented:false});
});

test('An unrelated source-added identity before a stale activation is not an insertion acknowledgement', () => {
    const owner=createEditorDocument({id:crypto.randomUUID(),html:'<main>Original</main>',css:'',widgets:[],diagnostics:[]});
    const captured=owner.view(), unrelated=crypto.randomUUID();
    const newer=`<main><div data-blokebot-element="${unrelated}">Newer source layer</div></main>`;
    owner.source('html',newer);
    const liveBefore=owner.view();
    const refused=owner.control({kind:'add'},captured.selected,captured.revision);
    assert.equal(insertedLayer(liveBefore,refused),null);
    assert.equal(owner.candidate().html,newer);
    assert.ok(refused.layers.some(layer=>layer.key===`element:${unrelated}`));
});
