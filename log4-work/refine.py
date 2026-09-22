from pathlib import Path
p=Path('C:/Github/CREMS/log4-work/create_log4.py')
s=p.read_text()
s=s.replace("E.SubElement(r,W+'t').text=text;p.append(r)","""rp=r.find('w:rPr',ns)
  if rp is not None:
   for bold in rp.findall('w:b',ns):rp.remove(bold)
  if ': ' in text and (text.startswith(('Review focus:', 'Next step:', 'Autonomy:', 'Influence:', 'Complexity:', 'Business Skills:'))):
   label,rest=text.split(': ',1); lead=deepcopy(r); pr=lead.find('w:rPr',ns)
   if pr is None:pr=E.SubElement(lead,W+'rPr')
   E.SubElement(pr,W+'b');E.SubElement(lead,W+'t').text=label+': ';lead[-1].set('{http://www.w3.org/XML/1998/namespace}space','preserve');p.append(lead);text=rest
  E.SubElement(r,W+'t').text=text;p.append(r)""")
s=s.replace("out=Path('C:/Github", """for idx in [35,52,53]:
  pr=ps[idx].find('w:pPr',ns)
  if pr is None:pr=E.SubElement(ps[idx],W+'pPr')
  E.SubElement(pr,W+'keepNext')
 for row in ts[4].findall('w:tr',ns)[:-1]:
  for p in row.findall('.//w:p',ns):
   pr=p.find('w:pPr',ns)
   if pr is None:pr=E.SubElement(p,W+'pPr')
   E.SubElement(pr,W+'keepNext')
 out=Path('C:/Github""")
p.write_text(s)
