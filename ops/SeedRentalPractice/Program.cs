using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Explicit, additive local fixtures. Never restore availability by clearing real work.
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var apiRoot = Path.Combine(root, "src/CREMS.Api");
string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(12)).DateTime);
var batch = Option("--batch") ?? today.ToString("yyyyMMdd");
if (!Regex.IsMatch(batch, "^[A-Za-z0-9]{1,12}$")) throw new ArgumentException("Batch must be 1–12 letters or numbers.");
var count = int.Parse(Option("--per-stage") ?? "30");
if (count is < 1 or > 200) throw new ArgumentException("Per-stage count must be 1–200.");
var config = new ConfigurationBuilder().SetBasePath(apiRoot).AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional:true).AddUserSecrets("crems-cs400-development").AddEnvironmentVariables().Build();
var connection = config.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connection) && File.Exists(Path.Combine(root,".env")))
{
    var values = File.ReadLines(Path.Combine(root,".env")).Where(x=>!string.IsNullOrWhiteSpace(x)&&!x.TrimStart().StartsWith('#')&&x.Contains('='))
        .Select(x=>x.Split('=',2)).ToDictionary(x=>x[0].Trim(),x=>x[1].Trim().Trim('"','\''));
    if (values.TryGetValue("CREMS_DB_PASSWORD", out var password)) connection = new SqlConnectionStringBuilder {
        DataSource="localhost,1433", InitialCatalog=values.GetValueOrDefault("CREMS_DB_NAME","Crems"), UserID="sa", Password=password, TrustServerCertificate=true }.ConnectionString;
}
if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Configure the local database connection or CREMS_DB_PASSWORD in .env.");
var target = new SqlConnectionStringBuilder(connection);
if (!target.InitialCatalog.Equals("Crems",StringComparison.OrdinalIgnoreCase) ||
    !new[]{"localhost","127.0.0.1"}.Contains(target.DataSource.Split(',')[0],StringComparer.OrdinalIgnoreCase))
    throw new InvalidOperationException("Practice seeding is restricted to the local Crems database.");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
