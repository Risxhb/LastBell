// run the page's script headless: stub the DOM, step the sim, report clean / wipe for each mistake option
const fs=require('fs');const html=fs.readFileSync(process.argv[2],'utf8');const js=html.split('<script>')[1].split('</script>')[0];
const ctx=new Proxy({},{get:(o,k)=>k in o?o[k]:(()=>{}),set:(o,k,v)=>(o[k]=v,true)});
const el=()=>({getContext:()=>ctx,width:760,height:760,onclick:null,onchange:null,textContent:'',innerHTML:'',value:''});
const els={};const document={getElementById:id=>els[id]||(els[id]=el())};
for(const m of ['','pass','half','chain']){
  const run=new Function('document','performance','requestAnimationFrame','MISTAKE',js.slice(0,js.lastIndexOf('reset(); {'))+"mistake=MISTAKE;reset();while(t<END&&!wiped){t+=1/120;step();} return {wiped,fails,checked,log};");
  const r=run(document,{now:()=>0},()=>{},m);
  console.log((m||'perfect').padEnd(8),r.wiped?'WIPE: '+r.wiped:'clean','· beats',r.checked+1,'· rule breaks',r.fails);
  if(!m) console.log(r.log.filter(l=>/✗|passes/.test(l)).slice(0,6).join('\n').replace(/<[^>]+>/g,''));
}
