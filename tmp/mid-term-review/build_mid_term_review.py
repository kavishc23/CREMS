from __future__ import annotations

from pathlib import Path
from datetime import date

from docx import Document
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT, WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Inches, Pt, RGBColor

# Configuration
ROOT = Path("C:/Github/CREMS")
OUT = ROOT / "output" / "documents" / "CREMS_Mid_Term_Review_Report.docx"
OUT.parent.mkdir(parents=True, exist_ok=True)

# Color scheme
BLACK = "151515"
YELLOW = "FFEA00"
GREEN = "08783E"
PALE_GREEN = "EAF4EE"
PALE_YELLOW = "FFF9CC"
LIGHT_GRAY = "F2F4F3"
MID_GRAY = "6B706D"
WHITE = "FFFFFF"
RED = "9B1C1C"
INK = "202421"
CONTENT_WIDTH = 9360


def shade(cell, fill):
    tc_pr = cell._tc.get_or_add_tcPr()
    shd = tc_pr.find(qn("w:shd"))
    if shd is None:
        shd = OxmlElement("w:shd")
        tc_pr.append(shd)
    shd.set(qn("w:fill"), fill)


def set_cell_text(cell, text, *, bold=False, color=INK, size=9, align=None):
    cell.text = ""
    p = cell.paragraphs[0]
    if align is not None:
        p.alignment = align
    p.paragraph_format.space_before = Pt(0)
    p.paragraph_format.space_after = Pt(0)
    p.paragraph_format.line_spacing = 1.05
    r = p.add_run(str(text))
    set_font(r, size=size, bold=bold, color=color)
    cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER


def set_font(run, *, name="Aptos", size=10.5, bold=None, italic=None, color=INK):
    run.font.name = name
    run._element.get_or_add_rPr().rFonts.set(qn("w:ascii"), name)
    run._element.get_or_add_rPr().rFonts.set(qn("w:hAnsi"), name)
    run.font.size = Pt(size)
    run.font.color.rgb = RGBColor.from_string(color)
    if bold is not None:
        run.bold = bold
    if italic is not None:
        run.italic = italic


def set_repeat_header(row):
    tr_pr = row._tr.get_or_add_trPr()
    tbl_header = OxmlElement("w:tblHeader")
    tbl_header.set(qn("w:val"), "true")
    tr_pr.append(tbl_header)


def set_cant_split(row):
    tr_pr = row._tr.get_or_add_trPr()
    cant = OxmlElement("w:cantSplit")
    tr_pr.append(cant)


def add_table(doc, headers, rows, weights, *, header_fill=GREEN, font_size=8.8, alignments=None):
    table = doc.add_table(rows=1, cols=len(headers))
    table.style = "Table Grid"
    table.autofit = False
    for idx, value in enumerate(headers):
        set_cell_text(table.rows[0].cells[idx], value, bold=True, color=WHITE, size=9, align=WD_ALIGN_PARAGRAPH.CENTER)
        shade(table.rows[0].cells[idx], header_fill)
    set_repeat_header(table.rows[0])
    for row_index, values in enumerate(rows):
        row = table.add_row()
        set_cant_split(row)
        for idx, value in enumerate(values):
            alignment = alignments[idx] if alignments else None
            set_cell_text(row.cells[idx], value, size=font_size, align=alignment)
            if row_index % 2 == 1:
                shade(row.cells[idx], LIGHT_GRAY)
    # Apply table geometry
    widths = column_widths_from_weights(weights, CONTENT_WIDTH)
    apply_table_geometry(table, widths, table_width_dxa=CONTENT_WIDTH, indent_dxa=120,
                         cell_margins_dxa={"top": 90, "bottom": 90, "start": 120, "end": 120})
    doc.add_paragraph().paragraph_format.space_after = Pt(0)
    return table


def column_widths_from_weights(weights, total_width_dxa):
    total_weight = sum(weights)
    return [int(w / total_weight * total_width_dxa) for w in weights]


