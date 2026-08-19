from pathlib import Path
import xml.etree.ElementTree as ET

OUT = Path('/Users/kavishchandra/Documents/CS400/output/diagrams/CREMS_Client_DFD.drawio')

BLACK='#111111'; YELLOW='#FFEA00'; PALE='#FFF9CC'; LIGHT='#F5F5F5'; GRAY='#626262'; BORDER='#B3B3B3'; WHITE='#FFFFFF'

def cell(root, id, value='', style='', vertex=False, edge=False, parent='1', x=0,y=0,w=0,h=0, source=None,target=None, relative=False):
    attrs={'id':id,'value':value,'style':style,'parent':parent}
    if vertex: attrs['vertex']='1'
    if edge: attrs['edge']='1'
    if source: attrs['source']=source
    if target: attrs['target']=target
    c=ET.SubElement(root,'mxCell',attrs)
    gattrs={'as':'geometry'}
    if relative:gattrs.update({'relative':'1'})
    else:gattrs.update({'x':str(x),'y':str(y),'width':str(w),'height':str(h)})
    ET.SubElement(c,'mxGeometry',gattrs)
    return c

def base(name,id):
    d=ET.Element('diagram',{'id':id,'name':name})
    m=ET.SubElement(d,'mxGraphModel',{'dx':'1920','dy':'1080','grid':'1','gridSize':'10','guides':'1','tooltips':'1','connect':'1','arrows':'1','fold':'1','page':'1','pageScale':'1','pageWidth':'1920','pageHeight':'1080','math':'0','shadow':'0'})
    r=ET.SubElement(m,'root'); ET.SubElement(r,'mxCell',{'id':'0'}); ET.SubElement(r,'mxCell',{'id':'1','parent':'0'})
    return d,r

title_style=f'text;html=1;align=center;verticalAlign=middle;fontSize=28;fontStyle=1;fontColor={BLACK};'
subtitle_style=f'text;html=1;align=center;verticalAlign=middle;fontSize=14;fontColor={GRAY};'
entity_style=f'rounded=0;whiteSpace=wrap;html=1;fillColor={LIGHT};strokeColor={BLACK};strokeWidth=2;fontSize=14;fontStyle=1;'
process_style=f'ellipse;whiteSpace=wrap;html=1;fillColor={YELLOW};strokeColor={BLACK};strokeWidth=2;fontSize=15;fontStyle=1;'
store_style=f'shape=partialRectangle;whiteSpace=wrap;html=1;left=0;right=0;fillColor={WHITE};strokeColor={BLACK};strokeWidth=2;fontSize=13;fontStyle=1;spacingLeft=12;spacingRight=12;'
edge_style=f'edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;endArrow=block;endFill=1;strokeColor={GRAY};fontSize=11;labelBackgroundColor={WHITE};'
event_edge_style=f'edgeStyle=orthogonalEdgeStyle;rounded=0;html=1;endArrow=block;endFill=1;dashed=1;strokeColor=#E8A302;fontSize=11;labelBackgroundColor={WHITE};'

def edge(r,id,source,target,label,style=edge_style,parent='1'):
    return cell(r,id,label,style,edge=True,parent=parent,source=source,target=target,relative=True)

# PAGE 1 - CONTEXT
d0,r=base('Level 0 - Context Diagram','dfd-context')
cell(r,'t0','CREMS Data Flow Diagram - Level 0',title_style,True,x=520,y=25,w=880,h=45)
cell(r,'s0','Context view: information exchanged between CREMS and its external stakeholders',subtitle_style,True,x=500,y=70,w=920,h=28)
cell(r,'p0','0.0<br><b>Carpenters Rental and Equipment<br>Management System</b>',process_style,True,x=705,y=330,w=510,h=260)
entities=[
('eCustomer','E1<br><b>Customer</b>',90,185,250,95),
('eRental','E2<br><b>Rental Staff</b>',90,600,250,95),
('eManager','E3<br><b>Branch and Division Management</b>',1535,180,300,100),
('eMaint','E4<br><b>Maintenance Team</b>',1535,400,300,95),
('eFinance','E5<br><b>Finance Team</b>',1535,620,300,95),
('eEmail','E6<br><b>Email Service</b>',810,855,300,90),
]
for i,v,x,y,w,h in entities:cell(r,i,v,entity_style,True,x=x,y=y,w=w,h=h)
flows=[
('c1','eCustomer','p0','Registration, search and booking request'),('c2','p0','eCustomer','Availability, quotation and booking status'),
('c3','eCustomer','p0','Acceptance, signature and payment details'),('c4','p0','eCustomer','Agreement, invoice and notifications'),
('c5','eRental','p0','Customer, booking, pickup and return data'),('c6','p0','eRental','Work queues, asset availability and hire status'),
('c7','eManager','p0','Approvals and report requests'),('c8','p0','eManager','Exceptions, performance and profitability reports'),
('c9','eMaint','p0','Maintenance work, parts and costs'),('c10','p0','eMaint','Fault referrals and service schedules'),
('c11','eFinance','p0','Payments, refunds and adjustments'),('c12','p0','eFinance','Invoices, balances and financial reports'),
('c13','p0','eEmail','Messages for delivery'),('c14','eEmail','p0','Delivery status'),
]
for vals in flows:edge(r,*vals)
cell(r,'note0','<b>Purpose</b><br>This context diagram confirms who exchanges information with CREMS. Internal processing and data storage are expanded on the Level 1 page.',f'rounded=1;whiteSpace=wrap;html=1;fillColor={PALE};strokeColor=#E8A302;fontSize=12;align=left;spacing=12;',True,x=510,y=970,w=900,h=70)

