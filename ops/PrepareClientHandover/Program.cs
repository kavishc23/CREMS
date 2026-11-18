using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
var apiRoot=Path.Combine(root,"src/CREMS.Api");
var output=Path.Combine(root,"output/client-handover"); Directory.CreateDirectory(output);
var config=new ConfigurationBuilder().SetBasePath(apiRoot).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true).AddUserSecrets("crems-cs400-development").AddEnvironmentVariables().Build();
var connection=config.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("Missing database connection.");
var target=new SqlConnectionStringBuilder(connection);
if(target.InitialCatalog!="Crems"||!(target.DataSource=="localhost"||target.DataSource.StartsWith("localhost,")||target.DataSource=="127.0.0.1"||target.DataSource.StartsWith("127.0.0.1,")))throw new InvalidOperationException("This utility only targets the local Crems database.");
await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
if((await db.Database.GetPendingMigrationsAsync()).Any())throw new InvalidOperationException("Apply the application's pending migrations before seeding.");
var email=Value("--customer-email")??throw new ArgumentException("Supply --customer-email for the existing customer account.");
var user=await db.Users.SingleAsync(x=>x.NormalizedEmail==email.ToUpperInvariant());
if(user.CustomerId is not {} customerId||!user.IsActive)throw new InvalidOperationException("An active, customer-linked account is required.");
var customer=await db.Customers.SingleAsync(x=>x.Id==customerId);
if(!customer.IsActive||customer.IsBlocked)throw new InvalidOperationException("The customer account must be eligible to rent.");
var adminRole=await db.Roles.SingleAsync(x=>x.Name==SystemRoles.SuperAdministrator);
var actorId=await db.UserRoles.Where(x=>x.RoleId==adminRole.Id).Select(x=>x.UserId).FirstAsync();
const string actor="Handover data preparation";
var now=DateTimeOffset.UtcNow; var today=new DateTimeOffset(now.ToOffset(TimeSpan.FromHours(12)).Date,TimeSpan.FromHours(12));
var localDate=DateOnly.FromDateTime(today.Date); var stamp=today.ToString("yyyyMMdd");
var prefix=$"BK-{stamp}-HO-";
var categories=await db.AssetCategories.Include(x=>x.Division).Include(x=>x.ServiceOffering).Include(x=>x.AttributeDefinitions).Where(x=>x.IsActive&&x.Division!.IsActive&&x.ServiceOffering!.IsActive).OrderBy(x=>x.Code).ToListAsync();
var branches=await db.Branches.Where(x=>x.IsActive&&x.IsPublic).OrderBy(x=>x.Code).ToListAsync();
var suva=branches.Single(x=>x.Code=="SUV");
var originals=await db.Assets.Include(x=>x.AssetCategory).Include(x=>x.AttributeValues).ToListAsync();
var originalPhotos=originals.Select(x=>new{x.Id,x.AssetNumber,x.PhotoUrlsJson}).ToArray();
var initialBookings=await db.Bookings.CountAsync(x=>x.CustomerId==customerId);var initialQuotes=await db.SalesQuotes.CountAsync(x=>x.CustomerId==customerId);
if(!args.Contains("--apply")&&!args.Contains("--verify")){
 Console.WriteLine($"Preview: active customer located; {originals.Count} assets, {categories.Count} active categories, {initialBookings} customer bookings, {initialQuotes} quotations. Use --apply to add the handover suite, or --verify for a read-only integrity check.");return;
}
if(await db.Bookings.AnyAsync(b=>b.BookingNumber.StartsWith(prefix)&&b.CustomerId!=customerId))throw new InvalidOperationException("This date already has a handover suite for another account. Use the original account.");
var scenarios=new List<Scenario>(); var additions=new List<Asset>();var counters=new Dictionary<Guid,int>();var used=originals.Select(x=>x.AssetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);var created=0;var repaired=0;
using var notifications=db.SuppressNotifications();
if(args.Contains("--apply")){
 await File.WriteAllTextAsync(Path.Combine(output,$"asset-photo-backup-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json"),JsonSerializer.Serialize(originalPhotos,json));
 await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
 if(!await db.Bookings.AnyAsync(x=>x.BookingNumber.StartsWith(prefix)))
 foreach(var category in categories)for(var i=0;i<8;i++)NewAsset(category,branches[i%branches.Count]);
 await db.SaveChangesAsync();
 if(!await db.Bookings.AnyAsync(x=>x.BookingNumber.StartsWith(prefix))){
  var specs=new[]{
   new Spec("REQ-NEW","New vehicle booking request","SEDAN","Draft",3,5,"Review pricing, required documents and confirmation. Try invalid dates, negative rate and unauthorized approval."),
   new Spec("QUO-DRAFT","Quotation awaiting staff pricing","PROJECTOR","QuoteDraft",4,6,"Enter hire and electricity quantities, save quotation, route approval and publish offer."),
   new Spec("QUO-ACCEPT","Sent quotation ready for customer acceptance","PROJECTOR","QuoteSent",5,7,"Customer reviews all lines, VAT and bond, then accepts. Staff confirms after any required approvals."),
   new Spec("QUO-DECLINE","Sent quotation ready for customer decline","SUV","QuoteSent",6,8,"Decline with a reason. A blank decline reason must be rejected."),
   new Spec("QUO-REVISE","Quotation revision and negotiation","PROJECTOR","QuoteRevised",7,9,"Inspect version history. Revise the offer; an old expectedVersion must fail and customer must review the latest version."),
   new Spec("QUO-EXPIRED","Expired quotation","SEDAN","QuoteExpired",3,5,"Expired offer cannot be accepted; revise validity and pricing before sending again."),
   new Spec("QUO-ACCEPTED","Accepted quotation awaiting confirmation","SEDAN","QuoteAccepted",3,5,"Show acceptance before confirmation; repeat acceptance must fail."),
   new Spec("QUO-REJECTED","Quotation declined by customer","SUV","QuoteRejected",4,6,"Show decline reason and closed customer journey; prevent conversion of declined offer."),
   new Spec("APR-BOOK","High-value booking awaiting approval","SUV","Approval",8,28,"Approve/reject using the configured stage workflow. A rental officer cannot approve a manager stage."),
   new Spec("APR-QUOTE","Quotation awaiting managerial approval","PROJECTOR","QuoteApproval",10,30,"Customer decision remains blocked while approval is pending. Staff approves before publishing."),
   new Spec("PICK-FULL","Pickup with full payment","SEDAN","Confirmed",0,2,"Inspect, verify original licence, collect full hire and bond, obtain customer signature and complete pickup."),
   new Spec("PICK-PART","Pickup with authorized partial payment","SEDAN","Confirmed",0,3,"Pay half, request manager-approved balance on return and provide a complete deferral reason."),
   new Spec("PICK-PAID","Pickup with prepaid hire and bond","SUV","Prepaid",0,2,"Hire/bond are already fully paid. Enter zero received now; collecting the same payment again must be rejected."),
   new Spec("PICK-DAMAGE","Pickup with accepted existing cosmetic damage","HATCHBACK","Confirmed",0,2,"Record a cosmetic scratch and photo, agree with customer, certify safe operation and add a detailed acceptance note."),
   new Spec("PICK-SWAP","Pickup requiring an available replacement","SEDAN","Confirmed",0,3,"Record an unsafe fault, select an available same-category asset and repeat inspection for the replacement."),
   new Spec("PICK-MAINT","Confirmed allocation subsequently enters maintenance","SEDAN","BlockedMaintenance",0,2,"Pickup must be blocked until work and safety release are complete; use replacement or resolve maintenance."),
   new Spec("PICK-INSPECT","Pickup blocked pending safety inspection","SEDAN","BlockedInspection",0,2,"Asset awaiting safety inspection must not be released until its inspection/release is complete."),
   new Spec("PICK-EQUIP","Projector pickup without driving licence","PROJECTOR","Confirmed",0,2,"Verify ID and Event Approval. No driving-licence requirement; show electricity quantity and included accessories."),
   new Spec("PICK-DOCS","Required Event Approval awaiting upload","PROJECTOR","MissingDocument",0,2,"Attach the missing configured approval from customer booking and inspect it in staff Documents before pickup."),
   new Spec("RET-CLEAN","Return today: full payment and full bond refund","SEDAN","ActivePaid",-2,0,"Finish return inspection and refund the whole FJD 100 bond. No hire is due and no extra VAT should appear."),
   new Spec("RET-DAMAGE","Return today: FJD 30 damage retained from bond","SEDAN","ActivePaid",-3,0,"Enter damage FJD 30 and retain FJD 30 from the FJD 100 bond; refund FJD 70. Do not charge damage twice."),
   new Spec("RET-PART","Return today: remaining hire after partial pickup","SUV","ActivePartial",-3,0,"Collect remaining hire plus any final charges. Retained damage bond must not discount unrelated unpaid hire."),
   new Spec("RET-UNPAID","Return today: no hire paid at pickup","SEDAN","ActiveUnpaid",-2,0,"Hire unpaid with bond held. Retain FJD 30 for damage and refund FJD 70: hire remains due in full."),
   new Spec("RET-LATE","Overdue rental with calculated late charge","SEDAN","ActiveLate",-4,-1,"Review calculated overdue fee; authorized staff may edit or waive with reason. Prevent future return timestamps."),
   new Spec("RET-FUEL","Return today: fuel, cleaning and excess usage","PICKUP","ActivePaid",-3,0,"Return with lower fuel, increased kilometres and cleaning charge; review each final charge before settlement."),
   new Spec("RET-EQUIP","Projector return: hours and accessories","PROJECTOR","ActivePaid",-2,0,"Record operating hours, cables/remote and condition; no driving licence or vehicle fuel fields."),
   new Spec("RET-FAULT","Return today with a fault requiring maintenance","SEDAN","ActivePaid",-2,0,"Record failed safety check and damage evidence; finish settlement and route asset into maintenance."),
   new Spec("EXT-NEW","Active rental ready for customer extension request","SEDAN","ActivePaid",-1,3,"Customer requests later return; staff approves. Added billing units and VAT must increase hire balance."),
   new Spec("EXT-PENDING","Extension request awaiting staff decision","SEDAN","ExtensionPending",-1,3,"Approve pending extension and compare before/after quantity, amount and return date. Reapproval must be blocked."),
   new Spec("EXT-CONFLICT","Extension request clashes with next reservation","SEDAN","ExtensionConflict",-1,2,"Requested extra dates overlap a separately confirmed reservation. Approval must return conflict and leave prices/dates intact."),
   new Spec("EXT-DECLINED","Previously declined extension request","SEDAN","ExtensionDeclined",-2,3,"Show customer-visible decline note and unchanged rental period."),
   new Spec("CAN-PENDING","Confirmed hire cancellation awaiting staff review","SEDAN","CancellationPending",2,4,"Approve or decline cancellation and review asset release and any recorded payment separately."),
   new Spec("CAN-DRAFT","Draft rental cancelled online","SEDAN","Cancelled",4,6,"Show customer cancellation history; a cancelled rental cannot be picked up."),
   new Spec("INC-REPORT","Active hire ready for emergency/incident reporting","SUV","ActivePaid",-1,3,"Customer submits breakdown with photo/document; staff reviews attachments and resolution."),
   new Spec("INC-DAMAGE","Open damage incident with reference attachments","SEDAN","IncidentDamage",-1,3,"Review customer description and attachment, record evidence-based action and customer-facing explanation."),
   new Spec("INC-BREAK","Breakdown with repair and early termination options","SUV","IncidentBreakdown",-1,3,"Investigate breakdown, request return or end hire after incident with recorded custody and out-of-service status."),
   new Spec("INC-THEFT","Theft incident under investigation","SUV","IncidentTheft",-2,3,"Review police reference, preserve custody trail, then end hire after incident; do not mark asset available."),
   new Spec("SET-OPEN","Incident-ended hire awaiting financial decisions","SEDAN","SettlementOpen",-3,2,"Assign owner; record responsibility, costs, evidence, follow-up and payment due date before issuing settlement."),
   new Spec("SET-DISPUTE","Disputed incident-ended hire","SUV","SettlementDisputed",-4,1,"Record dispute and follow-up. Do not close case until required decisions and account conditions are satisfied."),
   new Spec("FIN-PAID","Completed hire with paid invoice","SEDAN","CompletedPaid",-10,-8,"Open final invoice and printable sample agreement; duplicate return/payment must be rejected."),
   new Spec("FIN-DUE","Completed hire with outstanding invoice","SEDAN","CompletedDue",-9,-7,"Record remaining hire payment in Finance, view allocation and paid status; reject payment above balance."),
   new Spec("FIN-CREDIT","Completed hire with an applied credit note","SUV","CompletedCredit",-8,-6,"Show credit note and invoice balance reconciliation; original invoice lines stay visible."),
   new Spec("OPS-DELIVERY","Equipment quotation including delivery and operator","HEAVY_MACHINE","DeliveryQuote",5,8,"Review transport and operator prices, approval and delivery assignment; customer is not the equipment driver."),
   new Spec("OPS-SITE","Portable toilet hire with delivery","PORTABLE_TOILET","DeliveryQuote",6,9,"Review delivery/site details, servicing charges and collection workflow without vehicle licence fields."),
   new Spec("OPS-BIN","Big-bin hire quotation","BIG_BIN","QuoteSent",6,9,"Review per-unit quantities, waste/site conditions and total before acceptance."),
   new Spec("OPS-SCAFFOLD","Scaffolding hire quotation and installation","SCAFFOLD","DeliveryQuote",7,12,"Review scaffold area/quantity, installer and delivery costs; demonstrate quotation approval."),
   new Spec("REQ-EXPIRED","Expired unconfirmed booking request","HATCHBACK","Expired",-5,-3,"Expired request cannot proceed to pickup; create or revise an eligible request."),
   new Spec("QUO-NOSEND","Revised quotation not yet published","PROJECTOR","QuoteUnsentRevision",8,10,"Customer sees awaiting quotation while staff revises; stale old prices must not remain actionable.")
  };
  var n=0;
  foreach(var spec in specs){
   n++;var category=categories.Single(x=>x.Code==spec.Category);var asset=NewAsset(category,suva);var b=new Booking{BookingNumber=prefix+n.ToString("000"),CustomerId=customerId,Customer=customer,BranchId=suva.Id,Branch=suva,Status=BookingStatus.Draft,CreatedAt=now.AddHours(-2),UpdatedAt=now,TaxRate=category.Division!.DefaultTaxRate,DepositRequired=spec.Category=="PROJECTOR"?150:AssetCategoryPolicy.IsVehicle(asset.Type)?100:200,Notes=$"Client handover preparation — {spec.Key}: {spec.Title}. Fictional scenario; seeded receipts are not real transactions.\n{spec.Action}"};
   b.BondStatus=BondStatus.AwaitingPayment;
   var start=today.AddDays(spec.Start).AddHours(9);var end=today.AddDays(spec.End).AddHours(spec.End==0?16:9);
   var item=new BookingItem{BookingId=b.Id,AssetId=asset.Id,Asset=asset,StartAt=start,EndAt=end,DailyRate=asset.DailyRate,HireUnit=asset.HireUnit,HireQuantity=HirePricing.Quantity(asset.HireUnit,start,end)};b.Items.Add(item);
   var capture=await ConfiguredHireRules.Capture(db,asset,null,default);var answers=capture.Questions.ToDictionary(q=>q.Code,q=>q.Type=="Boolean"?"true":q.Options?.FirstOrDefault()??(q.Type is "Number" or "Integer"?"10":q.Type=="Date"?localDate.ToString("yyyy-MM-dd"):"Suva business function"));
   b.ConfigurationSnapshotJson=JsonSerializer.Serialize(capture with{Answers=answers},OrganizationConfiguration.Json);
   if(spec.Category=="PROJECTOR"){
    var fee=await db.ChargeDefinitions.FirstAsync(x=>x.Name=="Electricity Charge"&&x.IsActive);
    b.Charges.Add(new BookingCharge{BookingId=b.Id,AssetId=asset.Id,ChargeDefinitionId=fee.Id,Description=fee.Name,Category=fee.Category,Unit=fee.Unit,Quantity=4,UnitRate=fee.DefaultSellingRate,UnitCost=fee.DefaultCostRate,IsTaxable=fee.IsTaxable});
   }
   if(spec.State=="DeliveryQuote"){
    b.Charges.Add(new BookingCharge{BookingId=b.Id,AssetId=asset.Id,Description="Delivery and collection",Category=ChargeCategory.Transport,Unit=ChargeUnit.Trip,Quantity=2,UnitRate=125,UnitCost=85});
    b.Notes+="\nFulfilment: Delivery. Delivery address: 25 Victoria Parade, Suva.";
    if(spec.Category is "HEAVY_MACHINE" or "SCAFFOLD")b.Charges.Add(new BookingCharge{BookingId=b.Id,AssetId=asset.Id,Description=spec.Category=="SCAFFOLD"?"Qualified scaffold installer":"Certified equipment operator",Category=ChargeCategory.Operator,Unit=ChargeUnit.Hour,Quantity=8,UnitRate=42,UnitCost=30});
    db.DispatchJobs.Add(new DispatchJob{DispatchNumber=$"DSP-{stamp}-HO-{n:000}",BookingId=b.Id,BranchId=suva.Id,Type=DispatchType.Delivery,Status=DispatchStatus.Scheduled,ScheduledAt=start,Address="25 Victoria Parade, Suva",DeliveryCharge=250});
   }
   if(spec.State.StartsWith("Quote")||spec.State=="DeliveryQuote"){
    var status=spec.State switch{"QuoteDraft" or "QuoteApproval"=>QuoteStatus.Draft,"QuoteAccepted"=>QuoteStatus.Accepted,"QuoteRejected"=>QuoteStatus.Rejected,"QuoteExpired"=>QuoteStatus.Expired,"QuoteUnsentRevision"=>QuoteStatus.Negotiating,_=>QuoteStatus.Sent};
    var q=new SalesQuote{QuoteNumber=$"QUO-{stamp}-HO-{n:000}",CustomerId=customerId,BranchId=suva.Id,DivisionId=category.DivisionId,AssignedUserId=actorId,Status=status,ValidUntil=spec.State=="QuoteExpired"?now.AddDays(-1):now.AddDays(14),ConvertedBookingId=b.Id,JobSite="Suva business function",CreatedAt=now.AddDays(-1),UpdatedAt=now};
    var lines=RentalFinancials.Lines(b).Select(l=>new QuoteLine(l.Description,l.Quantity,l.Rate,Enum.Parse<ChargeUnit>(l.Unit),Category:l.Description.EndsWith(" hire")?ChargeCategory.BaseHire:ChargeCategory.Other,IsTaxable:l.IsTaxable)).ToList();
    var totals=QuotePolicy.Calculate(lines.Select(l=>(l.Quantity,l.Rate,l.IsTaxable)),0,b.TaxRate);q.Subtotal=totals.Subtotal;q.Tax=totals.Tax;q.Total=totals.Total;q.LineItemsJson=QuoteLineSerialization.Serialize(lines);
    if(status==QuoteStatus.Expired){b.Status=BookingStatus.Expired;q.LostReason="Offer validity elapsed.";}
    if(status==QuoteStatus.Rejected){b.Status=BookingStatus.Cancelled;q.LostReason="Customer selected different dates and declined the offer.";}
    if(spec.State is "QuoteRevised" or "QuoteUnsentRevision"){
     q.Version=2;db.QuoteRevisions.Add(new QuoteRevision{SalesQuoteId=q.Id,Version=1,SnapshotJson=JsonSerializer.Serialize(new{q.ValidUntil,Subtotal=q.Subtotal,Tax=q.Tax,Total=q.Total,q.LineItemsJson}),ChangeReason="Customer requested four electricity units and revised function dates.",ChangedByUserId=actorId});
     // Timestamp means offer publication only; no email-delivery claim is made.
     // Revised offer uses Sent; unpublished revision uses Negotiating without a send timestamp.
    }
    db.SalesQuotes.Add(q);
    if(spec.State=="QuoteApproval")await AddApproval(b,q);
   }
   if(spec.State is "Confirmed" or "MissingDocument" or "Prepaid" or "BlockedMaintenance" or "BlockedInspection" or "CancellationPending"){
    b.Status=BookingStatus.Confirmed;b.ApprovedAt=now.AddHours(-1);b.ApprovedByUserId=actorId;
    if(spec.State=="Prepaid"){Pay(b,RentalFinancials.Total(b),PaymentType.RentalCharge,"H");HoldBond(b);}
    if(spec.State=="BlockedMaintenance"){
     asset.Status=AssetStatus.Maintenance;
     db.MaintenanceJobs.Add(new MaintenanceJob{JobNumber=$"MNT-{stamp}-HO-{n:000}",AssetId=asset.Id,BranchId=asset.BranchId,ServiceType="Corrective repair",FaultDescription="Brake warning discovered after reservation; diagnosis and safety release required.",Status=MaintenanceStatus.InProgress,ReportedAt=now.AddHours(-1),StartedAt=now.AddHours(-1),ExpectedReleaseAt=now.AddDays(1),ReportedByName=actor});
    }else if(spec.State=="BlockedInspection")asset.Status=AssetStatus.Inspection;else asset.Status=AssetStatus.Reserved;
    if(spec.State=="CancellationPending")AddCase(b,"Cancellation request",CaseStatus.Open,"Customer's travel arrangements changed; please review cancellation and payment position.");
   }
   if(spec.State=="Approval")await AddApproval(b,null);
   if(spec.State=="Cancelled"){b.Status=BookingStatus.Cancelled;b.Notes+="\nCustomer cancelled online before confirmation.";}
   if(spec.State=="Expired")b.Status=BookingStatus.Expired;
   var active=spec.State.StartsWith("Active")||spec.State.StartsWith("Extension")||spec.State.StartsWith("Incident")||spec.State.StartsWith("Settlement")||spec.State.StartsWith("Completed");
   if(active){
    b.Status=BookingStatus.ConvertedToRental;asset.Status=AssetStatus.Rented;HoldBond(b);
    var paid=spec.State is "ActiveUnpaid"?0:spec.State is "ActivePartial" or "CompletedDue"?RentalFinancials.Money(RentalFinancials.Total(b)/2):RentalFinancials.Total(b);
    if(paid>0)Pay(b,paid,PaymentType.RentalCharge,"H");
    var checklist=capture.Templates.FirstOrDefault(x=>x.Stage==InspectionStage.PreHire);var labels=checklist==null?new[]{"Asset identity checked","Condition and accessories checked"}:OrganizationConfiguration.Checklist(checklist.ChecklistJson).ToArray();
    var photo=ReferencePhoto(asset);var signature=SampleSignature();
    var handover=new RentalInspection{BookingId=b.Id,Type=InspectionType.Handover,IdentificationVerified=true,DriverLicenceVerified=AssetCategoryPolicy.IsVehicle(asset.Type),MeterReading=capture.TracksMeter?asset.CurrentMeterReading:null,FuelLevelPercent=capture.TracksFuel?100:null,ConditionNotes="Sample handover record: operational condition and accessories recorded for the client demonstration.",SignatureName="Sample record — not a customer signature",SignatureDataUrl=signature,PaymentVerified=true,CompletedByUserId=actorId,CompletedByName=actor,CompletedAt=start,CreatedAt=start,EvidenceJson=JsonSerializer.Serialize(new{checklist=labels,checklistResults=labels.Select(label=>new{label,passed=true}),photos=new[]{photo},accessories="Keys / cables / accessories checked",seeded=true})};b.Inspections.Add(handover);
    if(AssetCategoryPolicy.IsVehicle(asset.Type))b.AuthorizedDrivers.Add(new AuthorizedDriver{BookingId=b.Id,FullName="Sample authorized driver",LicenceNumber="90000001",LicenceClass="2",LicenceExpiry=localDate.AddYears(2),Verified=true,IsPrimary=true});
    b.RentalAgreement=Agreement(b,asset,start,signature);
    db.AssetLifecycleEvents.Add(new AssetLifecycleEvent{AssetId=asset.Id,BookingId=b.Id,Type=AssetLifecycleEventType.CheckedOut,FromStatus=AssetStatus.Reserved,ToStatus=AssetStatus.Rented,OccurredAt=start,RecordedByUserId=actorId,RecordedByName=actor,Notes="Seeded sample handover, not an actual customer transaction."});
    if(spec.State.StartsWith("Extension")){
     var requested=end.AddDays(2);var status=spec.State=="ExtensionDeclined"?CaseStatus.Resolved:CaseStatus.Open;
     AddCase(b,"Extension request",status,$"Current return: {end:u}. Requested new return: {requested:u}. Reason: business travel extended.\nRequestedEndAt: {requested:O}");
     if(spec.State=="ExtensionConflict"){
      var next=new Booking{BookingNumber=prefix+n.ToString("000")+"-NEXT",CustomerId=customerId,BranchId=suva.Id,Status=BookingStatus.Confirmed,TaxRate=b.TaxRate,DepositRequired=100,BondStatus=BondStatus.AwaitingPayment,ApprovedAt=now,ApprovedByUserId=actorId,Notes="A valid subsequent reservation to demonstrate extension conflict protection; do not cancel until this case is shown.",ConfigurationSnapshotJson=b.ConfigurationSnapshotJson};
      next.Items.Add(new BookingItem{BookingId=next.Id,AssetId=asset.Id,StartAt=end.AddHours(3),EndAt=end.AddDays(3),DailyRate=asset.DailyRate,HireUnit=ChargeUnit.Day});db.Bookings.Add(next);
     }
    }
    if(spec.State.StartsWith("Incident")||spec.State.StartsWith("Settlement")){
     var type=spec.State=="IncidentTheft"?IncidentType.Theft:spec.State=="IncidentBreakdown"?IncidentType.Breakdown:IncidentType.Damage;
     var incident=new RentalIncident{BookingId=b.Id,IncidentNumber=$"INC-{stamp}-HO-{n:000}",Type=type,OccurredAt=now.AddHours(-1),Description=type==IncidentType.Theft?"Sample scenario: customer reports the vehicle missing from a locked parking area. Police and recovery follow-up required.":type==IncidentType.Breakdown?"Sample scenario: vehicle stopped with a warning light. Occupants are safe and awaiting roadside assistance.":"Sample scenario: customer reports a low-speed parking scrape. No injuries; repair cost awaits assessment.",Location="Suva Central",Status=spec.State=="IncidentDamage"?IncidentStatus.Open:IncidentStatus.Investigating,EstimatedCost=type==IncidentType.Theft?45000:250,EvidenceJson=JsonSerializer.Serialize(new[]{photo}),PoliceReference=type==IncidentType.Theft?$"SAMPLE-POL-{stamp}-01":null};
     b.Incidents.Add(incident);AddCase(b,"Customer incident",CaseStatus.InProgress,$"{incident.IncidentNumber}. Reference asset photo attached for illustration only; no real incident or evidentiary claim.");
     if(spec.State.StartsWith("Settlement")){
      b.Status=BookingStatus.Completed;b.IncidentClosedAt=now;b.RentalAgreement.Status=AgreementStatus.Completed;asset.Status=AssetStatus.Maintenance;
      db.MaintenanceJobs.Add(new MaintenanceJob{JobNumber=$"MNT-{stamp}-HO-{n:000}",AssetId=asset.Id,BranchId=asset.BranchId,ServiceType="Incident damage assessment",FaultDescription="Assess sample incident damage and safety before any future release.",SourceType="Damage",SourceReference=incident.IncidentNumber,ReportedAt=now,ReportedByName=actor});
      db.ManagementTasks.Add(new ManagementTask{BranchId=suva.Id,Category=TaskCategory.CustomerCase,Priority=TaskPriority.High,Title=$"Incident follow-up for {b.BookingNumber}",Description="Review recovery, evidence, responsibility and financial settlement.",SourceEntityType=nameof(Booking),SourceEntityId=b.Id,AssignedUserId=actorId,DueAt=now.AddHours(2)});
      var state=new IncidentSettlementState(1,[new(Guid.NewGuid(),"Bodywork assessment",250,"Pending","","Await written assessment",false,false)],[],"Sample incident investigation; no final liability decision.",localDate.AddDays(14),null,spec.State=="SettlementDisputed",null,null,null);
      db.IncidentSettlements.Add(new IncidentSettlement{BookingId=b.Id,DetailsJson=JsonSerializer.Serialize(state),PaymentDueDate=state.DueDate});
     }
    }
    if(spec.State.StartsWith("Completed")){
     b.Status=BookingStatus.Completed;b.UpdatedAt=now;asset.Status=AssetStatus.Available;b.RentalAgreement.Status=AgreementStatus.Completed;
     b.Inspections.Add(new RentalInspection{BookingId=b.Id,Type=InspectionType.Return,MeterReading=capture.TracksMeter?asset.CurrentMeterReading+150:null,FuelLevelPercent=capture.TracksFuel?100:null,ConditionNotes="Sample completed return: condition acceptable and accessories complete.",SignatureName="Sample return record",SignatureDataUrl=signature,CompletedByUserId=actorId,CompletedByName=actor,CompletedAt=end,CreatedAt=end,EvidenceJson=handover.EvidenceJson});
     b.BondStatus=BondStatus.Refunded;b.BondRefundAmount=b.BondAmountHeld;b.BondSettledAt=end;Pay(b,b.BondRefundAmount,PaymentType.BondRefund,"BR");
     b.Invoice=Invoice(b,end);
     if(spec.State=="CompletedCredit")db.CreditNotes.Add(RentalFinancials.ApplyCredit(b.Invoice,20,0,"Sample service goodwill adjustment approved for the handover case",actorId));
    }
   }
   if(spec.Category=="PROJECTOR"&&spec.State!="MissingDocument")AttachApproval(b);
   if(spec.Category=="SEDAN"&&spec.Key=="RET-CLEAN")History(asset);
   db.Bookings.Add(b);db.AuditEvents.Add(new AuditEvent{BranchId=b.BranchId,UserId=actorId,UserName=actor,Action="Handover scenario prepared",EntityType=nameof(Booking),EntityId=b.Id,Summary=$"{spec.Key}: {spec.Title}; fictional preparation data, not customer-performed activity."});
   scenarios.Add(new Scenario(spec.Key,spec.Title,b.Id,b.BookingNumber,spec.Action));created++;
  }
 }
 await db.SaveChangesAsync();
 var idsPath=Path.Combine(output,$"asset-ids-{stamp}.json");
 if(additions.Count>0)await File.WriteAllTextAsync(idsPath,JsonSerializer.Serialize(additions.Select(a=>a.Id),json));
 var seededIds=File.Exists(idsPath)?JsonSerializer.Deserialize<Guid[]>(File.ReadAllText(idsPath))??[]:Array.Empty<Guid>();
 foreach(var asset in originals.Where(a=>seededIds.Contains(a.Id))){
  var divisionOpen=await db.BranchDivisions.AnyAsync(l=>l.BranchId==asset.BranchId&&l.DivisionId==asset.DivisionId&&l.IsActive&&l.AcceptsBookings);
  var serviceOpen=await db.BranchDivisionServices.AnyAsync(l=>l.BranchId==asset.BranchId&&l.ServiceOfferingId==asset.ServiceOfferingId&&l.IsActive&&l.IsBookable);
  if(!divisionOpen||!serviceOpen){
   if(await db.BookingItems.AnyAsync(i=>i.AssetId==asset.Id))throw new InvalidOperationException("Cannot relocate allocated seeded asset "+asset.AssetNumber);
   asset.BranchId=suva.Id;asset.CurrentLocation=suva.Name+" rental depot";
  }
 }
 foreach(var asset in originals.Concat(additions).Where(x=>x.IsActive)){
  SeedAssetPhotoRepair.Restore(apiRoot,asset.Id,asset.PhotoUrlsJson);
  var urls=PhotoUrls(asset.PhotoUrlsJson).Where(url=>PhotoExists(asset,url)).ToList();
  var wrongBundled=asset.Name.Contains("Concrete Mixer",StringComparison.OrdinalIgnoreCase)&&urls.Any(x=>x.Contains("scissor-lift"));
  if(wrongBundled)urls=urls.Where(x=>!x.Contains("scissor-lift")).ToList();
  if(urls.Count==0){urls.Add(StorePhoto(asset,ImageFor(asset.AssetCategory?.Code??asset.Category??"",asset.Name,asset.Type)));repaired++;}
  asset.PhotoUrlsJson=JsonSerializer.Serialize(urls);
 }
 await db.SaveChangesAsync();await transaction.CommitAsync();
 if(scenarios.Count>0)await File.WriteAllTextAsync(Path.Combine(output,$"scenarios-{stamp}.json"),JsonSerializer.Serialize(scenarios,new JsonSerializerOptions(json){WriteIndented=true}));
 Console.WriteLine($"Created {created} primary scenarios, added {additions.Count} assets, repaired {repaired} existing photo sets. No email was queued.");
}
await Verify();

