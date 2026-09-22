from pathlib import Path
from zipfile import ZipFile
from lxml import etree as E
from copy import deepcopy
path=Path('C:/Github/CREMS/CS400-Log4-S11219885.docx')
ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'};W='{'+ns['w']+'}'
changes={
7:'Continue working on the asset module for Motors and Carptrac.',
8:'Work on asset categories, attributes, templates, and asset details.',
9:'Develop asset location and status tracking, meter records, and QR/barcode features.',
10:'Work on the availability calendar and checks for overlapping hire periods.',
11:'Develop rental rates, extra charges, deposits, discounts, and tax calculations.',
12:'Continue working on quotations, customer rates, and quotation updates.',
14:'I worked with the team on the asset module during 7-11 September.',
15:'We worked on asset categories, attributes, and templates for Motors and Carptrac.',
16:'We also continued working on asset locations, status history, meter records, and QR/barcode features.',
17:'During 14-18 September, I continued working with the team on availability, rates, and quotations.',
18:'We worked on availability checks and setting up hourly, daily, and per-unit rental rates.',
19:'We continued developing quotation features, including extra charges, deposits, discounts, tax, and quotation updates.',
20:'We also completed part of the upcoming pre-hire and post-hire work this week.',
21:'We started this work early so that we could stay ahead of time and have more time for the remaining tasks.',
24:'Continue testing the asset, availability, rate, and quotation features together.',
25:'Continue with bookings, approvals, agreements, and notifications in the next reporting period.',
26:'Finish the remaining pre-hire and post-hire work and connect it with the other modules.',
27:'We worked ahead of the plan by completing part of the pre-hire and post-hire tasks this week.',
29:'Review focus: We need to make sure the asset details suit both Motors and Carptrac.',
30:'Next step: We will continue checking the attributes and templates for each type of asset.',
32:'Review focus: Availability and rental rates need to stay consistent when creating quotations and bookings.',
33:'Next step: We will test overlapping hire periods, rate selection, and quotation calculations together.',
35:'Review focus: The pre-hire and post-hire features need to work properly with the other modules.',
36:'Next step: We will finish the remaining work and check how booking, handover, return, and inspection connect.',
39:'During these two weeks, I learned more about how asset details, availability, rental rates, and quotations work together in CREMS. Working on these features helped me understand why the system needs to support different types of rental assets. I also learned that a change in one module can affect other parts of the system. Starting some of the pre-hire and post-hire work early helped me understand the next stages of the hire process and the work we still need to complete.',
42:'Autonomy: I continued working on my tasks while also helping with the upcoming work. I want to improve how I check my work and follow up on anything that still needs to be completed.',
43:'Influence: Working with the team helped me understand why we need to keep each other updated. I want to be more confident in sharing my ideas and discussing how our tasks connect.',
44:'Complexity: Working on assets, availability, rates, and quotations helped me see how the modules depend on each other. I still need to improve my understanding of how changes in one module affect the rest of the hire process.',
45:'Business Skills: I learned more about planning my work and managing time. Doing some of the upcoming tasks early helped us stay ahead of schedule. I want to continue improving how I balance current tasks with the work coming next.',
53:'This log still needs to be reviewed by my group members.'}
with ZipFile(path) as z:
 parts={n:z.read(n) for n in z.namelist()};info=z.infolist()
root=E.fromstring(parts['word/document.xml']);ps=root.find('w:body',ns).findall('w:p',ns)
for idx,text in changes.items():
 p=ps[idx];r=deepcopy(p.find('w:r',ns))
 for child in list(r):
  if child.tag!=W+'rPr':r.remove(child)
 pr=r.find('w:rPr',ns)
 if pr is not None:
  for b in pr.findall('w:b',ns):pr.remove(b)
 for child in list(p):
  if child.tag!=W+'pPr':p.remove(child)
 if ': ' in text:
  label,text=text.split(': ',1);lead=deepcopy(r);lp=lead.find('w:rPr',ns)
  if lp is None:lp=E.SubElement(lead,W+'rPr')
  E.SubElement(lp,W+'b');t=E.SubElement(lead,W+'t');t.text=label+': ';t.set('{http://www.w3.org/XML/1998/namespace}space','preserve');p.append(lead)
 E.SubElement(r,W+'t').text=text;p.append(r)
parts['word/document.xml']=E.tostring(root,xml_declaration=True,encoding='UTF-8',standalone=True)
with ZipFile(path,'w') as z:
 for item in info:z.writestr(item,parts[item.filename])
print('Updated first-person wording')
