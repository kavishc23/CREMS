from pathlib import Path
from zipfile import ZipFile
from lxml import etree as E
from copy import deepcopy
import hashlib,json
src=Path('C:/Github/CREMS/CS400-Log4-S11219885.docx');out=Path('C:/Github/CREMS/CS400-Log5-S11219885.docx');work=Path('C:/Github/CREMS/log5-work')
ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'};W='{'+ns['w']+'}'
with ZipFile(src) as z:parts={n:z.read(n) for n in z.namelist()};info=z.infolist()
root=E.fromstring(parts['word/document.xml']);body=root.find('w:body',ns);ps=body.findall('w:p',ns);ts=body.findall('w:tbl',ns)
(work/'artifact.md').write_text('''# Log 5 template contract
Reference: C:/Github/CREMS/CS400-Log4-S11219885.docx. Preserve its six-page landscape layout, logo, section geometry, fonts, heading order, numbering, table grids and footer. Previously rendered and inspected in this task. All package parts except document.xml remain byte-identical.
Rewrite task and reflection paragraphs for 21 September to 2 October. Keep current maintenance work in progress; do not claim individual maintenance features are complete. Update report number and ten attendance dates, continuing the previous log week numbering. Clear old meeting details and signing date. Keep unconfirmed attendance and signatures blank. Remove surplus completed-task bullet slots. Render through Word because the packaged renderer has no available LibreOffice executable.
''',encoding='utf8')
(work/'reference.sha256').write_text(hashlib.sha256(src.read_bytes()).hexdigest())
def put(p,text):
 r=deepcopy(p.find('w:r',ns)) if p.find('w:r',ns) is not None else E.Element(W+'r')
 for c in list(r):
  if c.tag!=W+'rPr':r.remove(c)
 pr=r.find('w:rPr',ns)
 if pr is not None:
  for b in pr.findall('w:b',ns):pr.remove(b)
 for c in list(p):
  if c.tag!=W+'pPr':p.remove(c)
 if ': ' in text and text.split(': ',1)[0] in ['Autonomy','Influence','Complexity','Business Skills','Review focus','Next step']:
  label,text=text.split(': ',1);lead=deepcopy(r);pr=lead.find('w:rPr',ns)
  if pr is None:pr=E.SubElement(lead,W+'rPr')
  E.SubElement(pr,W+'b');t=E.SubElement(lead,W+'t');t.text=label+': ';t.set('{http://www.w3.org/XML/1998/namespace}space','preserve');p.append(lead)
 E.SubElement(r,W+'t').text=text;p.append(r)
def cell(c,text):
 pp=c.findall('w:p',ns);put(pp[0],text)
 for p in pp[1:]:c.remove(p)
changes={
7:'Continue working on bookings and converting quotations into bookings.',
8:'Work on booking approvals, agreements, and notifications.',
9:'Continue the pre-hire and post-hire work started in the previous reporting period.',
10:'Work on checklists, handover records, returns, and inspection details.',
11:'Check how the booking, pre-hire, and post-hire modules connect with each other.',
12:'Start work on the maintenance module as we move ahead with the project.',
14:'I continued working with the team on the CREMS project during this reporting period.',
15:'We had already completed part of the pre-hire and post-hire work in the previous period, which helped us stay ahead of the plan.',
16:'We have now started working on the maintenance module, which is our current focus.',
17:'The maintenance work is still in progress, and we will continue developing it in the next reporting period.',
24:'Continue developing the maintenance module and the remaining maintenance features.',
25:'Work on maintenance job records, service schedules, and maintenance costs.',
26:'Check how maintenance links with asset availability, damage records, and returning assets to service.',
27:'We have started maintenance earlier than the planned 5-9 October period. The module is still in progress.',
29:'Review focus: Assets under maintenance need to have the correct availability status.',
30:'Next step: We will check how maintenance status affects whether an asset can be hired out.',
32:'Review focus: Maintenance records need to keep job details and costs linked to the correct asset.',
33:'Next step: We will check these links as we continue developing the maintenance module.',
35:'Review focus: Post-hire damage and maintenance work need to connect properly.',
36:'Next step: We will check how damage found after a hire is followed up before the asset is returned to service.',
39:'During this reporting period, I started learning more about the maintenance side of CREMS. I am getting a better understanding of how maintenance connects with asset availability and the condition of an asset after a hire. This has helped me see why the system needs to keep track of assets that are not ready to be hired out. Since we are still working on this module, I want to improve my understanding of maintenance records, costs, and the checks needed before an asset can be used again.',
42:'Autonomy: I continued working on my tasks with the team as we moved into the maintenance module. I want to keep improving how I organise my work and follow up on unfinished tasks.',
43:'Influence: Working with the team has helped me understand why we need to keep each other updated as we move between modules. I want to keep improving how I share my ideas and discuss the work that still needs to be done.',
44:'Complexity: The maintenance module is helping me understand how asset status, availability, damage, and costs connect. I still need to learn more about how these parts should work together in the system.',
45:'Business Skills: Starting maintenance early has shown me the benefit of keeping up with the project schedule. I want to continue managing my time well while making sure we allow enough time to test the work.',
53:'This log still needs to be reviewed by my group members.'}
for i,t in changes.items():put(ps[i],t)
for i in [18,19,20,21]:body.remove(ps[i])
cell(ts[0].findall('w:tr',ns)[2].findall('w:tc',ns)[1],'Report #: 5 | Period: 21 September-2 October 2026')
for c in ts[1].findall('w:tr',ns)[1].findall('w:tc',ns):cell(c,'')
dates=['21 September','22 September','23 September','24 September','25 September','28 September','29 September','30 September','1 October','2 October']
for i,r in enumerate(ts[2].findall('w:tr',ns)[1:]):
 cs=r.findall('w:tc',ns);cell(cs[1],dates[i])
 if i%5==0:cell(cs[0],'Week '+str(8+i//5))
 for c in cs[3:]:cell(c,'')
cell(ts[3].findall('w:tr',ns)[0].findall('w:tc',ns)[1],'')
for idx in [38,48,49]:
 pr=ps[idx].find('w:pPr',ns)
 if pr is None:pr=E.SubElement(ps[idx],W+'pPr')
 E.SubElement(pr,W+'keepNext')
pr=ps[52].find('w:pPr',ns)
E.SubElement(pr,W+'pageBreakBefore')
last=ps[54]
for c in list(last):last.remove(c)
pr=E.SubElement(last,W+'pPr');sp=E.SubElement(pr,W+'spacing');sp.set(W+'before','0');sp.set(W+'after','0');sp.set(W+'line','20');sp.set(W+'lineRule','exact');rp=E.SubElement(pr,W+'rPr');E.SubElement(rp,W+'sz').set(W+'val','2')
parts['word/document.xml']=E.tostring(root,xml_declaration=True,encoding='UTF-8',standalone=True)
with ZipFile(out,'w') as z:
 for item in info:z.writestr(item,parts[item.filename])
print(out)