# PAGE 2 - LEVEL 1
d1,r=base('Level 1 - Main Processes','dfd-level1')
cell(r,'t1','CREMS Data Flow Diagram - Level 1',title_style,True,x=520,y=20,w=880,h=45)
cell(r,'s1','Main business processes, data stores and stakeholder information flows',subtitle_style,True,x=500,y=64,w=920,h=28)

# external entities
for vals in [
('customer','E1<br><b>Customer</b>',35,185,210,80),('staff','E2<br><b>Rental Staff</b>',35,475,210,80),
('management','E3<br><b>Management</b>',1675,190,210,80),('maintenanceTeam','E4<br><b>Maintenance Team</b>',1675,445,210,80),
('financeTeam','E5<br><b>Finance Team</b>',1675,700,210,80),('emailService','E6<br><b>Email Service</b>',850,965,220,70)]:
    i,v,x,y,w,h=vals;cell(r,i,v,entity_style,True,x=x,y=y,w=w,h=h)

# processes
processes=[
('access','1.0<br><b>Manage Accounts<br>and Access</b>',320,125),('booking','2.0<br><b>Manage Booking<br>and Quotation</b>',700,125),('hire','3.0<br><b>Manage Pickup,<br>Hire and Return</b>',1080,125),
('asset','4.0<br><b>Track Assets<br>and Usage</b>',320,515),('maint','5.0<br><b>Manage<br>Maintenance</b>',700,515),('finance','6.0<br><b>Manage Finance<br>and Reporting</b>',1080,515),
('notify','7.0<br><b>Send<br>Notifications</b>',700,790),
]
for i,v,x,y in processes:cell(r,i,v,process_style,True,x=x,y=y,w=255,h=125)

# stores top/middle bottom
stores=[
('dUsers','D1  Users and Access',310,330,275,58),('dCustomers','D2  Customer Records',615,330,275,58),('dBookings','D3  Bookings and Quotations',920,330,300,58),('dRentals','D4  Agreements and Inspections',1250,330,310,58),
('dAssets','D5  Assets, Status and Meters',305,710,300,58),('dMaintenance','D6  Maintenance Records',635,710,300,58),('dFinance','D7  Invoices and Payments',965,710,300,58),('dAudit','D8  Audit and Notification History',1295,710,315,58),
]
for i,v,x,y,w,h in stores:cell(r,i,v,store_style,True,x=x,y=y,w=w,h=h)

# Stakeholder to process flows
for vals in [
('f1','customer','access','Credentials and profile'),('f2','access','customer','Account and session status'),
('f3','customer','booking','Search and booking request'),('f4','booking','customer','Availability and quotation'),
('f5','customer','hire','Acceptance and signature'),('f6','hire','customer','Agreement and hire status'),
('f7','staff','booking','Review, pricing and allocation'),('f8','booking','staff','Booking work queue'),
('f9','staff','hire','Pickup and return details'),('f10','hire','staff','Hire status and next action'),
('f11','staff','asset','Asset details and QR scan'),('f12','asset','staff','Availability and lifecycle history'),
('f13','management','booking','Approval decision'),('f14','finance','management','Dashboards and reports'),
('f15','maintenanceTeam','maint','Work, parts and costs'),('f16','maint','maintenanceTeam','Jobs and service schedule'),
('f17','financeTeam','finance','Payments and adjustments'),('f18','finance','financeTeam','Invoices, balances and reports'),
]:edge(r,*vals)

# process-store and inter-process flows
for vals in [
('f19','access','dUsers','Access records'),('f20','access','dCustomers','Customer profile'),
('f21','booking','dCustomers','Customer eligibility'),('f22','booking','dBookings','Request, quote and approval'),('f23','booking','dAssets','Availability check'),
('f24','hire','dBookings','Confirmed booking'),('f25','hire','dRentals','Agreement and inspections'),('f26','hire','dAssets','Status and meter updates'),
('f27','asset','dAssets','Asset and lifecycle records'),('f28','asset','dAudit','Lifecycle event'),
('f29','hire','maint','Fault or damage referral'),('f30','maint','dAssets','Asset and meter details'),('f31','maint','dMaintenance','Job, parts and costs'),('f32','maint','dAssets','Availability update'),
('f33','hire','finance','Charges and return outcome'),('f34','finance','dBookings','Rental revenue'),('f35','finance','dMaintenance','Maintenance expense'),('f36','finance','dFinance','Invoices and payments'),
('f37','booking','notify','Booking event'),('f38','hire','notify','Agreement or return event'),('f39','finance','notify','Invoice event'),('f40','notify','dAudit','Message and delivery record'),
]:edge(r,*vals)

edge(r,'f41','notify','emailService','Email message',event_edge_style)
edge(r,'f42','emailService','notify','Delivery status',event_edge_style)
edge(r,'f43','notify','customer','Confirmation and reminder',event_edge_style)

cell(r,'legend','<b>DFD notation</b>&nbsp;&nbsp; Rectangle = external entity&nbsp;&nbsp;&nbsp; Yellow ellipse = CREMS process&nbsp;&nbsp;&nbsp; Open rectangle = data store&nbsp;&nbsp;&nbsp; Arrow = data flow',f'rounded=1;whiteSpace=wrap;html=1;fillColor={PALE};strokeColor=#E8A302;fontSize=12;align=center;',True,x=430,y=1040,w=1060,h=35)

mx=ET.Element('mxfile',{'host':'app.diagrams.net','modified':'2026-08-13T03:00:00.000Z','agent':'Codex','version':'24.7.17','type':'device','compressed':'false'})
mx.append(d0);mx.append(d1)
ET.indent(mx,space='  ')
OUT.write_bytes(ET.tostring(mx,encoding='utf-8',xml_declaration=True))
print(OUT)
