import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createSettingsDialog} from '../../../src/BlokeBot.Core/wwwroot/Features/Dashboards/Page/DashboardSettingsDialog.js';

function fixture() {
  class Node extends EventTarget {
    isConnected = true;
    getClientRects() { return [{}]; }
    closest() { return null; }
    focus() { f.focused = this; }
  }
  const calls=[],root=new Node(),dialog=new Node(),gear=new Node(),grid=new Node(),label=new Node(),width=new Node(),details=new Node();
  const f={root,dialog,gear,grid,label,width,details,calls,focused:null};
  root.dataset={settingsOwner:'first'};
  root.querySelector=selector=>selector.startsWith('[data-settings-for')?gear:selector==='[data-dashboard-grid]'?grid:null;
  dialog.querySelector=selector=>selector==='.dashboard-placement'?details:selector.startsWith('[data-placement-field')?width:label;
  dialog.showModal=()=>{dialog.open=true;};
  dialog.close=()=>{dialog.open=false;queueMicrotask(()=>dialog.dispatchEvent(new Event('close')));};
  f.owner=createSettingsDialog(root,dialog,{invokeMethodAsync:async(...args)=>calls.push(args)},error=>{throw error;});
  return f;
}

test('native cancel keeps draft authority separate, returns visible gear, and cannot reopen a dismissed owner',async()=>{
  const f=fixture();f.owner.sync('first','widget');f.owner.focus('first',true);
  assert.equal(f.details.open,true);assert.equal(f.focused,f.width);
  const cancel=new Event('cancel',{cancelable:true});f.dialog.dispatchEvent(cancel);await Promise.resolve();
  assert.equal(cancel.defaultPrevented,true);assert.equal(f.dialog.open,false);assert.equal(f.focused,f.gear);
  assert.deepEqual(f.calls,[['SettingsClosedAsync','first']]);
  f.owner.sync('first','widget');assert.equal(f.dialog.open,false);
  f.root.dataset.settingsOwner='second';f.owner.sync('second','widget');assert.equal(f.dialog.open,true);
  f.owner.dispose();
});

test('old queued native close and late sync/focus cannot dismiss or steal focus from a replacement modal owner',async()=>{
  const f=fixture();f.owner.sync('first','old');
  f.root.dataset.settingsOwner='closed';f.owner.sync('closed',null);
  f.root.dataset.settingsOwner='second';f.owner.sync('second','new');f.owner.focus('second',false);
  await Promise.resolve();
  assert.equal(f.dialog.open,true);assert.equal(f.focused,f.label);assert.deepEqual(f.calls,[]);
  f.owner.sync('first',null);f.owner.focus('first',true);
  assert.equal(f.dialog.open,true);assert.equal(f.focused,f.label);
  f.owner.dispose();f.dialog.dispatchEvent(new Event('cancel'));assert.deepEqual(f.calls,[]);
});

test('hidden or removed gear returns focus to the owned grid, never a hidden menuitem',()=>{
  const f=fixture();f.gear.isConnected=false;f.owner.sync('first','gone');
  f.root.dataset.settingsOwner='closed';f.owner.sync('closed',null);assert.equal(f.focused,f.grid);f.owner.dispose();
});


test('a busy disabled gear cannot absorb modal focus return; the owned grid remains the fallback',()=>{
  const f=fixture();f.gear.disabled=true;f.owner.sync('first','widget');
  f.root.dataset.settingsOwner='closed';f.owner.sync('closed',null);assert.equal(f.focused,f.grid);f.owner.dispose();
});
