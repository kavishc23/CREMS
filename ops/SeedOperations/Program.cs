using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Data;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using CREMS.Api.Controllers;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var apiRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/CREMS.Api"));
var config = new ConfigurationBuilder().SetBasePath(apiRoot).AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional:true).AddUserSecrets("crems-cs400-development").AddEnvironmentVariables().Build();
var connection = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Missing local database configuration.");
var target = new SqlConnectionStringBuilder(connection);
if (!target.InitialCatalog.Equals("Crems",StringComparison.OrdinalIgnoreCase) ||
    !(target.DataSource.StartsWith("localhost",StringComparison.OrdinalIgnoreCase) || target.DataSource.StartsWith("127.0.0.1",StringComparison.Ordinal)))
    throw new InvalidOperationException("This utility only supports the local Crems database.");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
if(args.Contains("--apply"))
{
    await db.Database.MigrateAsync();
    using var notifications=db.SuppressNotifications();
    await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    // Keep record identifiers and relationships intact while updating old fixture labels.
    var sql=(SqlConnection)db.Database.GetDbConnection();
    var columns=db.Model.GetEntityTypes().Where(x=>x.GetTableName()!=null).SelectMany(entity=>{
        var table=entity.GetTableName()!;var store=StoreObjectIdentifier.Table(table,entity.GetSchema());
        return entity.GetProperties().Where(p=>p.ClrType==typeof(string)&&p.GetColumnName(store)!=null)
            .Select(p=>new {Table=table,Column=p.GetColumnName(store)!});
    }).DistinctBy(x=>x.Table+"."+x.Column).Where(x=>x.Column is not ("StoragePath" or "PhotoUrlsJson" or "PasswordHash" or "SecurityStamp" or "ConcurrencyStamp")).ToList();
    var updated=0;
    var backups=new List<object>();
    foreach(var field in columns)
    {
        var table="["+field.Table.Replace("]","]]")+"]";var column="["+field.Column.Replace("]","]]")+"]";
        await using var read=new SqlCommand($"SELECT DISTINCT {column} FROM {table} WHERE {column} COLLATE Latin1_General_100_CI_AS LIKE @term",sql,(SqlTransaction)transaction.GetDbTransaction());
        read.Parameters.AddWithValue("@term","%demo%");
        var values=new List<string>();await using(var reader=await read.ExecuteReaderAsync()){while(await reader.ReadAsync())values.Add(reader.GetString(0));}
        foreach(var value in values)
        {
            var clean=Regex.Replace(value,@"\s*\(demo\)","",RegexOptions.IgnoreCase);
            clean=Regex.Replace(clean,"demonstration","operational",RegexOptions.IgnoreCase);
            clean=Regex.Replace(clean,"DEMO-","CFG-",RegexOptions.IgnoreCase);
            clean=Regex.Replace(clean,@"demo\.","seed.",RegexOptions.IgnoreCase);
            clean=Regex.Replace(clean,@"\bdemo\s+","",RegexOptions.IgnoreCase);
            clean=Regex.Replace(clean,"demo","seed",RegexOptions.IgnoreCase).Trim();
            if(field.Column.StartsWith("Normalized",StringComparison.Ordinal))clean=clean.ToUpperInvariant();
            backups.Add(new{field.Table,field.Column,OriginalValue=value,UpdatedValue=clean});
            await using var update=new SqlCommand($"UPDATE {table} SET {column}=@new WHERE {column}=@old",sql,(SqlTransaction)transaction.GetDbTransaction());
            update.Parameters.AddWithValue("@new",clean);update.Parameters.AddWithValue("@old",value);updated+=await update.ExecuteNonQueryAsync();
        }
    }
    // Only this local fixture utility changes the configured branch booking switches.
    var branchCodes=new[]{"SUV","NAD","LAU","LAB"};
    var branchIds=await db.Branches.Where(x=>branchCodes.Contains(x.Code)).Select(x=>x.Id).ToListAsync();
    foreach(var branch in await db.Branches.Where(x=>branchCodes.Contains(x.Code)&&x.IsActive).ToListAsync())branch.IsPublic=true;
    foreach(var link in await db.BranchDivisions.Where(x=>branchIds.Contains(x.BranchId)&&x.IsActive).ToListAsync())link.AcceptsBookings=true;
    var shipping=await db.Divisions.SingleAsync(x=>x.Code=="SHIPPING");
    var shippingSources=await db.Assets.AsNoTracking().Where(x=>x.DivisionId==shipping.Id&&x.AssetNumber.StartsWith("SHP-")&&x.AssetCategoryId!=null).ToListAsync();
    var stockIndex=0;
    foreach(var source in shippingSources.GroupBy(x=>new{x.BranchId,x.AssetCategoryId}).Select(x=>x.First()))
    {
        var number=$"SHP-{(await db.Branches.SingleAsync(x=>x.Id==source.BranchId)).Code}-{4301+stockIndex++}";
        if(await db.Assets.AnyAsync(x=>x.AssetNumber==number))continue;
        var stock=JsonSerializer.Deserialize<Asset>(JsonSerializer.Serialize(source))!;
        stock.Id=Guid.NewGuid();stock.AssetNumber=number;stock.Name=Regex.Replace(source.Name,@"\s*·.*$","")+" · Unit "+stockIndex.ToString("00");stock.SerialNumber=$"SHP-26-{4300+stockIndex}";
        stock.Status=AssetStatus.Available;stock.IsActive=true;stock.CreatedAt=DateTimeOffset.UtcNow;stock.NextServiceDate=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60);
        db.Assets.Add(stock);
    }
    // Historical imported repairs predate the safety-release fields. Complete their
    // fixture history without releasing any open work or staff-created jobs.
    var historic=await db.MaintenanceJobs.Where(x=>x.JobNumber.StartsWith("CFG-")&&x.Status==MaintenanceStatus.Completed&&x.CompletedAt!=null&&x.ReleasedAt==null).ToListAsync();
    foreach(var job in historic){job.ReleasedAt=job.CompletedAt!.Value.AddHours(1);job.ReleasedByName="Workshop supervisor";}
    string[] customerNames=["Pacific Site Contractors","Litia Vakaloloma","Sanjay Narayan","Northern Valley Transport","Mere Waqa","Arjun Prasad","Coral Coast Property Services","Ana Bale"];
    var legacyCustomers=await db.Customers.Where(x=>x.CustomerNumber.StartsWith("CFG-")).ToListAsync();
    for(var i=0;i<legacyCustomers.Count;i++)
    {
        var customer=legacyCustomers[i];
        customer.Name=customer.Name.StartsWith("Reporting example",StringComparison.OrdinalIgnoreCase)?customerNames[i%customerNames.Length]:Regex.Replace(customer.Name,@"\s+\d+$","");
        if(customer.Address?.StartsWith("Fictional",StringComparison.OrdinalIgnoreCase)==true)customer.Address=$"{10+i} Kings Road, Fiji";
    }
    foreach(var job in await db.MaintenanceJobs.Where(x=>x.JobNumber.StartsWith("CFG-")&&x.FaultDescription.StartsWith("Fictional")).ToListAsync()){job.FaultDescription="Service inspection identified wear requiring workshop attention.";job.Description="Inspect the affected components, replace worn parts and complete an operational check before release.";}
    foreach(var booking in await db.Bookings.Where(x=>x.BookingNumber.StartsWith("CFG-")&&x.Notes!=null&&x.Notes.StartsWith("Fictional")).ToListAsync())booking.Notes=booking.Status==BookingStatus.Completed?"Hire closed following return inspection and account reconciliation.":"Hire arranged through the branch rental desk; agreed dates and requirements recorded.";
    foreach(var payment in await db.RentalPayments.Where(x=>x.ReceiptNumber.StartsWith("CFG-")&&x.Note!=null&&x.Note.StartsWith("Fictional")).ToListAsync())payment.Note="Rental account payment recorded for invoice reconciliation.";
    var activeHires=await db.Bookings.Include(x=>x.Customer).Include(x=>x.Items).ThenInclude(x=>x.Asset)
        .Where(x=>x.BookingNumber.StartsWith("BK-2610-51")&&x.Status==BookingStatus.ConvertedToRental&&!db.RentalAgreements.Any(a=>a.BookingId==x.Id)).ToListAsync();
    var coordinator=await db.Users.FirstAsync(x=>x.IsActive);
    foreach(var hire in activeHires)
    {
        var item=hire.Items.First();
        db.RentalAgreements.Add(new RentalAgreement{AgreementNumber=hire.BookingNumber.Replace("BK-","RA-"),BookingId=hire.Id,BranchId=hire.BranchId,Status=AgreementStatus.Active,TermsVersion="2026.1",TermsJson="{\"accepted\":true}",CustomerSignatureName=hire.Customer!.Name,CustomerSignedAt=item.StartAt,ApprovedByUserId=coordinator.Id,ApprovedByName=coordinator.FullName,ApprovedAt=item.StartAt,CustomerSnapshotJson=JsonSerializer.Serialize(new{hire.Customer.Name,hire.Customer.CustomerNumber}),AssetSnapshotJson=JsonSerializer.Serialize(new{item.Asset!.Name,item.Asset.AssetNumber}),PricingSnapshotJson=JsonSerializer.Serialize(new{item.DailyRate,hire.TaxRate})});
        db.AssetInspections.Add(new AssetInspection{AssetId=item.AssetId,BookingId=hire.Id,Stage=InspectionStage.PreHire,Outcome=InspectionOutcome.Passed,CompletedAt=item.StartAt.AddMinutes(-20),CompletedByUserId=coordinator.Id,CompletedByName=coordinator.FullName,CustomerSignatureName=hire.Customer.Name,StaffSignatureName=coordinator.FullName,Notes="Pre-hire operational and condition checks completed before collection."});
    }
    const string marker="seed.operations-october-2026-v2";
    if(!await db.SystemSettings.AnyAsync(x=>x.Key==marker))
    {
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Pacific/Fiji").DateTime);
        var midnight=new DateTimeOffset(today.ToDateTime(new TimeOnly(9,0)),TimeSpan.FromHours(12));
        var actor=await db.Users.FirstAsync(x=>x.IsActive);
        var prototypes=(await db.Assets.AsNoTracking().Where(x=>x.IsActive&&x.DivisionId!=null&&x.AssetCategoryId!=null&&x.ServiceOfferingId!=null).OrderBy(x=>x.AssetNumber).ToListAsync())
            .GroupBy(x=>new{x.DivisionId,x.BranchId}).SelectMany(x=>x.Take(2)).ToList();
        var branches=await db.Branches.ToDictionaryAsync(x=>x.Id);var divisions=await db.Divisions.ToDictionaryAsync(x=>x.Id);
        string[] people=["Alisi Vakacegu","Rajesh Naidu","Merewalesi Tora","Vikash Sharma","Ana Rokotuibau","Priya Narayan","Jone Vakalalabure","Shalini Prasad","Sera Naleba","Rohit Chandra"];
        string[] businesses=["Pacific Harbour Civil Works","Nadi Garden Services","Lautoka Site Logistics","Northern Valley Contractors","Laucala Event Services"];
        for(var i=0;i<prototypes.Count;i++)
        {
            var source=prototypes[i];var branch=branches[source.BranchId];var division=divisions[source.DivisionId!.Value];
            var prefix=division.Code=="SHIPPING"?"SHP":division.Code=="MOTORS"?"VEH":"EQP";
            var asset=JsonSerializer.Deserialize<Asset>(JsonSerializer.Serialize(source))!;
            asset.Id=Guid.NewGuid();asset.AssetNumber=$"{prefix}-{branch.Code}-{4101+i}";asset.Name=Regex.Replace(source.Name,@"\s*·.*$","")+$" · Unit {i+1:00}";
            asset.SerialNumber=$"{prefix}-{today.Year}-{i+101:0000}";asset.RegistrationNumber=AssetCategoryPolicy.IsVehicle(asset.Type)?$"LU {710+i}":null;
            asset.VinOrChassisNumber=AssetCategoryPolicy.IsVehicle(asset.Type)?$"JN1CREMS26{i+1:00000000}":null;
            asset.Status=i%5==1?AssetStatus.Rented:i%5==2?AssetStatus.Maintenance:i%5==0?AssetStatus.Reserved:AssetStatus.Available;
            asset.CreatedAt=midnight.AddMonths(-6);asset.NextServiceDate=today.AddDays(45);asset.AcquisitionDate=today.AddMonths(-6);asset.CurrentLocation=branch.Name+" hire depot";
            db.Assets.Add(asset);
            var customer=new Customer{CustomerNumber=$"CUS-2610-{4101+i}",Name=i%3==0?businesses[i%businesses.Length]:people[i%people.Length],Type=i%3==0?CustomerType.Business:CustomerType.Individual,Email=$"accounts.{4101+i}@example.invalid",Address=$"{24+i} Kings Road, {branch.Name}, Fiji"};db.Customers.Add(customer);
            var pickup=midnight.AddDays(-16+i%6);var returned=pickup.AddDays(2+i%3);var rate=asset.DailyRate;var days=(decimal)(returned-pickup).TotalDays;
            var booking=new Booking{BookingNumber=$"BK-2610-{4101+i}",CustomerId=customer.Id,BranchId=branch.Id,Status=BookingStatus.Completed,TaxRate=division.DefaultTaxRate,Notes="Hire completed following return inspection and account reconciliation.",ApprovedByUserId=actor.Id,ApprovedAt=pickup.AddDays(-2),CreatedAt=pickup.AddDays(-4)};db.Bookings.Add(booking);
            db.BookingItems.Add(new BookingItem{BookingId=booking.Id,AssetId=asset.Id,StartAt=pickup,EndAt=returned,DailyRate=rate});
            var subtotal=rate*days;var tax=Math.Round(subtotal*division.DefaultTaxRate/100m,2);var total=subtotal+tax;var paid=i%4==0?Math.Round(total*.6m,2):total;
            var invoice=new RentalInvoice{InvoiceNumber=$"INV-2610-{4101+i}",BookingId=booking.Id,Subtotal=subtotal,TaxAmount=tax,Total=total,AmountPaid=paid,BalanceDue=total-paid,Status=paid==total?InvoiceStatus.Paid:InvoiceStatus.PartiallyPaid,IssuedAt=returned,CreatedAt=returned,LineItemsJson=JsonSerializer.Serialize(new[]{new{description=asset.Name+" hire",quantity=days,unitPrice=rate}})};db.RentalInvoices.Add(invoice);
            db.InvoiceLines.Add(new InvoiceLine{InvoiceId=invoice.Id,Description=asset.Name+" hire",Quantity=days,UnitPrice=rate,TaxRate=division.DefaultTaxRate});
            var payment=new RentalPayment{BookingId=booking.Id,Amount=paid,ReceiptNumber=$"RCT-2610-{4101+i}",Method=i%2==0?PaymentMethod.BankTransfer:PaymentMethod.Card,Type=PaymentType.RentalCharge,Status=PaymentStatus.Recorded,RecordedByUserId=actor.Id,RecordedByName=actor.FullName,CreatedAt=returned.AddHours(1),Note="Invoice payment received and allocated."};db.RentalPayments.Add(payment);
            db.PaymentAllocations.Add(new PaymentAllocation{PaymentId=payment.Id,InvoiceId=invoice.Id,Amount=paid,AllocatedByUserId=actor.Id});
            db.RentalAgreements.Add(new RentalAgreement{AgreementNumber=$"RA-2610-{4101+i}",BookingId=booking.Id,BranchId=branch.Id,Status=AgreementStatus.Completed,TermsVersion="2026.1",TermsJson=JsonSerializer.Serialize(new{division.DefaultRentalTerms}),CustomerSignatureName=customer.Name,CustomerSignedAt=pickup,ApprovedByUserId=actor.Id,ApprovedByName=actor.FullName,ApprovedAt=pickup,CustomerSnapshotJson=JsonSerializer.Serialize(new{customer.Name,customer.CustomerNumber}),AssetSnapshotJson=JsonSerializer.Serialize(new{asset.Name,asset.AssetNumber}),PricingSnapshotJson=JsonSerializer.Serialize(new{rate,total}),CreatedAt=pickup});
            var job=new MaintenanceJob{JobNumber=$"MNT-2610-{4101+i}",AssetId=asset.Id,BranchId=branch.Id,ServiceType=division.Code=="SHIPPING"?"Frame, fittings and sanitation service":"Scheduled safety inspection and lubrication",FaultDescription="Routine service completed before hire allocation.",Status=MaintenanceStatus.Completed,IsPreventive=true,HasEstimate=true,EstimatedCost=180,ActualCost=165,LabourCost=110,PartsCost=55,ReportedAt=pickup.AddDays(-2),StartedAt=pickup.AddDays(-2).AddHours(1),CompletedAt=pickup.AddDays(-1),ReleasedAt=pickup.AddDays(-1).AddHours(1),ReleasedByName=actor.FullName,ReportedByName=actor.FullName,DowntimeHours=5,NextServiceDate=today.AddDays(45)};db.MaintenanceJobs.Add(job);
            if(i%5==0||i%5==1)
            {
                var active=i%5==1;var start=active?midnight.AddDays(-1):midnight.AddDays(2);var end=start.AddDays(3);
                var next=new Booking{BookingNumber=$"BK-2610-{5101+i}",CustomerId=customer.Id,BranchId=branch.Id,Status=active?BookingStatus.ConvertedToRental:BookingStatus.Confirmed,TaxRate=division.DefaultTaxRate,ApprovedAt=start.AddDays(-2),ApprovedByUserId=actor.Id,Notes=active?"Asset collected after pre-hire inspection.":"Hire confirmed; collection instructions sent to customer.",CreatedAt=start.AddDays(-4)};db.Bookings.Add(next);
                db.BookingItems.Add(new BookingItem{BookingId=next.Id,AssetId=asset.Id,StartAt=start,EndAt=end,DailyRate=rate});
            }
            if(i%5==2)db.MaintenanceJobs.Add(new MaintenanceJob{JobNumber=$"MNT-2610-{5101+i}",AssetId=asset.Id,BranchId=branch.Id,Status=MaintenanceStatus.InProgress,ServiceType="Fittings replacement and operational check",FaultDescription="Replace worn fittings identified during depot inspection.",Priority=MaintenancePriority.Normal,HasEstimate=true,EstimatedCost=240,AssignedTo="Workshop service team",ReportedByName=actor.FullName,ReportedAt=midnight.AddDays(-1),StartedAt=midnight.AddDays(-1).AddHours(1),ExpectedReleaseAt=midnight.AddDays(1)});
        }
        db.SystemSettings.Add(new SystemSetting{Key=marker,Value=DateTimeOffset.UtcNow.ToString("O"),Category="Seed",Description="Local operational dataset import; retained to prevent duplicate imports."});
        Console.WriteLine($"Added {prototypes.Count} assets with customers, completed hires, invoices, allocated payments and maintenance history.");
    }
    var backupPath=Path.Combine(apiRoot,"../../outputs",$"operational-label-backup-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
    await File.WriteAllTextAsync(backupPath,JsonSerializer.Serialize(backups,new JsonSerializerOptions{WriteIndented=true}));
    await db.SaveChangesAsync();await transaction.CommitAsync();
    Console.WriteLine($"Updated {updated} stored text values and enabled configured branch booking links. No messages sent.");
}
foreach(var division in await db.Divisions.OrderBy(x=>x.Code).Select(x=>new {x.Id,x.Code,x.IsActive,x.IsPublic,x.CustomerBookingConfigurationJson}).ToListAsync())
{
    var assets = await db.Assets.Where(x=>x.DivisionId==division.Id).Select(x=>new {x.AssetNumber,x.IsActive,x.Type,x.BranchId,x.ServiceOfferingId}).ToListAsync();
    Console.WriteLine($"{division.Code}: active={division.IsActive}, public={division.IsPublic}, assets={assets.Count}, activeAssets={assets.Count(x=>x.IsActive)}, branchLinks={await db.BranchDivisions.CountAsync(x=>x.DivisionId==division.Id&&x.IsActive)}, bookableServices={await db.BranchDivisionServices.CountAsync(x=>x.DivisionId==division.Id&&x.IsActive&&x.IsBookable)}");
    Console.WriteLine(string.Join(", ",assets.Take(6).Select(x=>$"{x.AssetNumber} ({x.Type})")));
    Console.WriteLine("States: "+string.Join(", ",await db.Assets.Where(x=>x.DivisionId==division.Id).GroupBy(x=>x.Status).Select(x=>x.Key+":"+x.Count()).ToListAsync()));
    Console.WriteLine("Accepts bookings: "+await db.BranchDivisions.CountAsync(x=>x.DivisionId==division.Id&&x.AcceptsBookings&&x.IsActive));
    Console.WriteLine("Online requests: "+DivisionBookingFlow.Parse(division.CustomerBookingConfigurationJson).OnlineRequestsEnabled);
}
Console.WriteLine($"Bookings={await db.Bookings.CountAsync()}, maintenance={await db.MaintenanceJobs.CountAsync()}, invoices={await db.RentalInvoices.CountAsync()}, payments={await db.RentalPayments.CountAsync()}");
foreach(var branch in await db.Branches.Select(x=>new{x.Code,x.IsActive,x.IsPublic}).ToListAsync())Console.WriteLine($"Branch {branch.Code}: active={branch.IsActive}, public={branch.IsPublic}");
if(args.Contains("--verify"))
{
    var administrator=await (from user in db.Users join membership in db.UserRoles on user.Id equals membership.UserId join role in db.Roles on membership.RoleId equals role.Id where role.Name==SystemRoles.SuperAdministrator&&user.IsActive select user).FirstAsync();
    var http=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,administrator.Id.ToString()),new Claim(ClaimTypes.Role,SystemRoles.SuperAdministrator)],"Verification"))};
    var shipping=await db.Divisions.SingleAsync(x=>x.Code=="SHIPPING");
    var asset=await db.Assets.FirstAsync(x=>x.DivisionId==shipping.Id&&x.AssetNumber.Contains("-43")&&x.Status==AssetStatus.Available);
    var controller=new AssetQrController(db,new CurrentStaffScope(db)){ControllerContext=new ControllerContext{HttpContext=http}};
    foreach(var code in new[]{"  "+asset.AssetNumber.ToLowerInvariant()+"  ",asset.Id.ToString(),"CREMS:ASSET:"+asset.Id,"https://localhost/staff/scan?code="+Uri.EscapeDataString(asset.AssetNumber)})
    {
        var response=await controller.Resolve(new ResolveQrRequest(code),default);
        if(response is not OkObjectResult ok)throw new InvalidOperationException("Asset lookup failed for a supported code format.");
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(ok.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        if(!json.RootElement.GetProperty("availability").GetProperty("availableNow").GetBoolean())throw new InvalidOperationException("Unencumbered Shipping stock was incorrectly unavailable.");
    }
    Console.WriteLine("QR database checks passed: typed lowercase asset code, UUID, QR payload and code URL; live availability returned for Shipping stock.");
    using var client=new HttpClient();
    Console.WriteLine("Local API target: "+await client.GetStringAsync("http://127.0.0.1:5080/swagger/crems-environment.json"));
    using var publicResponse=await client.GetAsync("http://127.0.0.1:5080/api/public/assets?divisionId="+shipping.Id);
    publicResponse.EnsureSuccessStatusCode();using var catalogue=JsonDocument.Parse(await publicResponse.Content.ReadAsStringAsync());
    if(catalogue.RootElement.GetArrayLength()==0)throw new InvalidOperationException("Shipping catalogue is empty.");
    Console.WriteLine($"Live public API: {catalogue.RootElement.GetArrayLength()} Shipping assets returned.");
    if(args.Contains("--http-verify"))
    {
        var email=config["BootstrapAdmin:Email"];var password=config["BootstrapAdmin:Password"];
        if(string.IsNullOrWhiteSpace(email)||string.IsNullOrWhiteSpace(password))throw new InvalidOperationException("Local administrator configuration is required for authenticated HTTP verification.");
        using var handler=new HttpClientHandler{CookieContainer=new CookieContainer()};using var staff=new HttpClient(handler){BaseAddress=new Uri("http://127.0.0.1:5080"),Timeout=TimeSpan.FromSeconds(30)};
        staff.DefaultRequestHeaders.Add("X-CREMS-Window-Id",Guid.NewGuid().ToString());
        using var login=await staff.PostAsJsonAsync("/api/auth/login",new{email,password});login.EnsureSuccessStatusCode();
        using var lookup=await staff.PostAsJsonAsync("/api/asset-qr/resolve",new{code=asset.AssetNumber.ToLowerInvariant()});lookup.EnsureSuccessStatusCode();
        using var scan=JsonDocument.Parse(await lookup.Content.ReadAsStringAsync());
        if(!scan.RootElement.GetProperty("availability").GetProperty("availableNow").GetBoolean())throw new InvalidOperationException("HTTP scan availability was incorrect.");
        foreach(var url in new[]{"/api/assets?divisionId="+shipping.Id,"/api/maintenance-jobs/workspace?page=1&pageSize=20","/api/reporting/workspace?from=2026-10-01&to=2026-10-22"})
        {
            using var response=await staff.GetAsync(url);response.EnsureSuccessStatusCode();Console.WriteLine("Authenticated HTTP check passed: "+url.Split('?')[0]);
        }
        Console.WriteLine("Authenticated HTTP check passed: manual scan with live availability.");
        await staff.PostAsync("/api/auth/logout",null);
    }
}