if ((await db.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidOperationException("Apply application migrations before seeding practice records.");
var prefix=$"TRN-{batch}-";
var existing=await db.Assets.CountAsync(x=>x.AssetNumber.StartsWith(prefix));
var services=await db.ServiceOfferings.Include(x=>x.Division).Where(x=>x.IsActive&&x.Type==ServiceOfferingType.VehicleRental).ToListAsync();
var service=services.OrderByDescending(x=>x.Division!.Code=="MOTORS").FirstOrDefault() ?? throw new InvalidOperationException("Configure an active vehicle rental service first.");
var branches=await db.Branches.Where(b=>b.IsActive&&b.IsPublic&&db.BranchDivisions.Any(l=>l.BranchId==b.Id&&l.DivisionId==service.DivisionId&&l.IsActive&&l.AcceptsBookings)&&db.BranchDivisionServices.Any(l=>l.BranchId==b.Id&&l.ServiceOfferingId==service.Id&&l.IsActive&&l.IsBookable)).OrderBy(x=>x.Code).ToListAsync();
if(branches.Count==0)throw new InvalidOperationException("No bookable branches are linked to the vehicle service.");
Console.WriteLine($"Local Crems practice batch {batch}; {count} free vehicles + {count} pickups + {count} active returns.");
Console.WriteLine($"Service: {service.Name}; branches: {string.Join(", ",branches.Select(x=>x.Name))}. Existing batch vehicles: {existing}.");
if (!args.Contains("--apply")&&!args.Contains("--verify")&&!args.Contains("--record-odometers")) { Console.WriteLine("Read-only preview. Use --apply to add fixtures; --verify to audit this batch. Use a NEW --batch to replenish consumed examples."); return; }
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);json.Converters.Add(new JsonStringEnumConverter());
string J(object value)=>JsonSerializer.Serialize(value,json);
decimal Odometer(string stage, int index)
{
    var offset = stage == "FREE" ? 0 : stage == "PICK" ? 1 : 2;
    var reading = 12000m + offset * 10000m + index * 137m;
    var interval = MaintenanceRules.ReadMeterRule(service.MaintenanceRulesJson).Interval;
    // Keep the initial counter below the existing first service threshold.
    return interval.HasValue && reading >= interval.Value
        ? decimal.Round(interval.Value * (0.20m + ((offset * 17 + index) % 50) / 100m), 2)
        : reading;
}
if(args.Contains("--record-odometers"))
{
    using var suppression = db.SuppressNotifications();
    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var vehicles = await db.Assets.Where(x => x.AssetNumber.StartsWith(prefix)).ToListAsync();
    var changed = 0; var skipped = 0;
    foreach (var asset in vehicles)
    {
        var items = await db.BookingItems.Where(x => x.AssetId == asset.Id).Include(x => x.Booking).ThenInclude(x => x!.Inspections).ToListAsync();
        // Do not rewrite measurements or inspections already entered during practice.
        if (asset.CurrentMeterReading > 0 || await db.AssetMeterReadings.AnyAsync(x => x.AssetId == asset.Id) ||
            items.Any(x => x.Booking!.Inspections.Any(i => i.CompletedByName != "Training fixture importer" || i.Type == InspectionType.Return)))
        { skipped++; continue; }
        var parts = asset.AssetNumber.Split('-');
        var reading = Odometer(parts[^2], int.Parse(parts[^1]));
        asset.CurrentMeterReading = reading; asset.MeterUnit = "km";
        var handover = items.SelectMany(x => x.Booking!.Inspections).SingleOrDefault(x => x.Type == InspectionType.Handover);
        foreach (var inspection in items.SelectMany(x => x.Booking!.Inspections)) inspection.MeterReading = reading;
        foreach (var inspection in await db.AssetInspections.Where(x => x.AssetId == asset.Id && x.CompletedByName == "Training fixture importer").ToListAsync()) inspection.MeterReading = reading;
        foreach (var entry in await db.AssetLifecycleEvents.Where(x => x.AssetId == asset.Id && x.RecordedByName == "Training fixture importer").ToListAsync()) entry.MeterReading = reading;
        db.AssetMeterReadings.Add(new AssetMeterReading { AssetId = asset.Id, BookingId = handover?.BookingId, Type = MeterType.Odometer, Unit = "km", Reading = reading,
            FuelPercent = handover?.FuelLevelPercent, Source = handover is null ? MeterReadingSource.Manual : MeterReadingSource.PreHireInspection,
            RecordedAt = handover?.CompletedAt ?? DateTimeOffset.UtcNow, RecordedByUserId = Guid.Empty });
        changed++;
    }
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    Console.WriteLine($"Recorded initial odometers for {changed} vehicles; retained {skipped} vehicles with existing readings/practice activity.");
}
if(args.Contains("--apply"))
{
 using var suppression=db.SuppressNotifications();
 await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
 var category=await db.AssetCategories.FirstOrDefaultAsync(x=>x.DivisionId==service.DivisionId&&x.Code=="TRAINING_CAR");
 if(category is null){category=new AssetCategory{Code="TRAINING_CAR",Name="Training passenger vehicle",DivisionId=service.DivisionId,ServiceOfferingId=service.Id,DefaultAssetType=AssetType.PassengerVehicle,DefaultMeterType="km",PersonnelRequirement=PersonnelRequirement.None};db.AssetCategories.Add(category);}
 var labels=new[]{"Body, glass and existing damage checked","Tyres, lights, brakes and seatbelts checked","Odometer and fuel recorded","Keys, spare tyre, jack and accessories checked"};
 var checklist=J(new[]{new{section="Training vehicle",items=labels.Select(label=>new{label})}});
 foreach(var stage in new[]{InspectionStage.PreHire,InspectionStage.PostHire})
  if(!await db.InspectionTemplates.AnyAsync(x=>x.AssetCategoryId==category.Id&&x.Stage==stage&&x.IsActive))db.InspectionTemplates.Add(new InspectionTemplate{AssetCategoryId=category.Id,Name=$"Training vehicle {stage}",Stage=stage,ChecklistJson=checklist,RequiresCustomerSignature=true});
 await db.SaveChangesAsync();
 var signature="data:image/png;base64,"+Convert.ToBase64String(await File.ReadAllBytesAsync(Path.Combine(root,"ops/SeedRentalPractice/fixtures/training-signature.png")));
 var photoFile=Path.Combine(root,"src/crems-web/public/catalog/sedan.jpg");
 var photo="data:image/jpeg;base64,"+Convert.ToBase64String(await File.ReadAllBytesAsync(photoFile));
 var created=0;
 foreach(var stage in new[]{"FREE","PICK","RETURN"})for(var i=1;i<=count;i++)
 {
  var tag=$"{prefix}{stage}-{i:000}";
  if(await db.Assets.AnyAsync(x=>x.AssetNumber==tag))continue;
  var branch=branches[(i-1)%branches.Count];
  var active=stage=="RETURN";
  var asset=new Asset{AssetNumber=tag,Name=$"TRAINING ONLY Toyota Corolla {stage} {i:000}",Type=AssetType.PassengerVehicle,DivisionId=service.DivisionId,ServiceOfferingId=service.Id,AssetCategoryId=category.Id,AssetCategory=category,Category=category.Name,BranchId=branch.Id,Branch=branch,Division=service.Division,ServiceOffering=service,Manufacturer="Toyota",Model="Corolla",ModelYear=2024,Status=active?AssetStatus.Rented:AssetStatus.Available,CurrentMeterReading=Odometer(stage,i),MeterUnit="km",DailyRate=100,HireUnit=ChargeUnit.Day,UseServiceHireRate=false,InheritBond=false,DefaultBondAmount=100,PersonnelOverride=PersonnelRequirement.None,FulfilmentMode=AssetFulfilmentMode.PickupOnly,TurnaroundMinutes=0,NextServiceDate=today.AddYears(1),InsuranceExpiry=today.AddYears(1),PassingExpiry=today.AddYears(1),PhotoUrlsJson=J(new[]{"/catalog/sedan.jpg"}),CurrentLocation=$"{branch.Name} — fictional training fleet",OwnershipType="Training fixture"};
  db.Assets.Add(asset);created++;
  db.AssetMeterReadings.Add(new AssetMeterReading{AssetId=asset.Id,Type=MeterType.Odometer,Unit="km",Reading=asset.CurrentMeterReading!.Value,Source=MeterReadingSource.Manual,RecordedByUserId=Guid.Empty});
  if(stage=="FREE")continue;
  var custNumber=$"{prefix}{stage}-C{i:000}";
  var customer=new Customer{CustomerNumber=custNumber,Name=$"Training {stage} Customer {batch} {i:000}",Type=CustomerType.Individual,Address="Fictional local training record",IdentificationNumber=$"TRAINING-{batch}-{stage}-{i:000}",Email=null,IsActive=true,IsBlocked=false};
  db.Customers.Add(customer);
  var endDate=active?today.AddDays((i-1)%3-1):today.AddDays(2);
  var start=RentalAvailability.FijiTime(active?today.AddDays(-3):today,new TimeOnly(8,0));
  var end=RentalAvailability.FijiTime(endDate,new TimeOnly(16,0));
  var scenario=active?(i-1)%3:i%4;
  var bond=stage=="PICK"&&scenario==0?0m:100m;
  var booking=new Booking{BookingNumber=$"{prefix}{stage}-{i:000}",Customer=customer,CustomerId=customer.Id,Branch=branch,BranchId=branch.Id,Status=active?BookingStatus.ConvertedToRental:BookingStatus.Confirmed,TaxRate=service.Division!.DefaultTaxRate,DepositRequired=bond,BondAmountHeld=active?bond:0,BondStatus=bond==0?BondStatus.NotRequired:active?BondStatus.Held:BondStatus.AwaitingPayment,ApprovedAt=DateTimeOffset.UtcNow,Notes=$"FICTIONAL TRAINING ONLY. Batch {batch}. No actual identity verification, payment or contract. {(active?new[]{"Overdue return; hire prepaid; refund bond","Return due today; hire payable at return","Future due date; partial hire payment"}[scenario]:new[]{"No bond; collect hire at pickup","Collect hire and bond at pickup","Hire prepaid; collect bond","Partial hire paid; collect balance and bond"}[scenario])}. All fixtures use their own asset; existing records are untouched."};
  var item=new BookingItem{BookingId=booking.Id,Asset=asset,AssetId=asset.Id,StartAt=start,EndAt=end,DailyRate=100,HireUnit=ChargeUnit.Day,HireIsTaxable=true};booking.Items.Add(item);db.Bookings.Add(booking);
  await ConfiguredHireRules.CaptureIfMissing(db,booking,default);
  var total=RentalFinancials.Total(booking);
  var paid=active?(scenario==0?total:scenario==1?0:decimal.Round(total/2,2)):scenario==2?total:scenario==3?decimal.Round(total/2,2):0;
  void Payment(PaymentType type,decimal amount,string suffix){if(amount<=0)return;var payment=new RentalPayment{BookingId=booking.Id,Type=type,Method=PaymentMethod.Cash,Amount=amount,ReceiptNumber=$"{prefix}{stage}-{i:000}-{suffix}",RecordedByName="Training fixture importer",RecordedByUserId=Guid.Empty,Note="FICTIONAL TEST ENTRY — no money received"};booking.Payments.Add(payment);db.RentalPayments.Add(payment);}
  Payment(PaymentType.RentalCharge,paid,"H");
  if(!active)continue;
  Payment(PaymentType.BondCollection,bond,"B");
  var passed=labels.Select(x=>"Training vehicle: "+x).ToArray();
  var evidence=J(new{checklist=passed,checklistResults=passed.Select(label=>new{label,passed=true}),photos=new[]{photo},accessories="Training sample: keys, spare tyre and jack",damageZones=Array.Empty<object>()});
  var inspection=new RentalInspection{BookingId=booking.Id,Type=InspectionType.Handover,IdentificationVerified=true,DriverLicenceVerified=true,PaymentVerified=true,MeterReading=asset.CurrentMeterReading,FuelLevelPercent=100,ConditionNotes="Fictional training handover: intact, no existing damage.",DamageNotes="None — training fixture",SignatureName=customer.Name,SignatureDataUrl=signature,EvidenceJson=evidence,CompletedByName="Training fixture importer",CompletedAt=start};booking.Inspections.Add(inspection);db.RentalInspections.Add(inspection);
  db.AssetInspections.Add(new AssetInspection{AssetId=asset.Id,BookingId=booking.Id,Stage=InspectionStage.PreHire,Outcome=InspectionOutcome.Passed,ResponsesJson=J(passed),EvidenceJson=J(new[]{photo}),MeterReading=asset.CurrentMeterReading,FuelPercent=100,CustomerSignatureName=customer.Name,CustomerSignatureDataUrl=signature,StaffSignatureName="Training fixture importer",Notes="Synthetic fixture, not a physical inspection",CompletedByName="Training fixture importer",CompletedAt=start});
  db.AuthorizedDrivers.Add(new AuthorizedDriver{BookingId=booking.Id,FullName=customer.Name,LicenceNumber=$"9900{i:0000}",LicenceClass="1",LicenceExpiry=today.AddYears(1),Verified=true,IsPrimary=true});
  db.RentalAgreements.Add(new RentalAgreement{AgreementNumber=$"{prefix}RA-{i:000}",BookingId=booking.Id,BranchId=branch.Id,TermsVersion="TRAINING-ONLY",TermsJson=J(new[]{new{title="Fictional training agreement",content="Synthetic test fixture. No actual hire, payment or signature."}}),CustomerSnapshotJson=J(new{customerId=customer.Id,customer.CustomerNumber,customer.Name,type="Individual",email=(string?)null,phone=(string?)null,customer.Address,customer.IdentificationNumber}),AssetSnapshotJson=J(new{assetId=asset.Id,asset.AssetNumber,asset.Name,type=asset.Type.ToString(),registrationNumber=(string?)null,serialNumber=(string?)null}),PricingSnapshotJson=J(new{dailyRate=100m,subtotal=booking.Items.Sum(HirePricing.Amount),discountAmount=0m,additionalCharges=0m,taxRate=booking.TaxRate,taxAmount=HirePricing.Tax(booking),total,depositRequired=bond,hireUnit=ChargeUnit.Day,hireQuantity=HirePricing.Quantity(item),lines=RentalFinancials.Lines(booking)}),CustomerSignatureName=customer.Name,CustomerSignatureDataUrl=signature,CustomerSignedAt=start,ApprovedByName="Training fixture importer",ApprovedAt=start,Status=AgreementStatus.Active});
  db.AssetLifecycleEvents.Add(new AssetLifecycleEvent{AssetId=asset.Id,BookingId=booking.Id,Type=AssetLifecycleEventType.CheckedOut,FromStatus=AssetStatus.Available,ToStatus=AssetStatus.Rented,MeterReading=asset.CurrentMeterReading,OccurredAt=start,RecordedByName="Training fixture importer",Notes="Synthetic training checkout; no real asset released"});
 }
 await db.SaveChangesAsync();
 // Verify the same production readiness rules before committing the new batch.
 var ready=await db.Bookings.Include(x=>x.Customer).Include(x=>x.Items).ThenInclude(x=>x.Asset).Where(x=>x.BookingNumber.StartsWith(prefix)&&x.Status==BookingStatus.Confirmed).ToListAsync();
 foreach(var b in ready){var reasons=await PickupReadiness.Check(db,b,default);if(reasons.Count>0)throw new InvalidOperationException($"{b.BookingNumber}: {string.Join("; ",reasons)}");}
 await tx.CommitAsync();Console.WriteLine($"Added {created} vehicles. Existing batch records were retained without resetting their progress.");
}
var batchAssets=await db.Assets.Where(x=>x.AssetNumber.StartsWith(prefix)).ToListAsync();
var batchBookings=await db.Bookings.Include(x=>x.Customer).Include(x=>x.Items).ThenInclude(x=>x.Asset).Include(x=>x.Inspections).Include(x=>x.Payments).Include(x=>x.RentalAgreement).Where(x=>x.BookingNumber.StartsWith(prefix)).ToListAsync();
var ids=batchAssets.Select(x=>x.Id).ToArray();
var maintenance=await db.MaintenanceJobs.CountAsync(x=>ids.Contains(x.AssetId));
var errors=new List<string>();
foreach(var b in batchBookings.Where(x=>x.Status==BookingStatus.Confirmed)) errors.AddRange((await PickupReadiness.Check(db,b,default)).Select(x=>$"{b.BookingNumber}: {x}"));
foreach(var b in batchBookings.Where(x=>x.Status==BookingStatus.ConvertedToRental))if(b.RentalAgreement?.Status!=AgreementStatus.Active||!b.Inspections.Any(x=>x.Type==InspectionType.Handover)||b.Items.Any(x=>x.Asset!.Status!=AssetStatus.Rented)||b.BondAmountHeld!=b.DepositRequired)errors.Add($"Incomplete return fixture {b.BookingNumber}");
Console.WriteLine($"Verified: {batchAssets.Count} batch vehicles; {batchAssets.Count(x=>x.AssetNumber.Contains("-FREE-")&&x.Status==AssetStatus.Available)} free booking vehicles; {batchBookings.Count(x=>x.Status==BookingStatus.Confirmed)} confirmed pickups; {batchBookings.Count(x=>x.Status==BookingStatus.ConvertedToRental)} active returns; {maintenance} maintenance records; {errors.Count} fixture/readiness errors.");
foreach(var branch in branches)Console.WriteLine($"{branch.Name}: {batchAssets.Count(x=>x.BranchId==branch.Id)} vehicles, {batchBookings.Count(x=>x.BranchId==branch.Id&&x.Status==BookingStatus.Confirmed)} pickups, {batchBookings.Count(x=>x.BranchId==branch.Id&&x.Status==BookingStatus.ConvertedToRental)} returns.");
if(errors.Count>0)throw new InvalidOperationException(string.Join(Environment.NewLine,errors));

var recorded = await db.AssetMeterReadings.Where(x => ids.Contains(x.AssetId)).Select(x => x.AssetId).Distinct().CountAsync();
Console.WriteLine($"Odometers: {batchAssets.Count(x=>x.CurrentMeterReading>0)} positive current readings; {recorded} vehicles with meter history; range {batchAssets.Min(x=>x.CurrentMeterReading):N2}–{batchAssets.Max(x=>x.CurrentMeterReading):N2} km.");
