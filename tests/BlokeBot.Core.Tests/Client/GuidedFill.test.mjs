import test from 'node:test';
import assert from 'node:assert/strict';
import { gradient, editStop, fillValue, stopPosition, individualNumber } from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/GuidedFill.js';

test('Stop edits retain other authored segments, alpha functions and implicit stop interpolation',()=>{
 const value='linear-gradient(.25turn, rgba(20, 30, 40, .4) 0%, /* retained */ #abcdef, hsl(120 50% 30%) 100%)';
 const fill=gradient(value);assert(fill);assert.equal(stopPosition(fill,1),50);
 const changed=fillValue(editStop(fill,2,'position',80));
 assert.equal(changed,'linear-gradient(.25turn, rgba(20, 30, 40, .4) 0%, /* retained */ #abcdef, hsl(120 50% 30%) 80%)');
 assert.equal(fill.stops[2].position,100);
});

test('Unsupported layered, variable and specialist fills stay custom rather than supplying replacement stops',()=>{
 for(const value of ['linear-gradient(red, blue), url(image.png)','var(--fill)','conic-gradient(red, blue)','linear-gradient(red 0% 30%, blue)','radial-gradient(closest-side at 25% 40%, red, blue)','linear-gradient(var(--color), blue)'])assert.equal(gradient(value),null,value);
 assert.equal(individualNumber('rotateX(20deg)','rotate'),null);
 assert.equal(individualNumber('1 2','scale'),null);
 assert.equal(individualNumber('.5turn','rotate'),180);
 assert.equal(individualNumber('none ','scale'),1);
});
