using System.Data;
using System.Text.Json;
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

if (!args.Contains("--apply") && !args.Contains("--verify")) throw new InvalidOperationException("Use --apply to add the fictional demonstration or --verify to check it.");
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root, "src/CREMS.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets("crems-cs400-development").AddEnvironmentVariables().Build();
var connection = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Missing database connection.");
var target = new SqlConnectionStringBuilder(connection);
if (!string.Equals(target.InitialCatalog, "Crems", StringComparison.OrdinalIgnoreCase) ||
    !(target.DataSource.StartsWith("localhost,", StringComparison.OrdinalIgnoreCase) || target.DataSource == "localhost" ||
      target.DataSource.StartsWith("127.0.0.1,", StringComparison.Ordinal) || target.DataSource == "127.0.0.1"))
    throw new InvalidOperationException("This demonstration only targets the local Crems database.");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
using var suppression = db.SuppressNotifications();
if (args.Contains("--verify"))
{
    var demo = await db.Assets.AsNoTracking().SingleAsync(x => x.AssetNumber == "DEMO-UTIL-001");
    var bookings = await db.Bookings.Where(x => x.Items.Any(i => i.AssetId == demo.Id)).ToListAsync();
    var jobs = await db.MaintenanceJobs.Where(x => x.AssetId == demo.Id).ToListAsync();
    var summary = await AssetUsageSummary.Build(db, demo, 90, DateTimeOffset.UtcNow, CancellationToken.None);
    if (bookings.Count != 10 || bookings.Count(x => x.Status == BookingStatus.Completed) != 8 ||
        jobs.Count != 8 || jobs.Any(x => x.ReleasedAt == null) || summary.RentedHours != 744 ||
        summary.MaintenanceHours != 276 || summary.EstimatedRentalCount != 0 || summary.ConflictingHours != 0)
        throw new InvalidOperationException("The demo history no longer matches the expected demonstration scenario.");
    Console.WriteLine(JsonSerializer.Serialize(new { demo.AssetNumber, demo.Id, bookings = bookings.Count, maintenanceJobs = jobs.Count, summary },
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    return;
}
await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
const string number = "DEMO-UTIL-001";
if (await db.Assets.AnyAsync(x => x.AssetNumber == number))
{
    // Repair only our own demo's future creation dates from early importer versions.
    var now = DateTimeOffset.UtcNow;
    var futureDated = await db.Bookings.Include(x => x.Items)
        .Where(x => x.BookingNumber.StartsWith("DEMO-UTIL-") && x.CreatedAt > now).ToListAsync();
    foreach (var booking in futureDated)
    {
        booking.CreatedAt = now.AddDays(-1); booking.ApprovedAt = now.AddHours(-12);
        foreach (var item in booking.Items) item.CreatedAt = booking.CreatedAt;
    }
    if (futureDated.Count > 0)
    {
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        Console.WriteLine($"Corrected creation dates on {futureDated.Count} fictional demo bookings. No client records changed.");
        return;
    }
    Console.WriteLine($"{number} already exists. No records changed."); return;
}
var vehicleTypes = new[] { AssetType.Vehicle, AssetType.PassengerVehicle, AssetType.CommercialVehicle };
var source = await db.Assets.AsNoTracking().Where(x => x.IsActive && vehicleTypes.Contains(x.Type) && x.AssetCategoryId != null && x.DivisionId != null)
    .OrderBy(x => x.AssetNumber).FirstOrDefaultAsync() ?? throw new InvalidOperationException("A configured vehicle is required.");
var actor = await db.Users.AsNoTracking().Where(x => x.IsActive && x.CustomerId == null).OrderBy(x => x.Id).FirstAsync();
var tax = await db.Divisions.Where(x => x.Id == source.DivisionId).Select(x => x.DefaultTaxRate).SingleAsync();
var localToday = MaintenanceRules.LocalDate(DateTimeOffset.UtcNow);
var anchor = RentalAvailability.FijiTime(localToday, new TimeOnly(9, 0));
var asset = (Asset)db.Entry(source).CurrentValues.ToObject();
asset.Id = Guid.NewGuid(); asset.AssetNumber = number; asset.Name = "DEMO - Rental performance vehicle";
asset.RegistrationNumber = "DEMO-UTIL"; asset.SerialNumber = "DEMO-UTIL-SERIAL"; asset.VinOrChassisNumber = "DEMO-UTIL-CHASSIS";
asset.Status = AssetStatus.Available; asset.IsActive = true; asset.CreatedAt = anchor.AddDays(-180); asset.UpdatedAt = null;
asset.AcquisitionDate = localToday.AddDays(-180); asset.AcquisitionCost = 48000; asset.CurrentBookValue = 42000;
asset.CurrentLocation = "Demonstration fleet - fictional history"; asset.CurrentMeterReading = 42000; asset.MeterUnit = "km";
asset.DailyRate = 180; asset.NextServiceDate = localToday.AddDays(30); asset.InsuranceExpiry = localToday.AddMonths(6);
db.Assets.Add(asset);
var customer = new Customer { CustomerNumber = "DEMO-UTIL-CUS", Name = "DEMO - Pacific Hire Customer", Type = CustomerType.Business,
    Email = "asset-usage-demo@example.invalid", Address = "Fictional demonstration customer, Fiji", CreatedAt = asset.CreatedAt };
db.Customers.Add(customer);
void Life(AssetLifecycleEventType type, DateTimeOffset at, AssetStatus? from, AssetStatus to, Guid? booking = null) =>
    db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, Type = type, OccurredAt = at, CreatedAt = at,
        FromStatus = from, ToStatus = to, BookingId = booking, RecordedByUserId = actor.Id,
        RecordedByName = "Demo fleet coordinator", Notes = "FICTIONAL DEMO: no real hire or workshop activity occurred." });
