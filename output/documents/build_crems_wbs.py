from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.section import WD_SECTION, WD_ORIENT
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT, WD_TABLE_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT = Path('/Users/kavishchandra/Documents/CS400')
OUT = ROOT / 'output/documents/CREMS_Work_Breakdown_Structure.docx'
DIAGRAM = ROOT / 'output/documents/crems_wbs_diagram.png'
LOGO = ROOT / 'src/crems-web/public/brand/carpenters-logo.png'

YELLOW = 'FFEA00'; BLACK = '111111'; CHARCOAL = '2C2C2C'; GRAY = '626262'
LIGHT = 'F5F5F2'; PALE = 'FFF9CC'; WHITE = 'FFFFFF'; BORDER = 'D7D7D2'

groups = [
('1.1', 'Project\nManagement', ['Requirements baseline','Project plan','Sprint management','Risk & change control']),
('1.2', 'Analysis &\nDesign', ['Process models','System architecture','Database design','UI & wireframes']),
('1.3', 'Core\nPlatform', ['Authentication','Roles & permissions','Divisions & branches','Security, audit & email']),
('1.4', 'Asset\nManagement', ['Asset register','Categories & attributes','QR, meters & inspections','Lifecycle & profitability']),
('1.5', 'Customer\nManagement', ['Registration','Individual accounts','Corporate accounts','Customer portal']),
('1.6', 'Rental\nManagement', ['Availability & requests','Pricing & quotations','Approvals','Pickup, active hire & return']),
('1.7', 'Maintenance\nManagement', ['Maintenance requests','Preventive maintenance','Maintenance costs','Return to service']),
('1.8', 'Finance &\nReporting', ['Invoices & payments','Role dashboards','Operational reports','Financial reports']),
('1.9', 'Testing &\nAcceptance', ['Unit & API testing','Security testing','System testing','User acceptance testing']),
('1.10', 'Deployment &\nClosure', ['Production environment','Data preparation','Documentation & training','Handover & sign-off']),
]

