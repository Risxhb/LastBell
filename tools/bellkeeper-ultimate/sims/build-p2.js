const fs=require('fs');const tpl=fs.readFileSync('p2-template.html','utf8');const plan=fs.readFileSync('p2plan.json','utf8');
fs.writeFileSync(process.argv[2],tpl.replace('__PLAN__',plan));
