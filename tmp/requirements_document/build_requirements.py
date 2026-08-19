from __future__ import annotations

import sys
from datetime import date
from pathlib import Path

from docx import Document
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Inches, Pt, RGBColor


ROOT = Path("/Users/kavishchandra/Documents/CS400")
OUT = ROOT / "output/documents/CREMS_Software_Requirements_Specification_v1.0.docx"
OUT.parent.mkdir(parents=True, exist_ok=True)
SKILL = Path("/Users/kavishchandra/.codex/plugins/cache/openai-primary-runtime/documents/26.813.12317/skills/documents")
sys.path.insert(0, str(SKILL / "scripts"))
from table_geometry import apply_table_geometry, column_widths_from_weights


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
    widths = column_widths_from_weights(weights, CONTENT_WIDTH)
    apply_table_geometry(table, widths, table_width_dxa=CONTENT_WIDTH, indent_dxa=120,
                         cell_margins_dxa={"top": 90, "bottom": 90, "start": 120, "end": 120})
    doc.add_paragraph().paragraph_format.space_after = Pt(0)
    return table


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


def add_toc(doc):
    entries = [
        "1. Introduction",
        "2. Stakeholders and user classes",
        "3. Scope baseline",
        "4. Functional requirements",
        "5. Business rules",
        "6. Data requirements",
        "7. External interfaces",
        "8. Non-functional requirements",
        "9. Reporting requirements",
        "10. Acceptance and traceability",
        "11. Open decisions requiring Carpenters confirmation",
        "12. Change control",
        "13. Client sign-off",
        "Appendix A - Requirements summary",
    ]
    for entry in entries:
        p = doc.add_paragraph()
        p.paragraph_format.space_after = Pt(1)
        p.paragraph_format.line_spacing = 1.0
        p.paragraph_format.left_indent = Inches(0.12)
        r = p.add_run(entry)
        set_font(r, size=9.2, color=GREEN, bold=True)


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

header = section.header
hp = header.paragraphs[0]
hp.alignment = WD_ALIGN_PARAGRAPH.LEFT
hr = hp.add_run("CREMS | Software Requirements Specification")
set_font(hr, size=8.5, bold=True, color=MID_GRAY)
footer = section.footer
fp = footer.paragraphs[0]
fr = fp.add_run("Carpenters Fiji Pte Limited | Version 1.0 | Client sign-off baseline")
set_font(fr, size=8.3, color=MID_GRAY)
add_page_field(footer.add_paragraph())

# Cover page
add_para(doc, "CARPENTERS FIJI PTE LIMITED", bold=True, size=11, color=GREEN, after=40)
add_para(doc, "SOFTWARE REQUIREMENTS\nSPECIFICATION", bold=True, size=28, color=BLACK, after=8)
add_para(doc, "Car Rental and Equipment Management System (CREMS)", bold=True, size=16, color=GREEN, after=12)
add_para(doc, "Carpenters Motors and Carptrac MVP", size=13, color=MID_GRAY, after=28)
add_callout(doc, "Document purpose", "This specification defines the agreed business, functional, data, security and quality requirements that will form the baseline for development, acceptance testing and customer sign-off.", fill=PALE_YELLOW)
add_para(doc, "", after=22)
add_table(doc, ["Document control", "Value"], [
    ["Client", "Carpenters Fiji Pte Limited"],
    ["Project", "Car Rental and Equipment Management System (CREMS)"],
    ["In-scope divisions", "Carpenters Motors and Carptrac"],
    ["Prepared by", "Kavish Chandra; Shoneel Kumar; Sudhansu Jayshil Kisun; Rahul Chand"],
    ["Academic supervisor", "Dr Ravneil Nand"],
    ["Client sponsor", "Amit Kumar"],
    ["Version", "1.0 - Requirements sign-off baseline"],
    ["Document date", "18 August 2026"],
    ["Classification", "Client confidential - project use"],
], [1.65, 4.85], font_size=9.5)
add_para(doc, "Approval effect", bold=True, size=10, color=GREEN, after=3)
add_para(doc, "Signing this document confirms the requirements baseline, not acceptance of a completed system. Changes after sign-off will be assessed through the project change-control process.", size=10, after=0)

add_page_break(doc)
doc.add_heading("Document control", level=1)
add_table(doc, ["Version", "Date", "Prepared by", "Description", "Status"], [
    ["0.1", "18 Aug 2026", "CREMS team", "Initial requirements consolidation", "Draft"],
    ["1.0", "18 Aug 2026", "CREMS team", "Issued for Carpenters review and sign-off", "For approval"],
], [0.7, 1.0, 1.35, 2.55, 0.9])

doc.add_heading("Review and approval responsibilities", level=2)
add_table(doc, ["Role", "Responsibility"], [
    ["Client sponsor", "Confirms business scope, priorities, acceptance authority and change-control decisions."],
    ["Motors representative", "Validates vehicle rental, customer, inspection, pickup/return and fleet data requirements."],
    ["Carptrac representative", "Validates equipment categories, operators, meter rules, safety checks and maintenance requirements."],
    ["IT/security representative", "Validates hosting, access, session, audit, backup, data-retention and security requirements."],
    ["CREMS project team", "Maintains traceability, implements the approved baseline and records variances or assumptions."],
], [1.55, 4.95], font_size=9.3)

doc.add_heading("Reference documents", level=2)
for text in [
    "CREMS Feasibility Study, Version 1.0, 18 August 2026.",
    "CREMS Project Management Plan and twelve-week semester delivery baseline.",
    "Carpenters Vehicle Rental Inspection Checklist.",
    "Carptrac Equipment, Genset and Heavy Machine Rental Inspection Checklists.",
    "Rental Vehicle Stock List and Rental Stock List - CAT, supplied 18 August 2026.",
    "CREMS Client Data Mapping and Import Validation Pack, prepared for confirmation.",
]: add_bullet(doc, text)

doc.add_heading("Contents", level=1)
add_toc(doc)