rows = [
('1.0','CREMS Project','Secure, modular rental, asset-tracking and maintenance-management system for Carpenters Motors and Carptrac.','Approved CREMS solution'),
('1.1','Project Management','Management and control of project scope, time, risks and changes.','Approved plans and control records'),
('1.1.1','Requirements baseline','Confirm functional, non-functional and business requirements with the client.','Validated requirements specification'),
('1.1.2','Project management plan','Document scope, WBS, schedule, resources, communication, quality and risks.','Approved project management plan'),
('1.1.3','Sprint management','Plan, review and record weekly incremental work.','Sprint plans, reviews and logs'),
('1.1.4','Risk and change control','Identify risks, manage issues and control requested scope changes.','Risk, issue and change registers'),
('1.2','Analysis and System Design','Business, data, technical and interface designs for the proposed solution.','Approved system design package'),
('1.2.1','Business-process models','Model rental, inspection, approval, return and maintenance workflows.','Use cases and DFDs'),
('1.2.2','System architecture','Define the React, ASP.NET Core API and SQL Server architecture.','Architecture diagram and specification'),
('1.2.3','Database design','Define entities, relationships, constraints and data-isolation rules.','ERD and normalized schema'),
('1.2.4','Interface design','Design responsive public, customer and role-specific staff interfaces.','Validated wireframes'),
('1.3','Core Platform','Secure platform services shared by all CREMS modules.','Operational platform foundation'),
('1.3.1','Authentication','Provide staff and customer login, recovery and secure sessions.','Authentication service'),
('1.3.2','Roles and permissions','Control role, permission, division and branch access.','Access-control framework'),
('1.3.3','Organizational structure','Configure divisions, branches, services and staff scopes.','Organization configuration module'),
('1.3.4','Security and audit','Protect APIs, enforce timeouts and record critical activity.','Security controls and audit trail'),
('1.3.5','Communication services','Deliver secure email notifications and track delivery.','Notification service and templates'),
('1.4','Asset Management','Complete operational and financial register for rentable assets.','Asset-management module'),
('1.4.1','Asset register','Record identification, ownership, location, status, photographs and documents.','Searchable asset register'),
('1.4.2','Categories and attributes','Configure fields for vehicles, forklifts, excavators and future asset types.','Flexible category builder'),
('1.4.3','QR identification','Generate and scan QR codes for rapid asset identification.','QR identification workflow'),
('1.4.4','Inspections and meters','Capture inspections, odometers, operating hours and fuel readings.','Inspection and meter history'),
('1.4.5','Asset lifecycle','Track commissioning, availability, hire, maintenance and retirement.','Asset lifecycle history'),
('1.4.6','Asset profitability','Calculate utilization, revenue, cost, profit and cost per usage unit.','Asset performance dashboard'),
('1.5','Customer Management','Individual and corporate customer records with controlled portal access.','Customer-management module'),
('1.5.1','Customer registration','Capture contact, identification, eligibility and account status.','Verified customer profile'),
('1.5.2','Individual customers','Provide a simple vehicle-rental customer experience.','Individual customer workflow'),
('1.5.3','Corporate customers','Support contacts, purchase orders, credit controls and negotiated rates.','Corporate account controls'),
('1.5.4','Customer portal','Provide bookings, quotations, agreements, invoices and status tracking.','Authenticated customer portal'),
('1.6','Rental Management','End-to-end workflow from availability search to completed return.','Rental-management module'),
('1.6.1','Availability management','Detect booking, maintenance, inspection and allocation conflicts.','Availability calendar and validation'),
('1.6.2','Booking requests','Review, allocate, confirm, cancel and track customer requests.','Booking work queues'),
('1.6.3','Pricing and quotations','Calculate rates, operator, transport, deposit, discount and VAT.','Versioned customer quotations'),
('1.6.4','Approval workflows','Configure stages, limits, approvers, escalation and decisions.','Audited approval workflow'),
('1.6.5','Agreement and pickup','Verify requirements, inspect, sign, generate PDF and check out asset.','Signed agreement and pickup record'),
('1.6.6','Active hire operations','Monitor pickups, active hires, due dates, overdue items and incidents.','Hire operations workspace'),
('1.6.7','Return processing','Inspect, assess charges, invoice and determine the asset’s next status.','Completed return and final invoice'),
('1.7','Maintenance Management','Preventive and corrective maintenance across all supported assets.','Maintenance-management module'),
('1.7.1','Maintenance requests','Register faults, damage and maintenance jobs.','Prioritized maintenance jobs'),
('1.7.2','Preventive maintenance','Schedule work by date, kilometres or operating hours.','Preventive schedules and reminders'),
('1.7.3','Maintenance expenditure','Capture parts, labour, suppliers, transport and contractor costs.','Detailed maintenance cost record'),
('1.7.4','Return to service','Verify completed work, downtime and operational readiness.','Approved return-to-service record'),
('1.8','Finance and Reporting','Financial transactions and management information.','Finance and reporting module'),
('1.8.1','Invoices and payments','Manage invoices, deposits, payments, refunds and balances.','Customer financial records'),
('1.8.2','Role dashboards','Provide relevant views for officers, managers, finance and administrators.','Role-specific dashboards'),
('1.8.3','Operational reports','Report rental history, utilization, maintenance and overdue rentals.','Operational report suite'),
('1.8.4','Financial reports','Report revenue, expenditure, asset profitability and margins.','Management financial reports'),
('1.9','Testing and Acceptance','Verification that the delivered system meets approved requirements.','Tested and accepted system'),
('1.9.1','Unit and API testing','Verify business rules, calculations and API behaviour.','Automated test results'),
('1.9.2','Security testing','Verify authentication, authorization, isolation and sessions.','Security test evidence'),
('1.9.3','System testing','Test complete booking, hire, return and maintenance workflows.','System test report'),
('1.9.4','User acceptance testing','Run client scenarios, record feedback and correct issues.','Client acceptance record'),
('1.10','Deployment and Closure','Release, documentation, training and formal project completion.','Deployed and handed-over system'),
('1.10.1','Production environment','Configure the frontend, API, database, email, secrets and backups.','Production-ready environment'),
('1.10.2','Data preparation','Prepare initial divisions, branches, users, assets and settings.','Validated initial dataset'),
('1.10.3','Documentation and training','Prepare user, administrator, installation and technical guides.','Documentation and training package'),
('1.10.4','Handover and sign-off','Demonstrate the system and deliver code and documents.','Final client sign-off'),
]

def font(size, bold=False):
    candidates = ['/System/Library/Fonts/Supplemental/Arial Bold.ttf' if bold else '/System/Library/Fonts/Supplemental/Arial.ttf',
                  '/System/Library/Fonts/Helvetica.ttc']
    for p in candidates:
        if Path(p).exists(): return ImageFont.truetype(p, size)
    return ImageFont.load_default()

def multiline_center(draw, xy, text, f, fill, spacing=4):
    box = draw.multiline_textbbox((0,0), text, font=f, align='center', spacing=spacing)
    w,h=box[2]-box[0],box[3]-box[1]
    draw.multiline_text((xy[0]-w/2,xy[1]-h/2),text,font=f,fill=fill,align='center',spacing=spacing)

