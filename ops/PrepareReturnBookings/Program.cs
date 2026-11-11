using System.Security.Claims;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Operations;

using System.Data;
using System.Reflection;
using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root, "src/CREMS.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional:true)
    .AddUserSecrets("crems-cs400-development").AddEnvironmentVariables().Build();
var connection = config.GetConnectionString("DefaultConnection")!;
var target = new SqlConnectionStringBuilder(connection);
if (target.InitialCatalog != "Crems" || !(target.DataSource.StartsWith("localhost") || target.DataSource.StartsWith("127.0.0.1"))) throw new Exception("Local Crems only.");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
using var suppression = db.SuppressNotifications();
var today = MaintenanceRules.LocalDate(DateTimeOffset.UtcNow);
var start = RentalAvailability.FijiTime(today.AddDays(-1), new TimeOnly(9,0));
var end = RentalAvailability.FijiTime(today, new TimeOnly(23,59));
var prefix = $"BK-{today:yyyyMMdd}-RET";
if (args.Contains("--verify")) {
    var rows = await db.Bookings.Include(x=>x.Items).ThenInclude(x=>x.Asset).Include(x=>x.Branch).Include(x=>x.Customer).Include(x=>x.RentalAgreement).Include(x=>x.Inspections).Include(x=>x.Payments).Where(x=>x.BookingNumber.StartsWith(prefix)).ToListAsync();
    if(rows.Count!=2 || rows.Any(x=>x.Status!=BookingStatus.ConvertedToRental || !x.Inspections.Any(i=>i.Type==InspectionType.Handover) || x.Inspections.Any(i=>i.Type==InspectionType.Return) || x.RentalAgreement?.CustomerSignatureDataUrl?.StartsWith("data:image/png;base64,")!=true || x.Items.Any(i=>i.EndAt!=end))) throw new Exception("Return scenario verification failed.");
    foreach(var b in rows) Console.WriteLine(JsonSerializer.Serialize(new { b.Id,b.BookingNumber,Customer=b.Customer!.Name,Asset=b.Items.First().Asset!.AssetNumber,DueFiji=end.ToString("yyyy-MM-dd HH:mm zzz"),b.Status,Agreement=b.RentalAgreement!.AgreementNumber,Meter=b.Inspections.Single().MeterReading,Paid=b.Payments.Sum(p=>p.Amount)}));
    var output=Path.Combine(root,"tmp/return-bookings-qa");Directory.CreateDirectory(output);
    var jsonOptions=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    foreach(var b in rows) {
        await using var checkTx=await db.Database.BeginTransactionAsync();
        var configSnapshot=await ConfiguredHireRules.Resolve(db,b,CancellationToken.None);
        var template=configSnapshot?.Templates.FirstOrDefault(t=>t.Stage==InspectionStage.PostHire);
        var checklist=template is null?new List<string>{"Condition checked"}:OrganizationConfiguration.Checklist(template.ChecklistJson).ToList();
        var actorId=b.ApprovedByUserId!.Value;
        var principal=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,actorId.ToString()),new Claim(ClaimTypes.Role,SystemRoles.SuperAdministrator)},"local-verification"));
        var controller=new RentalOperationsController(db,new CurrentStaffScope(db)){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext{User=principal}}};
        var inspection=b.Inspections.Single();
        var request=new ReturnInspectionRequest(b.Items.First().Asset!.AssetNumber,true,true,inspection.MeterReading+100,100,"Vehicle clean and intact; all return checks passed.",null,b.Customer!.Name,0,null,null,0,0,0,0,0,null,PaymentMethod.Cash){ReturnedAt=DateTimeOffset.UtcNow,SignatureDataUrl=b.RentalAgreement!.CustomerSignatureDataUrl,ChecklistItems=checklist,ChecklistAllItems=checklist,EvidenceDataUrls=new[]{inspection.SignatureDataUrl!}};
        var result=await controller.Return(b.Id,request,CancellationToken.None);
        if(result is not OkObjectResult)throw new Exception("Return validation failed: "+JsonSerializer.Serialize((result as ObjectResult)?.Value));
        var invoice=await db.RentalInvoices.SingleAsync(x=>x.BookingId==b.Id);
        if(invoice.BalanceDue!=0 || b.RentalAgreement.Status!=AgreementStatus.Completed)throw new Exception("Unexpected settlement.");
        var approved=typeof(RentalAgreementsController).GetMethod("BuildApproved",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{b.RentalAgreement,b});
        await File.WriteAllTextAsync(Path.Combine(output,b.BookingNumber+".json"),JsonSerializer.Serialize(approved,jsonOptions));
        await checkTx.RollbackAsync();
        Console.WriteLine($"PASS: {b.BookingNumber} return completes with no outstanding balance; verification rolled back, rental still awaiting return.");
    }
    return;
}
if(!args.Contains("--apply")) throw new Exception("Use --apply or --verify.");
await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
if(await db.Bookings.AnyAsync(x=>x.BookingNumber.StartsWith(prefix))) {Console.WriteLine("Today's return bookings already exist; no changes.");return;}
var assets = await db.Assets.Include(x=>x.Branch).Include(x=>x.Division)
    .Where(x=>x.AssetNumber.StartsWith("CM-") && x.IsActive && x.Status==AssetStatus.Available &&
        !db.BookingItems.Any(i=>i.AssetId==x.Id && i.StartAt<end && i.EndAt>start && (i.Booking!.Status==BookingStatus.Confirmed || i.Booking.Status==BookingStatus.ConvertedToRental)) &&
        !db.BookingItems.Any(i=>i.AssetId==x.Id && i.Booking!.Status==BookingStatus.ConvertedToRental))
    .OrderBy(x=>x.AssetNumber).Take(2).ToListAsync();