add_page_break(doc)
doc.add_heading("1. Introduction", level=1)
doc.add_heading("1.1 Purpose", level=2)
add_para(doc, "This Software Requirements Specification (SRS) records what CREMS must do, the business rules it must enforce, the information it must hold, and the quality and security standards it must meet. It is the controlling baseline for design, development, testing, user acceptance and handover during the CS400 Semester 2, 2026 project.")
doc.add_heading("1.2 Product vision", level=2)
add_para(doc, "CREMS will provide Carpenters Motors and Carptrac with one configurable web platform for customer access, asset registration and tracking, quotation and booking, approval, rental agreement, pre-hire and post-hire inspection, check-out/check-in, maintenance, invoicing and asset-level performance reporting. Division and branch boundaries must remain visible and enforceable throughout the system.")
doc.add_heading("1.3 Definitions", level=2)
add_table(doc, ["Term", "Definition"], [
    ["Asset", "A vehicle, machine, generator, forklift or other item that Carpenters makes available for rental or hire."],
    ["Division", "A Carpenters operating business configured in CREMS, initially Motors or Carptrac."],
    ["Branch", "An operational location that may provide one or more division services."],
    ["Service", "A configurable rental or hire offering belonging to a division."],
    ["Booking request", "A customer's request for dates, service, category or asset; it is not confirmed until required checks and approvals pass."],
    ["Quotation", "A versioned commercial offer containing rates, charges, tax, deposit, validity and approval status."],
    ["Hire / rental", "A confirmed booking that has progressed through agreement and check-out into an active customer possession period."],
    ["Inspection", "A configurable, signed pre-hire or post-hire condition record with readings, responses, notes and evidence."],
    ["Meter", "Odometer kilometres, engine/operating hours, fuel level or another approved usage measurement."],
    ["MVP", "The agreed Motors and Carptrac scope targeted for the semester delivery baseline."],
], [1.35, 5.15], font_size=9.2)

doc.add_heading("1.4 Requirement language and priority", level=2)
add_para(doc, "The word must denotes a mandatory requirement. Should denotes a high-value requirement that may be deferred only through approved change control. Could denotes a future or optional capability outside the committed MVP unless subsequently approved.")
add_table(doc, ["Priority", "Meaning"], [
    ["Must", "Required for the agreed MVP or for security, data integrity or legal/operational safety."],
    ["Should", "Important to business value; included where capacity permits and removed before any Must item."],
    ["Could", "Future increment or enhancement; not part of the signed MVP baseline."],
], [1.1, 5.4], font_size=9.5)

doc.add_heading("2. Stakeholders and user classes", level=1)
add_table(doc, ["User class", "Primary responsibilities", "Scope"], [
    ["Public visitor", "Browse divisions, services, available categories/assets and indicative pricing; submit enquiries; register or sign in.", "Public, customer-safe data only"],
    ["Customer", "Maintain account, request quotations/bookings, provide required information, accept documents, track status and view own records.", "Own individual or corporate account"],
    ["Super Administrator", "Configure divisions, system-wide permissions, security settings and group-level access.", "Group-wide"],
    ["Administrator", "Manage users, operational configuration and audit within delegated authority; cannot control a Super Administrator.", "Assigned administrative scope"],
    ["Branch Manager", "Approve exceptions/high-value rentals and monitor branch operations and profitability.", "Assigned division and branch(es)"],
    ["Rental Officer", "Manage customers, requests, quotations, allocations, pickup and return.", "Assigned division and branch(es)"],
    ["Maintenance Officer", "Receive faults, schedule and complete work, record parts/labour/cost and release assets.", "Assigned maintenance scope"],
    ["Finance Officer", "Manage invoices, payments, refunds, credit and financial reporting.", "Assigned financial scope"],
    ["Driver / Operator", "View assignments, scan assets, record readings/timesheets, safety checks and delivery/pickup evidence.", "Assigned tasks and assets"],
], [1.25, 3.35, 1.9], font_size=8.7)

doc.add_heading("3. Scope baseline", level=1)
doc.add_heading("3.1 In scope", level=2)
for item in [
    "Carpenters Motors vehicle-rental operations and Carptrac equipment-hire operations.",
    "Public browsing plus authenticated individual and corporate customer accounts.",
    "Staff authentication, permission-based access and division/branch scoping.",
    "Configurable divisions, branches, services, asset categories, attributes, checklists, rates and approval workflows.",
    "Customer, asset, availability, quotation, booking, agreement, check-out, active hire and return workflows.",
    "Pre-hire and post-hire inspections based on the four supplied Carpenters checklists.",
    "Meter readings, QR identification, maintenance, costs, revenue, profitability, dashboards, reports, notifications and audit logs.",
    "An architecture that permits future divisions and hireable asset types to be configured without redesigning core modules.",
]: add_bullet(doc, item)
doc.add_heading("3.2 Out of scope for this MVP", level=2)
for item in [
    "Carpenters Shipping operations and assets, including bins, scaffolding and portable toilets.",
    "Freight forwarding, vessel operations, cargo manifests, customs, bills of lading and route optimization.",
    "Property leasing, hardware retail and other Carpenters divisions not explicitly approved for this baseline.",
    "Live bank/card payment-gateway integration, telematics/GPS hardware integration and accounting-system synchronization.",
    "Native mobile applications and guaranteed offline synchronization; the delivered interface is a responsive web application.",
    "Migration of unapproved production data. Supplied stock lists remain subject to mapping confirmation and cleansing.",
]: add_bullet(doc, item)
add_callout(doc, "Future-readiness rule", "Out-of-scope divisions must be addable later through division, branch, service, category, attribute, pricing and checklist configuration. This requirement does not authorize delivery of those divisions in the current MVP.", fill=PALE_GREEN)

doc.add_heading("3.3 Assumptions and dependencies", level=2)
assumptions = [
    ["A-01", "Carpenters will appoint representatives for Motors, Carptrac, IT/security and final acceptance."],
    ["A-02", "Carpenters will confirm branch/location structure and the authoritative asset identifiers before import."],
    ["A-03", "Customer, staff and operational data used for development will be synthetic or anonymised."],
    ["A-04", "Email delivery depends on a valid approved SMTP/email-service configuration and sender domain."],
    ["A-05", "Internet connectivity is available for normal operation; offline field synchronization is not committed."],
    ["A-06", "Carpenters will provide timely decisions and UAT participants within the twelve-week schedule."],
    ["A-07", "Rates, tax, deposits, credit limits and approval thresholds will be supplied or formally approved."],
]
add_table(doc, ["ID", "Assumption / dependency"], assumptions, [0.8, 5.7], font_size=9.3)

# Functional requirements
doc.add_heading("4. Functional requirements", level=1)
add_para(doc, "Each requirement has a stable identifier for traceability. Acceptance criteria describe the minimum observable evidence required during testing or demonstration.")

