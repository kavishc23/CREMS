from pathlib import Path
from zipfile import ZipFile
from lxml import etree as E
import hashlib,json
src=Path(r'C:/Users/Rahul/OneDrive/Desktop/USP_YR_4_S2/CS400/LOgs/CS400-Log3-S11219885.docx')
work=Path('C:/Github/CREMS/log4-work');work.mkdir(exist_ok=True)
b=src.read_bytes(); (work/'reference.docx').write_bytes(b)
ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
with ZipFile(src) as z:
 root=E.fromstring(z.read('word/document.xml')); body=root.find('w:body',ns)
 ps=body.findall('w:p',ns); ts=body.findall('w:tbl',ns)
 evidence={'sha256':hashlib.sha256(b).hexdigest(),'sections':[E.tostring(x).decode() for x in root.findall('.//w:sectPr',ns)],'parts':{n:hashlib.sha256(z.read(n)).hexdigest() for n in z.namelist()},'paragraphs':[{'index':i,'text':''.join(p.itertext()),'properties':E.tostring(p.find('w:pPr',ns)).decode() if p.find('w:pPr',ns) is not None else ''} for i,p in enumerate(ps)]}
(work/'evidence.json').write_text(json.dumps(evidence,indent=2),encoding='utf8')
(work/'artifact.md').write_text('''# Log 4 template contract
Reference: retained reference.docx from supplied Log 3. SHA-256 and exact section, paragraph and package properties are in evidence.json. Six landscape reference pages inspected from matching PDF.
Preserve all package parts except word/document.xml. Preserve logo, styles, numbering, footer, page geometry, table grids, borders, paragraph and run properties. Preserve the original heading order and metadata, changing report number and adding reporting dates.
Rewrite body task and reflection slots 7-12,14-21,24-27,29-36,39,42-45 for the new period. Remove unconfirmed historical issue claims. Table 1 meeting row is blank unless confirmed. Table 2 expands from five to ten weekdays using copied source rows, retaining columns and weekly merge pattern. Table 3 student date becomes 18 September 2026 and signature is cleared. Table 4 peer names retained; signatures cleared. Paragraph 53 states peer review pending.
Use source font, spacing and bullet definitions, allowing natural pagination. Remove old manual page breaks only if they cause empty pages. No source signature or peer attestation may be reused for the new reporting period.
''',encoding='utf8')
print('Reference retained and template contract recorded')
