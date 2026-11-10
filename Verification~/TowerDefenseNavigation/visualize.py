#!/usr/bin/env python3
"""Render a saved navigation trace as a standalone, offline HTML replay."""
import argparse
import html
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("trace", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
data = json.loads(args.trace.read_text(encoding="utf-8"))
encoded = json.dumps(data, ensure_ascii=False, separators=(",", ":")).replace("<", "\\u003c")
page = r"""<!doctype html>
<html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>__TITLE__</title><style>
body{margin:0;background:#101924;color:#e9eff5;font:15px/1.6 system-ui,sans-serif}main{max-width:1200px;margin:auto;padding:24px}
h1{font-size:22px;margin:0 0 8px}p{margin:8px 0;color:#aebfd0}.bar{display:flex;gap:12px;align-items:center;flex-wrap:wrap}
button,select{background:#263b50;color:#f3f7fc;border:1px solid #4f6a81;padding:7px 12px;border-radius:6px}input{flex:1;min-width:180px}
canvas{display:block;width:1100px;max-width:100%;height:auto;margin:16px auto;background:#15212e;border-radius:8px}
.legend span{margin-right:18px}.pill{padding:2px 8px;border-radius:4px;background:#263b50}#unit{min-height:48px}
</style><main><h1>__TITLE__</h1><div id="summary"></div>
<p>绿色为通行地形，浅色为允许驻留的停车格。蓝色移动，金色候场，红色攻击；灰色圆盘为基地。点击单位可查看其目标与轨迹。</p>
<div class="bar"><button id="back">上一帧</button><button id="play">播放</button><button id="next">下一帧</button>
<input id="frame" type="range" min="0" step="1" value="0"><span id="clock" class="pill"></span>
<select id="speed"><option value="200">慢速</option value="80" selected>正常</option value="20">快速</option></select></div>
<canvas id="map" width="1100" height="900"></canvas><div id="unit"></div>
<p>轨迹通常每 10 tick 采样；连续碰撞、静态障碍和攻击条件断言在每一个 tick 执行。这个回放展示纯逻辑坐标，不代表 Unity 渲染或设备性能验收。</p>
<script id="data" type="application/json">__DATA__</script><script>
const d=JSON.parse(document.getElementById('data').textContent),frames=d.trajectory,canvas=document.getElementById('map'),ctx=canvas.getContext('2d');
const slider=document.getElementById('frame'),clock=document.getElementById('clock'),cell=d.cellSize||1000;
slider.max=Math.max(0,frames.length-1);let chosen=null,playing=false,timer=null;
if(location.hash==='#last')slider.value=slider.max;
const pad=24,scale=Math.min((canvas.width-2*pad)/d.width,(canvas.height-2*pad)/d.height),ox=(canvas.width-d.width*scale)/2,oy=(canvas.height-d.height*scale)/2;
const tx=(d.target.x+.5)*cell,ty=(d.target.y+.5)*cell;
const px=x=>ox+x/cell*scale,py=y=>canvas.height-oy-y/cell*scale;
document.getElementById('summary').textContent=(d.success?'验收通过':'验收失败')+' · '+d.outcome+' · 接纳 '+d.spawned+'/'+d.requested+' · 拒绝 '+d.rejected+' · 攻击 '+d.attacking+' · 候场 '+d.waiting+(d.error?' · '+d.error:'');
function circle(x,y,r,fill,stroke){ctx.beginPath();ctx.arc(px(x),py(y),r/cell*scale,0,Math.PI*2);if(fill){ctx.fillStyle=fill;ctx.fill()}if(stroke){ctx.strokeStyle=stroke;ctx.stroke()}}
function draw(){const index=+slider.value,f=frames[index];if(!f)return;ctx.clearRect(0,0,canvas.width,canvas.height);
for(let y=0;y<d.height;y++)for(let x=0;x<d.width;x++){const c=y*d.width+x;ctx.fillStyle=!d.walkable[c]?'#1e2b37':(d.holdingAllowed[c]?'#76927b':'#355d52');ctx.fillRect(px(x*cell),py((y+1)*cell),scale+.1,scale+.1)}
circle(tx,ty,d.targetRadius,'#9eacb5','#dae2e8');const selected=f.units.find(u=>u.id===chosen);
if(selected){ctx.strokeStyle='#e8ffff';ctx.lineWidth=2;ctx.beginPath();let started=false;for(let i=0;i<=index;i++){const u=frames[i].units.find(v=>v.id===chosen);if(u){if(!started){ctx.moveTo(px(u.x),py(u.y));started=true}else ctx.lineTo(px(u.x),py(u.y))}}ctx.stroke();
const gx=selected.slotId%d.width,gy=Math.floor(selected.slotId/d.width);ctx.strokeStyle='#ffffff';ctx.strokeRect(px(gx*cell)+1,py((gy+1)*cell)+1,scale-2,scale-2)}
for(const u of f.units)circle(u.x,u.y,u.radius,u.state==='Attacking'?'#ff6c69':u.state==='Waiting'?'#f5cc6b':'#69baff',u.id===chosen?'#ffffff':null);
clock.textContent='tick '+f.tick+' · '+f.units.length+' 单位 · '+index+'/'+(frames.length-1);
document.getElementById('unit').textContent=selected?'单位 '+selected.id+' · '+selected.state+' · ('+selected.x+', '+selected.y+') mm · 目标格 '+selected.slotId:'选择一个单位以查看毫米坐标、状态、目标和已采样轨迹。'}
function stop(){playing=false;clearTimeout(timer);document.getElementById('play').textContent='播放'}
function run(){if(!playing)return;if(+slider.value>=frames.length-1){stop();return}slider.value=+slider.value+1;draw();timer=setTimeout(run,+document.getElementById('speed').value)}
slider.oninput=draw;document.getElementById('back').onclick=()=>{stop();slider.value=Math.max(0,+slider.value-1);draw()};document.getElementById('next').onclick=()=>{stop();slider.value=Math.min(frames.length-1,+slider.value+1);draw()};
document.getElementById('play').onclick=()=>{if(playing){stop();return}if(+slider.value>=frames.length-1)slider.value=0;playing=true;document.getElementById('play').textContent='暂停';run()};
canvas.onclick=e=>{const box=canvas.getBoundingClientRect(),x=(e.clientX-box.left)/box.width*canvas.width,y=(e.clientY-box.top)/box.height*canvas.height;const f=frames[+slider.value];let best=18;chosen=null;for(const u of f.units){const dist=Math.hypot(px(u.x)-x,py(u.y)-y);if(dist<best){best=dist;chosen=u.id}}draw()};
draw();</script></main></html>"""
title = f"{data['name']} · seed {data['seed']}"
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(page.replace("__TITLE__", html.escape(title)).replace("__DATA__", encoded), encoding="utf-8")
print(args.output)
