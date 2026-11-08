// node test-p5.js: each scene clean with perfect bots (several jitters), and each scripted mistake costs something
const fs=require('fs'),vm=require('vm');
const src=fs.readFileSync(__dirname+'/p5-example.html','utf8').match(/<script>([\s\S]*)<\/script>/)[1];
const m={exports:{}}; vm.runInNewContext(src,{module:m,Math,console}); const {runHeadless,SCENES}=m.exports;
let bad=0;
SCENES.forEach((S,si)=>{
  for(const j of [0,0.13,0.37,0.71,1.1]){ const F=runHeadless(si,'',j); const ok=!F.wiped&&F.hits===0&&(si<2||F.won);
    if(!ok)bad++; console.log(`${ok?'ok  ':'FAIL'} ${S.name} perfect j${j}: t ${F.t.toFixed(1)} hits ${F.hits} ${F.wiped||''} ${ok?'':F.log.slice(0,3).join(' | ').replace(/<[^>]+>/g,'')}`); }
  for(const [mk] of S.mistakes.slice(1)){ const F=runHeadless(si,mk,0); const ok=!!F.wiped||F.hits>0; if(!ok)bad++;
    console.log(`${ok?'ok  ':'FAIL'} ${S.name} mistake ${mk}: hits ${F.hits} ${F.wiped||'no wipe'}`); }
});
// P5 must not be clearable by waiting: XIII wipes
console.log(bad?`${bad} failing`:'all good'); process.exit(bad?1:0);