def apply_table_geometry(table, widths, table_width_dxa, indent_dxa=0, cell_margins_dxa=None):
    tbl_pr = table._tbl.tblPr
    w = tbl_pr.find(qn("w:tblW"))
    if w is None:
        w = OxmlElement("w:tblW")
        tbl_pr.append(w)
    w.set(qn("w:w"), str(table_width_dxa))
    w.set(qn("w:type"), "dxa")
    ind = OxmlElement("w:tblInd")
    ind.set(qn("w:w"), str(indent_dxa))
    ind.set(qn("w:type"), "dxa")
    tbl_pr.append(ind)
    grid = table._tbl.tblGrid
    for c in list(grid):
        grid.remove(c)
    for width in widths:
        c = OxmlElement("w:gridCol")
        c.set(qn("w:w"), str(width))
        grid.append(c)
    for row in table.rows:
        for i, cell in enumerate(row.cells):
            tcw = cell._tc.get_or_add_tcPr().find(qn("w:tcW"))
            if tcw is None:
                tcw = OxmlElement("w:tcW")
                cell._tc.get_or_add_tcPr().append(tcw)
            tcw.set(qn("w:w"), str(widths[i]))
            tcw.set(qn("w:type"), "dxa")
            if cell_margins_dxa:
                tc_pr = cell._tc.get_or_add_tcPr()
                mar = tc_pr.find(qn("w:tcMar"))
                if mar is None:
                    mar = OxmlElement("w:tcMar")
                    tc_pr.append(mar)
                for k, v in cell_margins_dxa.items():
                    x = OxmlElement(f"w:{k}")
                    x.set(qn("w:w"), str(v))
                    x.set(qn("w:type"), "dxa")
                    mar.append(x)


def add_para(doc, text="", *, bold=False, italic=False, color=INK, size=10.5, after=6, before=0, align=None, keep=False):
    p = doc.add_paragraph()
    if align is not None:
        p.alignment = align
    p.paragraph_format.space_before = Pt(before)
    p.paragraph_format.space_after = Pt(after)
    p.paragraph_format.line_spacing = 1.10
    p.paragraph_format.keep_with_next = keep
    r = p.add_run(text)
    set_font(r, size=size, bold=bold, italic=italic, color=color)
    return p


def add_bullet(doc, text, level=0):
    p = doc.add_paragraph(style="List Bullet" if level == 0 else "List Bullet 2")
    p.paragraph_format.space_after = Pt(4)
    p.paragraph_format.line_spacing = 1.10
    r = p.add_run(text)
    set_font(r, size=10.3)
    return p


def add_number(doc, text):
    p = doc.add_paragraph(style="List Number")
    p.paragraph_format.space_after = Pt(4)
    p.paragraph_format.line_spacing = 1.10
    r = p.add_run(text)
    set_font(r, size=10.3)
    return p


def add_callout(doc, label, text, fill=PALE_YELLOW):
    p = doc.add_paragraph()
    p.paragraph_format.space_before = Pt(4)
    p.paragraph_format.space_after = Pt(8)
    p.paragraph_format.line_spacing = 1.10
    p.paragraph_format.left_indent = Inches(0.08)
    p.paragraph_format.right_indent = Inches(0.08)
    p_pr = p._p.get_or_add_pPr()
    shd = OxmlElement("w:shd")
    shd.set(qn("w:fill"), fill)
    p_pr.append(shd)
    borders = OxmlElement("w:pBdr")
    for edge in ("top", "left", "bottom", "right"):
        border = OxmlElement(f"w:{edge}")
        border.set(qn("w:val"), "single")
        border.set(qn("w:sz"), "4")
        border.set(qn("w:color"), fill)
        border.set(qn("w:space"), "5")
        borders.append(border)
    p_pr.append(borders)
    r = p.add_run(f"{label}: ")
    set_font(r, size=10, bold=True, color=BLACK)
    r = p.add_run(text)
    set_font(r, size=10, color=BLACK)


def add_page_break(doc):
    doc.add_page_break()


def add_page_field(paragraph):
    paragraph.alignment = WD_ALIGN_PARAGRAPH.RIGHT
    r = paragraph.add_run("Page ")
    set_font(r, size=8.5, color=MID_GRAY)
    run = paragraph.add_run()
    begin = OxmlElement("w:fldChar"); begin.set(qn("w:fldCharType"), "begin")
    instr = OxmlElement("w:instrText"); instr.set(qn("xml:space"), "preserve"); instr.text = " PAGE "
    separate = OxmlElement("w:fldChar"); separate.set(qn("w:fldCharType"), "separate")
    text = OxmlElement("w:t"); text.text = "1"; separate.append(text)
    end = OxmlElement("w:fldChar"); end.set(qn("w:fldCharType"), "end")
    run._r.extend([begin, instr, separate, end])


# Create document
doc = Document()
section = doc.sections[0]
section.page_width = Inches(8.5)
section.page_height = Inches(11)
section.top_margin = Inches(0.8)
section.bottom_margin = Inches(0.8)
section.left_margin = Inches(1.0)
section.right_margin = Inches(1.0)
section.header_distance = Inches(0.45)
section.footer_distance = Inches(0.45)

