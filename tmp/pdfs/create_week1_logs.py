from reportlab.lib.pagesizes import letter
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import mm
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak, KeepTogether
from pathlib import Path

OUT = Path('/Users/kavishchandra/Documents/CS400/output/pdf')
OUT.mkdir(parents=True, exist_ok=True)

COMMON = {
    'period': 'Week 1: 10-14 August 2026',
    'title': 'Car and Rental Equipment Management System (CREMS)',
    'supervisor': 'To be confirmed',
    'report': '1',
}

members = [
    {
        'name': 'Kavish Chandra', 'file': 'Kavish_Chandra_Week_1_Log.pdf',
        'planned': 'Coordinate the first client meeting, research suitable technology stacks, consolidate the initial proposal, and lead the team discussion used to confirm the project scope and semester plan.',
        'completed': 'I prepared the meeting agenda with the team and participated in the client discussion. I recorded the main business direction: the system must be flexible across Carpenters Fiji divisions, with Carpenters Rentals and Carptrac as the initial focus. I consolidated the agreed MVP scope, including rentals, maintenance, asset tracking, inspections, approvals, notifications, reporting and role-based access. I also documented the client emphasis on revenue, expenditure and profitability at individual asset level.',
        'progress': 'The team is progressing according to the Week 1 plan. The scope and business priorities are clearer, but they remain subject to written client confirmation. No development completion is being reported for this period.',
        'pending': 'Circulate the meeting summary, obtain confirmation of the scope and terminology, complete the requirements catalogue, define acceptance criteria, and coordinate the first database schema workshop.',
        'issues': 'The initial proposal was focused mainly on vehicle and equipment rental, while the client described a broader group-wide solution. I addressed this by separating the immediate MVP divisions from future configurable divisions and by recording assumptions that require confirmation.',
        'learning': 'I improved my stakeholder communication and requirements leadership. The meeting showed me that a technically possible feature is not automatically a confirmed business requirement. My SFIA autonomy is developing because I can coordinate defined tasks, while still seeking client and supervisor validation for decisions that affect scope. I need to improve estimation and the traceability between meeting statements, requirements and acceptance criteria.',
        'peer': 'I shared the meeting structure and consolidated scope with the team. The other members reviewed the notes and challenged unclear assumptions, particularly around divisional access, customer accounts and maintenance costing.'
    },
    {
        'name': 'Shoneel Kumar', 'file': 'Shoneel_Kumar_Week_1_Log.pdf',
        'planned': 'Research the operational lifecycle of rentable vehicles and heavy equipment and prepare questions about asset information, maintenance, inspections, allocation and return procedures for the client meeting.',
        'completed': 'I contributed questions and notes concerning the asset lifecycle. I identified the need to capture asset identity, category, branch, division, availability, meter readings and maintenance history. During the client discussion, I focused on the requirement for pre-rental and post-rental checklists and the need to record detailed maintenance expenditure. I helped distinguish vehicle use measured in kilometres from equipment use measured in operating hours.',
        'progress': 'My assigned Week 1 requirements tasks were completed as planned. The lifecycle has been outlined at a business level, but detailed checklist fields and maintenance approval rules still require client confirmation.',
        'pending': 'Draft the asset and maintenance requirements, propose lifecycle states, list required inspection evidence, and prepare questions about service intervals, damage responsibility, warranty work and disposal.',
        'issues': 'Different asset categories do not follow exactly the same workflow. A vehicle, forklift, generator and container may use different meters, inspections and maintenance triggers. I addressed this by proposing configurable asset categories and meter types instead of assuming one vehicle-only process.',
        'learning': 'I learned to model requirements around business events rather than around one screen. My SFIA complexity level is developing because I can analyse several related workflows with guidance. I need to improve how I validate exceptions and ensure that proposed lifecycle states use the language followed by Carpenters staff.',
        'peer': 'I contributed the asset and maintenance perspective to the team discussion. Team members helped connect these requirements to finance, customer booking and access-control needs.'
    },
    {
        'name': 'Sudhansu Jayshil Kisun', 'file': 'Sudhansu_Jayshil_Kisun_Week_1_Log.pdf',
        'planned': 'Investigate user roles, data security, divisional access and reporting needs, and prepare client questions about who can view, approve and modify information.',
        'completed': 'I reviewed the proposed Administrator, Branch Manager and Rental Officer roles and contributed to the discussion on division and branch restrictions. I documented the client security expectations for API timeouts, session expiry and controlled browser sessions. I also recorded reporting requirements for asset revenue, operating expenses, maintenance costs, overdue rentals and audit history.',
        'progress': 'The Week 1 role and security requirements have been identified at a high level. Detailed permission matrices, session values and report definitions remain pending because they require business and technical approval.',
        'pending': 'Prepare a role-permission matrix, identify sensitive data fields, define audit events, document session and API security requirements, and clarify whether future division-level management roles are required.',
        'issues': 'Some role names describe job titles, but access also depends on branch and division. I addressed this by documenting role and data scope as separate concepts. This will allow the same role to have different permitted records without creating many hard-coded roles.',
        'learning': 'I developed my understanding of least-privilege access and requirements traceability. My SFIA influence is currently team-focused: I can explain security concerns and support decisions, but security settings must be reviewed by the supervisor and client. I need to improve threat identification and write more measurable non-functional requirements.',
        'peer': 'I presented the access and security considerations to the group. The team reviewed how these controls would affect customer, rental, maintenance and reporting workflows.'
    },
    {
        'name': 'Rahul Chand', 'file': 'Rahul_Chand_Week_1_Log.pdf',
        'planned': 'Research the customer journey and prepare questions covering enquiries, availability, quotations, booking, agreements, notifications and booking-status tracking.',
        'completed': 'I contributed to mapping the customer journey from browsing available services through enquiry, quotation and booking. I helped document that customers may be individuals or corporate accounts and may hire from different divisions. I recorded requirements for booking confirmation, reference-number tracking, rental agreements, email notifications and a simple experience for customers who only need a vehicle.',
        'progress': 'The main customer journey is progressing according to the Week 1 plan. Important decisions remain open, including which services can be booked immediately, which require a quotation, and what information corporate customers must provide.',
        'pending': 'Create customer journey diagrams, draft enquiry and booking data requirements, clarify quotation acceptance and cancellation rules, and list notification events and customer-facing status descriptions.',
        'issues': 'A single portal could overwhelm ordinary vehicle-rental customers when shipping and heavy-equipment services are also present. I addressed this by proposing division and service selection first, followed by a guided workflow that reveals only relevant fields.',
        'learning': 'I learned that user-friendly design begins during requirements gathering. My SFIA business-skills level is developing because I can relate customer needs to process requirements and communicate them to the team. I need to improve the way I validate usability assumptions with real users and turn journeys into testable acceptance criteria.',
        'peer': 'I contributed the customer perspective and reviewed it with the team. Other members connected the customer journey to asset availability, security, approvals and financial reporting.'
    },
]

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name='TitleCenter', parent=styles['Title'], alignment=TA_CENTER, fontName='Helvetica-Bold', fontSize=16, leading=20, textColor=colors.HexColor('#111111')))
styles.add(ParagraphStyle(name='Section', parent=styles['Heading2'], fontName='Helvetica-Bold', fontSize=10.5, leading=14, textColor=colors.HexColor('#111111'), spaceBefore=7, spaceAfter=3))
styles.add(ParagraphStyle(name='BodySmall', parent=styles['BodyText'], fontSize=9.2, leading=13, spaceAfter=4))
styles.add(ParagraphStyle(name='Small', parent=styles['BodyText'], fontSize=8, leading=10))