string? Value(string key){var pos=Array.IndexOf(args,key);return pos>=0&&pos+1<args.Length?args[pos+1]:null;}
Asset NewAsset(AssetCategory c,Branch branch){
 if(db.BranchDivisions.Any(l=>l.BranchId==branch.Id&&l.DivisionId==c.DivisionId&&(!l.IsActive||!l.AcceptsBookings))||db.BranchDivisionServices.Any(l=>l.BranchId==branch.Id&&l.ServiceOfferingId==c.ServiceOfferingId&&(!l.IsActive||!l.IsBookable)))branch=suva;
 var i=counters.GetValueOrDefault(c.Id)+1;counters[c.Id]=i;
 var vehicle=AssetCategoryPolicy.IsVehicle(c.DefaultAssetType??TypeFor(c.Code));
 string number;do{number=vehicle?$"LR{6000+additions.Count:0000}":$"CF-{c.Code.Replace("_","").Replace("-","")[..Math.Min(8,c.Code.Replace("_","").Replace("-","").Length)]}-{i:000}";if(used.Contains(number)){i++;counters[c.Id]=i;}}while(used.Contains(number)&&!vehicle);
 if(vehicle)while(used.Contains(number))number=$"LR{Random.Shared.Next(6000,9999)}";
 // A second run reuses the deterministic initial fleet, instead of duplicating it.
 var existing=originals.FirstOrDefault(x=>x.AssetNumber==number);if(existing!=null)return existing;used.Add(number);
 var (name,manufacturer,model,rate)=c.Code switch{
 "SEDAN" or "RENTAL_VEHICLE"=>("Toyota Corolla Sedan","Toyota","Corolla",135m),"HATCHBACK"=>("Toyota Yaris Hatchback","Toyota","Yaris",105m),"SUV"=>("Toyota RAV4 SUV","Toyota","RAV4",195m),"PICKUP"=>("Toyota Hilux Double Cab","Toyota","Hilux",225m),"MINIVAN"=>("Toyota Hiace Minivan","Toyota","Hiace",245m),
 "PROJECTOR"=>("Epson EB-X49 Projector","Epson","EB-X49",85m),"HEAVY_MACHINE"=>("CAT 320 Hydraulic Excavator","Caterpillar","320",650m),"FORKLIFT" or "DIESEL-FORKLIFT"=>("CAT DP30N 3 tonne forklift","Caterpillar","DP30N",285m),"GENSET"=>("CAT DE110E2 Generator","Caterpillar","DE110E2",470m),"GENERAL_EQUIPMENT"=>("Concrete Mixer 350L","Belle","350L",120m),"SCAFFOLD"=>("Frame Scaffolding Set","Industrial","Frame set",160m),"PORTABLE_TOILET"=>("Portable Toilet","PolyJohn","Single unit",48m),"BIG_BIN"=>("15 cubic metre General-Waste Bin","Industrial","15 m3",125m),_=>(c.Name+" rental unit","Industrial",c.Code,100m)};
 var meter=vehicle?25000m+i*120:c.Code is "SCAFFOLD" or "BIG_BIN" or "PORTABLE_TOILET"?0:120m+i*5;
 var a=new Asset{AssetNumber=number,Name=name,Manufacturer=manufacturer,Model=model,ModelYear=2023+i%2,Type=c.DefaultAssetType??TypeFor(c.Code),AssetCategoryId=c.Id,AssetCategory=c,Category=c.Name,DivisionId=c.DivisionId,Division=c.Division,ServiceOfferingId=c.ServiceOfferingId,ServiceOffering=c.ServiceOffering,BranchId=branch.Id,Branch=branch,Status=AssetStatus.Available,IsActive=true,DailyRate=rate,HireUnit=ChargeUnit.Day,InheritBond=true,PersonnelOverride=c.PersonnelRequirement,PersonnelRequirement=c.PersonnelRequirement,CurrentMeterReading=meter,MeterUnit=vehicle?"km":c.Code is "BIG_BIN" or "SCAFFOLD" or "PORTABLE_TOILET"?null:"hours",NextServiceDate=localDate.AddDays(60),PassingExpiry=vehicle?localDate.AddMonths(8):null,InsuranceExpiry=vehicle?localDate.AddMonths(6):null,AcquisitionDate=localDate.AddMonths(-18),AcquisitionCost=vehicle?45000:c.Code=="PROJECTOR"?1850:12000,OwnershipType="Owned",CurrentLocation=branch.Name+" rental depot",SerialNumber=vehicle?null:$"SN-{c.Code}-{stamp}-{i:003}",TurnaroundMinutes=vehicle?60:30,FulfilmentMode=AssetFulfilmentMode.PickupOrDelivery};
 a.PhotoUrlsJson=JsonSerializer.Serialize(new[]{StorePhoto(a,ImageFor(c.Code,a.Name,a.Type))});
 foreach(var def in c.AttributeDefinitions){
  var val=def.Code switch{"SEATS"=>c.Code=="MINIVAN"?"12":"5","TRANSMISSION"=>"Automatic","AIR_CONDITIONING"=>"true","BRIGHTNESS"=>"3600","RESOLUTION"=>"1024 x 768 (XGA)","HDMI_INPUT"=>"true","ACCESSORIES"=>"Remote, power lead, HDMI cable, carry case","CAPACITY"=>c.Code.Contains("FORKLIFT")?"3 tonnes":c.Code=="BIG_BIN"?"15 m3":c.Code=="GENERAL_EQUIPMENT"?"350 litres":"Standard manufacturer configuration",_=>null};
  val ??= originals.Where(x=>x.AssetCategoryId==c.Id).SelectMany(x=>x.AttributeValues).FirstOrDefault(x=>x.AttributeDefinitionId==def.Id&&!string.IsNullOrWhiteSpace(x.Value))?.Value;
  if(val!=null)a.AttributeValues.Add(new AssetAttributeValue{AssetId=a.Id,AttributeDefinitionId=def.Id,Value=val});
 }
 db.Assets.Add(a);additions.Add(a);
 if(!db.BranchDivisions.Local.Any(x=>x.BranchId==branch.Id&&x.DivisionId==c.DivisionId)&&!db.BranchDivisions.Any(x=>x.BranchId==branch.Id&&x.DivisionId==c.DivisionId))db.BranchDivisions.Add(new BranchDivision{BranchId=branch.Id,DivisionId=c.DivisionId,IsActive=true,AcceptsBookings=true});
 if(!db.BranchDivisionServices.Local.Any(x=>x.BranchId==branch.Id&&x.ServiceOfferingId==c.ServiceOfferingId)&&!db.BranchDivisionServices.Any(x=>x.BranchId==branch.Id&&x.ServiceOfferingId==c.ServiceOfferingId))db.BranchDivisionServices.Add(new BranchDivisionService{BranchId=branch.Id,DivisionId=c.DivisionId,ServiceOfferingId=c.ServiceOfferingId!.Value,IsActive=true,IsBookable=true});
 return a;
}
AssetType TypeFor(string code)=>code switch{"RENTAL_VEHICLE"=>AssetType.PassengerVehicle,"HEAVY_MACHINE"=>AssetType.HeavyEquipment,"FORKLIFT" or "DIESEL-FORKLIFT"=>AssetType.MaterialHandlingEquipment,"GENSET"=>AssetType.PowerEquipment,"SCAFFOLD"=>AssetType.Scaffolding,"PORTABLE_TOILET"=>AssetType.PortableSanitation,"BIG_BIN"=>AssetType.WasteContainer,_=>AssetType.LightEquipment};
string ImageFor(string code,string name,AssetType type){
 var text=(code+" "+name).ToUpperInvariant();
 return text.Contains("PROJECTOR")?"projector-eb-x49.jpg":text.Contains("HATCHBACK")||text.Contains("YARIS")?"hatchback-v2.png":text.Contains("SCAFFOLD")?"scaffolding-v2.jpg":text.Contains("TOILET")?"portable-toilet-v2.jpg":text.Contains("BIG_BIN")||type==AssetType.WasteContainer?"big-bin-v2.jpg":text.Contains("FORKLIFT")?"forklift.jpg":text.Contains("GENSET")||text.Contains("GENERATOR")?"generator.jpg":text.Contains("CONCRETE")?"concrete-mixer-v2.png":text.Contains("SCISSOR")?"scissor-lift.jpg":text.Contains("HILUX")||text.Contains("PICKUP")?"pickup-v2.jpg":text.Contains("MINIVAN")||text.Contains("MINIBUS")||text.Contains("HIACE")?"minibus.jpg":text.Contains("TRUCK")?"truck.jpg":text.Contains("SUV")||text.Contains("RAV4")||text.Contains("ZR-V")||text.Contains("CR-V")?"suv.jpg":type==AssetType.HeavyEquipment?"excavator.jpg":AssetCategoryPolicy.IsVehicle(type)?"sedan.jpg":type==AssetType.PropertyUnit?"office-v2.png":"concrete-mixer-v2.png";
}
string StorePhoto(Asset a,string file){var source=Path.Combine(root,"src/crems-web/public/catalog",file);if(!File.Exists(source))throw new FileNotFoundException("Missing catalogue photograph",source);var dir=Path.Combine(apiRoot,"App_Data/asset-images",a.Id.ToString("N"));Directory.CreateDirectory(dir);var stored="seed-"+file;if(!File.Exists(Path.Combine(dir,stored)))File.Copy(source,Path.Combine(dir,stored));return $"/api/public/assets/{a.Id}/photos/{stored}";}
string[] PhotoUrls(string value){try{return JsonSerializer.Deserialize<string[]>(value)??[];}catch(JsonException){return [];}}
bool PhotoExists(Asset a,string url){
 if(url.StartsWith("/catalog/")){var file=Path.GetFileName(url);return url=="/catalog/"+file&&File.Exists(Path.Combine(root,"src/crems-web/public/catalog",file))&&!file.EndsWith(".svg");}
 var p=$"/api/public/assets/{a.Id}/photos/";if(!url.StartsWith(p))return false;var name=url[p.Length..];return name==Path.GetFileName(name)&&File.Exists(Path.Combine(apiRoot,"App_Data/asset-images",a.Id.ToString("N"),name));
}
string ReferencePhoto(Asset a){var file=Path.Combine(root,"src/crems-web/public/catalog",ImageFor(a.AssetCategory!.Code,a.Name,a.Type));var mime=Path.GetExtension(file)==".png"?"image/png":"image/jpeg";var bytes=File.ReadAllBytes(file);if(bytes.Length>2*1024*1024){file=Path.Combine(root,"src/crems-web/public/catalog/projector-eb-x49.jpg");bytes=File.ReadAllBytes(file);mime="image/jpeg";}return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";}
string SampleSignature()=>"data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root,"ops/PrepareClientHandover/fixtures/sample-signature.png")));
void Pay(Booking b,decimal amount,PaymentType type,string suffix){b.Payments.Add(new RentalPayment{BookingId=b.Id,Amount=amount,Type=type,Method=PaymentMethod.Card,ReceiptNumber=$"HO-{stamp}-{b.BookingNumber[^3..]}-{suffix}",Note="Seeded sample receipt; no money was received or refunded.",RecordedByUserId=actorId,RecordedByName=actor,CreatedAt=b.Items.First().StartAt});}
void HoldBond(Booking b){b.BondAmountHeld=b.DepositRequired;b.BondStatus=BondStatus.Held;Pay(b,b.DepositRequired,PaymentType.BondCollection,"B");}
void AddCase(Booking b,string subject,CaseStatus status,string description){db.CustomerCases.Add(new CustomerCase{CaseNumber=$"CASE-{stamp}-HO-{b.BookingNumber[^3..]}-{subject.Split(' ')[0].ToUpperInvariant()}",CustomerId=customerId,BranchId=b.BranchId,Subject=$"{subject} — {b.BookingNumber}",Description=description,Type=CaseType.Enquiry,Status=status,Priority=CasePriority.Normal,DueAt=now.AddHours(4),Resolution=status==CaseStatus.Resolved?"Requested extension declined because the vehicle is required for a previously scheduled commitment.":null});}
async Task AddApproval(Booking b,SalesQuote? quote){
 var ctx=new ApprovalWorkflowService.BookingApprovalContext(b.BranchId,b.Items.First().Asset!.DivisionId,RentalFinancials.Total(b),!AssetCategoryPolicy.IsVehicle(b.Items.First().Asset!.Type),b.Charges.Any(x=>x.Category==ChargeCategory.Operator),false,b.Items.Select(x=>new ApprovalWorkflowService.BookingApprovalItem(x.Asset!.Type,x.StartAt,x.EndAt)).ToArray());
 var match=await ApprovalWorkflowService.MatchBookingAsync(db,ctx,default,quote!=null);
 var request=new ApprovalRequest{RequestNumber=$"APR-{stamp}-HO-{b.BookingNumber[^3..]}",BranchId=b.BranchId,EntityType=quote==null?nameof(Booking):nameof(SalesQuote),EntityId=quote?.Id??b.Id,Amount=RentalFinancials.Total(b),Reason="Client handover scenario: approval review required before release of offer or confirmation.",Type=ApprovalType.Booking,RequestedByUserId=actorId};
 if(match!=null)ApprovalWorkflowService.ConfigureFromMatch(request,match);else{request.TotalStages=1;request.StageDecisions.Add(new ApprovalStageDecision{StageNumber=1,StageName="Branch manager approval",AssignedRole=SystemRoles.BranchManager});}
 db.ApprovalRequests.Add(request);
}
RentalAgreement Agreement(Booking b,Asset a,DateTimeOffset at,string sig)=>new(){AgreementNumber=$"RA-{stamp}-HO-{b.BookingNumber[^3..]}",BookingId=b.Id,BranchId=b.BranchId,TermsVersion="2026-01",TermsJson=JsonSerializer.Serialize(new[]{new{title="Sample handover record",content="Fictional record prepared for client handover. The sample signature is not a legal customer signature."},new{title="Rental terms",content=a.Division?.DefaultRentalTerms??"Return the asset in the recorded condition by the agreed return time."}},json),CustomerSnapshotJson=JsonSerializer.Serialize(new{customerId,customer.CustomerNumber,customer.Name,type=customer.Type.ToString(),customer.Email,customer.Phone,customer.Address,customer.IdentificationNumber},json),AssetSnapshotJson=JsonSerializer.Serialize(new{assetId=a.Id,a.AssetNumber,a.Name,type=a.Type.ToString(),a.RegistrationNumber,a.SerialNumber},json),PricingSnapshotJson=JsonSerializer.Serialize(new{a.DailyRate,subtotal=b.Items.Sum(HirePricing.Amount),b.DiscountAmount,additionalCharges=RentalFinancials.Services(b),b.AdditionalChargesDescription,b.TaxRate,taxAmount=HirePricing.Tax(b),total=RentalFinancials.Total(b),b.DepositRequired,hireUnit=b.Items.First().HireUnit,hireQuantity=HirePricing.Quantity(b.Items.First()),lines=RentalFinancials.Lines(b)},OrganizationConfiguration.Json),CustomerSignatureName="Sample record — not a customer signature",CustomerSignatureDataUrl=sig,CustomerSignedAt=at,ApprovedByUserId=actorId,ApprovedByName=actor,ApprovedAt=at,Status=AgreementStatus.Active};
RentalInvoice Invoice(Booking b,DateTimeOffset at){var inv=new RentalInvoice{InvoiceNumber=$"INV-{stamp}-HO-{b.BookingNumber[^3..]}",BookingId=b.Id,Subtotal=RentalFinancials.Net(b),TaxAmount=HirePricing.Tax(b),Total=RentalFinancials.Total(b),AmountPaid=RentalFinancials.Paid(b),BalanceDue=Math.Max(0,RentalFinancials.Total(b)-RentalFinancials.Paid(b)),Status=RentalFinancials.Paid(b)>=RentalFinancials.Total(b)?InvoiceStatus.Paid:InvoiceStatus.PartiallyPaid,IssuedAt=at,LineItemsJson="[]"};foreach(var line in RentalFinancials.Lines(b))inv.Lines.Add(new InvoiceLine{InvoiceId=inv.Id,Description=line.Description,Quantity=line.Quantity,UnitPrice=line.Rate,TaxRate=b.TaxRate,IsTaxable=line.IsTaxable});inv.LineItemsJson=JsonSerializer.Serialize(inv.Lines.Select(l=>new{l.Description,l.Quantity,l.UnitPrice,l.TaxRate,l.IsTaxable}));foreach(var p in b.Payments.Where(x=>x.Type==PaymentType.RentalCharge))db.PaymentAllocations.Add(new PaymentAllocation{PaymentId=p.Id,InvoiceId=inv.Id,Amount=p.Amount,AllocatedByUserId=actorId});return inv;}
void AttachApproval(Booking b){var file=Path.Combine(root,"ops/PrepareClientHandover/fixtures/sample-event-approval.pdf");var bytes=File.ReadAllBytes(file);var storage=Path.Combine(customerId.ToString("N"),$"handover-{b.Id:N}-event-approval.pdf");var path=Path.Combine(apiRoot,"App_Data/customer-documents",storage);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);db.DocumentRecords.Add(new DocumentRecord{DocumentNumber=$"DOC-{stamp}-HO-{b.BookingNumber[^3..]}",EntityType=nameof(Booking),EntityId=b.Id,BranchId=b.BranchId,Type="Event Approval",FileName="Sample event approval.pdf",StoragePath=storage,ContentHash=Convert.ToHexString(SHA256.HashData(bytes))});}
void History(Asset a){for(var month=1;month<=4;month++)db.MaintenanceJobs.Add(new MaintenanceJob{JobNumber=$"MNT-{stamp}-HIST-{month}",AssetId=a.Id,BranchId=a.BranchId,ServiceType=month==4?"Vehicle passing":"Preventive maintenance",FaultDescription=month==4?"Scheduled annual passing inspection completed.":"Scheduled preventive service and condition checks completed.",Status=MaintenanceStatus.Completed,ReportedAt=today.AddDays(-30*month),StartedAt=today.AddDays(-30*month).AddHours(9),CompletedAt=today.AddDays(-30*month).AddHours(15),ReleasedAt=today.AddDays(-30*month).AddHours(16),ReleasedByName=actor,ActualCost=month==4?85:180,ExternalServiceCost=month==4?85:0,LabourCost=month==4?0:80,PartsCost=month==4?0:100,DowntimeHours=7,NextServiceDate=month==4?localDate.AddMonths(8):localDate.AddDays(60),IsPreventive=month!=4,ReportedByName=actor});}
async Task Verify(){
 db.ChangeTracker.Clear();var assets=await db.Assets.AsNoTracking().Where(x=>x.IsActive).ToListAsync();var missing=assets.Where(a=>!PhotoUrls(a.PhotoUrlsJson).Any(u=>PhotoExists(a,u))).Select(x=>x.AssetNumber).ToArray();
 var suite=await db.Bookings.AsNoTracking().Include(x=>x.Items).ThenInclude(x=>x.Asset).Include(x=>x.Customer).Include(x=>x.Branch).Include(x=>x.Inspections).Include(x=>x.Payments).Include(x=>x.Charges).Include(x=>x.Invoice).Where(x=>x.BookingNumber.StartsWith(prefix)).AsSplitQuery().ToListAsync();
 var quotes=await db.SalesQuotes.AsNoTracking().Where(x=>x.CustomerId==customerId&&x.QuoteNumber.StartsWith($"QUO-{stamp}-HO-")).ToListAsync();
 var failures=new List<string>();
 var seedIdsFile=Path.Combine(output,$"asset-ids-{stamp}.json");var newIds=File.Exists(seedIdsFile)?JsonSerializer.Deserialize<Guid[]>(File.ReadAllText(seedIdsFile))??[]:additions.Select(a=>a.Id).ToArray();
 var unlinked=await db.Assets.Where(a=>newIds.Contains(a.Id)&&a.IsActive&&(!db.BranchDivisions.Any(l=>l.BranchId==a.BranchId&&l.DivisionId==a.DivisionId&&l.IsActive&&l.AcceptsBookings)||!db.BranchDivisionServices.Any(l=>l.BranchId==a.BranchId&&l.ServiceOfferingId==a.ServiceOfferingId&&l.IsActive&&l.IsBookable))).Select(a=>a.AssetNumber).ToArrayAsync();
 if(unlinked.Length>0)failures.Add("Unbookable branch links: "+string.Join(",",unlinked));
 if(missing.Length>0)failures.Add("Assets missing a local photograph: "+string.Join(",",missing));
 foreach(var b in suite){if(b.CustomerId!=customerId||b.Items.Count!=1||b.Items.Any(x=>x.EndAt<=x.StartAt||x.DailyRate<=0))failures.Add("Invalid booking ownership/dates/rate "+b.BookingNumber);if(b.Invoice is {} inv&&(inv.Total<0||inv.BalanceDue!=Math.Max(0,inv.Total-inv.AmountPaid)))failures.Add("Invoice balance mismatch "+b.BookingNumber);}
 var readiness=new List<object>();foreach(var b in suite.Where(x=>x.Status==BookingStatus.Confirmed))readiness.Add(new{b.BookingNumber,reasons=await PickupReadiness.Check(db,b,default)});
 await File.WriteAllTextAsync(Path.Combine(output,"photo-manifest.json"),JsonSerializer.Serialize(assets.Select(a=>new{a.Id,a.AssetNumber,status=a.Status.ToString(),urls=PhotoUrls(a.PhotoUrlsJson)}),new JsonSerializerOptions(json){WriteIndented=true}));
 var report=new{date=localDate,customer=email,primaryScenarios=scenarios.Count>0?scenarios.Count:suite.Count(x=>!x.BookingNumber.EndsWith("-NEXT")),bookings=suite.Count,quotations=quotes.Count,totalAssets=await db.Assets.CountAsync(),activeAssets=assets.Count,availableAssets=assets.Count(x=>x.Status==AssetStatus.Available),activeAssetsWithPhotos=assets.Count-missing.Length,missingPhotos=missing,readiness,failures,records=suite.OrderBy(x=>x.BookingNumber).Select(x=>new{x.Id,x.BookingNumber,status=x.Status.ToString(),asset=x.Items.First().Asset!.AssetNumber,assetName=x.Items.First().Asset!.Name,start=x.Items.First().StartAt,end=x.Items.First().EndAt,total=RentalFinancials.Total(x),paid=RentalFinancials.Paid(x),x.DepositRequired,x.BondAmountHeld,x.Notes})};
 await File.WriteAllTextAsync(Path.Combine(output,$"verification-{stamp}.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions(json){WriteIndented=true}));Console.WriteLine($"Verified {suite.Count} handover bookings, {quotes.Count} quotations, {assets.Count-missing.Length}/{assets.Count} active assets with accessible local photo files; available assets {assets.Count(x=>x.Status==AssetStatus.Available)}. Integrity failures: {failures.Count}.");if(failures.Count>0)throw new InvalidOperationException(string.Join("; ",failures));
}
record Spec(string Key,string Title,string Category,string State,int Start,int End,string Action);
record Scenario(string Key,string Title,Guid BookingId,string BookingNumber,string Action);