Life(AssetLifecycleEventType.Commissioned, asset.CreatedAt, null, AssetStatus.Available);
var schedule = new[] { (-150, 5), (-125, 4), (-85, 5), (-72, 4), (-58, 6), (-42, 7), (-25, 5), (-12, 4), (7, 3), (15, 2) };
for (var i = 0; i < schedule.Length; i++)
{
    var (offset, days) = schedule[i]; var start = anchor.AddDays(offset); var end = start.AddDays(days); var done = offset < 0;
    var reference = $"DEMO-UTIL-{i + 1:00}";
    var booking = new Booking { BookingNumber = reference + "-BK", CustomerId = customer.Id, BranchId = asset.BranchId,
        Status = done ? BookingStatus.Completed : BookingStatus.Confirmed, TaxRate = tax,
        CreatedAt = done ? start.AddDays(-5) : DateTimeOffset.UtcNow.AddDays(-1),
        ApprovedAt = done ? start.AddDays(-4) : DateTimeOffset.UtcNow.AddHours(-12),
        ApprovedByUserId = actor.Id, Notes = "FICTIONAL DEMO: no actual rental occurred.", BondStatus = BondStatus.NotRequired };
    db.Bookings.Add(booking);
    db.BookingItems.Add(new BookingItem { BookingId = booking.Id, AssetId = asset.Id, StartAt = start, EndAt = end,
        DailyRate = asset.DailyRate, CreatedAt = booking.CreatedAt });
    if (!done) continue;
    foreach (var type in new[] { InspectionType.Handover, InspectionType.Return })
    {
        var at = type == InspectionType.Handover ? start : end;
        var reading = 33000 + i * 1000 + (type == InspectionType.Return ? 600 : 0);
        db.RentalInspections.Add(new RentalInspection { BookingId = booking.Id, Type = type, CompletedAt = at, CreatedAt = at,
            CompletedByUserId = actor.Id, CompletedByName = "Demo rental officer", IdentificationVerified = true,
            DriverLicenceVerified = true, PaymentVerified = true, FuelLevelPercent = 90, MeterReading = reading,
            ConditionNotes = "FICTIONAL DEMO: inspected in good condition.", SignatureName = "Demo Customer", EvidenceJson = "[]" });
        db.AssetMeterReadings.Add(new AssetMeterReading { AssetId = asset.Id, BookingId = booking.Id, Type = MeterType.Odometer,
            Unit = "km", Reading = reading, RecordedAt = at, CreatedAt = at, RecordedByUserId = actor.Id,
            Source = type == InspectionType.Handover ? MeterReadingSource.PreHireInspection : MeterReadingSource.PostHireInspection });
    }
    Life(AssetLifecycleEventType.CheckedOut, start, AssetStatus.Available, AssetStatus.Rented, booking.Id);
    Life(AssetLifecycleEventType.PostHireInspection, end, AssetStatus.Rented, AssetStatus.Available, booking.Id);
    var subtotal = asset.DailyRate * days; var vat = Math.Round(subtotal * tax / 100, 2);
    var invoice = new RentalInvoice { InvoiceNumber = reference + "-INV", BookingId = booking.Id, Subtotal = subtotal,
        TaxAmount = vat, Total = subtotal + vat, AmountPaid = subtotal + vat, BalanceDue = 0, Status = InvoiceStatus.Paid,
        IssuedAt = end, CreatedAt = end, LineItemsJson = JsonSerializer.Serialize(new[] { new { description = asset.Name, quantity = days, unitPrice = asset.DailyRate } }) };
    db.RentalInvoices.Add(invoice);
    db.InvoiceLines.Add(new InvoiceLine { InvoiceId = invoice.Id, Description = "FICTIONAL DEMO vehicle hire", Quantity = days, UnitPrice = asset.DailyRate, TaxRate = tax, CreatedAt = end });
    var payment = new RentalPayment { BookingId = booking.Id, Type = PaymentType.RentalCharge, Method = PaymentMethod.Cash,
        Amount = invoice.Total, ReceiptNumber = reference + "-RCT", RecordedByUserId = actor.Id, RecordedByName = "Demo rental officer",
        CreatedAt = end, Note = "FICTIONAL DEMO: no payment was received." };
    db.RentalPayments.Add(payment);
    db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = payment.Id, InvoiceId = invoice.Id, Amount = payment.Amount,
        AllocatedByUserId = actor.Id, CreatedAt = end });
}
var workshop = new[] { (-143, 24), (-117, 48), (-78, 36), (-65, 60), (-49, 48), (-31, 72), (-17, 24), (-6, 36) };
var services = new[] { "Oil and filter service", "Brake service", "Tyres and alignment", "Cooling system repair" };
for (var i = 0; i < workshop.Length; i++)
{
    var (offset, hours) = workshop[i]; var start = anchor.AddDays(offset); var release = start.AddHours(hours);
    var inspection = new AssetInspection { AssetId = asset.Id, Stage = InspectionStage.Maintenance, Outcome = InspectionOutcome.Passed,
        CompletedAt = release, CreatedAt = release, CompletedByUserId = actor.Id, CompletedByName = "Demo safety inspector",
        Notes = "FICTIONAL DEMO: passed safety checks and returned to service." };
    db.AssetInspections.Add(inspection);
    db.MaintenanceJobs.Add(new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = $"DEMO-UTIL-MNT-{i + 1:00}",
        ServiceType = services[i % services.Length], FaultDescription = "FICTIONAL DEMO: routine workshop example.",
        Description = "Fictional history for the rental performance demonstration.", Status = MaintenanceStatus.Completed,
        IsPreventive = i % 2 == 0, ReportedAt = start, StartedAt = start.AddHours(1), CompletedAt = release.AddHours(-2),
        ReleasedAt = release, ReleasedByName = "Demo safety inspector", ReleaseInspectionId = inspection.Id,
        DowntimeHours = hours, LabourHours = 2 + i, LabourRate = 35, LabourCost = (2 + i) * 35,
        PartsCost = 80 + i * 25, ActualCost = (2 + i) * 35 + 80 + i * 25, UseDetailedCosts = true,
        CreatedAt = start, UpdatedAt = release, InvoiceNumber = $"DEMO-UTIL-WORKSHOP-{i + 1:00}" });
    Life(AssetLifecycleEventType.MaintenanceStarted, start, AssetStatus.Available, AssetStatus.Maintenance);
    Life(AssetLifecycleEventType.ReturnedToService, release, AssetStatus.Maintenance, AssetStatus.Available);
}
await db.SaveChangesAsync();
await transaction.CommitAsync();
var usage = await AssetUsageSummary.Build(db, asset, 90, DateTimeOffset.UtcNow, CancellationToken.None);
Console.WriteLine($"Created {number} ({asset.Id}): 8 historical hires, 2 future bookings, 8 released maintenance jobs, inspections, readings, invoices and fictional receipts.");
Console.WriteLine($"Last 90 days: rented {usage.RentedHours} h, maintenance {usage.MaintenanceHours} h, available {usage.AvailableHours} h. No existing records changed. No notifications sent.");
