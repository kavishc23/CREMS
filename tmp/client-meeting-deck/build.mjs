import fs from 'node:fs/promises';
import { Presentation, PresentationFile } from '@oai/artifact-tool';

const W=1280,H=720;
const BLACK='#0B0B0B', YELLOW='#FFED00', WHITE='#FFFFFF', OFF='#F5F4EF', GRAY='#6B6B67', LINE='#D9D8D2', OLIVE='#A69B00';
const root='/Users/kavishchandra/Documents/CS400';
const out=`${root}/docs/CREMS-Client-Meeting-Stack-Scope-Timeline.pptx`;
const tmp=`${root}/tmp/client-meeting-deck/output`;
await fs.mkdir(tmp,{recursive:true});

const pres=Presentation.create({slideSize:{width:W,height:H}});

function rect(slide,left,top,width,height,fill,opts={}){
  return slide.shapes.add({geometry:opts.geometry||'rect',name:opts.name,position:{left,top,width,height},fill,line:{style:'solid',fill:opts.line||fill,width:opts.lineWidth||0},borderRadius:opts.radius});
}
function text(slide,value,left,top,width,height,size=24,color=BLACK,bold=false,opts={}){
  const s=slide.shapes.add({geometry:'textbox',name:opts.name,position:{left,top,width,height},fill:'none',line:{style:'solid',fill:'none',width:0}});
  s.text=value;
  s.text.style={fontFamily:'Aptos',fontSize:size,color,bold,alignment:opts.align||'left',verticalAlignment:opts.valign||'top',wrap:true};
  return s;
}
function line(slide,x,y,w,color=YELLOW,h=5){rect(slide,x,y,w,h,color);}
async function img(slide,path,left,top,width,height,fit='cover',alt=''){
  const bytes=await fs.readFile(path);
  return slide.images.add({blob:new Uint8Array(bytes),contentType:path.endsWith('.png')?'image/png':'image/jpeg',alt,fit,position:{left,top,width,height},geometry:'roundRect',borderRadius:18});
}
function chrome(slide,num,title,light=true){
  slide.background.fill=light?OFF:BLACK;
  text(slide,'CREMS  /  CLIENT MEETING',64,28,420,22,12,light?GRAY:'#B8B8B2',true);
  text(slide,String(num).padStart(2,'0'),1160,28,56,22,12,light?GRAY:'#B8B8B2',true,{align:'right'});
  if(title) text(slide,title,64,67,1120,54,36,light?BLACK:WHITE,true,{name:`slide-${num}-title`});
  line(slide,64,128,78,YELLOW,5);
}
function notes(slide,body){slide.speakerNotes.textFrame.setText(`${body}\n\n[Sources]\n- Client/project team scope and timeline supplied 10 August 2026.\n[/Sources]`);}

// 1 — title
{
 const s=pres.slides.add(); s.background.fill=BLACK;
 rect(s,0,0,22,H,YELLOW);
 await img(s,`${root}/src/crems-web/public/brand/carpenters-logo.png`,80,72,92,92,'contain','Carpenters Fiji logo');
 text(s,'CARPENTERS FIJI',200,78,500,30,18,YELLOW,true);
 text(s,'CREMS',80,205,520,76,60,WHITE,true);
 text(s,'Technology, pilot scope\nand semester delivery plan',80,294,710,150,42,WHITE,true);
 text(s,'Carpenters Rentals + Carptrac',82,482,600,38,23,YELLOW,true);
 text(s,'Client meeting  •  10 August 2026',82,545,480,28,18,'#C7C7C2',false);
 await img(s,`${root}/src/crems-web/public/catalog/excavator.jpg`,850,0,430,720,'cover','Carptrac excavator');
 rect(s,806,0,70,720,BLACK);
 notes(s,'Open by confirming the purpose: agree the platform foundation, the first two divisions and the delivery baseline through 30 October.');
}

