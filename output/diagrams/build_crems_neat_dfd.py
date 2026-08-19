from pathlib import Path
import xml.etree.ElementTree as ET

OUT=Path('/Users/kavishchandra/Documents/CS400/output/diagrams/CREMS_Detailed_Neat_DFD.drawio')
B='#111111';Y='#FFEA00';P='#FFF9CC';W='#FFFFFF';L='#F5F5F5';G='#626262';BD='#B3B3B3'

def add(root,id,value,style,x,y,w,h):
    c=ET.SubElement(root,'mxCell',{'id':id,'value':value,'style':style,'vertex':'1','parent':'1'})
    ET.SubElement(c,'mxGeometry',{'x':str(x),'y':str(y),'width':str(w),'height':str(h),'as':'geometry'})
def edge(root,id,s,t,label,dashed=False):
    style=f'edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;endArrow=block;endFill=1;strokeColor={"#E8A302" if dashed else G};fontSize=11;labelBackgroundColor={W};'+('dashed=1;' if dashed else '')
    c=ET.SubElement(root,'mxCell',{'id':id,'value':label,'style':style,'edge':'1','parent':'1','source':s,'target':t})
    ET.SubElement(c,'mxGeometry',{'relative':'1','as':'geometry'})

mx=ET.Element('mxfile',{'host':'app.diagrams.net','modified':'2026-08-13T04:00:00.000Z','agent':'Codex','version':'24.7.17','type':'device','compressed':'false'})
d=ET.SubElement(mx,'diagram',{'id':'crems-neat-dfd','name':'CREMS Level 1 DFD'})
m=ET.SubElement(d,'mxGraphModel',{'dx':'1920','dy':'1080','grid':'1','gridSize':'10','guides':'1','tooltips':'1','connect':'1','arrows':'1','fold':'1','page':'1','pageScale':'1','pageWidth':'1920','pageHeight':'1080','math':'0','shadow':'0'})
r=ET.SubElement(m,'root');ET.SubElement(r,'mxCell',{'id':'0'});ET.SubElement(r,'mxCell',{'id':'1','parent':'0'})

title=f'text;html=1;align=center;verticalAlign=middle;fontSize=28;fontStyle=1;fontColor={B};'
sub=f'text;html=1;align=center;verticalAlign=middle;fontSize=14;fontColor={G};'
entity=f'rounded=0;whiteSpace=wrap;html=1;fillColor={L};strokeColor={B};strokeWidth=2;fontSize=13;fontStyle=1;'
proc=f'ellipse;whiteSpace=wrap;html=1;fillColor={Y};strokeColor={B};strokeWidth=2;fontSize=14;fontStyle=1;'
store=f'shape=partialRectangle;whiteSpace=wrap;html=1;left=0;right=0;fillColor={W};strokeColor={B};strokeWidth=2;fontSize=12;fontStyle=1;spacingLeft=10;spacingRight=10;'
band=f'rounded=1;whiteSpace=wrap;html=1;fillColor={P};strokeColor=#E8A302;fontSize=12;align=center;'

add(r,'title','CREMS Detailed Data Flow Diagram - Level 1',title,500,18,920,45)
add(r,'subtitle','Validated booking-to-return information flow for Carpenters Motors and Carptrac',sub,480,62,960,28)

# External entities
add(r,'customer','E1<br><b>Customer</b>',entity,35,165,205,78)
add(r,'rentalOfficer','E2<br><b>Rental Officer</b>',entity,35,440,205,78)
add(r,'fieldStaff','E3<br><b>Driver / Operator</b>',entity,35,730,205,78)
add(r,'manager','E4<br><b>Branch Manager</b>',entity,1680,160,205,78)
add(r,'maintenanceOfficer','E5<br><b>Maintenance Officer</b>',entity,1680,430,205,78)
add(r,'financeOfficer','E6<br><b>Finance / Management</b>',entity,1680,710,205,78)
add(r,'emailProvider','E7<br><b>Email Provider</b>',entity,850,940,220,68)

# Processes - coherent lifecycle
add(r,'account','1.0<br><b>Manage Customer<br>and Access</b>',proc,310,125,255,125)
add(r,'booking','2.0<br><b>Process Booking,<br>Pricing and Approval</b>',proc,700,125,275,125)
add(r,'pickup','3.0<br><b>Prepare Agreement<br>and Check Out Asset</b>',proc,1110,125,275,125)
add(r,'return','4.0<br><b>Inspect and<br>Process Return</b>',proc,1400,455,255,125)
add(r,'assetMaintenance','5.0<br><b>Track Assets<br>and Maintenance</b>',proc,970,455,275,125)
add(r,'financeReporting','6.0<br><b>Manage Finance<br>and Reporting</b>',proc,530,455,275,125)
add(r,'notify','7.0<br><b>Send and Track<br>Notifications</b>',proc,310,735,255,115)

