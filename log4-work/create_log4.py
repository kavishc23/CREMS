from pathlib import Path
from zipfile import ZipFile
from lxml import etree as E
from copy import deepcopy
work=Path('C:/Github/CREMS/log4-work')
ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
W='{'+ns['w']+'}'
with ZipFile(work/'reference.docx') as z:
 root=E.fromstring(z.read('word/document.xml')); body=root.find('w:body',ns); ps=body.findall('w:p',ns); ts=body.findall('w:tbl',ns)
 def put(p,text):
  runs=p.findall('w:r',ns); r=deepcopy(runs[0]) if runs else E.Element(W+'r')
  for child in list(r):
   if child.tag!=W+'rPr': r.remove(child)
  for child in list(p):
   if child.tag!=W+'pPr':p.remove(child)
  rp=r.find('w:rPr',ns)
  if rp is not None:
   for bold in rp.findall('w:b',ns):rp.remove(bold)
  if ': ' in text and (text.startswith(('Review focus:', 'Next step:', 'Autonomy:', 'Influence:', 'Complexity:', 'Business Skills:'))):
   label,rest=text.split(': ',1); lead=deepcopy(r); pr=lead.find('w:rPr',ns)
   if pr is None:pr=E.SubElement(lead,W+'rPr')
   E.SubElement(pr,W+'b');E.SubElement(lead,W+'t').text=label+': ';lead[-1].set('{http://www.w3.org/XML/1998/namespace}space','preserve');p.append(lead);text=rest
  E.SubElement(r,W+'t').text=text;p.append(r)
 def cell(c,text):
  pp=c.findall('w:p',ns)
  put(pp[0],text)
  for p in pp[1:]:c.remove(p)
 changes={
 7:'Develop the flexible asset module for Motors and Carptrac rental assets.',
 8:'Work on asset categories, dynamic attributes, templates, and asset details.',
 9:'Develop branch, location and status tracking, meters, and QR/barcode support.',
 10:'Work on asset availability and a conflict-aware calendar.',
 11:'Develop rental rates, extra charges, deposits, discounts, and tax handling.',
 12:'Continue quotation development, including customer-specific rates and quote revisions.',
 14:'We worked on the flexible asset module during 7-11 September.',
 15:'Our asset work covered categories, dynamic attributes, Motors and Carptrac templates, and asset information.',
 16:'We progressed the tracking of asset locations, status history, meters, and QR/barcode identification.',
 17:'During 14-18 September, we worked on availability, rental rates, and quotations.',
 18:'Our work covered availability conflicts and hourly, daily, and per-unit rate configuration.',
 19:'We progressed quotation features covering rates, extra charges, deposits, discounts, tax, and revisions.',
 20:'We also completed part of the upcoming pre-hire and post-hire work during this reporting period.',
 21:'Bringing this work forward helped us stay ahead of schedule and prepare for the remaining hire workflow.',
 24:'Continue integration and testing of the asset, availability, rate, and quotation features.',
 25:'Progress bookings, approval stages, agreements, and notifications in the next reporting period.',
 26:'Complete the remaining pre-hire and post-hire work and its integration with the hire workflow.',
 27:'Deviation from plan: part of the later pre-hire and post-hire work was completed early to stay ahead of schedule.',
 29:'Review focus: Flexible asset information must support both Motors and Carptrac requirements.',
 30:'Next step: Continue checking category attributes and templates against the requirements for each asset type.',
 32:'Review focus: Availability and pricing must remain consistent throughout quotation and booking workflows.',
 33:'Next step: Check overlapping hire periods, rate selection, and quotation calculations during integration testing.',
 35:'Review focus: The early pre-hire and post-hire work must connect correctly to the remaining workflow.',
 36:'Next step: Complete the remaining work and verify the links between booking, handover, return, and inspection.',
 39:'During this reporting period, I developed my understanding of how asset information, availability, pricing, and quotations connect within CREMS. The work highlighted the need for flexible data structures across different rental assets and consistent business rules throughout the hire process. Starting part of the pre-hire and post-hire work early also helped me understand how later activities depend on the asset and booking modules.',
 42:'Autonomy: I continued contributing to the current development work while preparing for upcoming tasks. I aim to take greater ownership of checking my work and following through on integration issues.',
 43:'Influence: Working with the team reinforced the importance of coordinating tasks across modules. I aim to communicate dependencies and progress clearly so that early work supports the agreed schedule.',
 44:'Complexity: This period helped me understand the links between flexible assets, availability, rental rates, and quotations. I will continue improving my ability to follow business rules across the complete hire workflow.',
 45:'Business Skills: Bringing some upcoming work forward highlighted the value of planning and prioritisation. I aim to maintain clear progress records while balancing current deliverables with preparation for later milestones.',
 53:'Peer review to be completed for this reporting period.'}
 for i,t in changes.items():put(ps[i],t)
 cell(ts[0].findall('w:tr',ns)[2].findall('w:tc',ns)[1],'Report #: 4 | Period: 7-18 September 2026')
 for c,v in zip(ts[1].findall('w:tr',ns)[1].findall('w:tc',ns),['Client meeting','18 September 2026','']):cell(c,v)
 rows=ts[2].findall('w:tr',ns); base=deepcopy(rows[1])
 for r in rows[1:]:ts[2].remove(r)
 for i,day in enumerate([7,8,9,10,11,14,15,16,17,18]):
  r=deepcopy(base);cells=r.findall('w:tc',ns)
  vals=['Week '+str(6+i//5) if i%5==0 else '',f'{day} September',['Monday','Tuesday','Wednesday','Thursday','Friday'][i%5],'','','']
  for j,(c,v) in enumerate(zip(cells,vals)):
   cell(c,v)
   if j==0:
    vm=c.find('w:tcPr/w:vMerge',ns)
    if vm is not None:
     if i%5==0:vm.set(W+'val','restart')
     else:vm.attrib.clear()
  ts[2].append(r)
 cell(ts[3].findall('w:tr',ns)[0].findall('w:tc',ns)[1],'18 September 2026')
 for r in ts[4].findall('w:tr',ns)[1:]:cell(r.findall('w:tc',ns)[1],'')
 for idx in [35,47,52,53]:
  pr=ps[idx].find('w:pPr',ns)
  if pr is None:pr=E.SubElement(ps[idx],W+'pPr')
  E.SubElement(pr,W+'keepNext')
 for row in ts[4].findall('w:tr',ns)[:-1]:
  for p in row.findall('.//w:p',ns):
   pr=p.find('w:pPr',ns)
   if pr is None:pr=E.SubElement(p,W+'pPr')
   E.SubElement(pr,W+'keepNext')
 out=Path('C:/Github/CREMS/CS400-Log4-S11219885.docx')
 with ZipFile(out,'w') as dest:
  for item in z.infolist():dest.writestr(item,E.tostring(root,xml_declaration=True,encoding='UTF-8',standalone=True) if item.filename=='word/document.xml' else z.read(item.filename))
 print(out)