# Styles
styles = doc.styles
normal = styles["Normal"]
normal.font.name = "Aptos"
normal._element.rPr.rFonts.set(qn("w:ascii"), "Aptos")
normal._element.rPr.rFonts.set(qn("w:hAnsi"), "Aptos")
normal.font.size = Pt(10.5)
normal.font.color.rgb = RGBColor.from_string(INK)
normal.paragraph_format.space_after = Pt(6)
normal.paragraph_format.line_spacing = 1.10

for name, size, color, before, after in [
    ("Heading 1", 16, GREEN, 16, 8),
    ("Heading 2", 13, GREEN, 12, 6),
    ("Heading 3", 11.5, BLACK, 8, 4),
]:
    style = styles[name]
    style.font.name = "Aptos Display"
    style._element.rPr.rFonts.set(qn("w:ascii"), "Aptos Display")
    style._element.rPr.rFonts.set(qn("w:hAnsi"), "Aptos Display")
    style.font.size = Pt(size)
    style.font.bold = True
    style.font.color.rgb = RGBColor.from_string(color)
    style.paragraph_format.space_before = Pt(before)
    style.paragraph_format.space_after = Pt(after)
    style.paragraph_format.keep_with_next = True

# Header and footer
header = section.header
hp = header.paragraphs[0]
hp.alignment = WD_ALIGN_PARAGRAPH.LEFT
hr = hp.add_run("CREMS | Mid-Term Review Report")
set_font(hr, size=8.5, bold=True, color=MID_GRAY)
footer = section.footer
fp = footer.paragraphs[0]
fr = fp.add_run("Carpenters Fiji Pte Limited | CS400 Mid-Term Review | S2 2026")
set_font(fr, size=8.3, color=MID_GRAY)
add_page_field(footer.add_paragraph())

# ==================== COVER PAGE ====================
add_para(doc, "", after=40)
add_para(doc, "CARPENTERS FIJI PTE LIMITED", bold=True, size=11, color=GREEN, after=40)
add_para(doc, "MID-TERM REVIEW", bold=True, size=28, color=BLACK, after=8)
add_para(doc, "REPORT", bold=True, size=28, color=BLACK, after=12)
add_para(doc, "Car Rental and Equipment Management System (CREMS)", bold=True, size=16, color=GREEN, after=28)

add_callout(doc, "Document purpose", "This mid-term review reports on the progress of the CREMS project, including the requirements baseline, system architecture, project plan updates, risk assessment, and timeline for remaining deliverables.", fill=PALE_YELLOW)

add_para(doc, "", after=22)
add_table(doc, ["Document control", "Value"], [
    ["Client", "Carpenters Fiji Pte Limited"],
    ["Project", "Car Rental and Equipment Management System (CREMS)"],
    ["Course", "CS400 Industry Experience Project - S2 2026"],
    ["Group number", "10"],
    ["Academic supervisor", "Dr Ravneil Nand"],
    ["Client supervisor", "Amit Kumar"],
    ["Co-supervisor", "Shanil Naidu"],
    ["Version", "1.0 - Mid-Term Review"],
    ["Document date", "1 September 2026"],
    ["Classification", "Client confidential - project use"],
], [1.65, 4.85], font_size=9.5)

add_para(doc, "", after=12)
add_para(doc, "TEAM MEMBERS", bold=True, size=12, color=GREEN, after=8)
add_table(doc, ["Name", "Student ID", "Role"], [
    ["Kavish Chandra", "S11219143", "Project Leader / Client Liaison"],
    ["Shoneel Kumar", "S11219651", "Backend and Data Lead"],
    ["Sudhansu Kisun", "S11219520", "Frontend and UX Lead"],
    ["Rahul Chand", "S11219885", "Quality and DevOps Lead"],
], [2.5, 1.5, 2.0], font_size=9.5)

add_page_break(doc)

# ==================== TABLE OF CONTENTS ====================
doc.add_heading("Contents", level=1)
toc_entries = [
    "1. Introduction",
    "2. Requirements Document (SRS)",
    "3. System Architecture and Design",
    "4. Project Plan Updates",
    "5. Milestones Achieved",
    "6. Obstacles Encountered and Mitigation",
    "7. Changes to the Plan",
    "8. Risk Management Update",
    "9. Timeline for Remaining Deliverables",
    "10. Conclusion",
    "Appendix A - SRS Summary",
    "Appendix B - Design Document Summary",
]
for entry in toc_entries:
    p = doc.add_paragraph()
    p.paragraph_format.space_after = Pt(1)
    p.paragraph_format.line_spacing = 1.0
    p.paragraph_format.left_indent = Inches(0.12)
    r = p.add_run(entry)
    set_font(r, size=9.2, color=GREEN, bold=True)

add_page_break(doc)

