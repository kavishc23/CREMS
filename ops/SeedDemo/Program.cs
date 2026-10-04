using System.Security.Cryptography;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

if (!args.Contains("--apply")) throw new InvalidOperationException("Use --apply to add the local fictional demo dataset.");
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var apiRoot = Path.Combine(root, "src/CREMS.Api");
var config = new ConfigurationBuilder().SetBasePath(apiRoot).AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional: true).AddUserSecrets("crems-cs400-development").AddEnvironmentVariables().Build();
var connection = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Missing database connection.");
var target = new SqlConnectionStringBuilder(connection);
if (target.InitialCatalog != "Crems" || !(target.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || target.DataSource.StartsWith("127.0.0.1", StringComparison.Ordinal)))
    throw new InvalidOperationException("This importer only targets the local Crems demo database.");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
using var suppression = db.SuppressNotifications();
await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
const string marker = "demo.realistic-september-v1";
if (await db.SystemSettings.AnyAsync(x => x.Key == marker)) { Console.WriteLine("Dataset already imported; no records changed."); return; }
var now = DateTimeOffset.UtcNow;
var today = DateOnly.FromDateTime(now.UtcDateTime);
var branches = await db.Branches.Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();
var prototypes = await db.Assets.AsNoTracking().Where(x => x.IsActive && x.DivisionId != null && x.ServiceOfferingId != null && x.AssetCategoryId != null).OrderBy(x => x.AssetNumber).ToListAsync();
var roles = await db.Roles.ToDictionaryAsync(x => x.Name!);
var templates = await db.InspectionTemplates.AsNoTracking().ToListAsync();
var divisions = await db.Divisions.ToDictionaryAsync(x => x.Id);
if (branches.Count == 0 || prototypes.Count == 0) throw new InvalidOperationException("Run the base development seed first.");
var actor = await db.Users.Where(x => x.IsActive).OrderBy(x => x.Id).FirstAsync();
var counts = new SortedDictionary<string, int>();
void Add<T>(T entity) where T : class { db.Add(entity); var key = typeof(T).Name; counts[key] = counts.GetValueOrDefault(key) + 1; }
string Json(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
string[] names = ["Litia Ravulo", "Arjun Kumar", "Mere Waqa", "Ravi Prasad", "Ana Bale", "Sanjay Deo", "Sera Ratu", "Nilesh Chand", "Jone Naceva", "Pooja Singh", "Adi Tawake", "Dev Sharma"];
string[] companies = ["Coral Coast Site Services", "Western Ridge Construction", "Island Event Logistics", "Northern Harvest Cooperative", "Lagoon Resort Projects", "Reefline Civil Works", "Valley Farm Supplies", "Sunrise Community Events"];
string[] jobs = ["Oil and filter service", "Brake inspection and adjustment", "Hydraulic hose replacement", "Battery and charging diagnosis", "Cooling system inspection", "Tyre replacement and alignment"];
var staff = new List<ApplicationUser>();
foreach (var branch in branches)
{
    var divisionId = prototypes.First(x => x.BranchId == branch.Id).DivisionId!.Value;
    foreach (var role in new[] { SystemRoles.BranchManager, SystemRoles.RentalOfficer, SystemRoles.MaintenanceOfficer })
    {
        var email = $"demo.{branch.Code.ToLowerInvariant()}.{role.ToLowerInvariant()}@example.invalid";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), FullName = names[staff.Count % names.Length] + " (Demo)", BranchId = branch.Id, DivisionId = divisionId, EmailConfirmed = true, MustChangePassword = true, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
        // No shared demo password: administrators can reset these fictional accounts.
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        Add(user); Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roles[role].Id }); staff.Add(user);
    }
}
var assets = new List<Asset>();
var customers = new List<Customer>();
for (var i = 0; i < 48; i++)
{
    var source = prototypes[i % prototypes.Count];
    var branch = branches.Single(x => x.Id == source.BranchId);
    // Clone mapped scalar values, keeping the existing category/service configuration intact.
    var asset = (Asset)db.Entry(source).CurrentValues.ToObject();
    asset.Id = Guid.NewGuid(); asset.AssetNumber = $"DEMO-{branch.Code}-{i + 1:000}";
    asset.Name = source.Name + $" · Fleet {i + 1:00}"; asset.CreatedAt = now.AddMonths(-18).AddDays(i);
    asset.Status = AssetStatus.Available; asset.RegistrationNumber = source.RegistrationNumber == null ? null : $"DEMO-{i + 1:04}";
    asset.SerialNumber = $"DEMO-SN-{i + 1:0000}"; asset.VinOrChassisNumber = $"DEMO-CHASSIS-{i + 1:0000}";
    asset.CurrentMeterReading = 1200 + i * 275; asset.AcquisitionDate = today.AddMonths(-18); asset.AcquisitionCost = 15000 + i * 2200;
    asset.CurrentBookValue = Math.Round(asset.AcquisitionCost * .82m, 2); asset.NextServiceDate = today.AddDays(20 + i % 45);
    asset.InsuranceExpiry = today.AddDays(45 + i * 3); asset.WarrantyExpiry = today.AddMonths(6); asset.PhotoUrlsJson = source.PhotoUrlsJson;
    asset.CurrentLocation = $"{branch.Name} depot — bay {i % 8 + 1}"; Add(asset); assets.Add(asset);
    var customer = new Customer { CustomerNumber = $"DEMO-CUS-{i + 1:000}", Name = i % 3 == 0 ? companies[i % companies.Length] + $" {i / 8 + 1} (Demo)" : names[i % names.Length] + $" {i / 12 + 1} (Demo)", Type = i % 3 == 0 ? CustomerType.Business : CustomerType.Individual,
        Email = $"demo.customer{i + 1:000}@example.invalid", Address = $"Demo premises {10 + i}, {branch.Name}, Fiji", HirePreferences = [CustomerHirePreference.Vehicles, CustomerHirePreference.Equipment], CreatedAt = now.AddMonths(-6).AddDays(i) };
    Add(customer); customers.Add(customer);
    if (customer.Type == CustomerType.Business) Add(new CorporateAccount { CustomerId = customer.Id, LegalName = customer.Name, CreditLimit = 15000 + 1000 * i, PaymentTermsDays = 30, PurchaseOrderRequired = true, BillingContactJson = Json(new { name = names[i % names.Length], email = customer.Email }), JobSitesJson = Json(new[] { new { name = "Project site", address = customer.Address } }) });
    if (i < 24)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = customer.Email, NormalizedUserName = customer.Email!.ToUpperInvariant(), Email = customer.Email, NormalizedEmail = customer.Email.ToUpperInvariant(), FullName = customer.Name, CustomerId = customer.Id, EmailConfirmed = true, MustChangePassword = true, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        Add(user); Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roles[SystemRoles.Customer].Id });
    }
    Add(new AssetLifecycleEvent { AssetId = asset.Id, Type = AssetLifecycleEventType.Commissioned, ToStatus = AssetStatus.Available, OccurredAt = now.AddMonths(-6), RecordedByUserId = actor.Id, RecordedByName = "Demo fleet coordinator", Notes = "Fictional fleet commissioning record." });
    for (var m = 0; m < 3; m++) Add(new AssetMeterReading { AssetId = asset.Id, Type = AssetCategoryPolicy.IsVehicle(asset.Type) ? MeterType.Odometer : MeterType.EngineHours, Unit = asset.MeterUnit ?? "hours", Reading = asset.CurrentMeterReading!.Value - 200 + m * 100, FuelPercent = 90 - m * 10, RecordedAt = now.AddDays(-45 + m * 20), Source = MeterReadingSource.Manual, RecordedByUserId = actor.Id });
    Add(new AssetCostEntry { AssetId = asset.Id, BranchId = branch.Id, Category = i % 2 == 0 ? AssetCostCategory.Cleaning : AssetCostCategory.Transport, Description = i % 2 == 0 ? "Post-hire detailing and consumables (demo)" : "Low-bed repositioning to depot (demo)", Amount = i % 2 == 0 ? 65 : 280, OccurredOn = today.AddDays(-8), Supplier = "Demo fleet services", ReferenceNumber = $"DEMO-EXP-{i + 1:000}", RecordedByUserId = actor.Id, RecordedByName = "Demo finance coordinator" });
    Add(new TelematicsSnapshot { AssetId = asset.Id, Provider = "Demo simulator", RecordedAt = now.AddMinutes(-15 - i), Latitude = -18.14m + i * .002m, Longitude = 178.42m - i * .003m, Odometer = AssetCategoryPolicy.IsVehicle(asset.Type) ? asset.CurrentMeterReading : null, EngineHours = AssetCategoryPolicy.IsVehicle(asset.Type) ? null : asset.CurrentMeterReading, FuelPercent = 35 + i % 60, FaultCodesJson = "[]" });
}
var rentals = new List<Booking>();
for (var i = 0; i < assets.Count; i++)
{
    var asset = assets[i]; var customer = customers[i];
    var taxRate = divisions[asset.DivisionId!.Value].DefaultTaxRate;
    for (var cycle = 0; cycle < 3; cycle++)
    {
        var start = now.AddDays(-80 + cycle * 22 + i % 5); var end = start.AddDays(3 + i % 5);
        var booking = new Booking { BookingNumber = $"DEMO-BK-{i + 1:000}-{cycle + 1}", CustomerId = customer.Id, BranchId = asset.BranchId, Status = BookingStatus.Completed, TaxRate = taxRate, Notes = "Fictional completed hire; all records are demonstration data.", CreatedAt = start.AddDays(-5), ApprovedByUserId = actor.Id, ApprovedAt = start.AddDays(-2) };
        Add(booking); rentals.Add(booking);
        Add(new BookingItem { BookingId = booking.Id, AssetId = asset.Id, StartAt = start, EndAt = end, DailyRate = asset.DailyRate });
        var days = (decimal)(end - start).TotalDays; var subtotal = days * asset.DailyRate; var tax = Math.Round(subtotal * taxRate / 100, 2); var total = subtotal + tax;
        var paid = i % 5 == 0 && cycle == 2 ? Math.Round(total / 2, 2) : total;
        var invoice = new RentalInvoice { InvoiceNumber = $"DEMO-INV-{i + 1:000}-{cycle + 1}", BookingId = booking.Id, LineItemsJson = Json(new[] { new { description = asset.Name + " hire", quantity = days, unitPrice = asset.DailyRate } }), Subtotal = subtotal, TaxAmount = tax, Total = total, AmountPaid = paid, BalanceDue = total - paid, Status = paid == total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid, IssuedAt = end, CreatedAt = end };
        Add(invoice); Add(new InvoiceLine { InvoiceId = invoice.Id, Description = asset.Name + " hire", Quantity = days, UnitPrice = asset.DailyRate, TaxRate = taxRate });
        var payment = new RentalPayment { BookingId = booking.Id, Type = PaymentType.RentalCharge, Method = (PaymentMethod)(i % 3), Amount = paid, ReceiptNumber = $"DEMO-RCT-{i + 1:000}-{cycle + 1}", RecordedByUserId = actor.Id, RecordedByName = "Demo finance coordinator", CreatedAt = end.AddHours(1), Note = "Fictional payment; no funds transferred." };
        Add(payment); Add(new PaymentAllocation { PaymentId = payment.Id, InvoiceId = invoice.Id, Amount = paid, AllocatedByUserId = actor.Id });
        var agreement = new RentalAgreement { AgreementNumber = $"DEMO-RA-{i + 1:000}-{cycle + 1}", BookingId = booking.Id, BranchId = asset.BranchId, TermsVersion = "DEMO-1", TermsJson = Json(new { demo = true, accepted = true }), CustomerSnapshotJson = Json(new { customer.CustomerNumber, customer.Name, customer.Email }), AssetSnapshotJson = Json(new { asset.AssetNumber, asset.Name }), PricingSnapshotJson = Json(new { asset.DailyRate, taxRate }), CustomerSignatureName = customer.Name, CustomerSignedAt = start, ApprovedByUserId = actor.Id, ApprovedByName = "Demo rental coordinator", ApprovedAt = start, Status = AgreementStatus.Completed, CreatedAt = start };
        Add(agreement);
        foreach (var stage in new[] { InspectionStage.PreHire, InspectionStage.PostHire })
        {
            var at = stage == InspectionStage.PreHire ? start : end;
            Add(new AssetInspection { AssetId = asset.Id, BookingId = booking.Id, TemplateId = templates.FirstOrDefault(x => x.AssetCategoryId == asset.AssetCategoryId && x.Stage == stage)?.Id, Stage = stage, Outcome = InspectionOutcome.Passed, MeterReading = asset.CurrentMeterReading - (3 - cycle) * 100 + (stage == InspectionStage.PostHire ? 50 : 0), FuelPercent = 90, CompletedAt = at, CompletedByUserId = actor.Id, CompletedByName = "Demo rental coordinator", CustomerSignatureName = customer.Name, StaffSignatureName = "Demo rental coordinator", Notes = "Demonstration inspection: controls, bodywork and accessories checked." });
            Add(new RentalInspection { BookingId = booking.Id, Type = stage == InspectionStage.PreHire ? InspectionType.Handover : InspectionType.Return, IdentificationVerified = true, DriverLicenceVerified = AssetCategoryPolicy.IsVehicle(asset.Type), PaymentVerified = true, FuelLevelPercent = 90, ConditionNotes = "Demo record: serviceable condition.", SignatureName = customer.Name, CompletedByUserId = actor.Id, CompletedByName = "Demo rental coordinator", CompletedAt = at });
            Add(new AssetLifecycleEvent { AssetId = asset.Id, BookingId = booking.Id, Type = stage == InspectionStage.PreHire ? AssetLifecycleEventType.CheckedOut : AssetLifecycleEventType.ReturnedToService, FromStatus = stage == InspectionStage.PreHire ? AssetStatus.Available : AssetStatus.Inspection, ToStatus = stage == InspectionStage.PreHire ? AssetStatus.Rented : AssetStatus.Available, OccurredAt = at, RecordedByUserId = actor.Id, RecordedByName = "Demo rental coordinator" });
        }
        if (cycle == 2 && i % 6 == 0)
        {
            Add(new RentalIncident { IncidentNumber = $"DEMO-INC-{i + 1:000}", BookingId = booking.Id, Type = IncidentType.Damage, OccurredAt = end, Description = "Minor scuff identified at return; polished out during detailing (demo).", Location = "Return inspection bay", EstimatedCost = 65, Status = IncidentStatus.Resolved });
            Add(new RentalAgreementAddendum { RentalAgreementId = agreement.Id, AddendumNumber = $"DEMO-ADD-{i + 1:000}", Reason = "Collection contact changed before pickup (demo)", ChangesJson = Json(new { collectionContact = customer.Name }), CustomerSignatureName = customer.Name, CustomerSignedAt = start, ApprovedByUserId = actor.Id, ApprovedByName = "Demo rental coordinator" });
        }
        if (cycle == 2 && paid < total) Add(new ManagementTask { BranchId = asset.BranchId, Category = TaskCategory.UnpaidInvoice, Priority = TaskPriority.Normal, Title = $"Follow up {invoice.InvoiceNumber}", Description = $"Fictional account balance FJD {total - paid:0.00}.", DueAt = now.AddDays(2), SourceEntityType = nameof(RentalInvoice), SourceEntityId = invoice.Id });
    }
    if (i % 4 < 2)
    {
        var active = i % 4 == 0; var start = active ? now.AddDays(-2) : now.AddDays(3 + i % 4); var end = start.AddDays(7);
        var booking = new Booking { BookingNumber = $"DEMO-BK-{i + 1:000}-LIVE", CustomerId = customer.Id, BranchId = asset.BranchId, Status = active ? BookingStatus.ConvertedToRental : BookingStatus.Confirmed, TaxRate = taxRate, ApprovedByUserId = actor.Id, ApprovedAt = now.AddDays(-4), Notes = "Fictional current hire scenario.", DepositRequired = 0, BondStatus = BondStatus.NotRequired };
        Add(booking); Add(new BookingItem { BookingId = booking.Id, AssetId = asset.Id, StartAt = start, EndAt = end, DailyRate = asset.DailyRate });
        asset.Status = active ? AssetStatus.Rented : AssetStatus.Reserved;
        Add(new DispatchJob { DispatchNumber = $"DEMO-DSP-{i + 1:000}", BookingId = booking.Id, BranchId = asset.BranchId, Type = DispatchType.CustomerPickup, Status = active ? DispatchStatus.Completed : DispatchStatus.Scheduled, ScheduledAt = start, CompletedAt = active ? start : null, Address = customer.Address, AssignedDriver = "Customer collection" });
        if (active)
        {
            Add(new RentalAgreement { AgreementNumber = $"DEMO-RA-{i + 1:000}-LIVE", BookingId = booking.Id, BranchId = asset.BranchId, TermsVersion = "DEMO-1", TermsJson = "{\"demo\":true}", CustomerSnapshotJson = Json(new { customer.Name, customer.Email }), AssetSnapshotJson = Json(new { asset.AssetNumber, asset.Name }), PricingSnapshotJson = Json(new { asset.DailyRate, taxRate }), CustomerSignatureName = customer.Name, CustomerSignedAt = start, ApprovedByUserId = actor.Id, ApprovedByName = "Demo rental coordinator", ApprovedAt = start, Status = AgreementStatus.Active });
            Add(new RentalInspection { BookingId = booking.Id, Type = InspectionType.Handover, IdentificationVerified = true, DriverLicenceVerified = AssetCategoryPolicy.IsVehicle(asset.Type), PaymentVerified = true, ConditionNotes = "Fictional handover completed.", FuelLevelPercent = 90, CompletedByUserId = actor.Id, CompletedByName = "Demo rental coordinator", CompletedAt = start });
        }
    }
    var quoteSubtotal = asset.DailyRate * (4 + i % 6); var quoteTax = Math.Round(quoteSubtotal * taxRate / 100, 2);
    var quote = new SalesQuote { QuoteNumber = $"DEMO-QUO-{i + 1:000}", CustomerId = customer.Id, BranchId = asset.BranchId, DivisionId = asset.DivisionId, Status = i % 2 == 0 ? QuoteStatus.Draft : QuoteStatus.Negotiating, ValidUntil = now.AddDays(14), JobSite = customer.Address, Subtotal = quoteSubtotal, Tax = quoteTax, Total = quoteSubtotal + quoteTax, LineItemsJson = QuoteLineSerialization.Serialize([new QuoteLine(asset.Name + " hire", 4 + i % 6, asset.DailyRate, ChargeUnit.Day)]) };
    Add(quote); Add(new QuoteRevision { SalesQuoteId = quote.Id, Version = 1, SnapshotJson = Json(new { quote.QuoteNumber, quote.Total, quote.LineItemsJson }), ChangeReason = "Initial fictional customer enquiry", ChangedByUserId = actor.Id });
    if (i % 2 == 0) Add(new CustomerCase { CaseNumber = $"DEMO-CASE-{i + 1:000}", CustomerId = customer.Id, BranchId = asset.BranchId, Type = i % 4 == 0 ? CaseType.Enquiry : CaseType.General, Status = i % 3 == 0 ? CaseStatus.Resolved : CaseStatus.InProgress, Subject = i % 4 == 0 ? "Request for extended hire pricing" : "Confirm collection arrangements", Description = $"Demo enquiry for {asset.Name}; customer prefers morning collection.", DueAt = now.AddDays(2), Resolution = i % 3 == 0 ? "Customer provided with revised options (demo)." : null });
    // Limited to the new asset, so existing fleet pricing is unchanged.
    Add(new PricingRule { Name = $"Demo weekly rate — {asset.AssetNumber}", AssetId = asset.Id, BranchId = asset.BranchId, DivisionId = asset.DivisionId, Period = RatePeriod.Weekly, Rate = asset.DailyRate * 6, MinimumDuration = 7, EffectiveFrom = now.AddDays(-90) });
}
var suppliers = new List<Supplier>();
string[] supplierNames = ["Demo Pacific Filter Supplies", "Demo Western Tyre Centre", "Demo Island Hydraulics", "Demo Northern Auto Electrical", "Demo Fleet Consumables", "Demo Site Safety Supply", "Demo Coast Transport", "Demo Workshop Equipment"];
for (var i = 0; i < supplierNames.Length; i++) { var supplier = new Supplier { SupplierNumber = $"DEMO-SUP-{i + 1:000}", Name = supplierNames[i], Email = $"demo.supplier{i + 1}@example.invalid", Address = branches[i % branches.Count].Name + " — demonstration address", PaymentTermsDays = i % 2 == 0 ? 30 : 14 }; Add(supplier); suppliers.Add(supplier); }
var parts = new List<InventoryPart>();
string[] partNames = ["Oil filter cartridge", "Air filter element", "Hydraulic hose assembly", "Brake pad set", "Drive belt", "Battery terminal kit"];
foreach (var branch in branches)
{
    for (var p = 0; p < partNames.Length; p++) { var part = new InventoryPart { PartNumber = $"DEMO-{branch.Code}-PART-{p + 1}", Name = partNames[p], BranchId = branch.Id, Supplier = suppliers[p].Name, UnitCost = 25 + p * 35, QuantityOnHand = p == 2 ? 2 : 12 + p * 3, ReorderLevel = 5, QuantityOnOrder = p == 2 ? 10 : 0 }; Add(part); parts.Add(part); }
    var ordered = parts.Last(x => x.BranchId == branch.Id && x.QuantityOnOrder > 0);
    var po = new PurchaseOrder { PurchaseOrderNumber = $"DEMO-PO-{branch.Code}-001", BranchId = branch.Id, Supplier = ordered.Supplier!, Status = PurchaseOrderStatus.Ordered, Total = ordered.UnitCost * 10, LinesJson = Json(new[] { new { partId = ordered.Id, partNumber = ordered.PartNumber, description = ordered.Name, quantity = 10, unitCost = ordered.UnitCost } }), RequestedByUserId = actor.Id, ApprovedByUserId = actor.Id };
    Add(po);
    Add(new ManagementTask { BranchId = branch.Id, Category = TaskCategory.LowStock, Priority = TaskPriority.High, Title = $"Replenish hydraulic hoses — {branch.Name}", Description = $"Two on hand; ten ordered on {po.PurchaseOrderNumber}.", DueAt = now.AddDays(3), SourceEntityType = nameof(PurchaseOrder), SourceEntityId = po.Id });
    for (var p = 0; p < 3; p++)
    {
        var division = prototypes.First(x => x.BranchId == branch.Id).DivisionId!.Value;
        var person = new Personnel { EmployeeNumber = $"DEMO-{branch.Code}-EMP-{p + 1}", FullName = names[(parts.Count + p) % names.Length] + " (Demo)", BranchId = branch.Id, DivisionId = division, Type = p == 0 ? PersonnelType.Technician : p == 1 ? PersonnelType.Operator : PersonnelType.Driver, StandardCostRate = 18 + p * 4, OvertimeCostRate = 27 + p * 6, StandardChargeRate = 35 + p * 5, OvertimeChargeRate = 52.5m + p * 7.5m };
        Add(person); Add(new PersonnelQualification { PersonnelId = person.Id, Name = p == 0 ? "Workshop safety and maintenance" : "Equipment operation and site induction", CertificateNumber = $"DEMO-CERT-{branch.Code}-{p + 1}", IssuedOn = today.AddMonths(-6), ExpiresOn = today.AddMonths(18), SafetyInduction = true });
        if (p != 0)
        {
            var booking = rentals.First(x => x.BranchId == branch.Id);
            var item = db.BookingItems.Local.Single(x => x.BookingId == booking.Id);
            var assignment = new BookingPersonnelAssignment { BookingId = booking.Id, PersonnelId = person.Id, Role = p == 1 ? "Site operator" : "Delivery driver", StartAt = item.StartAt, EndAt = item.StartAt.AddHours(8), CustomerHourlyRate = 0, InternalHourlyCost = person.StandardCostRate, Status = AssignmentStatus.Completed };
            Add(assignment); Add(new PersonnelTimesheet { AssignmentId = assignment.Id, WorkDate = DateOnly.FromDateTime(item.StartAt.UtcDateTime), RegularHours = 8, Notes = "Fictional internal support shift included in hire package.", ApprovedByUserId = actor.Id, ApprovedAt = item.StartAt.AddDays(1) });
        }
    }
}
for (var i = 0; i < assets.Count; i++)
{
    var asset = assets[i]; var supplier = suppliers[i % suppliers.Count]; var part = parts.First(x => x.BranchId == asset.BranchId);
    var job = new MaintenanceJob { JobNumber = $"DEMO-MNT-{i + 1:000}-HIST", AssetId = asset.Id, BranchId = asset.BranchId, Status = MaintenanceStatus.Completed, ServiceType = jobs[i % jobs.Length], FaultDescription = "Scheduled service and wear inspection following the previous hire.", Description = "Fictional workshop record: service consumables replaced; operational checks passed.", AssignedTo = staff.First(x => x.BranchId == asset.BranchId).FullName, SupplierId = supplier.Id, Supplier = supplier.Name, IsPreventive = i % 2 == 0, Priority = MaintenancePriority.Normal, PartsCost = part.UnitCost * 2, LabourCost = 120 + i * 3, EstimatedCost = part.UnitCost * 2 + 150 + i * 3, ActualCost = part.UnitCost * 2 + 120 + i * 3, PartsUsed = $"2 × {part.Name}", InvoiceNumber = $"DEMO-WS-{i + 1:000}", MeterReading = asset.CurrentMeterReading, DowntimeHours = 4 + i % 8, ReportedAt = now.AddDays(-10), CompletedAt = now.AddDays(-9), NextServiceDate = asset.NextServiceDate, NextServiceMeter = asset.CurrentMeterReading + 500, WarrantyCovered = i % 5 == 0, WarrantyClaimNumber = i % 5 == 0 ? $"DEMO-WAR-{i + 1:000}" : null, CreatedAt = now.AddDays(-10) };
    Add(job); Add(new MaintenancePartUsage { MaintenanceJobId = job.Id, InventoryPartId = part.Id, Quantity = 2, UnitCost = part.UnitCost });
    if (i % 4 == 2)
    {
        var active = new MaintenanceJob { JobNumber = $"DEMO-MNT-{i + 1:000}-OPEN", AssetId = asset.Id, BranchId = asset.BranchId, Status = (MaintenanceStatus)((i / 4) % 3), ServiceType = jobs[(i + 2) % jobs.Length], FaultDescription = i % 3 == 0 ? "Hydraulic leak found during depot inspection." : "Intermittent warning under load; workshop diagnosis required.", AssignedTo = staff.First(x => x.BranchId == asset.BranchId).FullName, SupplierId = supplier.Id, Supplier = supplier.Name, Priority = (MaintenancePriority)(1 + i % 3), EstimatedCost = 450 + i * 25, ReportedAt = now.AddDays(-2), NextServiceDate = today.AddDays(14), NextServiceMeter = asset.CurrentMeterReading + 500, ParentFailureJobId = job.Id };
        Add(active); asset.Status = AssetStatus.Maintenance;
        Add(new AssetLifecycleEvent { AssetId = asset.Id, Type = AssetLifecycleEventType.MaintenanceStarted, FromStatus = AssetStatus.Available, ToStatus = AssetStatus.Maintenance, OccurredAt = active.ReportedAt, RecordedByUserId = actor.Id, RecordedByName = "Demo workshop coordinator", Notes = active.FaultDescription });
        Add(new BusinessAlert { Category = AlertCategory.MaintenanceDue, BranchId = asset.BranchId, DivisionId = asset.DivisionId, Title = $"Workshop attention: {asset.AssetNumber}", Message = active.FaultDescription, EntityType = nameof(MaintenanceJob), EntityId = active.Id, Priority = TaskPriority.High });
        Add(new InAppNotification { BranchId = asset.BranchId, DivisionId = asset.DivisionId, Kind = "Maintenance", Title = $"Demo workshop job: {active.JobNumber}", Message = active.FaultDescription, Url = $"/staff/maintenance?search={active.JobNumber}", EventKey = $"demo-sept-maintenance:{active.Id}", RelatedEntityType = nameof(MaintenanceJob), RelatedEntityId = active.Id, Severity = "Warning" });
        var approval = new ApprovalRequest { RequestNumber = $"DEMO-APR-{i + 1:000}", BranchId = asset.BranchId, Type = ApprovalType.MajorRepair, EntityType = nameof(MaintenanceJob), EntityId = active.Id, Amount = active.EstimatedCost, Reason = "Workshop expenditure approval for fictional repair scenario.", RequestedByUserId = actor.Id };
        Add(approval); Add(new ApprovalStageDecision { ApprovalRequestId = approval.Id, StageNumber = 1, StageName = "Branch manager review", AssignedRole = SystemRoles.BranchManager });
        Add(new ManagementTask { BranchId = asset.BranchId, Category = TaskCategory.PendingApproval, Title = $"Review repair estimate {active.JobNumber}", Description = "Demo repair approval awaiting workshop review.", DueAt = now.AddDays(1), SourceEntityType = nameof(ApprovalRequest), SourceEntityId = approval.Id, Priority = TaskPriority.High });
    }
    if (i % 8 == 3)
    {
        Add(new MaintenanceJob { JobNumber = $"DEMO-MNT-{i + 1:000}-CANCEL", AssetId = asset.Id, BranchId = asset.BranchId, Status = MaintenanceStatus.Cancelled, ServiceType = "Duplicate service request", FaultDescription = "Duplicate fault report closed after workshop review.", Description = "Service already recorded against the original job (demo).", ReportedAt = now.AddDays(-12), CreatedAt = now.AddDays(-12) });
        var destination = branches.First(x => x.Id != asset.BranchId);
        Add(new AssetTransfer { TransferNumber = $"DEMO-TRF-{i + 1:000}", AssetId = asset.Id, FromBranchId = asset.BranchId, ToBranchId = destination.Id, Status = TransferStatus.Requested, Reason = "Proposed repositioning for upcoming demand (demo); asset has not moved.", RequestedByUserId = actor.Id, TransferCost = 180 });
    }
}
// Reference photos are explicitly labelled as demo references, not evidence of real damage.
var documentRoot = Path.Combine(apiRoot, "App_Data", "maintenance-documents");
Directory.CreateDirectory(documentRoot);
var photoSource = Path.Combine(root, "src/crems-web/public/catalog/forklift.jpg");
if (File.Exists(photoSource))
{
    var index = 0;
    foreach (var job in db.MaintenanceJobs.Local.Where(x => x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled).ToList())
    {
        var storageName = $"demo-sept-reference-{++index:000}.jpg";
        if (!File.Exists(Path.Combine(documentRoot, storageName))) File.Copy(photoSource, Path.Combine(documentRoot, storageName));
        Add(new DocumentRecord { DocumentNumber = $"DEMO-DOC-{index:000}", EntityType = nameof(MaintenanceJob), EntityId = job.Id, BranchId = job.BranchId, Type = "CompletionEvidence", FileName = "Demo-reference-photo-not-actual-repair-evidence.jpg", StoragePath = storageName });
    }
}
foreach (var invoice in db.RentalInvoices.Local.Where(x => x.InvoiceNumber.EndsWith("-3")).Take(6).ToList())
    Add(new CreditNote { CreditNoteNumber = $"DEMO-CN-{invoice.InvoiceNumber}", InvoiceId = invoice.Id, Reason = "Draft goodwill adjustment awaiting review (demo); not applied to balance.", Subtotal = 25, TaxAmount = 0, Total = 25, Status = CreditNoteStatus.Draft, IssuedByUserId = actor.Id });
Add(new AuditEvent { UserId = actor.Id, UserName = "Demo dataset importer", Action = "Demo data imported", EntityType = "DemoDataset", EntityId = Guid.NewGuid(), Summary = "Added fictional September demo scenarios; existing records preserved and outbound notifications suppressed." });
Add(new SystemSetting { Key = marker, Value = now.ToString("O"), Category = "Demo", Description = "One-time fictional scenario import marker. Do not delete to avoid duplicate imports." });
await db.SaveChangesAsync();
await transaction.CommitAsync();
Console.WriteLine("Committed fictional demo dataset to local Crems.");
foreach (var (name, count) in counts) Console.WriteLine($"{name}: +{count}");
Console.WriteLine($"Total added: {counts.Values.Sum()} records.");