def make_diagram():
    pc=lambda value: '#'+value
    W,H=3200,1800; img=Image.new('RGB',(W,H),'white'); d=ImageDraw.Draw(img)
    root=(760,65,2440,235); d.rounded_rectangle(root,26,fill=pc(YELLOW),outline=pc(BLACK),width=5)
    multiline_center(d,(1600,150),'1.0  CARPENTERS RENTAL AND EQUIPMENT\nMANAGEMENT SYSTEM (CREMS)',font(44,True),pc(BLACK),8)
    d.line((1600,235,1600,315),fill=pc(BLACK),width=5); d.line((170,315,3030,315),fill=pc(BLACK),width=5)
    top_y=355; box_w=280; gap=25
    for idx,(code,title,items) in enumerate(groups):
        x=25+idx*(box_w+gap); cx=x+box_w//2
        d.line((cx,315,cx,top_y),fill=pc(BLACK),width=4)
        d.rounded_rectangle((x,top_y,x+box_w,top_y+190),18,fill=pc(BLACK),outline=pc(BLACK),width=3)
        multiline_center(d,(cx,top_y+95),f'{code}\n{title}',font(28,True),pc(YELLOW),5)
        y=top_y+225
        for j,item in enumerate(items):
            d.line((cx,y-35,cx,y),fill=pc(GRAY),width=3)
            d.rounded_rectangle((x,y,x+box_w,y+185),14,fill=pc(PALE if j%2==0 else LIGHT),outline=pc(BORDER),width=3)
            multiline_center(d,(cx,y+92),f'{code}.{j+1}\n{item}',font(24,j==0),pc(BLACK),5)
            y+=225
    d.text((90,1710),'Figure 1. Deliverable-oriented CREMS Work Breakdown Structure',font=font(26,True),fill=pc(CHARCOAL))
    img.save(DIAGRAM,quality=95)

def shade(cell, fill):
    tcPr=cell._tc.get_or_add_tcPr(); shd=tcPr.find(qn('w:shd'))
    if shd is None: shd=OxmlElement('w:shd'); tcPr.append(shd)
    shd.set(qn('w:fill'),fill)

def set_cell_margins(cell, top=100, start=120, bottom=100, end=120):
    tc=cell._tc; tcPr=tc.get_or_add_tcPr(); tcMar=tcPr.first_child_found_in('w:tcMar')
    if tcMar is None: tcMar=OxmlElement('w:tcMar'); tcPr.append(tcMar)
    for m,v in [('top',top),('start',start),('bottom',bottom),('end',end)]:
        n=tcMar.find(qn('w:'+m))
        if n is None: n=OxmlElement('w:'+m); tcMar.append(n)
        n.set(qn('w:w'),str(v)); n.set(qn('w:type'),'dxa')

def set_repeat_header(row):
    trPr=row._tr.get_or_add_trPr(); e=OxmlElement('w:tblHeader'); e.set(qn('w:val'),'true'); trPr.append(e)

def set_table_widths(table, widths):
    table.autofit=False
    tblPr=table._tbl.tblPr; tblW=tblPr.find(qn('w:tblW'))
    if tblW is None: tblW=OxmlElement('w:tblW'); tblPr.append(tblW)
    total=sum(widths); tblW.set(qn('w:w'),str(total)); tblW.set(qn('w:type'),'dxa')
    tblInd=tblPr.find(qn('w:tblInd'))
    if tblInd is None: tblInd=OxmlElement('w:tblInd'); tblPr.append(tblInd)
    tblInd.set(qn('w:w'),'120'); tblInd.set(qn('w:type'),'dxa')
    grid=table._tbl.tblGrid
    for child in list(grid): grid.remove(child)
    for w in widths:
        col=OxmlElement('w:gridCol'); col.set(qn('w:w'),str(w)); grid.append(col)
    for row in table.rows:
        for i,cell in enumerate(row.cells):
            tcW=cell._tc.get_or_add_tcPr().find(qn('w:tcW'))
            if tcW is None: tcW=OxmlElement('w:tcW'); cell._tc.get_or_add_tcPr().append(tcW)
            tcW.set(qn('w:w'),str(widths[i])); tcW.set(qn('w:type'),'dxa'); set_cell_margins(cell)

def page_field(paragraph):
    paragraph.alignment=WD_ALIGN_PARAGRAPH.RIGHT
    r=paragraph.add_run('Page '); r.font.size=Pt(9); r.font.color.rgb=RGBColor(98,98,98)
    fld=OxmlElement('w:fldSimple'); fld.set(qn('w:instr'),'PAGE'); paragraph._p.append(fld)