# ==================== 1. INTRODUCTION ====================
doc.add_heading("1. Introduction", level=1)
add_para(doc, "This mid-term review reports on the progress of the Car Rental and Equipment Management System (CREMS) project being developed for Carpenters Fiji Pte Limited as part of the CS400 Industry Experience Project at the University of the South Pacific.", size=10.5)
add_para(doc, "The review covers work completed during Weeks 1-5 of the 14-week project timeline (3 August - 1 September 2026). It presents the approved requirements baseline, system architecture, project plan updates, milestones achieved, obstacles encountered, risk mitigation strategies, and the timeline for completing remaining deliverables.", size=10.5)
add_para(doc, "The CREMS project aims to deliver a configurable, group-wide platform for managing rental revenue, asset maintenance expenditure, and customer service across Carpenters Fiji divisions, with initial focus on Carpenters Motors and Carptrac operations.", size=10.5)

add_page_break(doc)

# ==================== 2. REQUIREMENTS DOCUMENT ====================
doc.add_heading("2. Requirements Document (SRS)", level=1)
add_para(doc, "The Software Requirements Specification (SRS) document was completed and approved as Version 1.0 on 18 August 2026. It serves as the controlling baseline for design, development, testing, user acceptance, and handover throughout the project.", size=10.5)

doc.add_heading("2.1 SRS Overview", level=2)
add_para(doc, "The SRS defines the complete business, functional, data, security, and quality requirements for the CREMS Motors and Carptrac MVP. Key sections include:", size=10.5)
for item in [
    "Introduction and product vision for the CREMS platform",
    "Stakeholders and user classes (Public visitor, Customer, Administrator, Branch Manager, Rental Officer, Maintenance Officer, Finance Officer, Driver/Operator)",
    "Scope baseline confirming in-scope and out-of-scope capabilities",
    "130+ functional requirements across 10 domains (Identity, Organization, Customers, Assets, Bookings, Rentals, Inspections, Maintenance, Personnel, Finance)",
    "15 core business rules governing rental operations",
    "Data requirements covering all operational domains",
    "External interfaces (Web UI, REST API, Database, Email, QR scanner, PDF generation)",
    "Non-functional requirements (Security, Performance, Reliability, Usability, Maintainability)",
    "Reporting requirements and acceptance criteria",
    "12 open decisions requiring Carpenters confirmation",
]:
    add_bullet(doc, item)

doc.add_heading("2.2 Requirements Summary", level=2)
add_table(doc, ["Requirement Domain", "Total", "Must", "Should"], [
    ["Identity and Access (IAM)", "15", "13", "2"],
    ["Organization and Divisions (ORG)", "10", "8", "2"],
    ["Customer Management (CUS)", "10", "7", "3"],
    ["Asset Register (AST)", "15", "9", "6"],
    ["Quotation and Booking (BKG)", "13", "10", "3"],
    ["Rental Operations (HIR)", "11", "11", "0"],
    ["Inspections (INS)", "11", "10", "1"],
    ["Maintenance (MNT)", "10", "5", "5"],
    ["Personnel and Field (OPS)", "6", "2", "4"],
    ["Finance and Reporting (FIN)", "14", "9", "5"],
], [3.0, 0.8, 0.8, 0.9], font_size=8.5,
          alignments=[None, WD_ALIGN_PARAGRAPH.CENTER, WD_ALIGN_PARAGRAPH.CENTER, WD_ALIGN_PARAGRAPH.CENTER])

add_callout(doc, "Full SRS Document", "The complete Software Requirements Specification v1.0 is included as Appendix A of this report and is available as a standalone document: CREMS_Software_Requirements_Specification_v1.0.pdf", fill=PALE_GREEN)

add_page_break(doc)

# ==================== 3. SYSTEM ARCHITECTURE ====================
doc.add_heading("3. System Architecture and Design", level=1)
add_para(doc, "The Functional Specification Document (FSD) translates the approved requirements into observable system behavior and defines the technical architecture for implementation.", size=10.5)

doc.add_heading("3.1 Architectural Overview", level=2)
add_para(doc, "CREMS uses a modular monolith architecture: one React frontend, one ASP.NET Core API, and one SQL Server database. This approach keeps deployment and debugging manageable while retaining clean API boundaries for future extensions.", size=10.5)

add_table(doc, ["Layer", "Technology", "Responsibility"], [
    ["Frontend", "React 19, TypeScript, Vite, Material UI", "Responsive public site, customer portal, task-based staff portal"],
    ["API", "ASP.NET Core 10 Web API", "Validation, authorization, workflow orchestration, REST contracts"],
    ["Identity", "ASP.NET Core Identity, secure cookies", "Staff/customer separation, roles, recovery, session security"],
    ["Data", "SQL Server 2022, Entity Framework Core", "Relational integrity, transactions, migrations, reporting"],
    ["Delivery", "Docker Compose, GitHub, Postman", "Repeatable environments, collaboration, testing"],
], [1.5, 2.5, 3.0], font_size=8.8)

