from pathlib import Path
p=Path('C:/Github/CREMS/log5-work/create_log5.py');s=p.read_text();s=s.replace("parts['word/document.xml']=E.tostring", """for idx in [38,48,49]:
 pr=ps[idx].find('w:pPr',ns)
 if pr is None:pr=E.SubElement(ps[idx],W+'pPr')
 E.SubElement(pr,W+'keepNext')
last=ps[54]
for c in list(last):last.remove(c)
pr=E.SubElement(last,W+'pPr');sp=E.SubElement(pr,W+'spacing');sp.set(W+'before','0');sp.set(W+'after','0');sp.set(W+'line','20');sp.set(W+'lineRule','exact');rp=E.SubElement(pr,W+'rPr');E.SubElement(rp,W+'sz').set(W+'val','2')
parts['word/document.xml']=E.tostring""");p.write_text(s)
