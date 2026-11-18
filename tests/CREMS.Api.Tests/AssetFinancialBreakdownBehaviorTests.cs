using System.Text.Json;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Xunit;
namespace CREMS.Api.Tests;

public sealed class AssetFinancialBreakdownBehaviorTests
{
    private static CancellationToken T => TestContext.Current.CancellationToken;
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    [Fact]
    public async Task Empty_asset_has_no_revenue_and_keeps_acquisition_separate()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (_, asset) = await MaintenanceJobsTests.Setup(db);
        asset.AcquisitionCost = 5000; asset.CurrentBookValue = 4000;
        await db.SaveChangesAsync(T);
        var result = Json(await AssetFinancialBreakdown.Build(db, asset.Id, T));
        Assert.Equal(0, result.GetProperty("rentalRevenue").GetDecimal());
        Assert.Equal(0, result.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(-5000, result.GetProperty("lifetimeNetAfterAcquisition").GetDecimal());
        Assert.Equal(4000, result.GetProperty("currentBookValue").GetDecimal());
        Assert.Empty(result.GetProperty("ledger").EnumerateArray());
        Assert.Equal(0, result.GetProperty("rentalCount").GetInt32());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_charges_credits_staff_time_and_direct_costs_reconcile_without_double_counting(bool voided)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (_, asset) = await MaintenanceJobsTests.Setup(db);
        var other = new Asset { AssetNumber = "OTHER", Name = "Other equipment", BranchId = asset.BranchId };
        var customer = new Customer { CustomerNumber = "FIN-C", Name = "Training customer" };
        var rentalStart = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        var booking = new Booking { BookingNumber = "FIN-B", CustomerId = customer.Id, BranchId = asset.BranchId, Status = BookingStatus.Completed, DiscountAmount = 20, AdditionalCharges = 90, AdditionalChargesDescription = "Return fee", Items = [new() { AssetId = asset.Id, StartAt = rentalStart, EndAt = rentalStart.AddDays(2), HireQuantity = 2, DailyRate = 100 }, new() { AssetId = other.Id, StartAt = rentalStart, EndAt = rentalStart.AddDays(2), HireQuantity = 2, DailyRate = 50 }], Charges = [new() { AssetId = null, Category = ChargeCategory.Driver, Description = "Shared driver", Quantity = 2, UnitRate = 30, UnitCost = 20 }, new() { AssetId = asset.Id, Category = ChargeCategory.Fuel, Description = "Fuel", Quantity = 1, UnitRate = 10, UnitCost = 5 }, new() { AssetId = other.Id, Category = ChargeCategory.Fuel, Description = "Other fuel", Quantity = 1, UnitRate = 10, UnitCost = 8 }] };
        var invoice = new RentalInvoice { BookingId = booking.Id, InvoiceNumber = "INV-FIN", LineItemsJson = "[]", Total = 400, Status = voided ? InvoiceStatus.Voided : InvoiceStatus.Issued };
        booking.Invoice = invoice;
        var assignment = new BookingPersonnelAssignment { BookingId = booking.Id, Role = "Driver", InternalHourlyCost = 10 };
        var sheet = new PersonnelTimesheet { AssignmentId = assignment.Id, WorkDate = new(2026, 11, 16), RegularHours = 8, OvertimeHours = 2 };
        booking.Payments.Add(new RentalPayment { ReceiptNumber = "RC-FIN", Amount = 50, RecordedByName = "Training staff", Type = PaymentType.RentalCharge });
        db.AddRange(other, customer, booking, assignment, sheet,
            new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "M-ACTUAL", ServiceType = "Repair", ActualCost = 20 },
            new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "M-ESTIMATE", ServiceType = "Service", PartsCost = 10 },
            new CreditNote { InvoiceId = invoice.Id, CreditNoteNumber = "CN-FIN", Reason = "Agreed credit", Subtotal = 20, Total = 20, Status = CreditNoteStatus.Applied },
            new CreditNote { InvoiceId = invoice.Id, CreditNoteNumber = "CN-UNAPPLIED", Reason = "Draft credit", Subtotal = 999, Status = CreditNoteStatus.Issued },
            new AssetCostEntry { AssetId = asset.Id, BranchId = asset.BranchId, Category = AssetCostCategory.Fuel, Description = "Direct fuel", Amount = 15, OccurredOn = new(2026, 11, 16), RecordedByName = "Training staff" },
            new AssetTransfer { AssetId = asset.Id, TransferNumber = "TR-RECEIVED", Reason = "Training transfer", Status = TransferStatus.Received, TransferCost = 25 },
            new AssetTransfer { AssetId = asset.Id, TransferNumber = "TR-PENDING", Reason = "Pending transfer", TransferCost = 999 });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(T);
        var result = Json(await AssetFinancialBreakdown.Build(db, asset.Id, T));
        var ledger = result.GetProperty("ledger").EnumerateArray().ToArray();
        var shared = Assert.Single(ledger, x => x.GetProperty("category").GetString() == "Driver");
        Assert.Equal(30, shared.GetProperty("revenue").GetDecimal());
        Assert.Equal(0, shared.GetProperty("expense").GetDecimal());
        Assert.Contains("shared equally", shared.GetProperty("description").GetString());
        Assert.Equal(55, Assert.Single(ledger, x => x.GetProperty("category").GetString() == "Personnel").GetProperty("expense").GetDecimal());
        Assert.Equal(25, result.GetProperty("transferExpense").GetDecimal());
        Assert.Equal(130, result.GetProperty("totalExpense").GetDecimal());
        Assert.DoesNotContain(ledger, x => x.GetProperty("reference").GetString() == "TR-PENDING");
        Assert.Equal(1, result.GetProperty("rentalCount").GetInt32());
        Assert.Equal(2, result.GetProperty("rentalDays").GetDouble());
        Assert.Equal(30, result.GetProperty("maintenanceExpense").GetDecimal());
        Assert.Equal(50, Assert.Single(result.GetProperty("payments").EnumerateArray()).GetProperty("amount").GetDecimal());
        Assert.Equal(2, result.GetProperty("maintenance").GetArrayLength());
        var revenue = ledger.Sum(x => x.GetProperty("revenue").GetDecimal());
        Assert.Equal(voided ? 0 : 225, revenue);
        Assert.Equal(revenue - 130, result.GetProperty("operatingProfit").GetDecimal());
        Assert.Equal(revenue, result.GetProperty("rentals")[0].GetProperty("assetRevenue").GetDecimal());
    }
}