doc.add_heading("3.2 Backend Modules", level=2)
add_para(doc, "The system is organized into the following domain modules:", size=10.5)
for module in [
    "Identity and access management",
    "Branches and organization",
    "Customers and portal management",
    "Assets and catalogue",
    "Bookings and availability",
    "Rentals and returns",
    "Inspections and damage",
    "Maintenance and costs",
    "Reporting and dashboards",
    "Notifications and email",
    "Auditing and history",
]:
    add_bullet(doc, module)

doc.add_heading("3.3 Key Design Decisions", level=2)
add_table(doc, ["Decision", "Rationale"], [
    ["GUID primary keys", "Avoid exposing predictable record counts; simplify offline imports"],
    ["DateTimeOffset for events", "Proper time zone handling; Fiji-local conversion at boundaries"],
    ["Explicit asset states", "Clear operational state rather than deriving from UI labels"],
    ["Booking lines carry agreed rate", "Master-rate changes do not alter historical records"],
    ["SQL constraints + transactional checks", "Joint prevention of double booking"],
    ["Soft delete via deactivation", "Preserve audit history and referential integrity"],
], [2.0, 5.0], font_size=9.0)

add_callout(doc, "Full FSD Document", "The complete Functional Specification Document v1.0 is included as Appendix B of this report and is available as a standalone document: CREMS_Functional_Specification_Document_v1.0.pdf", fill=PALE_GREEN)

add_page_break(doc)

# ==================== 4. PROJECT PLAN UPDATES ====================
doc.add_heading("4. Project Plan Updates", level=1)
add_para(doc, "The project is planned as a 12-week agile delivery from 10 August to 30 October 2026, organized into one-week sprints with weekly client checkpoints. The following sections detail concrete steps taken during the first four weeks.", size=10.5)

doc.add_heading("4.1 Work Completed (Weeks 1-4)", level=2)

doc.add_heading("Week 1: Scope and Foundation (10-14 August)", level=3)
for item in [
    "Client meeting and domain research",
    "Approved Motors/Carptrac boundary confirmation",
    "Requirements baseline and risk register establishment",
    "Git repository and branch conventions setup",
    "Solution skeleton and initial SQL Server container configuration",
]:
    add_bullet(doc, item)

doc.add_heading("Week 2: Authentication and Startup Services (17-21 August)", level=3)
for item in [
    "Staff/customer sign-in implementation",
    "Secure password handling with ASP.NET Identity",
    "Cookie/session timeout configuration",
    "API authorization defaults",
    "Health checks and error handling foundations",
]:
    add_bullet(doc, item)

doc.add_heading("Week 3: Organization and Access Isolation (24-28 August)", level=3)
for item in [
    "Division/branch administration",
    "User assignments and role matrix",
    "API-level division and branch isolation",
    "Configuration screens for organization setup",
]:
    add_bullet(doc, item)

doc.add_heading("Week 4: Customer Module (31 August - 4 September)", level=3)
for item in [
    "Individual and corporate customer profiles",
    "Contacts and approved renters management",
    "Customer portal foundation",
    "Validation and search functionality",
]:
    add_bullet(doc, item)

add_page_break(doc)

# ==================== 5. MILESTONES ACHIEVED ====================
doc.add_heading("5. Milestones Achieved", level=1)

add_table(doc, ["Milestone", "Sprint", "Target Date", "Status"], [
    ["M1 — Scope and engineering foundation", "Week 1", "14 August 2026", "Completed"],
    ["M2 — Secure sign-in demo", "Week 2", "21 August 2026", "Completed"],
    ["M3 — Access-isolation demo", "Week 3", "28 August 2026", "Completed"],
    ["M4 — Configurable asset-register demo", "Week 5", "11 September 2026", "In Progress"],
    ["M5 — Quotation-to-booking demo", "Week 7", "25 September 2026", "Pending"],
    ["M6 — Motors lifecycle demo", "Week 8", "2 October 2026", "Pending"],
    ["M7 — Carptrac lifecycle demo", "Week 10", "16 October 2026", "Pending"],
    ["M8 — Final handover", "Week 12", "30 October 2026", "Pending"],
], [2.2, 0.8, 1.3, 1.3], font_size=9.0,
          alignments=[None, WD_ALIGN_PARAGRAPH.CENTER, WD_ALIGN_PARAGRAPH.CENTER, WD_ALIGN_PARAGRAPH.CENTER])

