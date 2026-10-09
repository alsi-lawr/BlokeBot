import { lexer } from 'css-tree';
import { parsedCss } from './SourceRanges.js';

export function solidColor(value) {return !!lexer.matchType('color',value).matched;}

export function gradient(value) {
    const parsed=parsedCss(value,'value'),nodes=parsed.tree?.children.toArray();
    if(parsed.diagnostics.length||nodes?.length!==1||nodes[0].type!=='Function'||!['linear-gradient','radial-gradient'].includes(nodes[0].name))return null;
    const fn=nodes[0],groups=[[]],segments=[];let start=fn.loc.start.offset+fn.name.length+1;
    for(const node of fn.children.toArray()) {
        if(node.type==='Operator'&&node.value===','){segments.push({start,text:value.slice(start,node.loc.start.offset)});start=node.loc.end.offset;groups.push([]);}else groups.at(-1).push(node);
    }
    segments.push({start,text:value.slice(start,fn.loc.end.offset-1)});
    const raw=group=>{const index=groups.indexOf(group);return segments[index].text.trim();};
    let direction=fn.name==='linear-gradient'?'180deg':'ellipse at center',angle=180,centre='center';
    const first=groups[0];
    if(fn.name==='linear-gradient'&&first.length===1&&first[0].type==='Dimension'&&['deg','rad','grad','turn'].includes(first[0].unit)){
        direction=raw(groups[0]);groups.shift();segments.shift();angle=Number(first[0].value)*({deg:1,rad:180/Math.PI,grad:.9,turn:360}[first[0].unit]);
    }else if(fn.name==='linear-gradient'&&first[0]?.type==='Identifier'&&first[0].name==='to'){
        const directions={'to top':0,'to top right':45,'to right top':45,'to right':90,'to right bottom':135,'to bottom right':135,'to bottom':180,'to bottom left':225,'to left bottom':225,'to left':270,'to left top':315,'to top left':315};
        direction=raw(groups[0]);groups.shift();segments.shift();angle=directions[direction];if(angle===undefined)return null;
    }else if(fn.name==='radial-gradient'&&first[0]?.type==='Identifier'&&['circle','ellipse','at'].includes(first[0].name)){
        direction=raw(groups[0]);groups.shift();segments.shift();const tokens=first.map(node=>node.name);const at=tokens.indexOf('at');
        if(at<0){if(first.length!==1)return null;}else{
            const position=tokens.slice(at+1).join(' ');if(at>1||at===1&&!['circle','ellipse'].includes(tokens[0])||!['center','left','right','top','bottom','left top','right top','left bottom','right bottom'].includes(position))return null;
            centre=position;
        }
    }
    if(groups.length<2)return null;
    const stops=[];
    for(const group of groups){
        const color=group[0],position=group[1];
        if(!color?.loc||group.length>2||position&&position.type!=='Percentage')return null;
        const colorValue=value.slice(color.loc.start.offset,color.loc.end.offset);
        if(!lexer.matchType('color',colorValue).matched)return null;
        const segment=segments[groups.indexOf(group)],leading=segment.text.length-segment.text.trimStart().length;
        stops.push({color:colorValue,position:position?Number(position.value):null,raw:raw(group),
            colorStart:color.loc.start.offset-segment.start-leading,colorEnd:color.loc.end.offset-segment.start-leading,
            positionStart:position?position.loc.start.offset-segment.start-leading:null,positionEnd:position?position.loc.end.offset-segment.start-leading:null});
    }
    return {kind:fn.name==='linear-gradient'?'linear':'radial',direction,angle,centre,stops};
}

export function stopPosition(fill,index) {
    if(fill.stops[index].position!==null)return fill.stops[index].position;
    const positions=fill.stops.map(stop=>stop.position);positions[0]??=0;positions[positions.length-1]??=100;
    let previous=0,next=positions.length-1;
    for(let i=0;i<index;i++)if(positions[i]!==null)previous=i;
    for(let i=index+1;i<positions.length;i++)if(positions[i]!==null){next=i;break;}
    return positions[previous]+(positions[next]-positions[previous])*(index-previous)/(next-previous);
}

export function fillValue(fill) {return `${fill.kind}-gradient(${fill.direction}, ${fill.stops.map(stop=>stop.raw).join(', ')})`;}
export function editStop(fill,index,property,value) {
    const stops=fill.stops.map(stop=>({...stop})),stop=stops[index];
    if(property==='color')stop.raw=stop.raw.slice(0,stop.colorStart)+value+stop.raw.slice(stop.colorEnd);
    else if(stop.positionStart===null)stop.raw+=` ${value}%`;
    else stop.raw=stop.raw.slice(0,stop.positionStart)+`${value}%`+stop.raw.slice(stop.positionEnd);
    stop[property]=value;
    return {...fill,stops};
}

export function individualNumber(value,property) {
    if(!value||value.trim()==='none')return property==='rotate'?0:1;
    const nodes=parsedCss(value,'value').tree?.children.toArray();
    if(nodes?.length!==1)return null;
    const node=nodes[0];
    if(property==='scale')return node.type==='Number'?Number(node.value):node.type==='Percentage'?Number(node.value)/100:null;
    return node.type==='Dimension'&&['deg','rad','grad','turn'].includes(node.unit)?Number(node.value)*({deg:1,rad:180/Math.PI,grad:.9,turn:360}[node.unit]):null;
}