# Logical data stores representing actual CREMS records
add(r,'dCustomer','D1&nbsp;&nbsp;Customers, Users and Access',store,285,300,305,58)
add(r,'dOrg','D2&nbsp;&nbsp;Divisions, Branches, Services and Rates',store,635,300,350,58)
add(r,'dBooking','D3&nbsp;&nbsp;Bookings, Quotations and Approvals',store,1030,300,355,58)
add(r,'dRental','D4&nbsp;&nbsp;Agreements, Drivers and Inspections',store,1320,650,340,58)
add(r,'dAsset','D5&nbsp;&nbsp;Assets, Status, Meters and Lifecycle',store,930,650,350,58)
add(r,'dMaintenance','D6&nbsp;&nbsp;Maintenance Jobs, Parts and Costs',store,530,650,350,58)
add(r,'dFinance','D7&nbsp;&nbsp;Invoices, Payments and Audit History',store,250,650,330,58)

# External inputs and outputs
flows=[
('a1','customer','account','Registration and credentials',False),('a2','account','customer','Account and session status',False),
('a3','customer','booking','Search and booking request',False),('a4','booking','customer','Availability and quotation',False),
('a5','customer','pickup','Identification, acceptance and signature',False),('a6','pickup','customer','Signed agreement and hire details',False),
('a7','rentalOfficer','booking','Review, pricing and allocation',False),('a8','booking','rentalOfficer','Queue, warnings and booking status',False),
('a9','rentalOfficer','pickup','Pickup checks and readings',False),('a10','pickup','rentalOfficer','Check-out confirmation',False),
('a11','rentalOfficer','return','Return readings, condition and charges',False),('a12','return','rentalOfficer','Return outcome and next asset status',False),
('a13','fieldStaff','pickup','QR scan and handover details',False),('a14','fieldStaff','return','QR scan and field evidence',False),
('a15','manager','booking','Approval or rejection',False),('a16','financeReporting','manager','Branch performance and exceptions',False),
('a17','maintenanceOfficer','assetMaintenance','Work details, parts and costs',False),('a18','assetMaintenance','maintenanceOfficer','Fault referrals and service schedule',False),
('a19','financeOfficer','financeReporting','Payments, refunds and report request',False),('a20','financeReporting','financeOfficer','Invoices, balances and profitability',False),
]
for vals in flows:edge(r,*vals)

# Process and datastore flows
internal=[
('i1','account','dCustomer','Customer and access records'),
('i2','booking','dCustomer','Customer eligibility'),('i3','booking','dOrg','Services, calendars and rates'),('i4','booking','dBooking','Request, quote and approval'),('i5','booking','dAsset','Availability query'),
('i6','pickup','dBooking','Confirmed booking and allocation'),('i7','pickup','dRental','Agreement, driver and pre-hire inspection'),('i8','pickup','dAsset','On-hire status and opening meter'),
('i9','return','dRental','Post-hire inspection and evidence'),('i10','return','dAsset','Return status and closing meter'),('i11','return','assetMaintenance','Damage or fault referral'),('i12','return','financeReporting','Final charges and usage'),
('i13','assetMaintenance','dAsset','Asset, meter and lifecycle records'),('i14','assetMaintenance','dMaintenance','Jobs, parts, labour and expense'),
('i15','financeReporting','dBooking','Rental revenue source'),('i16','financeReporting','dMaintenance','Maintenance expense source'),('i17','financeReporting','dFinance','Invoice, payment and audit record'),
('i18','booking','notify','Quote and booking event'),('i19','pickup','notify','Agreement and pickup event'),('i20','return','notify','Return and invoice event'),('i21','financeReporting','notify','Balance or statement event'),('i22','notify','dFinance','Notification audit record'),
]
for vals in internal:edge(r,*vals)
edge(r,'n1','notify','emailProvider','Email for delivery',True)
edge(r,'n2','emailProvider','notify','Delivery status',True)
edge(r,'n3','notify','customer','Confirmation, agreement or invoice',True)

add(r,'scope','<b>Scope shown:</b> customer access, booking, configurable pricing and approval, asset allocation, pickup agreement, inspections, return, maintenance, invoicing and asset profitability. Technical API calls are intentionally excluded.',band,360,1025,1200,42)
add(r,'legend','<b>Notation:</b>&nbsp;&nbsp; Grey rectangle = external entity&nbsp;&nbsp;&nbsp; Yellow ellipse = CREMS process&nbsp;&nbsp;&nbsp; Open rectangle = logical data store&nbsp;&nbsp;&nbsp; Arrow = named data flow',band,390,1080,1140,36)

ET.indent(mx,space='  ')
OUT.write_bytes(ET.tostring(mx,encoding='utf-8',xml_declaration=True))
print(OUT)