functional_sections = [
    ("4.1 Identity, authentication and access", [
        ("IAM-01", "Staff and customers must authenticate using a unique email address and password.", "Valid credentials create a session; invalid credentials return a generic failure without revealing account existence.", "Must"),
        ("IAM-02", "The system must securely hash passwords and must never store or log plain-text passwords.", "Database and logs contain only framework-managed hashes; password values are absent from application logs.", "Must"),
        ("IAM-03", "Accounts must support Invited, Activation Pending, Active, Locked, Suspended and Deactivated states.", "Only Active accounts can sign in; every state change is audited.", "Must"),
        ("IAM-04", "Users must be able to request a time-limited, single-use password-reset link delivered to their registered email.", "Expired, reused or invalid reset tokens are rejected; successful reset revokes prior sessions.", "Must"),
        ("IAM-05", "Staff sessions must expire after a configurable inactivity period and absolute maximum lifetime.", "Idle and absolute expiry tests end the session and require re-authentication.", "Must"),
        ("IAM-06", "Session cookies must be HttpOnly, Secure and SameSite and must not expose authentication tokens to client scripts.", "Browser inspection confirms cookie flags and no token in local/session storage.", "Must"),
        ("IAM-07", "A login session must be bound to the originating browser-window session and invalidated when the window session is absent or replaced.", "Copying the cookie into a different/new window context does not create a valid session under the agreed window-session design.", "Must"),
        ("IAM-08", "The system must enforce permissions in addition to roles for sensitive actions.", "API tests demonstrate allow/deny behaviour for configured permissions such as rentals.approve and reports.financial.", "Must"),
        ("IAM-09", "Staff access must support one or more division/branch assignments, temporary access and group-wide access where authorized.", "Records returned by APIs match active scope assignments and effective dates.", "Must"),
        ("IAM-10", "Customer portal access must be separate from customer rental eligibility/business status.", "Disabling portal access does not automatically block the customer from staff-managed rental eligibility, and vice versa.", "Must"),
        ("IAM-11", "Authorized administrators must reset passwords, resend activation, lock/unlock accounts and revoke sessions.", "Each action works within administrator authority and creates an audit event.", "Must"),
        ("IAM-12", "Users must be able to view and revoke their active sessions.", "Revoked sessions fail on their next authenticated request.", "Should"),
        ("IAM-13", "The system must record successful/failed login, logout, expiry, reset and suspicious-login events with time, IP and device/browser summary.", "Security history is queryable by authorized users and excludes secrets.", "Must"),
        ("IAM-14", "Separation-of-duties rules must prevent self-elevation, unauthorized Super Administrator changes and self-approval of restricted transactions.", "Negative authorization tests reject each prohibited combination.", "Must"),
        ("IAM-15", "Sensitive Super Administrator and financial operations should support step-up verification/MFA when enabled.", "Configured operations require a valid second factor; disabled configuration preserves the approved MVP login flow.", "Should"),
    ]),
    ("4.2 Organization, divisions, branches and services", [
        ("ORG-01", "Only Super Administrators must create, deactivate or materially configure divisions.", "Normal administrators and branch managers receive 403 for division configuration APIs.", "Must"),
        ("ORG-02", "A division must store code, name, description, contact details, branding, visibility, status, currency, tax and default rental terms.", "Create/edit/view operations persist and return every approved field.", "Must"),
        ("ORG-03", "A branch must store code, name, address, contacts, operating hours, pickup/return instructions, visibility and status.", "Branch detail shows all configured operational fields.", "Must"),
        ("ORG-04", "A physical branch may serve multiple divisions and expose different services for each division.", "Configuration enables Motors and/or Carptrac per branch without duplicate branch records.", "Must"),
        ("ORG-05", "The division-branch relationship must control local contacts, manager, enabled services, booking availability, maintenance capability, pricing and workflow overrides.", "A disabled relationship prevents new bookings while preserving history.", "Must"),
        ("ORG-06", "Administrators must configure services without code changes, including hire unit, personnel/delivery requirements, quote requirement, documents, deposit, inspection and meter rules.", "A new test service can be configured and used without application recompilation.", "Must"),
        ("ORG-07", "Branches must support weekly hours, public holidays, temporary closures and pickup/return cut-off times.", "Availability rejects or warns for a closed period according to configuration.", "Should"),
        ("ORG-08", "Inactive divisions, branches and services must be hidden from new public/staff transactions but retained in historical records.", "Historical reports still resolve names while selection lists omit inactive items.", "Must"),
        ("ORG-09", "Branch managers may edit only approved operational details for their assigned branches and must not access the administration branch-configuration workspace.", "UI navigation and API authorization both enforce the restriction.", "Must"),
        ("ORG-10", "The platform must allow a future division and hireable asset category to be configured without redesigning core rental, asset and maintenance entities.", "A configuration-only proof creates a sample inactive future division and category in staging.", "Must"),
    ]),
    ("4.3 Customer and portal management", [
        ("CUS-01", "The system must maintain separate individual and corporate customer accounts.", "Registration and staff creation capture the correct fields and customer type.", "Must"),
        ("CUS-02", "Customer records must hold a unique customer number, contacts, address, identification and active/blocked status.", "Required-field and uniqueness validation is enforced.", "Must"),
        ("CUS-03", "Corporate customers must support legal/tax details, multiple contacts, purchase orders, approved renters/drivers, credit limits and payment terms.", "Corporate detail persists and displays all configured controls.", "Should"),
        ("CUS-04", "Public visitors must browse customer-safe assets/services, indicative pricing and availability without staff-login links.", "Public page shows no staff authentication entry and returns only approved public fields.", "Must"),
        ("CUS-05", "A customer must authenticate before submitting a booking, tracking detailed status or viewing account documents.", "Unauthenticated protected portal routes redirect to customer sign-in.", "Must"),
        ("CUS-06", "Customers must select Motors or Carptrac and see a simplified experience relevant to the chosen service.", "Vehicle customers are not forced through operator/equipment fields unless applicable.", "Must"),
        ("CUS-07", "Customers must request quotations/bookings with dates, branch/service, category/asset preference and required contact information.", "A valid request creates a reference and appears in the relevant staff queue.", "Must"),
        ("CUS-08", "Customers must track their own quotations, bookings, agreements and invoices and must not access another customer's records.", "Ownership authorization tests return 403/404 for cross-customer access.", "Must"),
        ("CUS-09", "Customers must be able to submit enquiries and receive an acknowledgement/reference.", "Valid enquiry persists and notification is queued/sent.", "Should"),
        ("CUS-10", "Staff must see a consolidated customer activity view across authorized divisions.", "Customer profile shows scoped requests, rentals, invoices, cases and audit-relevant activity.", "Should"),
    ]),
    ("4.4 Asset register, lifecycle and availability", [
        ("AST-01", "Staff must register an asset with asset number, division, branch, service, category, manufacturer, model and active status.", "Required fields are validated and the asset becomes searchable within scope.", "Must"),
        ("AST-02", "Vehicle assets must support registration, VIN/chassis, engine, year, colour, odometer, registration/insurance and warranty information.", "Vehicle profile displays configured values and uniqueness validation for approved identifiers.", "Must"),
        ("AST-03", "Equipment assets must support serial, model, capacity/subtype, engine/operating hours and personnel requirement.", "Equipment profile changes fields dynamically by configured category.", "Must"),
        ("AST-04", "Super Administrators must configure asset categories and typed attributes including required, searchable, reportable and customer-visible flags.", "A new attribute appears on the asset form and obeys its configured rules.", "Must"),
        ("AST-05", "An asset profile must provide Overview, Specifications, Availability, Inspections, Meter Readings, Maintenance, Costs & Revenue, Documents, Lifecycle and Audit views.", "Authorized staff can navigate each view from one asset workspace.", "Must"),
        ("AST-06", "The system must generate a unique QR code that resolves to an authorized asset identification/check-in/check-out workflow.", "Scanning a valid QR locates one asset; altered/unknown codes are rejected.", "Must"),
        ("AST-07", "Asset lifecycle must support Commissioned, Available, Reserved, Pre-hire Inspection, Checked Out, On Hire, Returned, Post-hire Inspection, Maintenance/Damage Assessment and Retired/Disposed states.", "Only valid transitions succeed and every transition records actor, time and context.", "Must"),
        ("AST-08", "Lifecycle events must record user, timestamp, booking, previous/new status, meter, notes and evidence where applicable.", "Asset history shows an immutable chronological record.", "Must"),
        ("AST-09", "The system must store odometer, engine/operating hours, fuel and other configured meter readings with source and timestamp.", "New readings cannot be lower than the prior approved reading unless an authorized correction is recorded.", "Must"),
        ("AST-10", "Availability must consider bookings, maintenance, inspections, transfers, branch closures and personnel requirements.", "Conflict tests prevent allocation for every blocking event type.", "Must"),
        ("AST-11", "Staff and customers must receive a clear reason when an asset is unavailable and authorized staff should see suitable alternatives.", "Availability response includes reason code and same-category alternatives where available.", "Should"),
        ("AST-12", "Asset documents and photos must be associated with the asset and protected by authorization.", "Unauthorized downloads fail; metadata records uploader, type and timestamp.", "Should"),
        ("AST-13", "Asset financial data must include rental/service revenue and maintenance, operator, transport, labour, fuel and other costs.", "Asset financial view reconciles stored charge/cost entries for a selected period.", "Should"),
        ("AST-14", "The system should calculate utilization, gross profit, margin and cost per kilometre/hour where data is available.", "Calculations use documented formulas and match test datasets.", "Should"),
        ("AST-15", "Asset retirement/disposal must prevent future bookings while preserving history and financial records.", "Retired assets disappear from availability but remain reportable.", "Must"),
    ]),
    ("4.5 Quotation, booking and approval", [
        ("BKG-01", "Staff must manage booking work queues for New Request, Quotation Required, Awaiting Approval, Confirmed and Cancelled/Expired.", "Each queue shows count and only matching records within user scope.", "Must"),
        ("BKG-02", "A booking workspace must show customer, service, dates, branch, asset/availability, pricing, approvals, documents, notes and activity.", "Opening a request provides the complete workspace without requiring database access.", "Must"),
        ("BKG-03", "The system must prevent overlapping confirmed/reserved bookings for the same asset.", "Concurrent conflict test allows only one conflicting confirmation.", "Must"),
        ("BKG-04", "A quotation must calculate hire duration and support base hire, operator/driver, transport, labour, fuel, deposit, discount and tax components.", "Test quotation line items and totals match the approved calculation rules.", "Must"),
        ("BKG-05", "Pricing must resolve the applicable rate by customer contract, branch override, division, category/asset and standard fallback order.", "Pricing tests prove each precedence level and fallback.", "Should"),
        ("BKG-06", "Rate configuration must support hourly, daily, weekly, monthly, per-unit, trip, kilometre, tonne, square-metre and fixed units.", "Configured charge unit persists and calculates with entered quantity.", "Must"),
        ("BKG-07", "Quotations must support version history, validity/expiry, approval status and customer acceptance/decline.", "Revising a quote preserves prior immutable versions and creates a current version.", "Must"),
        ("BKG-08", "Internal cost and margin information must be hidden from customers.", "Customer quote/API contains selling amounts only.", "Must"),
        ("BKG-09", "Approval workflows must allow configurable stage count, approver role/named user, value threshold, division scope and escalation.", "A staged test booking routes to configured approvers in order.", "Should"),
        ("BKG-10", "An approver must record approval, rejection or revision reason; unauthorized or self-approval must be rejected where prohibited.", "Decision history records actor, time, stage and reason.", "Must"),
        ("BKG-11", "Approval delegation should support effective start/end dates and division scope.", "An active delegate can act only within the configured period/scope.", "Should"),
        ("BKG-12", "A confirmed booking must require an available asset or approved category-allocation workflow, required approvals and valid customer status.", "Confirmation fails with a clear unmet-requirement list when any condition is missing.", "Must"),
        ("BKG-13", "The system must email approved quotations and booking confirmations using configured templates.", "Delivery attempt and result are recorded against the transaction.", "Must"),
    ]),
    ("4.6 Agreement, pickup, active hire and return", [
        ("HIR-01", "A rental agreement must be generated only after booking confirmation, asset allocation, required identity/licence verification, payment/deposit checks and pre-hire inspection.", "Agreement generation remains blocked until every configured prerequisite passes.", "Must"),
        ("HIR-02", "Pickup must be a guided workflow: customer, QR/asset, identification/licence, meter/fuel, inspection, charges/agreement, signature and agent approval.", "Staff cannot complete checkout while a mandatory step is incomplete.", "Must"),
        ("HIR-03", "The agreement must contain customer, authorized driver/operator, asset, dates, rates/charges, deposit, liability, condition acknowledgement and approved terms.", "Generated document contains values from the confirmed booking and approved terms version.", "Must"),
        ("HIR-04", "Customer and agent signatures must be captured at pickup and timestamped.", "Signed agreement records names, signatures, time and completing user.", "Must"),
        ("HIR-05", "After signing, the agreement must be immutable; corrections must use an addendum or controlled cancellation/reissue process.", "Edit API rejects signed agreement changes and retains original document.", "Must"),
        ("HIR-06", "Checkout must generate a PDF, email the customer copy, change the asset to On Hire and create audit/lifecycle records.", "One completed checkout produces all four outcomes atomically or reports failure without partial state.", "Must"),
        ("HIR-07", "Hire Operations must provide Pickup Today, On Hire, Due Today, Overdue, Return in Progress and Recently Completed queues.", "Records appear in the correct queue based on status and timestamps.", "Must"),
        ("HIR-08", "Return must be guided: QR scan, booking/customer confirmation, return time, meter/fuel, post-hire inspection, condition comparison, damage, final charges, acknowledgement and invoice.", "Staff cannot finish return until required steps complete.", "Must"),
        ("HIR-09", "The system must calculate approved late, fuel, cleaning, damage and excess-usage charges from return data.", "Test returns match configured rules and retain calculation detail.", "Must"),
        ("HIR-10", "The system must determine the next asset state from inspection/fault outcomes: Available, Inspection, Damage Assessment or Maintenance.", "Return completion applies the documented decision rules without arbitrary staff status selection.", "Must"),
        ("HIR-11", "Overdue hires must be clearly flagged and trigger configured alerts/notifications.", "A past-due active hire appears as overdue and creates one deduplicated alert.", "Must"),
    ]),
    ("4.7 Configurable inspections", [
        ("INS-01", "Administrators must configure pre-hire and post-hire templates by asset category without code changes.", "A new checklist item appears in the selected category/stage only.", "Must"),
        ("INS-02", "Template items must support label, section, order, response type, required flag, evidence rule and issue action.", "Configured properties are enforced on inspection completion.", "Must"),
        ("INS-03", "Vehicle templates must support the supplied body, glass, lighting, tyres, interior, accessories, fuel and odometer fields.", "All supplied vehicle checklist fields appear in the approved templates.", "Must"),
        ("INS-04", "General equipment templates must support physical condition, controls, power, accessories, labels, operation, cables/hoses, cleanliness and damage.", "All supplied general-equipment checklist fields appear.", "Must"),
        ("INS-05", "Genset templates must support engine, control panel, battery, fuel, cooling, exhaust, alternator, canopy, meters, safety, electrical connections, earth stake, logbook, extinguisher, leaks and cleanliness.", "All supplied genset checklist fields appear.", "Must"),
        ("INS-06", "Heavy-machine templates must support engine, hydraulics, tracks/tyres, cab, alarms, attachments, leaks, extinguisher and damage diagram reference.", "All supplied heavy-machine checklist fields appear.", "Must"),
        ("INS-07", "Inspections must capture customer/booking/asset context, meter/fuel, responses, notes, damage map, evidence, signatures, user and completion time.", "Completed inspection record contains every applicable field.", "Must"),
        ("INS-08", "Pre-hire and post-hire condition must be comparable item-by-item.", "Return view highlights changed responses and new damage.", "Must"),
        ("INS-09", "Safety-critical failures must block checkout or return-to-service according to approved configuration.", "A configured critical failure prevents the relevant transition and explains why.", "Must"),
        ("INS-10", "Approved issues must create a damage case or maintenance referral when configured.", "Issue action creates one linked record and avoids duplicates.", "Should"),
        ("INS-11", "Completed signed inspections must be immutable; authorized corrections must preserve an audit trail.", "Direct edit is rejected; correction records original and replacement values/reason.", "Must"),
    ]),
    ("4.8 Maintenance and operating cost", [
        ("MNT-01", "Authorized staff must log preventive, corrective, inspection-referred and damage-related maintenance jobs.", "Each job links asset, branch, type, fault, status, dates and reporter.", "Must"),
        ("MNT-02", "Maintenance workflow must support Reported, Scheduled, In Progress, Waiting for Parts, Completed and Cancelled states.", "Only documented transitions are accepted and audited.", "Must"),
        ("MNT-03", "Preventive maintenance should be triggered by date, kilometres, engine/operating hours or whichever approved threshold occurs first.", "Threshold test creates one due reminder/job at the boundary.", "Should"),
        ("MNT-04", "Jobs must capture parts, internal labour hours/cost, external contractor invoices, transport, fuel and other actual expenses.", "Completed job total reconciles its cost components.", "Should"),
        ("MNT-05", "Maintenance must record downtime start/end and prevent conflicting rental allocation.", "Unavailable period appears on asset calendar and blocks booking.", "Must"),
        ("MNT-06", "Maintenance completion must record work performed, final cost, next-service rule and completion evidence.", "Completion fails when required fields are missing.", "Must"),
        ("MNT-07", "An asset must require authorized return-to-service approval before becoming Available after a blocking job.", "Closing the job alone does not bypass approval.", "Must"),
        ("MNT-08", "Maintenance history must show repeat failures and cumulative cost per asset.", "Asset maintenance view aggregates jobs and identifies repeated fault categories.", "Should"),
        ("MNT-09", "Authorized users should manage suppliers, parts inventory and parts usage.", "Part issue reduces available quantity and links cost to the job.", "Should"),
        ("MNT-10", "Warranty status and expiry should be visible when assessing maintenance cost responsibility.", "Job view warns when the asset is within recorded warranty.", "Should"),
    ]),
    ("4.9 Operators, drivers and field work", [
        ("OPS-01", "Personnel records must support operator, driver, technician, labourer and supervisor types with branch/division scope.", "Personnel selection is filtered by type and authorized scope.", "Must"),
        ("OPS-02", "Operator/driver qualifications must store certificate/licence, issue/expiry and safety-induction status.", "Expired or missing mandatory qualification blocks assignment.", "Must"),
        ("OPS-03", "Carptrac bookings must support required/optional operator assignment and availability checking.", "Booking confirmation rejects a required service with no qualified available operator.", "Must"),
        ("OPS-04", "Personnel assignments must record booking, asset, role, schedule, status and charge/cost rates.", "Assignment appears in both booking and personnel schedule.", "Should"),
        ("OPS-05", "Operators/drivers should record start/stop time, meter, safety checks, photos, signature and task status from a mobile-friendly workflow.", "Authorized assigned user completes the workflow on a target mobile viewport.", "Should"),
        ("OPS-06", "Timesheets must separate standard and overtime hours and support supervisor/customer approval where required.", "Approved timesheet calculates charge and employee cost using configured rates.", "Should"),
    ]),
    ("4.10 Finance, reporting and notifications", [
        ("FIN-01", "The system must generate an invoice from confirmed rental charges and approved return adjustments.", "Invoice lines reconcile quotation/booking and return charges.", "Must"),
        ("FIN-02", "Authorized finance users must record payments, partial payments, refunds and outstanding balances.", "Balance updates correctly and prohibited self-approval rules are enforced.", "Must"),
        ("FIN-03", "Corporate accounts should support credit limits, payment terms, purchase orders and account statements.", "Approval warns/blocks according to configured credit rule and statement reconciles invoices/payments.", "Should"),
        ("FIN-04", "Asset profitability must compare rental/service revenue with maintenance, operator, transport, labour, fuel and other recorded costs.", "Report matches a controlled test ledger per asset and period.", "Should"),
        ("FIN-05", "Role-specific dashboards must show only useful, authorized operational and financial measures.", "Rental officer, manager, maintenance, finance and administrator views differ and respect scope.", "Must"),
        ("FIN-06", "Reports must include rental history, fleet/equipment utilization, maintenance, revenue, overdue rentals, customer history and asset profit/loss.", "Each report filters by authorized date/division/branch and returns expected test data.", "Must"),
        ("FIN-07", "Authorized reports should export to CSV, Excel and PDF.", "Exported values match on-screen filters and totals.", "Should"),
        ("NOT-01", "The system must send or queue booking confirmations, approved quotations, signed agreements, invoices, overdue notices and maintenance reminders.", "Each configured event creates one notification with delivery status.", "Must"),
        ("NOT-02", "Email templates must support approved placeholders and must not expose internal costs or unauthorized data.", "Template preview and sent content contain only allow-listed fields.", "Must"),
        ("NOT-03", "Failed delivery attempts must be logged and retryable without duplicating the business transaction.", "A simulated failure records error and later retry succeeds once.", "Must"),
        ("RPT-01", "The system must maintain an audit log for critical create, update, delete, approval, login, checkout, return, finance and configuration events.", "Audit entry records actor, time, action, entity and non-secret summary.", "Must"),
        ("RPT-02", "Administrators must be able to search audit events but must not modify or delete them through normal application functions.", "Audit mutation endpoints are absent or denied.", "Must"),
    ]),
]