def footer(canvas, doc):
    canvas.saveState()
    canvas.setStrokeColor(colors.HexColor('#ffdf00')); canvas.setLineWidth(2); canvas.line(18*mm, 15*mm, 198*mm, 15*mm)
    canvas.setFont('Helvetica', 8); canvas.setFillColor(colors.HexColor('#555555'))
    canvas.drawString(18*mm, 10*mm, 'CS400 Industry Experience Project - Semester 2, 2026')
    canvas.drawRightString(198*mm, 10*mm, f'Page {doc.page}')
    canvas.restoreState()

def make(member):
    path = OUT / member['file']
    doc = SimpleDocTemplate(str(path), pagesize=letter, rightMargin=18*mm, leftMargin=18*mm, topMargin=16*mm, bottomMargin=20*mm)
    story = [Paragraph('CS400 PROJECT UPDATE REPORT LOG', styles['TitleCenter']), Paragraph('Week 1 - Requirements Gathering and Scope Finalisation', ParagraphStyle(name='Sub', parent=styles['Heading2'], alignment=TA_CENTER, fontSize=11, textColor=colors.HexColor('#555555'))), Spacer(1, 7)]
    info = [[Paragraph('<b>Student Name / ID</b>', styles['Small']), Paragraph(f"{member['name']} / __________________", styles['Small'])], [Paragraph('<b>Project Title</b>', styles['Small']), Paragraph(COMMON['title'], styles['Small'])], [Paragraph('<b>Supervisor</b>', styles['Small']), Paragraph(COMMON['supervisor'], styles['Small'])], [Paragraph('<b>Report Number</b>', styles['Small']), Paragraph(COMMON['report'], styles['Small'])], [Paragraph('<b>Reporting Period</b>', styles['Small']), Paragraph(COMMON['period'], styles['Small'])]]
    table = Table(info, colWidths=[42*mm, 132*mm]); table.setStyle(TableStyle([('GRID',(0,0),(-1,-1),.6,colors.HexColor('#333333')),('BACKGROUND',(0,0),(0,-1),colors.HexColor('#f2f2ef')),('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),6),('RIGHTPADDING',(0,0),(-1,-1),6),('TOPPADDING',(0,0),(-1,-1),5),('BOTTOMPADDING',(0,0),(-1,-1),5)])); story += [table, Spacer(1, 8)]
    sections = [('1. Planned tasks', member['planned']), ('2. Tasks completed during this period', member['completed']), ('3. Progress against plan', member['progress']), ('4. Pending tasks and next actions', member['pending']), ('5. Issues faced and how they were addressed', member['issues']), ('6. Learning journey and SFIA reflection', member['learning']), ('7. Peer contribution and review', member['peer'])]
    for h,b in sections: story += [Paragraph(h, styles['Section']), Paragraph(b, styles['BodySmall'])]
    story += [Spacer(1, 7), Paragraph('<b>Evidence available:</b> client meeting notes, agreed scope list, team planning records, requirements questions and Carpenters division research.', styles['BodySmall']), PageBreak(), Paragraph('DECLARATION AND REVIEW', styles['TitleCenter']), Spacer(1, 12), Paragraph('I confirm that I have reviewed this report and that it represents my individual contribution and reflection for the reporting period.', styles['BodySmall']), Spacer(1, 14)]
    sign = [[Paragraph('<b>Signed (Student)</b>', styles['Small']), '', Paragraph('<b>Date</b>', styles['Small']), ''], [Paragraph('<b>Peer review comments</b>', styles['Small']), Paragraph('Peer reviewer to confirm that the contribution described above is consistent with the team records.', styles['Small']), '', ''], [Paragraph('<b>Peer reviewer name/signature</b>', styles['Small']), '', Paragraph('<b>Date</b>', styles['Small']), ''], [Paragraph('<b>Supervisor comments</b>', styles['Small']), '', '', ''], [Paragraph('<b>Signed (Supervisor)</b>', styles['Small']), '', Paragraph('<b>Date</b>', styles['Small']), '']]
    st = Table(sign, colWidths=[42*mm,74*mm,20*mm,38*mm], rowHeights=[16*mm,28*mm,16*mm,34*mm,16*mm]); st.setStyle(TableStyle([('GRID',(0,0),(-1,-1),.7,colors.black),('SPAN',(1,1),(3,1)),('SPAN',(1,3),(3,3)),('VALIGN',(0,0),(-1,-1),'TOP'),('BACKGROUND',(0,0),(0,-1),colors.HexColor('#f2f2ef')),('LEFTPADDING',(0,0),(-1,-1),6),('TOPPADDING',(0,0),(-1,-1),6)])); story.append(st)
    doc.build(story, onFirstPage=footer, onLaterPages=footer)
    return path

for member in members:
    print(make(member))