add_callout(doc, "Milestone Status", "The project is on track with the planned schedule. Milestones 1-3 were completed on time. Milestone 4 is currently in progress and on schedule for completion by 11 September 2026.", fill=PALE_GREEN)

add_page_break(doc)

# ==================== 6. OBSTACLES ENCOUNTERED ====================
doc.add_heading("6. Obstacles Encountered and Mitigation", level=1)

doc.add_heading("6.1 Requirements Uncertainty", level=2)
add_para(doc, "Several business rules remain subject to client confirmation, including pricing rules, approval thresholds, and inspection requirements.", size=10.5)
add_para(doc, "Mitigation:", bold=True, size=10.5, after=4)
for item in [
    "Maintained a requirements register tracking 18 open decisions",
    "Documented provisional assumptions where implementation must begin early",
    "Scheduled fortnightly client checkpoints for decision confirmation",
    "Designed configurable rules to accommodate various pricing and approval models",
]:
    add_bullet(doc, item)

doc.add_heading("6.2 Scope Management", level=2)
add_para(doc, "The group-wide modular scope presents complexity in designing a system that supports multiple divisions without duplicating code or data.", size=10.5)
add_para(doc, "Mitigation:", bold=True, size=10.5, after=4)
for item in [
    "Adopted capability-driven division configuration",
    "Implemented progressive disclosure for customer journeys",
    "Protected MVP boundary with strict change control",
    "Used feature flags for division-specific capabilities",
]:
    add_bullet(doc, item)

doc.add_heading("6.3 Technical Complexity", level=2)
add_para(doc, "The dual customer/staff authentication model with division/branch scoping adds complexity to authorization and data access.", size=10.5)
add_para(doc, "Mitigation:", bold=True, size=10.5, after=4)
for item in [
    "Implemented CurrentStaffScope service for consistent scope enforcement",
    "Created granular permission system beyond basic roles",
    "Used server-side query filtering to prevent data leakage",
    "Developed comprehensive authorization test suite",
]:
    add_bullet(doc, item)

doc.add_heading("6.4 Client Availability", level=2)
add_para(doc, "Scheduling client meetings and obtaining timely decisions on business rules has been challenging.", size=10.5)
add_para(doc, "Mitigation:", bold=True, size=10.5, after=4)
for item in [
    "Prepared focused decision requests with clear options and defaults",
    "Used requirements register to prioritize decisions by impact",
    "Documented provisional assumptions to maintain development momentum",
    "Scheduled regular fortnightly checkpoints",
]:
    add_bullet(doc, item)

add_page_break(doc)

# ==================== 7. CHANGES TO THE PLAN ====================
doc.add_heading("7. Changes to the Plan", level=1)

add_para(doc, "The following changes have been made to the original project plan since its approval:", size=10.5)

doc.add_heading("7.1 Scope Clarification", level=2)
add_para(doc, "Following the first client meeting, the scope was clarified to emphasize group-wide modular capabilities rather than a vehicle-only rental system. This led to:", size=10.5)
for item in [
    "Addition of configurable division capabilities and service offerings",
    "Implementation of personnel requirement configuration for equipment hire",
    "Enhanced customer model supporting both individual and corporate accounts",
    "Progressive disclosure design for public customer journeys",
]:
    add_bullet(doc, item)

doc.add_heading("7.2 Architecture Refinement", level=2)
add_para(doc, "The architecture was refined based on implementation experience:", size=10.5)
for item in [
    "Adopted window-bound session security model",
    "Implemented staged email delivery with redirect-all for testing",
    "Added comprehensive permission system beyond basic roles",
    "Configured division-aware staff scope with branch assignment",
]:
    add_bullet(doc, item)

doc.add_heading("7.3 Schedule Adjustments", level=2)
add_para(doc, "No significant schedule adjustments have been required. The project remains on track with the original timeline. Minor adjustments include:", size=10.5)
for item in [
    "Overlapping work packages to maintain continuous progress",
    "Early start on asset/customer modules to validate architecture decisions",
]:
    add_bullet(doc, item)

add_page_break(doc)

# ==================== 8. RISK MANAGEMENT UPDATE ====================
doc.add_heading("8. Risk Management Update", level=1)