for heading, requirements in functional_sections:
    doc.add_heading(heading, level=2)
    add_table(doc, ["ID", "Requirement", "Acceptance criterion", "Priority"], requirements,
              [0.72, 3.05, 2.0, 0.73], font_size=8.25,
              alignments=[WD_ALIGN_PARAGRAPH.CENTER, None, None, WD_ALIGN_PARAGRAPH.CENTER])

doc.add_heading("5. Business rules", level=1)
business_rules = [
    ("BR-01", "Division and branch scope is enforced by the API; hiding a menu item is never sufficient authorization."),
    ("BR-02", "An inactive division, branch, service, category, asset or account cannot be selected for a new transaction."),
    ("BR-03", "One asset cannot have overlapping blocking bookings, maintenance, inspection or transfer periods."),
    ("BR-04", "A meter reading normally cannot decrease; correction requires authority, reason and audit history."),
    ("BR-05", "A required operator/driver must be qualified, available and assigned before the relevant booking is confirmed or checked out."),
    ("BR-06", "A high-value or exception transaction follows its configured approval stages in order."),
    ("BR-07", "The creator cannot approve their own restricted transaction when separation-of-duties configuration applies."),
    ("BR-08", "A signed agreement and completed signed inspection are immutable records."),
    ("BR-09", "An asset with a safety-critical failed inspection or open blocking maintenance job cannot be Available or checked out."),
    ("BR-10", "Return completion derives the next asset state from inspection and maintenance rules."),
    ("BR-11", "Customer-visible prices exclude internal cost, margin, staff comments and restricted asset/customer fields."),
    ("BR-12", "Customer portal records are restricted to the authenticated customer's individual or linked corporate account."),
    ("BR-13", "Tax, deposits, rate precedence, minimum hire, excess usage and after-hours rules are configuration-driven and versioned where used in a quote."),
    ("BR-14", "Financial and profitability values are only as complete as recorded revenue and cost entries; incomplete data must be visibly identified."),
    ("BR-15", "Production import requires approved mapping, corrected source data, staging reconciliation and written authorization."),
]
add_table(doc, ["ID", "Business rule"], business_rules, [0.8, 5.7], font_size=9.2)

