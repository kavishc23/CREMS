using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Domain.Corporate;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
var config=new ConfigurationBuilder().SetBasePath(Path.Combine(root,"src/CREMS.Api")).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",optional:true).AddUserSecrets("crems-cs400-development").AddEnvironmentVariables().Build();
var connection=config.GetConnectionString("DefaultConnection")??throw new Exception("No connection configured");
var target=new SqlConnectionStringBuilder(connection);
if(target.InitialCatalog!="Crems"||!(target.DataSource.StartsWith("localhost")||target.DataSource.StartsWith("127.0.0.1")))throw new Exception("Local Crems only");
await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
using var suppress=db.SuppressNotifications();
await using var tx=await db.Database.BeginTransactionAsync();
var branch=await db.Branches.SingleAsync(x=>x.Code=="SUV");
var division=await db.Divisions.SingleAsync(x=>x.Code=="MOTORS");
var service=await db.ServiceOfferings.FirstAsync(x=>x.DivisionId==division.Id&&x.Type==ServiceOfferingType.VehicleRental&&x.IsActive);
var category=await db.AssetCategories.FirstAsync(x=>x.DivisionId==division.Id&&x.Name=="Training passenger vehicle");
var manager=await db.Users.SingleAsync(x=>x.FullName=="Laisenia Vakalalabure");
var now=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(12));var day=new DateTimeOffset(now.Year,now.Month,now.Day,9,0,0,TimeSpan.FromHours(12));
var checklistJson=(await db.InspectionTemplates.FirstOrDefaultAsync(x=>x.AssetCategoryId==category.Id&&x.Stage==InspectionStage.PreHire&&x.IsActive))?.ChecklistJson;
var labels=new List<string>();
if(checklistJson!=null){using var parsed=JsonDocument.Parse(checklistJson);foreach(var section in parsed.RootElement.EnumerateArray())foreach(var item in section.GetProperty("items").EnumerateArray())labels.Add(section.GetProperty("section").GetString()+": "+(item.ValueKind==JsonValueKind.String?item.GetString():item.GetProperty("label").GetString()));}
var photoFile=Path.Combine(root,"src/crems-web/public/catalog/sedan.jpg");var photo="data:image/jpeg;base64,"+Convert.ToBase64String(await File.ReadAllBytesAsync(photoFile));
if(args.Contains("--refresh-training-photo")){
var inspections=await db.RentalInspections.Where(x=>x.Booking!.BookingNumber.StartsWith("MAN-SUV-20261118-")).ToListAsync();
foreach(var inspection in inspections){var node=System.Text.Json.Nodes.JsonNode.Parse(inspection.EvidenceJson??"{}")!;node["photos"]=new System.Text.Json.Nodes.JsonArray(photo);inspection.EvidenceJson=node.ToJsonString();}
await db.SaveChangesAsync();await tx.CommitAsync();Console.WriteLine("Training sample vehicle photos updated: "+inspections.Count);return;
}
var output=new List<object>();
foreach(var mode in new[]{"BOOK","QUOTE","QAPP","ACCEPT","PICK","RETURN","MAINT"}){
 var number="MAN-SUV-20261118-"+mode;
 if(await db.Assets.AnyAsync(x=>x.AssetNumber==number)){Console.WriteLine("Already exists: "+number);continue;}
 var asset=new Asset{AssetNumber=number,Name="TRAINING ONLY Suva manual "+mode,Type=AssetType.PassengerVehicle,Status=mode=="RETURN"?AssetStatus.Rented:AssetStatus.Available,DivisionId=division.Id,ServiceOfferingId=service.Id,AssetCategoryId=category.Id,Category=category.Name,BranchId=branch.Id,RegistrationNumber="TRAIN-"+mode,Manufacturer="Toyota",Model="Corolla training example",ModelYear=2025,MeterUnit="km",CurrentMeterReading=25000,DailyRate=150,HireUnit=ChargeUnit.Day,InheritBond=false,DefaultBondAmount=0,PersonnelRequirement=PersonnelRequirement.None,PersonnelOverride=PersonnelRequirement.None,FulfilmentMode=AssetFulfilmentMode.PickupOnly,NextServiceDate=DateOnly.FromDateTime(now.DateTime).AddMonths(6),InsuranceExpiry=DateOnly.FromDateTime(now.DateTime).AddYears(1),PassingExpiry=DateOnly.FromDateTime(now.DateTime).AddYears(1),CurrentLocation="Suva training bay",IsActive=true,PhotoUrlsJson="[]"};db.Assets.Add(asset);
 if(mode=="MAINT"){output.Add(new{mode,asset=number,assetId=asset.Id});continue;}
 var customer=new Customer{CustomerNumber="MAN-CUST-20261118-"+mode,Name="TRAINING ONLY Suva manual customer "+mode,Type=CustomerType.Individual,Phone="5550000",Address="Suva training example",IdentificationNumber="TRAINING ONLY NO REAL ID",Email=null};db.Customers.Add(customer);
 var status=mode=="PICK"?BookingStatus.Confirmed:mode=="RETURN"?BookingStatus.ConvertedToRental:BookingStatus.Draft;
 var start=mode=="RETURN"?day.AddDays(-1):mode=="PICK"?day:day.AddDays(2);
 var end=mode=="RETURN"?day: start.AddDays(2);
 var booking=new Booking{BookingNumber=number,CustomerId=customer.Id,BranchId=branch.Id,Status=status,TaxRate=0,DepositRequired=0,BondStatus=BondStatus.NotRequired,Notes="TRAINING ONLY FOR ILLUSTRATED STAFF MANUAL. Fictional customer. No real hire, payment or contract. Do not email."};
 booking.Items.Add(new BookingItem{AssetId=asset.Id,StartAt=start,EndAt=end,DailyRate=150,HireUnit=ChargeUnit.Day});db.Bookings.Add(booking);
 if(mode is "PICK" or "RETURN"){
 booking.AuthorizedDrivers.Add(new AuthorizedDriver{FullName="TRAINING DEMO DRIVER",LicenceNumber="123456",LicenceClass="1",LicenceExpiry=new DateOnly(2030,1,1),Verified=true,IsPrimary=true});
 booking.Inspections.Add(new RentalInspection{Type=InspectionType.Handover,IdentificationVerified=true,DriverLicenceVerified=true,MeterReading=25000,FuelLevelPercent=100,ConditionNotes="TRAINING ONLY all checks passed; no damage observed.",DamageNotes="None observed",PaymentVerified=true,CompletedByUserId=manager.Id,CompletedByName=manager.FullName,EvidenceJson=JsonSerializer.Serialize(new{checklist=labels,photos=new[]{photo},accessories="One key and safety kit",checklistResults=labels.Select(label=>new{label,passed=true})})});
 booking.Payments.Add(new RentalPayment{Type=PaymentType.RentalCharge,Amount=mode=="RETURN"?150:300,ReceiptNumber=number+"-DEMO-NO-MONEY",Method=PaymentMethod.Cash,RecordedByUserId=manager.Id,RecordedByName="TRAINING ONLY",Note="Fictional practice receipt; no actual money received."});
 }
 if(mode=="RETURN")booking.RentalAgreement=new RentalAgreement{BookingId=booking.Id,BranchId=branch.Id,AgreementNumber="MAN-AGR-20261118-RETURN",TermsVersion="TRAINING",TermsJson="[\"TRAINING ONLY NO LEGAL CONTRACT\"]",CustomerSnapshotJson=JsonSerializer.Serialize(new{name=customer.Name,customerNumber=customer.CustomerNumber}),AssetSnapshotJson=JsonSerializer.Serialize(new{name=asset.Name,assetNumber=asset.AssetNumber}),PricingSnapshotJson="{\"total\":150,\"depositRequired\":0}",CustomerSignatureName="TRAINING DEMO",ApprovedByUserId=manager.Id,ApprovedByName=manager.FullName,ApprovedAt=now,CustomerSignedAt=now.AddDays(-1),Status=AgreementStatus.Active};
 SalesQuote? quote=null;
 if(mode is "QUOTE" or "QAPP" or "ACCEPT"){
 quote=new SalesQuote{QuoteNumber="MAN-QUO-20261118-"+mode,CustomerId=customer.Id,BranchId=branch.Id,DivisionId=division.Id,Status=mode=="ACCEPT"?QuoteStatus.Accepted:QuoteStatus.Draft,ConvertedBookingId=booking.Id,ValidUntil=now.AddDays(10),Subtotal=300,Total=300,LineItemsJson="[{\"description\":\"Training vehicle hire\",\"quantity\":2,\"rate\":150,\"costRate\":0,\"unit\":\"Day\",\"category\":\"BaseHire\",\"isTaxable\":true}]"};db.SalesQuotes.Add(quote);
 }
 if(mode is "BOOK" or "QAPP"){
 var approval=new ApprovalRequest{RequestNumber="MAN-APR-20261118-"+mode,BranchId=branch.Id,Type=ApprovalType.Booking,EntityType=quote==null?nameof(Booking):nameof(SalesQuote),EntityId=quote?.Id??booking.Id,Amount=300,Reason=number+": Training only review by Suva Branch Manager",RequestedByUserId=manager.Id,Status=ApprovalStatus.Pending};
 approval.StageDecisions.Add(new ApprovalStageDecision{StageNumber=1,StageName="Suva manager training review",AssignedRole="BranchManager",Status=ApprovalStatus.Pending});db.ApprovalRequests.Add(approval);
 }
 output.Add(new{mode,asset=number,assetId=asset.Id,booking=number,bookingId=booking.Id,quoteId=quote?.Id});
}
await db.SaveChangesAsync();await tx.CommitAsync();
var file=Path.Combine(root,"output/rental-manager-suva-manual/training-records.json");await File.WriteAllTextAsync(file,JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Training records created with notifications suppressed. "+output.Count+" rows in manifest.");