doc.add_heading("8.1 Current Risk Register", level=2)
add_table(doc, ["ID", "Risk", "P", "I", "Score", "Status", "Mitigation"], [
    ["R1", "Requirements unclear/change late", "4", "5", "20 H", "Active", "Decision log, change control, fortnightly validation"],
    ["R2", "Scope exceeds capacity", "4", "5", "20 H", "Active", "MVP protection, MoSCoW priorities"],
    ["R3", "Authorization data leakage", "3", "5", "15 H", "Mitigated", "Server scope queries, negative tests, peer review"],
    ["R4", "Booking rules incorrect", "3", "5", "15 H", "Active", "Transactions, overlap tests, domain rules"],
    ["R5", "Equipment safety incomplete", "3", "5", "15 H", "Active", "Client validation, mandatory gates"],
    ["R6", "Credential exposure", "2", "5", "10 M", "Mitigated", "Synthetic data, user secrets, scanning"],
    ["R7", "Member unavailability", "3", "4", "12 M", "Active", "Pairing, shared docs, cross-training"],
    ["R8", "Late integration", "3", "4", "12 M", "Active", "CI, fortnightly demos"],
    ["R9", "Client availability delays", "3", "4", "12 M", "Active", "Early booking, focused questions"],
    ["R10", "Data loss", "2", "5", "10 M", "Mitigated", "GitHub remote, backup rehearsal"],
    ["R11", "Email misconfiguration", "2", "4", "8 M", "Active", "Staging redirect-all, delivery log"],
    ["R12", "UI complexity", "3", "4", "12 M", "Active", "Task language, progressive disclosure"],
    ["R13", "Report reconciliation", "3", "4", "12 M", "Active", "Defined formulas, reconciliation tests"],
    ["R14", "Deployment owner unknown", "3", "4", "12 M", "Active", "Decision by W11, portable staging"],
    ["R15", "Schedule delay", "3", "4", "12 M", "New", "Scope substitution, 80% iteration target"],
], [0.5, 2.0, 0.4, 0.4, 0.7, 0.7, 2.8], font_size=7.5)

doc.add_heading("8.2 New Risks Identified", level=2)
add_para(doc, "One new risk has been identified since the original plan:", size=10.5)
add_table(doc, ["ID", "Risk", "P", "I", "Score", "Mitigation"], [
    ["R15", "Schedule delay due to requirements uncertainty", "3", "4", "12 M", "Scope substitution policy, 80% iteration target, escalation after 2-day blockers"],
], [0.5, 3.5, 0.4, 0.4, 0.7, 2.5], font_size=8.5)

doc.add_heading("8.3 Risk Mitigation Progress", level=2)
add_para(doc, "The following risks have been effectively mitigated through implementation:", size=10.5)
for item in [
    "R3 (Authorization): Implemented comprehensive server-side scope enforcement and negative testing",
    "R6 (Credentials): Established secret management, synthetic data policies, and repository scanning",
    "R10 (Data loss): Configured GitHub remote, protected branches, and backup/restore procedures",
]:
    add_bullet(doc, item)

add_page_break(doc)

# ==================== 9. TIMELINE FOR REMAINING DELIVERABLES ====================
doc.add_heading("9. Timeline for Remaining Deliverables", level=1)

add_table(doc, ["Week", "Dates", "Focus", "Exit Deliverable"], [
    ["W5", "7-11 Sep", "Complete asset module, categories, dynamic attributes", "M4: Asset-register demo"],
    ["W6", "14-18 Sep", "Availability, rates and quotations", "Rate configuration demo"],
    ["W7", "21-25 Sep", "Bookings, approvals, agreements, notifications", "M5: Quotation-to-booking demo"],
    ["W8", "28 Sep-2 Oct", "Pre-hire, QR check-out, return, post-hire inspection", "M6: Motors lifecycle demo"],
    ["W9", "5-9 Oct", "Maintenance, damage and detailed expenditure", "Maintenance module demo"],
    ["W10", "12-16 Oct", "Carptrac personnel and equipment-hire variations", "M7: Carptrac lifecycle demo"],
    ["W11", "19-23 Oct", "Dashboards, profitability, integration and UAT", "Feature-complete build"],
    ["W12", "26-30 Oct", "Stabilization, handover and closure", "M8: Final handover"],
], [0.8, 1.5, 2.5, 2.5], font_size=8.5)

doc.add_heading("9.1 Critical Path", level=2)
add_para(doc, "The critical path for remaining work includes:", size=10.5)
for item in [
    "Asset/service catalogue completion (W6)",
    "Availability and booking rules (W7-8)",
    "Agreement and checkout workflow (W9)",
    "Return and maintenance processing (W10)",
    "Client UAT and acceptance (W13)",
]:
    add_bullet(doc, item)

