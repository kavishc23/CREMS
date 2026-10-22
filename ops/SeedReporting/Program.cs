using System.Data;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

if (!args.Contains("--apply")) throw new InvalidOperationException("Use --apply to add fictional reporting data to the local Crems database.");
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var apiRoot = Path.Combine(root, "src/CREMS.Api");
var config = new ConfigurationBuilder().SetBasePath(apiRoot).AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets("crems-cs400-development").AddEnvironmentVariables().Build();
var connection = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Missing database connection.");
var target = new SqlConnectionStringBuilder(connection);
if (!string.Equals(target.InitialCatalog, "Crems", StringComparison.OrdinalIgnoreCase) ||
    !(target.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
      target.DataSource.StartsWith("127.0.0.1", StringComparison.Ordinal)))
    throw new InvalidOperationException("Reporting seed only targets the local Crems database.");

await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
using var suppression = db.SuppressNotifications();
var fiji = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Fiji");
var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, fiji).DateTime);
var first = new DateOnly(today.Year, today.Month, 1);
var marker = $"seed.reporting-{today:yyyy-MM}";
await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
if (await db.SystemSettings.AnyAsync(x => x.Key == marker))
{
    Console.WriteLine($"Reporting examples for {today:yyyy-MM} already exist; no records changed.");
    return;
}

var assets = await db.Assets.AsNoTracking().Where(x => x.IsActive && x.DivisionId != null && x.DailyRate > 0 && x.Branch!.IsActive)
    .OrderBy(x => x.AssetNumber).ToListAsync();
var selected = assets.GroupBy(x => new { x.BranchId, x.DivisionId }).SelectMany(group => group.Take(3)).Take(12).ToList();
if (selected.Count < 4) throw new InvalidOperationException("At least four configured assets are needed. Run the development seed first.");
var divisions = await db.Divisions.AsNoTracking().Where(x => selected.Select(a => a.DivisionId).Contains(x.Id)).ToDictionaryAsync(x => x.Id);
var actor = await db.Users.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).FirstOrDefaultAsync()
    ?? throw new InvalidOperationException("An active staff account is required before adding reporting examples.");
var now = DateTimeOffset.UtcNow;
var completedDays = Math.Max(1, today.Day - 2);
var bookingCount = 0; var invoiceCount = 0; var paymentCount = 0; var maintenanceCount = 0;
for (var index = 0; index < selected.Count; index++)
{
    var asset = selected[index];
    var dayNumber = Math.Min(completedDays, 2 + index);
    var pickupDate = first.AddDays(dayNumber - 1);
    var pickupLocal = pickupDate.ToDateTime(new TimeOnly(9, 0));
    var pickup = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(pickupLocal, fiji));
    var returnAt = pickup.AddDays(index % 2 + 1);
    var rate = asset.DailyRate;
    var days = index % 2 + 1;
    var taxRate = divisions[asset.DivisionId!.Value].DefaultTaxRate;
    var subtotal = rate * days;
    var tax = Math.Round(subtotal * taxRate / 100, 2);
    var total = subtotal + tax;
    var paid = index % 6 == 0 ? 0 : index % 4 == 0 ? Math.Round(total / 2, 2) : total;
    var reference = $"CFG-RPT-{today:yyyyMM}-{index + 1:00}";
    var customer = new Customer
    {
        CustomerNumber = reference + "-CUS", Name = new[] { "Pacific Site Contractors", "Litia Vakaloloma", "Sanjay Narayan", "Northern Valley Transport", "Mere Waqa", "Arjun Prasad" }[index % 6],
        Type = index % 3 == 0 ? CustomerType.Business : CustomerType.Individual,
        Email = $"reporting.{today:yyyyMM}.{index + 1:00}@example.invalid",
        Address = "Fictional reporting scenario, Fiji", CreatedAt = pickup.AddDays(-5)
    };
    db.Customers.Add(customer);
    var booking = new Booking
    {
        BookingNumber = reference + "-BK", CustomerId = customer.Id, BranchId = asset.BranchId,
        Status = BookingStatus.Completed, TaxRate = taxRate,
        Notes = "Fictional current-month reporting example. No real hire occurred.",
        ApprovedByUserId = actor.Id, ApprovedAt = pickup.AddDays(-2), CreatedAt = pickup.AddDays(-4)
    };
    db.Bookings.Add(booking);
    db.BookingItems.Add(new BookingItem { BookingId = booking.Id, AssetId = asset.Id, StartAt = pickup, EndAt = returnAt, DailyRate = rate });
    bookingCount++;
    var invoice = new RentalInvoice
    {
        InvoiceNumber = reference + "-INV", BookingId = booking.Id,
        LineItemsJson = JsonSerializer.Serialize(new[] { new { description = asset.Name + " hire ", quantity = days, unitPrice = rate } }),
        Subtotal = subtotal, TaxAmount = tax, Total = total, AmountPaid = paid, BalanceDue = total - paid,
        Status = paid == 0 ? InvoiceStatus.Issued : paid == total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid,
        IssuedAt = returnAt, CreatedAt = returnAt
    };
    db.RentalInvoices.Add(invoice);
    db.InvoiceLines.Add(new InvoiceLine { InvoiceId = invoice.Id, Description = asset.Name + " hire ", Quantity = days, UnitPrice = rate, TaxRate = taxRate });
    invoiceCount++;
    if (paid > 0)
    {
        var payment = new RentalPayment
        {
            BookingId = booking.Id, Type = PaymentType.RentalCharge, Method = (PaymentMethod)(index % 3),
            Amount = paid, ReceiptNumber = reference + "-RCT", RecordedByUserId = actor.Id,
            RecordedByName = "Reporting importer", CreatedAt = returnAt.AddHours(1),
            Note = "Fictional receipt; no money transferred."
        };
        db.RentalPayments.Add(payment);
        db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = payment.Id, InvoiceId = invoice.Id, Amount = paid, AllocatedByUserId = actor.Id });
        paymentCount++;
    }
    var completed = index % 4 != 0;
    db.MaintenanceJobs.Add(new MaintenanceJob
    {
        JobNumber = reference + "-MNT", AssetId = asset.Id, BranchId = asset.BranchId,
        Status = completed ? MaintenanceStatus.Completed : MaintenanceStatus.InProgress,
        ServiceType = index % 2 == 0 ? "Scheduled inspection and service" : "Post-hire condition repair",
        FaultDescription = "Fictional workshop reporting example.",
        Description = "operational record only; no actual workshop work was performed.",
        IsPreventive = index % 2 == 0, EstimatedCost = 95 + index * 20,
        ActualCost = completed ? 80 + index * 20 : null,
        LabourCost = completed ? 80 + index * 20 : 0,
        DowntimeHours = completed ? 3 + index % 5 : 0,
        ReportedAt = pickup.AddDays(-1), CompletedAt = completed ? pickup : null,
        CreatedAt = pickup.AddDays(-1)
    });
    maintenanceCount++;
}
db.SystemSettings.Add(new SystemSetting
{
    Key = marker, Value = now.ToString("O"), Category = "Seed",
    Description = "One-time current-month fictional bookings, invoices, payments and maintenance reporting examples."
});
await db.SaveChangesAsync();
await transaction.CommitAsync();
Console.WriteLine($"Added fictional {today:yyyy-MM} reporting examples to local Crems: {bookingCount} bookings, {invoiceCount} invoices, {paymentCount} payments and {maintenanceCount} maintenance jobs. No existing records were changed.");