doc.add_heading("6. Data requirements", level=1)
add_para(doc, "The logical data structure must preserve group, division and branch scope while allowing flexible attributes and checklists. Exact physical schema may evolve provided these data obligations and relationships remain traceable.")
add_table(doc, ["Data area", "Minimum information", "Key controls"], [
    ["Identity/access", "Users, roles, permissions, scope assignments, sessions, lifecycle state, login/security events", "Least privilege; revocation; secrets excluded from logs"],
    ["Organization", "Divisions, branches, division-branch relationships, services, calendars, contacts, terms", "Unique codes; effective active state; historical retention"],
    ["Customers", "Individual/corporate profile, contacts, identification, drivers, credit/business status, portal status", "Customer ownership; sensitive-field restriction; duplicate detection"],
    ["Assets", "Identifiers, category, attributes, division/branch/service, lifecycle, location, ownership, value, documents", "Approved identifier uniqueness; scope; status integrity"],
    ["Meters", "Asset, type, unit, reading, fuel, source, booking, timestamp, recorder", "Monotonic validation; correction audit"],
    ["Rental", "Request, quotation versions/lines, approvals, booking, items, charges, agreement, pickup/return", "Transactional consistency; immutable signed documents"],
    ["Inspections", "Template/version, stage, response, outcome, readings, evidence, damage map, signatures", "Version retained; required/critical rules; immutable completion"],
    ["Maintenance", "Job, fault, status, dates, parts, labour, contractor, costs, downtime, evidence, return-to-service", "Cost reconciliation; availability block; approval"],
    ["Finance", "Invoice, line items, payments, refunds, balances, customer/asset cost and revenue entries", "Authorization; audit; calculation precision"],
    ["Audit/notification", "Event, actor, time, entity, summary; message template, recipient, result, retry", "Append-only audit behaviour; no secret/sensitive content"],
], [1.15, 3.55, 1.8], font_size=8.5)