if(assets.Count!=2)throw new Exception("Need two unallocated available catalogue vehicles.");
var actor = await db.Users.Where(x=>x.IsActive && x.CustomerId==null).OrderBy(x=>x.Id).FirstAsync();
var names = new[]{"Arun Prasad","Mere Williams"};
var options=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
for(var i=0;i<2;i++) {
    var asset=assets[i]; var number=$"{prefix}-{i+1:00}";
    var signature="data:image/png;base64,"+Convert.ToBase64String(await File.ReadAllBytesAsync(Path.Combine(root,$"ops/PrepareReturnBookings/signature-{i+1}.png")));
    var customer=new Customer{CustomerNumber=$"CUS-{today:yyyyMMdd}-RET-{i+1:00}",Name=names[i],Type=CustomerType.Individual,Email=$"return-{i+1}@example.invalid",Address="Suva, Fiji",IdentificationNumber=$"SAMPLE-ID-{i+1:0000}",CreatedAt=start.AddDays(-2)};
    var booking=new Booking{BookingNumber=number,Customer=customer,CustomerId=customer.Id,BranchId=asset.BranchId,Branch=asset.Branch,Status=BookingStatus.ConvertedToRental,TaxRate=asset.Division!.DefaultTaxRate,ApprovedByUserId=actor.Id,ApprovedAt=start.AddDays(-1),CreatedAt=start.AddDays(-2),UpdatedAt=start,BondStatus=BondStatus.NotRequired,Notes="Presentation sample: fictional customer, illustrative pickup signature and payment; no actual hire or money received. Confirmed and pickup completed; return pending."};
    booking.Items.Add(new BookingItem{BookingId=booking.Id,AssetId=asset.Id,Asset=asset,StartAt=start,EndAt=end,DailyRate=asset.DailyRate,HireUnit=ChargeUnit.Day,CreatedAt=booking.CreatedAt});
    var draftMethod=typeof(RentalAgreementsController).GetMethod("BuildDraft",BindingFlags.NonPublic|BindingFlags.Static)!;
    var draft=JsonSerializer.SerializeToElement(draftMethod.Invoke(null,new object[]{booking}),options);
    var meter=asset.CurrentMeterReading??24000+i*1500;
    var photoPath=JsonSerializer.Deserialize<string[]>(asset.PhotoUrlsJson ?? "[]")!.First(x=>x.StartsWith("/catalog/"));
    var photo=Convert.ToBase64String(await File.ReadAllBytesAsync(Path.Combine(root,"src/crems-web/public",photoPath.TrimStart('/'))));
    var photoMime=photoPath.EndsWith(".png")?"image/png":"image/jpeg";
    booking.Inspections.Add(new RentalInspection{BookingId=booking.Id,Type=InspectionType.Handover,IdentificationVerified=true,DriverLicenceVerified=true,PaymentVerified=true,MeterReading=meter,FuelLevelPercent=100,ConditionNotes="Clean exterior and interior; tyres, lights, brakes and safety equipment checked. No new damage. Two keys, spare wheel, jack and tool kit supplied.",SignatureName=names[i],SignatureDataUrl=signature,CompletedByUserId=actor.Id,CompletedByName=actor.FullName,CompletedAt=start,CreatedAt=start,EvidenceJson=JsonSerializer.Serialize(new{checklist=new[]{"Tyres and wheels","Lights and indicators","Brakes","Bodywork and interior","Safety equipment"},photos=new[]{"data:"+photoMime+";base64,"+photo},accessories="Two keys, spare wheel, jack and tool kit"},options)});
    booking.RentalAgreement=new RentalAgreement{BookingId=booking.Id,BranchId=booking.BranchId,AgreementNumber=$"RA-{today:yyyyMMdd}-RET-{i+1:00}",TermsVersion="CREMS-RA-2026.1-DRAFT",TermsJson=draft.GetProperty("terms").GetRawText(),CustomerSnapshotJson=draft.GetProperty("customer").GetRawText(),AssetSnapshotJson=draft.GetProperty("asset").GetRawText(),PricingSnapshotJson=draft.GetProperty("pricing").GetRawText(),CustomerSignatureName=names[i],CustomerSignatureDataUrl=signature,CustomerSignedAt=start,ApprovedByUserId=actor.Id,ApprovedByName=actor.FullName,ApprovedAt=start,Status=AgreementStatus.Active,CreatedAt=start};
    booking.AuthorizedDrivers.Add(new AuthorizedDriver{BookingId=booking.Id,FullName=names[i],LicenceNumber=$"SAMPLE-DL-{i+1:0000}",LicenceClass="2",LicenceExpiry=today.AddYears(2),IsPrimary=true,Verified=true});
    var total=draft.GetProperty("pricing").GetProperty("total").GetDecimal();
    booking.Payments.Add(new RentalPayment{BookingId=booking.Id,Type=PaymentType.RentalCharge,Method=PaymentMethod.Cash,Amount=total,ReceiptNumber=$"RCT-{today:yyyyMMdd}-RET-{i+1:00}",RecordedByUserId=actor.Id,RecordedByName=actor.FullName,CreatedAt=start,Note="Illustrative presentation payment; no money received."});
    db.Bookings.Add(booking);
    asset.Status=AssetStatus.Rented;asset.CurrentMeterReading=meter;asset.UpdatedAt=start;
    db.AssetLifecycleEvents.Add(new AssetLifecycleEvent{AssetId=asset.Id,BookingId=booking.Id,Type=AssetLifecycleEventType.CheckedOut,FromStatus=AssetStatus.Available,ToStatus=AssetStatus.Rented,OccurredAt=start,CreatedAt=start,MeterReading=meter,RecordedByUserId=actor.Id,RecordedByName=actor.FullName,Notes=$"Pickup completed for {number}; presentation sample."});
    db.AssetMeterReadings.Add(new AssetMeterReading{AssetId=asset.Id,BookingId=booking.Id,Type=MeterType.Odometer,Unit="km",Reading=meter,FuelPercent=100,RecordedAt=start,CreatedAt=start,RecordedByUserId=actor.Id,Source=MeterReadingSource.PreHireInspection});
    Console.WriteLine($"Prepared {number}: {names[i]}, {asset.AssetNumber}, meter {meter}, due {end:yyyy-MM-dd HH:mm zzz}, sample hire paid {total:F2}.");
}
await db.SaveChangesAsync();await tx.CommitAsync();
Console.WriteLine("Two active signed rentals created. Return not completed. No notifications sent.");