def set_page_start(section, value):
    sectPr=section._sectPr; node=sectPr.find(qn('w:pgNumType'))
    if node is None: node=OxmlElement('w:pgNumType'); sectPr.append(node)
    node.set(qn('w:start'),str(value))

def add_text(cell,text,bold=False,color=BLACK,size=9):
    p=cell.paragraphs[0]; p.paragraph_format.space_after=Pt(0); p.paragraph_format.line_spacing=1.05
    r=p.add_run(text); r.bold=bold; r.font.name='Arial'; r.font.size=Pt(size); r.font.color.rgb=RGBColor.from_string(color)
    return p

make_diagram()
doc=Document(); sec=doc.sections[0]; sec.top_margin=Inches(.75);sec.bottom_margin=Inches(.7);sec.left_margin=Inches(.8);sec.right_margin=Inches(.8);sec.header_distance=Inches(.35);sec.footer_distance=Inches(.35)
styles=doc.styles
normal=styles['Normal'];normal.font.name='Arial';normal.font.size=Pt(10.5);normal.font.color.rgb=RGBColor.from_string(BLACK);normal.paragraph_format.space_after=Pt(6);normal.paragraph_format.line_spacing=1.10
for name,size,before,after in [('Heading 1',16,16,8),('Heading 2',13,12,6),('Heading 3',11,8,4)]:
    s=styles[name];s.font.name='Arial';s.font.size=Pt(size);s.font.bold=True;s.font.color.rgb=RGBColor.from_string(BLACK);s.paragraph_format.space_before=Pt(before);s.paragraph_format.space_after=Pt(after);s.paragraph_format.keep_with_next=True

# Cover
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_before=Pt(40)
p.add_run().add_picture(str(LOGO),width=Inches(1.0))
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_before=Pt(26);p.paragraph_format.space_after=Pt(7)
r=p.add_run('WORK BREAKDOWN STRUCTURE');r.font.name='Arial';r.font.size=Pt(26);r.bold=True;r.font.color.rgb=RGBColor.from_string(BLACK)
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_after=Pt(20)
r=p.add_run('Carpenters Rental and Equipment Management System (CREMS)');r.font.name='Arial';r.font.size=Pt(15);r.bold=True
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER
r=p.add_run('Carpenters Motors and Carptrac');r.font.name='Arial';r.font.size=Pt(12);r.font.color.rgb=RGBColor.from_string(GRAY)
call=doc.add_table(rows=1,cols=1);call.alignment=WD_TABLE_ALIGNMENT.CENTER;set_table_widths(call,[7200]);shade(call.cell(0,0),PALE)
add_text(call.cell(0,0),'Purpose: Define the complete project scope as a deliverable-oriented hierarchy and decompose it into manageable, traceable work packages.',False,BLACK,11).alignment=WD_ALIGN_PARAGRAPH.CENTER
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_before=Pt(30)
r=p.add_run('Prepared for the CS400 Industrial Experience Project\nSemester 2, 2026');r.font.name='Arial';r.font.size=Pt(11);r.font.color.rgb=RGBColor.from_string(GRAY)

doc.add_page_break()
doc.add_heading('1. Purpose and Structure',level=1)
doc.add_paragraph('This Work Breakdown Structure (WBS) defines the full scope required to design, build, test and hand over CREMS. It is organized by project deliverables rather than by calendar sequence. Each descending level gives a more detailed definition of the work, with the lowest level representing manageable work packages.')
doc.add_paragraph('The structure follows established WBS principles: hierarchical decomposition, unique WBS identifiers, deliverable orientation and the 100% rule. Collectively, the elements below cover the complete approved project scope without intentionally duplicating work between packages.')
doc.add_heading('WBS Levels',level=2)
t=doc.add_table(rows=1,cols=3);t.alignment=WD_TABLE_ALIGNMENT.CENTER;t.style='Table Grid'
for i,h in enumerate(['Level','Meaning','CREMS example']): shade(t.cell(0,i),BLACK);add_text(t.cell(0,i),h,True,YELLOW,10)
for vals in [('Level 1','Complete project','1.0 CREMS Project'),('Level 2','Major project deliverable','1.4 Asset Management'),('Level 3','Manageable work package','1.4.4 Inspections and Meters')]:
    cells=t.add_row().cells
    for i,v in enumerate(vals):add_text(cells[i],v,i==0)