// 2 — commitment snapshot
{
 const s=pres.slides.add(); chrome(s,2,'Today we are confirming one practical delivery baseline');
 text(s,'TWO PILOT DIVISIONS',72,180,330,28,16,GRAY,true);
 text(s,'Carpenters Rentals\nCarptrac',72,220,360,104,34,BLACK,true);
 line(s,72,344,220,YELLOW,8);
 text(s,'ONE MODULAR PLATFORM',472,180,330,28,16,GRAY,true);
 text(s,'Shared capabilities,\nconfigurable rules',472,220,370,104,34,BLACK,true);
 line(s,472,344,220,YELLOW,8);
 text(s,'TWELVE WEEKS',872,180,300,28,16,GRAY,true);
 text(s,'10 August to\n30 October 2026',872,220,330,104,34,BLACK,true);
 line(s,872,344,220,YELLOW,8);
 rect(s,72,414,1136,184,WHITE,{line:LINE,lineWidth:1,radius:16});
 text(s,'Outcome',100,446,180,30,22,BLACK,true);
 text(s,'A tested rental and maintenance MVP that supports vehicle and heavy-equipment workflows while leaving a clean path for future Carpenters divisions.',280,440,870,95,27,BLACK,false);
 notes(s,'Use this as the meeting anchor. The team is not proposing separate systems for each division; it is proposing one reusable platform with two initial operating contexts.');
}

// 3 — pilots
{
 const s=pres.slides.add(); chrome(s,3,'Two pilot divisions prove the model across different operations');
 await img(s,`${root}/src/crems-web/public/catalog/suv.jpg`,64,170,360,220,'cover','Rental SUV');
 text(s,'Carpenters Rentals',64,412,360,38,30,BLACK,true);
 text(s,'Vehicle-led booking, agreements, pickup, return, damage evidence and fleet utilisation.',64,463,360,105,20,GRAY,false);
 await img(s,`${root}/src/crems-web/public/catalog/excavator.jpg`,460,170,360,220,'cover','Heavy equipment excavator');
 text(s,'Carptrac',460,412,360,38,30,BLACK,true);
 text(s,'Equipment availability, hour-meter tracking, personnel requirements, inspections and maintenance.',460,463,360,105,20,GRAY,false);
 rect(s,856,170,352,398,BLACK,{radius:18});
 text(s,'What this validates',884,202,290,34,25,YELLOW,true);
 text(s,'• One customer foundation\n\n• One asset lifecycle\n\n• Different operational rules\n\n• Division and branch controls\n\n• Reusable reporting',884,262,282,255,21,WHITE,false);
 notes(s,'Explain that these divisions were chosen because together they test both straightforward vehicle rental and more specialised equipment operations.');
}

// 4 — modular model
{
 const s=pres.slides.add(); chrome(s,4,'Shared modules reduce duplication; rules stay configurable');
 text(s,'SHARED CREMS CORE',64,170,420,30,17,GRAY,true);
 text(s,'Identity  •  Customers  •  Branches  •  Assets  •  Bookings  •  Agreements  •  Inspections  •  Maintenance  •  Notifications  •  Audit  •  Reports',64,214,1144,78,27,BLACK,true);
 line(s,64,320,1144,YELLOW,10);
 text(s,'RENTALS CONFIGURATION',64,367,340,28,16,GRAY,true);
 text(s,'Vehicle categories\nLicence and driver details\nFuel and odometer\nDaily rental and return rules',64,408,390,158,22,BLACK,false);
 text(s,'CARPTRAC CONFIGURATION',480,367,350,28,16,GRAY,true);
 text(s,'Equipment categories\nHour-meter readings\nOperator or technician rules\nEquipment maintenance controls',480,408,390,158,22,BLACK,false);
 text(s,'FUTURE DIVISIONS',896,367,300,28,16,GRAY,true);
 text(s,'Add a division\nEnable capabilities\nConfigure its workflow\nReuse the core platform',896,408,300,158,22,BLACK,false);
 notes(s,'Describe the architecture in business terms. Shared modules are built once; division settings decide which workflow and data fields apply.');
}

