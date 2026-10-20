from pathlib import Path
from zipfile import ZipFile
from lxml import etree as E
from copy import deepcopy
import hashlib
src=Path('C:/Github/CREMS/CS400-Log5-S11219885.docx');out=Path('C:/Github/CREMS/CS400-Log6-S11219885.docx');work=Path('C:/Github/CREMS/log6-work')
ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'};W='{'+ns['w']+'}'
with ZipFile(src) as z:parts={n:z.read(n) for n in z.namelist()};info=z.infolist()
root=E.fromstring(parts['word/document.xml']);body=root.find('w:body',ns);ps=body.findall('w:p',ns);ts=body.findall('w:tbl',ns)
(work/'artifact.md').write_text('''# Log 6 template contract
Reference: C:/Github/CREMS/CS400-Log5-S11219885.docx. Six landscape pages, inspected in the preceding task. Preserve page geometry, logo, headings, typography, lists, table grids, footer and all package parts except document.xml. Rewrite body slots 7-17,20-32,35,38-41 for progress on nearly all areas, current report preparation and bug fixes, and testing planned for 19-23 October. Update metadata to Log 6, 5-16 October 2026. Update meeting to 8th Client Meeting on 8 October; time unconfirmed. Update ten weekday attendance rows; hours, signatures and signing date remain blank. Continue previous academic week labels as Weeks 10 and 11. Retain peer review pending. Use Word rendering fallback previously validated because the packaged LibreOffice renderer is unavailable on this host.
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
 if ': ' in text and text.split(': ',1)[0] in ['Autonomy','Influence','Complexity','Business Skills','Issue','Resolution','Review focus','Next step']:
  label,text=text.split(': ',1);lead=deepcopy(r);pr=lead.find('w:rPr',ns)
  if pr is None:pr=E.SubElement(lead,W+'rPr')
  E.SubElement(pr,W+'b');t=E.SubElement(lead,W+'t');t.text=label+': ';t.set('{http://www.w3.org/XML/1998/namespace}space','preserve');p.append(lead)
 E.SubElement(r,W+'t').text=text;p.append(r)
def cell(c,text):
 pp=c.findall('w:p',ns);put(pp[0],text)
 for p in pp[1:]:c.remove(p)
changes={
7:'Continue working on the remaining parts of CREMS, including the maintenance module.',
8:'Review the progress made across the system and identify any unfinished work.',
9:'Attend the 8th client meeting on 8 October.',
10:'Work on the project report and document the work completed so far.',
11:'Fix the bugs identified while working on the system.',
12:'Prepare for further testing and resolving any remaining issues in the following week.',
14:'I continued working with the team on CREMS, and we have now worked on almost all parts of the system.',
15:'We held our 8th client meeting on 8 October 2026.',
16:'During the week of 12-16 October, I have been working with the team on the project report.',
17:'We are also working on a few bug fixes while preparing the report. Further testing and fixes are still planned for next week.',
20:'Continue working on the project report and update it as the remaining work is completed.',
21:'During 19-23 October, we will test the system and fix any other bugs we find.',
22:'Check that the different parts of the system work properly together and test the fixes again.',
23:'We will also investigate and resolve any other issues found during testing before moving towards project completion.',
25:'Issue: There are still a few bugs that need to be fixed.',
26:'Resolution: We are currently working on these fixes and will check them again during testing.',
28:'Review focus: We have worked on almost everything, but the system still needs further testing.',
29:'Next step: Next week, we will test the system, identify any remaining problems, and work on resolving them.',
31:'Review focus: The project report needs to match the work done in the system.',
32:'Next step: We will continue updating the report as we complete the remaining fixes and testing.',
35:'During this reporting period, I learned more about the work needed as the project gets closer to completion. Working on the report helped me look back at what we have done and explain it more clearly. I also realised that working on almost all the features does not mean the system is fully finished. We still need to test it properly and fix any remaining problems. I want to use next week to improve my testing skills and check that the fixes work as expected.',
38:'Autonomy: I continued working on the report and the remaining fixes with the team. I want to keep improving how I manage my tasks and follow up on work that still needs to be completed.',
39:'Influence: Working on the report and bug fixes has shown me why we need to keep each other updated. I want to communicate clearly about the issues I find and the work that still needs attention.',
40:'Complexity: As we get closer to finishing, I am learning that a small change can affect other parts of the system. I want to improve how I check these connections when testing and fixing bugs.',
41:'Business Skills: This period has helped me practise balancing development work with report writing. I want to manage my time well so that we can complete the report while allowing enough time for testing and fixes.'}
for i,t in changes.items():put(ps[i],t)
cell(ts[0].findall('w:tr',ns)[2].findall('w:tc',ns)[1],'Report #: 6 | Period: 5-16 October 2026')
for c,t in zip(ts[1].findall('w:tr',ns)[1].findall('w:tc',ns),['8th Client Meeting','8 October 2026','']):cell(c,t)
for i,r in enumerate(ts[2].findall('w:tr',ns)[1:]):
 cs=r.findall('w:tc',ns);cell(cs[1],str([5,6,7,8,9,12,13,14,15,16][i])+' October')
 if i%5==0:cell(cs[0],'Week '+str(10+i//5))
 for c in cs[3:]:cell(c,'')
parts['word/document.xml']=E.tostring(root,xml_declaration=True,encoding='UTF-8',standalone=True)
with ZipFile(out,'w') as z:
 for item in info:z.writestr(item,parts[item.filename])
with ZipFile(src) as a,ZipFile(out) as b:print('Changed parts:',[n for n in a.namelist() if a.read(n)!=b.read(n)])
print(out)
