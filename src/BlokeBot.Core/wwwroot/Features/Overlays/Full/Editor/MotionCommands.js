import { planStyles } from './VisualStyles.js';
import { string } from 'css-tree';

export function motionPreset(snapshot,key,{phase,preset,duration,easing}) {
    if(!['entrance','exit','state'].includes(phase)||!['none','fade','rise','scale'].includes(preset)||!Number.isFinite(duration)||duration<0)return {kind:'invalid'};
    const name=`blokebot-${crypto.randomUUID()}`;
    const initial=preset==='rise'?'opacity:0;translate:0 16px':preset==='scale'?'opacity:0;scale:.92':'opacity:0';
    const end=preset==='rise'?'opacity:1;translate:0 0':preset==='scale'?'opacity:1;scale:1':'opacity:1';
    const animation=preset==='none'?'none':`${name} ${duration}ms ${easing} both`;
    const planned=planStyles(snapshot,key,{[phase==='entrance'?'animation':`--blokebot-motion-${phase}`]:animation});
    if(planned.kind!=='planned')return planned;
    const identity=planned.selected.slice(planned.selected.indexOf(':')+1);
    const selector=`[data-blokebot-${planned.selected.startsWith('widget:')?'widget':'element'}=${string.encode(identity)}]`;
    const keyframes=preset==='none'?'':`\n@keyframes ${name} { from { ${phase==='exit'?end:initial} } to { ${phase==='exit'?initial:end} } }\n`;
    const playback=phase==='entrance'?'':`\n${selector}[data-blokebot-phase=${string.encode(phase)}] { animation: var(--blokebot-motion-${phase}); }\n`;
    const reduced=preset==='none'?'':`\n@media (prefers-reduced-motion: reduce) { ${selector}${phase==='entrance'?'':`[data-blokebot-phase=${string.encode(phase)}]`} { animation: none; } }\n`;
    const addition=keyframes+playback+reduced;
    const tail=planned.edits.find(edit=>edit.buffer==='css'&&edit.start===snapshot.css.length&&edit.end===snapshot.css.length);
    if(tail)tail.after+=addition;
    else if(addition)planned.edits.push({buffer:'css',start:snapshot.css.length,end:snapshot.css.length,before:'',after:addition});
    return planned;
}
