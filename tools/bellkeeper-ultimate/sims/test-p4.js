// run the P4 page's fight headless: perfect must lose nobody on every start jitter; each mistake must cost the run
const fs=require('fs');const html=fs.readFileSync(process.argv[2]||'p4-example.html','utf8');const js=html.split('<script>')[1].split('</script>')[0];
const m={exports:{}};new Function('module','window',js)(m,undefined);
const seeds=+(process.argv[3]||10); let clean=0;
const bad=F=>F.log.filter(l=>/✗/.test(l)).slice(0,2).reverse().join(' | ').replace(/<[^>]+>/g,'');
for(let j=0;j<seeds;j++){const F=m.exports.runHeadless('',j*0.37);if(!F.wiped&&F.deaths===0)clean++;else console.log('perfect jitter',j,F.wiped||'survived','t',F.t.toFixed(1),'deaths',F.deaths,'|',bad(F));}
console.log(`perfect: ${clean}/${seeds} clean`);
{ const F=m.exports.runHeadless('',0); console.log('damage over',F.t.toFixed(0),'s (I and IV are melee):',F.dealt.map(x=>x|0).join(' / ')); }
for(const mk of ['still','stay','jam','pit']){const F=m.exports.runHeadless(mk);console.log(mk.padEnd(6),F.wiped?'WIPE: '+F.wiped:'survived','· t',F.t.toFixed(1),'· deaths',F.deaths,'|',bad(F));}
