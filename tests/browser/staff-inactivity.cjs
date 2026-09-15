const assert = require('node:assert/strict')
const fs = require('node:fs')
const vm = require('node:vm')
const ts = require('../../src/crems-web/node_modules/typescript')
const code = ts.transpileModule(fs.readFileSync('src/crems-web/src/auth/inactivity.ts','utf8'), {compilerOptions:{module:ts.ModuleKind.CommonJS}}).outputText
function setup(){
  let now=0, next=0, renewals=0, expired=0
  const events=new Map(), timers=new Map()
  const context={exports:{}, Date:{now:()=>now}, window:{
    addEventListener:(name,fn)=>events.set(name,fn), removeEventListener:name=>events.delete(name),
    setTimeout:(fn,delay)=>{timers.set(++next,{fn,at:now+delay});return next},clearTimeout:id=>timers.delete(id)
  },document:{visibilityState:'visible',addEventListener:(name,fn)=>events.set(name,fn),removeEventListener:name=>events.delete(name)}}
  vm.runInNewContext(code,context)
  const stop=context.exports.monitorInactivity(()=>expired++,()=>renewals++)
  return {advance:ms=>{now+=ms;for(const [id,t] of [...timers])if(t.at<=now){timers.delete(id);t.fn()}},activity:()=>events.get('keydown')?.(),stop,
    counts:()=>({renewals,expired}),events,timers}
}
const active=setup()
for(let minute=0;minute<20;minute++){active.advance(60_000);active.activity();active.activity()}
assert.deepEqual(active.counts(),{renewals:20,expired:0})
active.advance(15*60_000)
assert.deepEqual(active.counts(),{renewals:20,expired:1})
active.activity()
assert.deepEqual(active.counts(),{renewals:20,expired:1})
active.stop();assert.equal(active.events.size,0);assert.equal(active.timers.size,0)
const idle=setup();idle.advance(15*60_000)
assert.deepEqual(idle.counts(),{renewals:0,expired:1})
console.log('PASS: active work renews once/minute; idle expires at 15 minutes; expired sessions are not revived; cleanup removes listeners.')
