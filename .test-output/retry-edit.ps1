@'
import json,re
from pathlib import Path
files=sorted(Path('C:/Users/Rahul/.codex/sessions').rglob('*.jsonl'),key=lambda p:p.stat().st_mtime,reverse=True)[:3]
for f in files:
 for l in f.read_text(encoding='utf-8').splitlines():
  if '# simplify details, technician' not in l: continue
  o=json.loads(l)
  def walk(x):
   if isinstance(x,dict):
    for v in x.values():yield from walk(v)
   elif isinstance(x,list):
    for v in x:yield from walk(v)
   elif isinstance(x,str):yield x
  for v in walk(o):
   if 'text(await tools.exec_command({cmd:' in v and '# simplify details, technician' in v:
    m=re.search(r'cmd:("(?:[^"\\]|\\.)*")',v)
    if m:
     cmd=json.loads(m.group(1)).replace(".decode('cp1252')",".decode('cp1252')")
     Path('.test-output/retry-edit.ps1').write_text(cmd,encoding='utf-8')
     print('Recovered edit command');raise SystemExit
raise SystemExit('Edit command not found')
'@ | python -
if ($LASTEXITCODE -eq 0) { & ./.test-output/retry-edit.ps1 }