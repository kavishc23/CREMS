from pathlib import Path
from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.section import WD_SECTION
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

OUT = Path("output/documents/Kavish_Chandra_Year_4_Part_A_Portfolio.docx")
OUT.parent.mkdir(parents=True, exist_ok=True)

BLUE = RGBColor(31, 77, 120)
DARK = RGBColor(31, 43, 55)
MUTED = RGBColor(90, 99, 110)
PALE = "E8EEF5"
LIGHT = "F4F6F9"

doc = Document()
section = doc.sections[0]
section.page_width = Inches(8.5)
section.page_height = Inches(11)
section.top_margin = Inches(0.8)
section.bottom_margin = Inches(0.75)
section.left_margin = Inches(0.9)
section.right_margin = Inches(0.9)
section.header_distance = Inches(0.35)
section.footer_distance = Inches(0.35)

styles = doc.styles
normal = styles["Normal"]
normal.font.name = "Aptos"
normal._element.rPr.rFonts.set(qn("w:ascii"), "Aptos")
normal._element.rPr.rFonts.set(qn("w:hAnsi"), "Aptos")
normal.font.size = Pt(10.5)
normal.font.color.rgb = DARK
normal.paragraph_format.space_after = Pt(6)
normal.paragraph_format.line_spacing = 1.12

for name, size, before, after in [
    ("Heading 1", 16, 14, 6),
    ("Heading 2", 12.5, 10, 4),
    ("Heading 3", 11, 8, 3),
]:
    s = styles[name]
    s.font.name = "Aptos Display"
    s._element.rPr.rFonts.set(qn("w:ascii"), "Aptos Display")
    s._element.rPr.rFonts.set(qn("w:hAnsi"), "Aptos Display")
    s.font.size = Pt(size)
    s.font.bold = True
    s.font.color.rgb = BLUE
    s.paragraph_format.space_before = Pt(before)
    s.paragraph_format.space_after = Pt(after)
    s.paragraph_format.keep_with_next = True

def shade(cell, fill):
    tcPr = cell._tc.get_or_add_tcPr()
    shd = tcPr.find(qn("w:shd"))
    if shd is None:
        shd = OxmlElement("w:shd")
        tcPr.append(shd)
    shd.set(qn("w:fill"), fill)

def set_cell_margins(cell, top=90, start=120, bottom=90, end=120):
    tc = cell._tc
    tcPr = tc.get_or_add_tcPr()
    tcMar = tcPr.first_child_found_in("w:tcMar")
    if tcMar is None:
        tcMar = OxmlElement("w:tcMar")
        tcPr.append(tcMar)
    for m, v in (("top", top), ("start", start), ("bottom", bottom), ("end", end)):
        node = tcMar.find(qn(f"w:{m}"))
        if node is None:
            node = OxmlElement(f"w:{m}")
            tcMar.append(node)
        node.set(qn("w:w"), str(v))
        node.set(qn("w:type"), "dxa")

def set_table_widths(table, widths):
    table.autofit = False
    for row in table.rows:
        for idx, width in enumerate(widths):
            row.cells[idx].width = Inches(width)
            row.cells[idx].vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
            set_cell_margins(row.cells[idx])
    tblPr = table._tbl.tblPr
    tblW = tblPr.find(qn("w:tblW"))
    if tblW is None:
        tblW = OxmlElement("w:tblW")
        tblPr.append(tblW)
    tblW.set(qn("w:w"), str(int(sum(widths) * 1440)))
    tblW.set(qn("w:type"), "dxa")
    tblInd = tblPr.find(qn("w:tblInd"))
    if tblInd is None:
        tblInd = OxmlElement("w:tblInd")
        tblPr.append(tblInd)
    tblInd.set(qn("w:w"), "120")
    tblInd.set(qn("w:type"), "dxa")