// 5 — MVP operations
{
 const s=pres.slides.add(); chrome(s,5,'The MVP covers the full rental transaction lifecycle');
 text(s,'1',66,180,56,56,42,YELLOW,true); text(s,'Control access',130,184,310,36,26,BLACK,true);
 text(s,'Staff authentication and Administrator, Branch Manager and Rental Officer permissions.',130,230,430,62,19,GRAY,false);
 text(s,'2',66,326,56,56,42,YELLOW,true); text(s,'Prepare the rental',130,330,340,36,26,BLACK,true);
 text(s,'Vehicle and equipment masters, customers, reservations and real-time availability.',130,376,430,62,19,GRAY,false);
 text(s,'3',650,180,56,56,42,YELLOW,true); text(s,'Release the asset',714,184,360,36,26,BLACK,true);
 text(s,'Agreement generation, allocation, QR identification and inspection-based check-out.',714,230,430,62,19,GRAY,false);
 text(s,'4',650,326,56,56,42,YELLOW,true); text(s,'Close the rental',714,330,340,36,26,BLACK,true);
 text(s,'Return processing, inspections, damage reporting and maintenance request creation.',714,376,430,62,19,GRAY,false);
 rect(s,64,510,1144,90,BLACK,{radius:14});
 text(s,'A single traceable flow: reservation → agreement → allocation → check-out → return → maintenance decision',92,537,1090,34,23,WHITE,true,{align:'center'});
 notes(s,'Emphasise end-to-end traceability. Each transaction changes availability and creates evidence for the next operational step.');
}

// 6 — management
{
 const s=pres.slides.add(); chrome(s,6,'Management visibility is built into the same operational data');
 text(s,'DASHBOARD',64,174,260,28,16,GRAY,true);
 text(s,'Available\nCurrently rented\nUnder maintenance\nUpcoming bookings\nOverdue rentals\nRevenue summary',64,214,360,260,27,BLACK,true);
 line(s,450,170,6,390,YELLOW,6);
 text(s,'REPORTS',500,174,260,28,16,GRAY,true);
 text(s,'Rental history\nFleet utilisation\nEquipment utilisation\nMaintenance report\nRevenue report\nOverdue rentals\nCustomer rental history',500,214,380,300,24,BLACK,false);
 rect(s,920,170,288,390,BLACK,{radius:18});
 text(s,'Controls',950,202,230,30,25,YELLOW,true);
 text(s,'Email notifications\n\nPreventive maintenance reminders\n\nQR / barcode identification\n\nCritical transaction audit logs',950,260,222,240,20,WHITE,false);
 notes(s,'Connect reporting to the operational workflow: dashboards and reports are not a separate data-entry exercise; they are generated from booking, return and maintenance records.');
}

// 7 — stack
{
 const s=pres.slides.add(); chrome(s,7,'The selected stack is secure, maintainable and team-ready');
 const rows=[
 ['EXPERIENCE','React + TypeScript + Vite + Material UI','Responsive staff and customer interfaces'],
 ['APPLICATION','ASP.NET Core 10 Web API','Business rules, workflows and integrations'],
 ['IDENTITY','ASP.NET Core Identity','Authentication, recovery and role-based access'],
 ['DATA','SQL Server 2022 + Entity Framework Core','Transactions, relationships and migrations'],
 ['DELIVERY','Docker + GitHub + Postman','Consistent setup, collaboration and API testing'],
 ['SERVICES','SMTP / email provider + PDF + QR libraries','Notifications, agreements and asset identification']
 ];
 let y=168;
 for(const [a,b,c] of rows){
   text(s,a,64,y,160,26,15,GRAY,true);
   text(s,b,230,y-2,610,34,21,BLACK,true);
   text(s,c,870,y,338,42,17,GRAY,false);
   rect(s,64,y+49,1144,1,LINE); y+=72;
 }
 notes(s,'Confirm that this is the stack already used by the project. Explain that Docker supports the team’s mix of macOS and Windows development environments.');
}

// 8 — weeks 1-6
{
 const s=pres.slides.add(); chrome(s,8,'Weeks 1–6 establish the secure rental foundation');
 const items=[
 ['01','10–14 Aug','Scope, schema, stack and authentication'],
 ['02','17–21 Aug','Divisions, branches, roles and access scope'],
 ['03','24–28 Aug','Vehicle and equipment master management'],
 ['04','31 Aug–4 Sep','Individual and corporate customer records'],
 ['05','7–11 Sep','Availability, reservations and conflict prevention'],
 ['06','14–18 Sep','Agreements, approvals and asset allocation']
 ];
 let y=166;
 for(const [w,d,f] of items){
   text(s,w,64,y,64,34,26,YELLOW,true);
   text(s,d,146,y+3,180,28,17,GRAY,true);
   text(s,f,342,y,820,38,23,BLACK,w==='01');
   rect(s,64,y+50,1144,1,LINE); y+=76;
 }
 notes(s,'Week 1 is the foundation sprint. From Week 2 onward, each sprint adds a demonstrable increment on top of the existing platform.');
}