doc.add_heading("6.1 Client-supplied data mapping status", level=2)
add_para(doc, "The supplied stock lists contain 39 vehicle records and 79 Carptrac asset records. An anonymised 118-record mapping/import package has been prepared. Production identifiers have not been imported or committed to source control.")
add_callout(doc, "Import gate", "Carpenters must confirm the canonical asset identifier, branch/location mapping, Carptrac Sort and Make codebooks, the meaning of Type, and identified vehicle data anomalies before production import.", fill=PALE_YELLOW)

doc.add_heading("6.2 Retention and privacy", level=2)
for item in [
    "Collect only information required for rental, safety, maintenance, finance, access or audit purposes.",
    "Restrict identification, licence, contact, signature, financial and security-event information by role and purpose.",
    "Do not store API/SMTP secrets, passwords or raw authentication tokens in source control or logs.",
    "Carpenters must approve retention periods for agreements, inspections, photographs, invoices, audit records and login history.",
    "Deletion/anonymisation must not break statutory, financial, safety or audit retention obligations.",
]: add_bullet(doc, item)

doc.add_heading("7. External interfaces", level=1)
add_table(doc, ["Interface", "Requirement"], [
    ["Web user interface", "Responsive React interface for current Chrome and Edge; customer portal mobile-friendly; staff optimized for desktop/tablet with mobile field flows where specified."],
    ["REST API", "ASP.NET Core API using JSON, explicit authorization, validation, pagination/filtering and consistent error responses; no public administrative endpoint."],
    ["Database", "SQL Server accessed through controlled application services/EF Core migrations; application users do not receive direct database access."],
    ["Email service", "SMTP or approved email API for transactional notifications; configuration supplied through protected environment secrets."],
    ["QR scanner/camera", "Browser-supported QR scanning or manual code entry; scanned code resolves through authorization rather than embedding sensitive data."],
    ["Document generation", "PDF quotation/agreement/invoice outputs with stable references and authorized download/email delivery."],
    ["Future integrations", "Accounting, payment, telematics and object-storage integrations must use versioned APIs/webhooks and remain outside MVP until approved."],
], [1.35, 5.15], font_size=9.2)