doc.add_heading("9.2 Upcoming Client Decisions", level=2)
add_para(doc, "The following client decisions are required to maintain schedule:", size=10.5)
add_table(doc, ["Decision", "Required By", "Impact if Delayed"], [
    ["Pricing and approval rules", "Before W7", "Cannot implement quotation engine"],
    ["Inspection template confirmation", "Before W9", "Cannot finalize checkout workflow"],
    ["Corporate account rules", "Before W7", "Cannot complete corporate features"],
    ["Deployment infrastructure", "Before W11", "Cannot prepare production environment"],
], [2.5, 1.5, 3.0], font_size=8.5)

add_page_break(doc)

# ==================== 10. CONCLUSION ====================
doc.add_heading("10. Conclusion", level=1)
add_para(doc, "The CREMS project has completed its first five weeks on schedule with all planned milestones achieved. The requirements baseline has been established, the system architecture is implemented, and significant progress has been made on identity, access management, and core domain entities.", size=10.5)
add_para(doc, "Key achievements include:", size=10.5)
for item in [
    "Approved SRS document with 130+ functional requirements across 10 domains",
    "Implemented secure authentication with role-based access and division/branch scoping",
    "Established modular architecture supporting configurable division capabilities",
    "Developed comprehensive FSD translating requirements into observable behavior",
    "Maintained effective client communication through fortnightly checkpoints",
]:
    add_bullet(doc, item)

add_para(doc, "The primary risks remain requirements uncertainty and scope management, both of which are being actively mitigated through the requirements register, change control, and client engagement processes. No schedule adjustments are currently required.", size=10.5)
add_para(doc, "The team remains confident in delivering the CREMS MVP within the planned 14-week timeline, meeting all academic and client acceptance criteria.", size=10.5)

add_page_break(doc)

# ==================== APPENDIX A - SRS SUMMARY ====================
doc.add_heading("Appendix A - SRS Summary", level=1)
add_para(doc, "The complete Software Requirements Specification v1.0 (CREMS_Software_Requirements_Specification_v1.0.pdf) is available alongside this report. Key sections include:", size=10.5)

add_table(doc, ["Section", "Description"], [
    ["1. Introduction", "Purpose, product vision, definitions, and requirement priority language"],
    ["2. Stakeholders", "User classes and their primary responsibilities"],
    ["3. Scope Baseline", "In-scope, out-of-scope, assumptions, and dependencies"],
    ["4. Functional Requirements", "130+ requirements across 10 functional domains"],
    ["5. Business Rules", "15 core business rules governing operations"],
    ["6. Data Requirements", "Logical data structure and client-supplied data mapping"],
    ["7. External Interfaces", "Web UI, REST API, database, email, QR scanner, PDF"],
    ["8. Non-Functional Reqs", "Security, performance, reliability, usability, maintainability"],
    ["9. Reporting", "Role-specific dashboards and reports"],
    ["10. Acceptance", "UAT scenarios and traceability"],
    ["11. Open Decisions", "12 decisions requiring Carpenters confirmation"],
    ["12. Change Control", "Change management process"],
    ["13. Client Sign-off", "Approval and signature section"],
], [1.5, 5.5], font_size=9.0)

add_page_break(doc)

# ==================== APPENDIX B - DESIGN DOCUMENT SUMMARY ====================
doc.add_heading("Appendix B - Design Document Summary", level=1)
add_para(doc, "The complete Functional Specification Document v1.0 (CREMS_Functional_Specification_Document_v1.0.pdf) is available alongside this report. Key sections include:", size=10.5)

add_table(doc, ["Section", "Description"], [
    ["1. Introduction", "Purpose, in-scope divisions, functional boundaries"],
    ["2. Architecture", "System context, functional design principles"],
    ["3. Roles & Permissions", "Role definitions and permission rules"],
    ["4. UI Behavior", "Common user interface patterns and standards"],
    ["5. Authentication", "Staff/customer auth, sessions, access administration"],
    ["6. Organization", "Divisions, branches, services configuration"],
    ["7. Customers", "Customer records and portal functions"],
    ["8. Assets", "Asset register, QR, lifecycle, availability"],
    ["9. Bookings", "Requests, quotations, approvals workflow"],
    ["10. Rentals", "Pickup, agreements, active hire, returns"],
    ["11. Maintenance", "Inspections and maintenance processing"],
    ["12. Personnel", "Operators, drivers, field operations"],
    ["13. Finance", "Invoicing, profitability, dashboards"],
    ["14. Notifications", "Email, documents, scheduled processing"],
    ["15. API/Validation", "API conventions, errors, audit behavior"],
    ["16. Acceptance", "Functional acceptance scenarios"],
    ["17. Assumptions", "Assumptions, decisions, change control"],
    ["18. Sign-off", "Client approval section"],
], [1.5, 5.5], font_size=9.0)

# Save document
doc.save(OUT)
print(f"Mid-term review report created: {OUT}")