def keep_table_rows_together(table, repeat_header=True):
    for index, row in enumerate(table.rows):
        trPr = row._tr.get_or_add_trPr()
        cant_split = OxmlElement("w:cantSplit")
        trPr.append(cant_split)
        if index == 0 and repeat_header:
            header = OxmlElement("w:tblHeader")
            header.set(qn("w:val"), "true")
            trPr.append(header)

def add_bullet(text, level=0):
    p = doc.add_paragraph(style="List Bullet")
    p.paragraph_format.left_indent = Inches(0.5)
    p.paragraph_format.first_line_indent = Inches(-0.25)
    p.paragraph_format.space_after = Pt(4)
    p.add_run(text)
    return p

def add_link(paragraph, text, url):
    part = paragraph.part
    rel_id = part.relate_to(url, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink", is_external=True)
    hyperlink = OxmlElement("w:hyperlink")
    hyperlink.set(qn("r:id"), rel_id)
    run = OxmlElement("w:r")
    rPr = OxmlElement("w:rPr")
    color = OxmlElement("w:color")
    color.set(qn("w:val"), "0563C1")
    underline = OxmlElement("w:u")
    underline.set(qn("w:val"), "single")
    rPr.append(color)
    rPr.append(underline)
    run.append(rPr)
    text_node = OxmlElement("w:t")
    text_node.text = text
    run.append(text_node)
    hyperlink.append(run)
    paragraph._p.append(hyperlink)

# Running header/footer
hp = section.header.paragraphs[0]
hp.text = "CS001 | Foundations of Professional Practice"
hp.alignment = WD_ALIGN_PARAGRAPH.RIGHT
for run in hp.runs:
    run.font.name = "Aptos"
    run.font.size = Pt(8.5)
    run.font.color.rgb = MUTED

fp = section.footer.paragraphs[0]
fp.alignment = WD_ALIGN_PARAGRAPH.CENTER
run = fp.add_run("Kavish Chandra  •  Year 4 Part A  •  Semester 2, 2026")
run.font.size = Pt(8.5)
run.font.color.rgb = MUTED

# Masthead
p = doc.add_paragraph()
p.paragraph_format.space_after = Pt(2)
r = p.add_run("YEAR 4 PART A PORTFOLIO")
r.font.name = "Aptos Display"
r.font.size = Pt(23)
r.font.bold = True
r.font.color.rgb = BLUE

p = doc.add_paragraph()
p.paragraph_format.space_after = Pt(12)
r = p.add_run("What professional capabilities do I need and what professional capabilities do I have?")
r.font.size = Pt(13)
r.font.color.rgb = DARK

meta = doc.add_table(rows=3, cols=2)
meta.alignment = WD_TABLE_ALIGNMENT.LEFT
meta.style = "Table Grid"
set_table_widths(meta, [1.55, 4.95])
for row, (label, value) in zip(meta.rows, [
    ("Student", "Kavish Chandra"),
    ("Career objective", "Full-Stack Software Engineer"),
    ("SFIA responsibility", "Level 3 – Apply"),
]):
    row.cells[0].text = label
    row.cells[1].text = value
    shade(row.cells[0], PALE)
    for run in row.cells[0].paragraphs[0].runs:
        run.bold = True
        run.font.color.rgb = BLUE

doc.add_heading("Reflection", level=1)

reflection_paragraphs = [
"After graduation, I intend to work as a full-stack software engineer, developing secure and useful business systems. My present profile is best described by SFIA Level 3: Apply. The statement, “Works under general direction to complete assigned tasks,” reflects how I contribute to the CS400 Industry Experience Project while still receiving guidance and milestone review. In the Car and Rental Equipment Management System (CREMS) project for Carpenters Fiji, I have managed varied tasks across the React and TypeScript interface, ASP.NET Core API, database integration, security controls and technical documentation. My Git history, which records most of the project commits under my account, supports my ability to organise my own work and deliver within team deadlines.",
"My strongest current specialist capability is Programming/Software Development (PROG) at Level 3. I have designed, coded, tested, documented and improved moderately complex features, including dashboards, customer functions, rental agreements and browser-session controls. The repository also shows that I applied secure development practices through role-based access, time-limited sessions, request rate limiting and server-side authorisation. Automated booking, quotation and session-policy tests demonstrate that I consider verification part of development rather than an afterthought. I also show Level 3 generic capabilities in communication, collaboration, problem-solving and learning: I helped establish the team environment, prepared project and client documentation, translated business needs into technical work and adopted unfamiliar technologies across the full stack.",
"For employment, I need to strengthen Requirements Definition and Management (REQM) and Software Design (SWDN) at Level 3. The project contains a requirements register, specifications, architecture decisions and client-meeting material, but I need more consistent practice in tracing each requirement from its source through design, implementation, testing and acceptance. I also need to compare design alternatives more systematically and document how security, accessibility, performance, scalability and maintainability influence decisions. I plan to improve by maintaining a prioritised backlog with acceptance criteria, linking requirements to commits and tests, requesting peer reviews and recording design decisions before implementation.",
"I consider myself developing toward being an exemplary USP graduate rather than claiming that the process is complete. My work demonstrates initiative, technical competence, teamwork and responsibility to a real client. However, professional practice also requires honesty about uncertainty, respect for client data, dependable communication, openness to feedback and accountability for quality. My generic and specialist skills are both assessed at Level 3, so they are broadly aligned. My next goal is to make that alignment more consistent: every technical contribution should be supported by clear stakeholder communication, traceable evidence, thoughtful testing and reflection on its business impact."
]
for text in reflection_paragraphs:
    p = doc.add_paragraph(text)
    p.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY
    p.paragraph_format.space_after = Pt(8)

words = sum(len(p.replace("–", " ").split()) for p in reflection_paragraphs)
p = doc.add_paragraph()
p.alignment = WD_ALIGN_PARAGRAPH.RIGHT
p.paragraph_format.space_after = Pt(4)
r = p.add_run(f"Reflection word count: {words}")
r.italic = True
r.font.size = Pt(9)
r.font.color.rgb = MUTED

doc.add_heading("Capability summary", level=1)
table = doc.add_table(rows=1, cols=4)
table.style = "Table Grid"
table.alignment = WD_TABLE_ALIGNMENT.LEFT
set_table_widths(table, [1.35, 0.75, 2.25, 2.15])
headers = ["Capability", "Level", "Current evidence", "Development priority"]
for i, h in enumerate(headers):
    table.rows[0].cells[i].text = h
    shade(table.rows[0].cells[i], PALE)
    for run in table.rows[0].cells[i].paragraphs[0].runs:
        run.bold = True
        run.font.color.rgb = BLUE

rows = [
    ("Generic responsibility", "3", "Own work, team decisions, client-facing artefacts and delivery milestones.", "Improve estimation, concise progress reporting and early escalation of risks."),
    ("PROG – Programming/software development", "3", "React/TypeScript frontend; ASP.NET Core API; security controls; automated policy tests.", "Increase test coverage, structured reviews, deployment knowledge and maintainability measurement."),
    ("REQM – Requirements definition and management", "2 → 3", "Requirements register, specifications and client-meeting material.", "Add source traceability, acceptance criteria, prioritisation and controlled change records."),
    ("SWDN – Software design", "2 → 3", "Modular-monolith architecture, domain modules, interface designs and wireframes.", "Record alternatives and trade-offs; address security, accessibility, performance and scalability explicitly."),
]
for vals in rows:
    cells = table.add_row().cells
    for i, val in enumerate(vals):
        cells[i].text = val
    cells[1].paragraphs[0].alignment = WD_ALIGN_PARAGRAPH.CENTER
keep_table_rows_together(table)

doc.add_heading("Professional attitudes", level=1)
for item in [
    "Integrity and confidentiality – protect client information, credentials and operational data.",
    "Accountability – own assigned work, verify it and communicate delays or risks early.",
    "Respect and collaboration – listen to teammates and stakeholders, review constructively and respond to feedback.",
    "Continuous learning – seek better approaches and keep technical knowledge current.",
    "User and business focus – judge success by stakeholder value, usability and reliability, not only by completed code.",
]:
    add_bullet(item)

doc.add_heading("Artefacts to upload or link in Mahara", level=1)
evidence = [
    ("1", "CREMS repository or commit-history screenshot", "Shows sustained individual contribution, version control and delivery across the semester."),
    ("2", "README.md and docs/architecture.md", "Shows the technology stack, secure engineering rules and modular system design."),
    ("3", "CREMS Software Requirements Specification v1.0", "Supports requirements analysis, documentation and understanding of client needs."),
    ("4", "CREMS Client Meeting – Stack, Scope and Timeline presentation", "Supports professional communication, teamwork and engagement with stakeholders."),
    ("5", "BookingPolicyTests.cs, QuotePolicyTests.cs and WindowSessionRegistryTests.cs", "Supports verification, problem-solving and attention to reliability and security."),
    ("6", "Customer dashboard, rental agreement or session-control screenshots", "Provides visible evidence of implemented full-stack features."),
    ("7", "Kavish Chandra Week 1 Log", "Supports reflection, planning and personal accountability."),
]
table = doc.add_table(rows=1, cols=3)
table.style = "Table Grid"
table.alignment = WD_TABLE_ALIGNMENT.LEFT
set_table_widths(table, [0.45, 2.65, 3.4])
for i, h in enumerate(["#", "Artefact", "What it demonstrates"]):
    table.rows[0].cells[i].text = h
    shade(table.rows[0].cells[i], PALE)
    for run in table.rows[0].cells[i].paragraphs[0].runs:
        run.bold = True
        run.font.color.rgb = BLUE
for vals in evidence:
    cells = table.add_row().cells
    for i, val in enumerate(vals):
        cells[i].text = val
    cells[0].paragraphs[0].alignment = WD_ALIGN_PARAGRAPH.CENTER
keep_table_rows_together(table)

doc.add_heading("Suggested Mahara arrangement", level=1)
for item in [
    "Place the Reflection section first under the portfolio question.",
    "Place the Capability summary and Professional attitudes immediately below the reflection.",
    "Create an Artefacts section and upload or link the seven items above with one-sentence captions.",
    "Check that every link is accessible to the assessor before submission.",
]:
    add_bullet(item)

doc.add_heading("Sources", level=1)
p = doc.add_paragraph()
p.add_run("Assessment brief: ").bold = True
p.add_run("CS001 Foundations of Professional Practice, Year 4 Part A Assessment Notes, Semester 2 2026.")

for label, url in [
    ("SFIA 9 Level 3 – Apply", "https://sfia-online.org/en/sfia-9/responsibilities/level-3"),
    ("SFIA 9 Programming/software development (PROG)", "https://sfia-online.org/en/sfia-9/skills/programming-software-development"),
    ("SFIA 9 Requirements definition and management (REQM)", "https://sfia-online.org/en/sfia-9/skills/requirements-definition-and-management"),
    ("SFIA 9 Software design (SWDN)", "https://sfia-online.org/en/sfia-9/skills/software-design"),
]:
    p = doc.add_paragraph(style="List Bullet")
    p.paragraph_format.left_indent = Inches(0.5)
    p.paragraph_format.first_line_indent = Inches(-0.25)
    add_link(p, label, url)

doc.core_properties.title = "Year 4 Part A Portfolio – Professional Capabilities"
doc.core_properties.subject = "CS001 Foundations of Professional Practice"
doc.core_properties.author = "Kavish Chandra"
doc.core_properties.keywords = "SFIA, professional practice, portfolio, software engineering, CREMS"
doc.save(OUT)
print(OUT)
print(f"Reflection words: {words}")