doc.add_heading("8. Non-functional requirements", level=1)
nfrs = [
    ("NFR-PERF-01", "Ordinary authenticated API/page interactions must complete within 2 seconds for at least 95% of requests under the agreed pilot workload.", "Performance test report"),
    ("NFR-PERF-02", "Standard reports should complete within 5 seconds for the agreed pilot dataset; long exports must show progress or run asynchronously.", "Report timing tests"),
    ("NFR-PERF-03", "List APIs must use server-side pagination, bounded filtering and database indexes for common scope/status/date searches.", "API and query review"),
    ("NFR-SEC-01", "Every non-public API endpoint must default to authenticated, explicitly authorized access.", "Endpoint authorization inventory and negative tests"),
    ("NFR-SEC-02", "No cross-division, cross-branch or cross-customer data leakage is permitted.", "Isolation test suite: zero leakage"),
    ("NFR-SEC-03", "Cookie-authenticated state-changing requests must be protected against CSRF and session fixation.", "Security tests"),
    ("NFR-SEC-04", "Authentication and sensitive APIs must use rate limiting, lockout and generic error messages.", "Abuse/lockout tests"),
    ("NFR-SEC-05", "All production/staging transport must use HTTPS with TLS 1.2 or later.", "Deployment inspection"),
    ("NFR-SEC-06", "Secrets must be stored outside source control using approved secret/configuration mechanisms and rotated when exposed.", "Repository and configuration audit"),
    ("NFR-SEC-07", "Uploaded documents/images must validate type and size and be protected from unauthorized access; production design should support malware scanning and private storage.", "Upload security tests"),
    ("NFR-SEC-08", "Dependencies must be scanned before release with no unresolved Critical/High vulnerability accepted without documented approval.", "Dependency scan report"),
    ("NFR-SEC-09", "The solution must address applicable OWASP Top 10 risks including injection, broken access, XSS, CSRF and insecure upload/configuration.", "Security checklist and tests"),
    ("NFR-REL-01", "Critical multi-record operations such as checkout and return must be transactional and must not leave partial state.", "Failure-injection integration tests"),
    ("NFR-REL-02", "Database backup and restore must be rehearsed before production acceptance.", "Successful restore evidence"),
    ("NFR-REL-03", "Database migrations must have a tested apply path and documented recovery/rollback approach.", "Migration rehearsal"),
    ("NFR-REL-04", "No unresolved Critical or High defect may remain at final acceptance unless the client signs a documented exception.", "Defect register"),
    ("NFR-USE-01", "At least 80% of representative users must complete agreed core UAT tasks without facilitator assistance.", "Observed UAT results"),
    ("NFR-USE-02", "Navigation must provide role-relevant menus, breadcrumbs, clear queue counts, one primary action and plain-language statuses.", "Usability review/UAT"),
    ("NFR-USE-03", "Forms must provide field labels, validation messages, required markers and keyboard-accessible controls.", "Accessibility/usability checklist"),
    ("NFR-COMP-01", "Critical journeys must pass on agreed current Chrome and Edge versions and responsive target viewports.", "Cross-browser test results"),
    ("NFR-MAIN-01", "Code must avoid mutable global state, separate concerns, use reusable functions/services and follow repository conventions.", "Peer review checklist"),
    ("NFR-MAIN-02", "All merges to the protected branch require peer review and relevant automated tests.", "Repository protection/review evidence"),
    ("NFR-MAIN-03", "API, setup, configuration, operational and recovery documentation must be updated with implementation changes.", "Definition-of-Done audit"),
    ("NFR-SCALE-01", "A new division, branch, service, category, attribute, rate unit and inspection template must be addable without core module redesign.", "Configuration proof in staging"),
    ("NFR-SCALE-02", "The production sizing baseline must be confirmed with Carpenters for assets, customers, bookings, documents, concurrent users and retention volume.", "Approved capacity worksheet"),
    ("NFR-AUD-01", "Critical audit events must be append-only through normal application functions and retained for the approved period.", "Audit integrity and retention test"),
]
add_table(doc, ["ID", "Non-functional requirement", "Verification"], nfrs, [1.05, 4.15, 1.3], font_size=8.5)

doc.add_heading("9. Reporting requirements", level=1)
add_table(doc, ["Report / dashboard", "Minimum measures", "Authorized audience"], [
    ["Rental Officer dashboard", "Today's pickups/returns, inspections due, new requests, overdue hires and next action", "Rental Officer; Branch Manager"],
    ["Branch dashboard", "Availability, utilization, overdue, approvals, maintenance, revenue/cost/profit for assigned branch", "Branch Manager; authorized management"],
    ["Division dashboard", "Revenue, expenditure, utilization, downtime and asset profitability across division branches", "Authorized division/group management"],
    ["Asset profit and loss", "Hire/service revenue, maintenance/operator/transport/labour/fuel cost, gross profit and margin", "Finance; authorized managers"],
    ["Rental history", "Customer, asset, dates, branch, status, charges and agreement reference", "Scoped staff; customer sees own history"],
    ["Fleet/equipment utilization", "Available, reserved, on hire, inspection, maintenance, downtime and utilization percentage", "Operations and management"],
    ["Maintenance", "Jobs, due/overdue schedule, faults, downtime, parts/labour/contractor cost and repeat failures", "Maintenance and management"],
    ["Overdue rentals", "Customer/contact, asset, due date/time, days overdue, balance and follow-up status", "Rental/branch/finance staff"],
    ["Customer rental history", "Requests, quotes, rentals, incidents, invoices, payments and balances", "Scoped staff; customer sees own account"],
    ["Audit/security", "Critical actions and security events filtered by actor/date/entity/action", "Administrator/Super Administrator"],
], [1.55, 3.65, 1.3], font_size=8.5)

doc.add_heading("10. Acceptance and traceability", level=1)
doc.add_heading("10.1 Requirement acceptance", level=2)
add_para(doc, "A requirement is accepted only when its implementation is demonstrated in the agreed environment, its acceptance criterion passes, applicable authorization/isolation tests pass, and the result is recorded in the UAT or test evidence. A screen mock-up or partial backend endpoint alone does not constitute acceptance of an end-to-end requirement.")
doc.add_heading("10.2 Minimum end-to-end UAT scenarios", level=2)
uat = [
    ("UAT-01", "Individual customer browses Motors, signs in, requests a vehicle, receives quotation/confirmation and tracks status."),
    ("UAT-02", "Corporate Carptrac customer requests equipment requiring an operator, purchase order and approval."),
    ("UAT-03", "Rental officer allocates an available asset and completes pre-hire inspection, agreement signature and QR checkout."),
    ("UAT-04", "Branch manager approves an authorized high-value/exception transaction and cannot approve a prohibited own transaction."),
    ("UAT-05", "Operator views assignment, records safety/meter/timesheet data and remains limited to assigned scope."),
    ("UAT-06", "Rental officer processes return, compares inspections, records damage/charges and routes the asset to maintenance."),
    ("UAT-07", "Maintenance officer records work, costs and return-to-service; availability is blocked until approved."),
    ("UAT-08", "Finance officer generates invoice/payment and branch manager reviews scoped asset profitability."),
    ("UAT-09", "Unauthorized cross-branch, cross-division and cross-customer API requests are rejected."),
    ("UAT-10", "Session timeout, reset, revocation and security-event history behave as approved."),
]
add_table(doc, ["Scenario", "Expected journey"], uat, [0.9, 5.6], font_size=9.2)