set_table_widths(t,[1200,3400,4760]);set_repeat_header(t.rows[0])
doc.add_heading('Scope Boundary',level=2)
sc=doc.add_table(rows=1,cols=1);sc.alignment=WD_TABLE_ALIGNMENT.CENTER;set_table_widths(sc,[9360]);shade(sc.cell(0,0),PALE)
add_text(sc.cell(0,0),'Current scope: Carpenters Motors vehicle rental and Carptrac equipment hire, including customer, asset, booking, quotation, approval, agreement, inspection, return, maintenance, finance and reporting functions. Carpenters Shipping is excluded from the present scope. The architecture must nevertheless support future divisions, services and asset types through configuration rather than extensive redevelopment.',False,BLACK,10)
doc.add_paragraph('Reference: Project Management Institute, “Developing and Elaborating Effective Work Breakdown Structures.”',style=None).runs[0].italic=True

# landscape diagram page
land=doc.add_section(WD_SECTION.NEW_PAGE);land.orientation=WD_ORIENT.LANDSCAPE;land.page_width=Inches(11);land.page_height=Inches(8.5);land.top_margin=Inches(.55);land.bottom_margin=Inches(.55);land.left_margin=Inches(.55);land.right_margin=Inches(.55)
set_page_start(land,3)
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_after=Pt(4);r=p.add_run('2. CREMS WBS Hierarchy');r.bold=True;r.font.name='Arial';r.font.size=Pt(18)
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_after=Pt(8);r=p.add_run('Major deliverables and their principal work packages');r.font.name='Arial';r.font.size=Pt(10);r.font.color.rgb=RGBColor.from_string(GRAY)
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.add_run().add_picture(str(DIAGRAM),width=Inches(9.65))

# portrait table
port=doc.add_section(WD_SECTION.NEW_PAGE);port.orientation=WD_ORIENT.PORTRAIT;port.page_width=Inches(8.5);port.page_height=Inches(11);port.top_margin=Inches(.65);port.bottom_margin=Inches(.65);port.left_margin=Inches(.6);port.right_margin=Inches(.6)
set_page_start(port,4)
doc.add_heading('3. Detailed Work Breakdown Structure',level=1)
doc.add_paragraph('Table 1 provides the WBS dictionary summary for each major deliverable and work package. The expected output defines the completion evidence for the corresponding element.')
table=doc.add_table(rows=1,cols=4);table.alignment=WD_TABLE_ALIGNMENT.CENTER;table.style='Table Grid'
headers=['WBS ID','Deliverable / Work Package','Scope Description','Expected Output']
for i,h in enumerate(headers):shade(table.cell(0,i),BLACK);add_text(table.cell(0,i),h,True,YELLOW,9)
set_repeat_header(table.rows[0])
for code,name,scope,output in rows:
    cells=table.add_row().cells;major=code.count('.')==1
    if major:
        for c in cells:shade(c,YELLOW if code=='1.0' else PALE)
    add_text(cells[0],code,True,BLACK,8.5)
    add_text(cells[1],name,major,BLACK,8.5)
    add_text(cells[2],scope,False,BLACK,8.5)
    add_text(cells[3],output,major,BLACK,8.5)
    for c in cells:c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER
set_table_widths(table,[850,2150,4000,2360])

doc.add_page_break()
doc.add_heading('4. WBS Application and Control',level=1)
doc.add_paragraph('The project schedule, sprint backlog, responsibility assignments, risk register and progress reports should reference these WBS identifiers. This maintains traceability from an approved requirement to its design, implementation, testing evidence and final deliverable.')
for title,text in [
('Scope control','New work must be mapped to an existing WBS package or processed as a formal scope change.'),
('Progress reporting','Progress should be measured at the work-package level using agreed completion evidence.'),
('Responsibility assignment','Each work package should have one accountable team member, with supporting members recorded separately.'),
('Acceptance','A work package is complete only after its expected output has been produced, reviewed and accepted where required.')]:
    p=doc.add_paragraph();p.paragraph_format.space_after=Pt(5);r=p.add_run(title+': ');r.bold=True;r.font.name='Arial';r2=p.add_run(text);r2.font.name='Arial'

# Header/footer
for s in doc.sections:
    s.header.is_linked_to_previous=False; s.footer.is_linked_to_previous=False
    hp=s.header.paragraphs[0];hp.text='CREMS | Work Breakdown Structure';hp.alignment=WD_ALIGN_PARAGRAPH.LEFT
    for r in hp.runs:r.font.name='Arial';r.font.size=Pt(8);r.font.color.rgb=RGBColor.from_string(GRAY)
    fp=s.footer.paragraphs[0]; fp.clear(); page_field(fp)

doc.core_properties.title='CREMS Work Breakdown Structure'
doc.core_properties.subject='Formal deliverable-oriented Work Breakdown Structure for the CREMS project'
doc.core_properties.author='CREMS Project Team'
doc.save(OUT)
print(OUT)