// 9 — weeks 7-12
{
 const s=pres.slides.add(); chrome(s,9,'Weeks 7–12 complete operations, insight and handover');
 const items=[
 ['07','21–25 Sep','QR-assisted check-out and condition inspections'],
 ['08','28 Sep–2 Oct','Return, damage evidence and charge decisions'],
 ['09','5–9 Oct','Maintenance schedules, jobs, costs and reminders'],
 ['10','12–16 Oct','Dashboard, utilisation and management reports'],
 ['11','19–23 Oct','Notifications, audit, integration and security testing'],
 ['12','26–30 Oct','Client UAT, defect correction, training and handover']
 ];
 let y=166;
 for(const [w,d,f] of items){
   text(s,w,64,y,64,34,26,YELLOW,true);
   text(s,d,146,y+3,180,28,17,GRAY,true);
   text(s,f,342,y,820,38,23,BLACK,w==='12');
   rect(s,64,y+50,1144,1,LINE); y+=76;
 }
 notes(s,'The last two sprints protect integration and acceptance time. New scope should not displace security, UAT, documentation or handover activities.');
}

// 10 — cadence
{
 const s=pres.slides.add(); chrome(s,10,'Every sprint ends with working evidence, not only code');
 const xs=[64,350,636,922];
 const titles=['MONDAY','TUE–WED','THURSDAY','FRIDAY'];
 const bodies=['Plan the sprint\nand confirm acceptance','Develop, review\nand unit test','Integrate frontend,\nAPI and database','Demonstrate, record\nfeedback and improve'];
 for(let i=0;i<4;i++){
   text(s,titles[i],xs[i],190,220,28,16,GRAY,true);
   text(s,bodies[i],xs[i],240,230,120,26,BLACK,true);
   line(s,xs[i],390,205,YELLOW,8);
 }
 rect(s,64,466,1144,118,BLACK,{radius:16});
 text(s,'Definition of done',92,496,250,30,22,YELLOW,true);
 text(s,'Accepted criteria • integrated frontend and API • authorization tested • migration included • peer reviewed • demonstration recorded',342,490,820,56,21,WHITE,false);
 notes(s,'This slide demonstrates delivery discipline. Each Friday provides evidence and an opportunity for client feedback before the next increment.');
}

// 11 — milestones / close
{
 const s=pres.slides.add(); s.background.fill=BLACK;
 text(s,'CREMS DELIVERY BASELINE',64,44,520,24,14,YELLOW,true);
 text(s,'A focused pilot now.\nA reusable platform next.',64,112,700,110,45,WHITE,true);
 const milestones=[['14 AUG','Scope, stack and architecture'],['11 SEP','Booking workflow'],['09 OCT','Rental and maintenance lifecycle'],['23 OCT','Integrated release candidate'],['30 OCT','UAT and handover']];
 let y=282;
 for(const [d,m] of milestones){text(s,d,66,y,125,26,17,YELLOW,true); text(s,m,210,y-2,620,32,24,WHITE,true); y+=64;}
 await img(s,`${root}/src/crems-web/public/brand/carpenters-logo.png`,1040,80,120,120,'contain','Carpenters Fiji logo');
 text(s,'Carpenters Rentals + Carptrac',886,588,292,28,17,'#C7C7C2',true,{align:'right'});
 notes(s,'Close on the delivery commitment: the two pilot divisions create a credible semester outcome and validate the architecture for future divisions.');
}

for(let i=0;i<pres.slides.items.length;i++){
 const slide=pres.slides.items[i];
 const png=await pres.export({slide,format:'png',scale:1});
 await fs.writeFile(`${tmp}/slide-${i+1}.png`,new Uint8Array(await png.arrayBuffer()));
 const layout=await slide.export({format:'layout'});
 await fs.writeFile(`${tmp}/slide-${i+1}.layout.json`,await layout.text());
}
const montage=await pres.export({format:'webp',montage:true,scale:1});
await fs.writeFile(`${tmp}/montage.webp`,new Uint8Array(await montage.arrayBuffer()));
const pptx=await PresentationFile.exportPptx(pres);
await pptx.save(out);
console.log(out);