doc.add_heading("10.3 Requirements traceability", level=2)
add_para(doc, "The project team will maintain a traceability register linking each approved requirement ID to design/component, implementation task, test case, test result, defect (if any) and client acceptance status. Deferred or changed requirements must retain their original ID and a recorded disposition.")

doc.add_heading("11. Open decisions requiring Carpenters confirmation", level=1)
decisions = [
    ("DEC-01", "Assets", "Which identifier is the official permanent asset number for Motors and Carptrac?", "Fleet/asset owners", "Before production import"),
    ("DEC-02", "Branches", "Are Suva Rona Street, Foster Road and Autofield Nakasi separate branches, depots or sub-locations?", "Operations", "Before organization setup approval"),
    ("DEC-03", "Carptrac taxonomy", "Provide definitions for all Sort and Make codes and confirm whether Type means model, capacity or subtype.", "Carptrac asset owner", "Before category/data approval"),
    ("DEC-04", "Vehicle data", "Confirm Stocked_Date meaning and correct missing/invalid registration, VIN and suspicious odometer values.", "Motors fleet owner", "Before import"),
    ("DEC-05", "Inspections", "Which checklist items are safety-critical, require photos, block checkout/availability or create maintenance/damage cases?", "Rental, HSE, maintenance", "Before template acceptance"),
    ("DEC-06", "Signatures", "Confirm required signatories for each vehicle/equipment delivery and return scenario.", "Rental operations", "Before agreement/inspection acceptance"),
    ("DEC-07", "Fuel/meters", "Confirm fuel scale and approved primary/secondary meter types by category.", "Operations/maintenance", "Before meter acceptance"),
    ("DEC-08", "Pricing", "Provide authoritative rates, tax treatment, deposits, discounts, minimum periods, excess usage and approval thresholds.", "Finance/management", "Before quotation acceptance"),
    ("DEC-09", "Security", "Confirm session idle/absolute timeouts, MFA policy and authorized customer/staff retention periods.", "IT/security", "Before security acceptance"),
    ("DEC-10", "Capacity", "Confirm expected asset/customer/booking/document volumes and concurrent users for production sizing.", "IT/management", "Before deployment design"),
    ("DEC-11", "Email", "Confirm production sender domain, service, recipients and retry/escalation policy.", "IT/communications", "Before email acceptance"),
    ("DEC-12", "Hosting", "Confirm staging/production hosting ownership, backup location, recovery objectives and support handover model.", "IT/management", "Before deployment"),
]
add_table(doc, ["ID", "Area", "Decision required", "Owner", "Required by"], decisions, [0.7, 1.0, 3.0, 1.05, 0.75], font_size=8.2)
add_callout(doc, "Sign-off treatment", "Open decisions do not authorize the CREMS team to invent business rules. They remain controlled clarifications. Where implementation must begin earlier, the team will use an explicitly documented provisional assumption and return it for approval.", fill=PALE_YELLOW)

doc.add_heading("12. Change control", level=1)
for step in [
    "Requestor records the proposed change, business reason, urgency and affected requirement IDs.",
    "CREMS team assesses scope, design, data, security, testing, schedule and resource impact.",
    "Client sponsor and project/academic authority approve, reject or defer the change.",
    "Approved change receives a new document version, traceability update and revised acceptance criteria.",
    "Development begins only after approval unless the change is an urgent security correction within existing authority.",
]: add_number(doc, step)
add_para(doc, "Verbal discussion does not change the signed baseline until the decision is recorded and approved.", bold=True, color=RED)

doc.add_heading("13. Client sign-off", level=1)
add_para(doc, "By signing below, the parties confirm that this document accurately represents the requirements baseline for the CREMS Motors and Carptrac MVP. Sign-off authorizes the project team to proceed against this baseline and subjects later additions, removals or material changes to change control. Sign-off does not state that development is complete or that final acceptance has occurred.")
add_table(doc, ["Approval statement", "Status / comment"], [
    ["In-scope divisions and exclusions are correct.", ""],
    ["User classes, access boundaries and security requirements are acceptable.", ""],
    ["Functional requirements and priorities represent the agreed MVP.", ""],
    ["Supplied stock-list mappings and inspection templates may proceed to detailed validation, subject to open decisions.", ""],
    ["Non-functional requirements and UAT approach are acceptable.", ""],
    ["Open decisions and responsible owners are acknowledged.", ""],
], [4.75, 1.75], font_size=9.2)

add_page_break(doc)
doc.add_heading("13.1 Signatures", level=2)
signature_rows = [
    ["Client Sponsor - Carpenters Fiji", "Amit Kumar", "________________________", "____________"],
    ["Motors Representative", "", "________________________", "____________"],
    ["Carptrac Representative", "", "________________________", "____________"],
    ["Client IT/Security Representative", "", "________________________", "____________"],
    ["Academic Supervisor", "Dr Ravneil Nand", "________________________", "____________"],
    ["Project Leader", "Kavish Chandra", "________________________", "____________"],
]
add_table(doc, ["Role", "Name", "Signature", "Date"], signature_rows, [2.0, 1.5, 2.0, 1.0], font_size=9.2)

doc.add_heading("Appendix A - Requirements summary", level=1)
counts = []
for heading, reqs in functional_sections:
    must = sum(1 for r in reqs if r[3] == "Must")
    should = sum(1 for r in reqs if r[3] == "Should")
    counts.append([heading.split(" ", 1)[1], len(reqs), must, should])
counts.append(["Non-functional requirements", len(nfrs), len(nfrs), 0])
add_table(doc, ["Area", "Total", "Must", "Should"], counts, [3.6, 0.95, 0.95, 1.0], font_size=9.2,
          alignments=[None, WD_ALIGN_PARAGRAPH.CENTER, WD_ALIGN_PARAGRAPH.CENTER, WD_ALIGN_PARAGRAPH.CENTER])
add_para(doc, "Detailed totals are informational; the signed priority in each requirement row controls.", italic=True, color=MID_GRAY, size=9.3)

# Core properties and update fields.
doc.core_properties.title = "CREMS Software Requirements Specification"
doc.core_properties.subject = "Client sign-off baseline for Carpenters Motors and Carptrac MVP"
doc.core_properties.author = "CREMS Project Team"
doc.core_properties.keywords = "CREMS, requirements, Carpenters Fiji, Motors, Carptrac, sign-off"
settings = doc.settings._element
update = settings.find(qn("w:updateFields"))
if update is None:
    update = OxmlElement("w:updateFields")
    settings.append(update)
update.set(qn("w:val"), "true")

doc.save(OUT)
print(OUT)
