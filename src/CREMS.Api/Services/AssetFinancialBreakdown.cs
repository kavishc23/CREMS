using CREMS.Api.Data;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class AssetFinancialBreakdown
{
    public sealed record Entry(Guid Id, DateOnly Date, string Reference, string Category, string Description,
        decimal Revenue, decimal Expense, string? RelatedReference = null, decimal? Quantity = null,
        decimal? Rate = null, string? RecordedBy = null);

    public static async Task<object> Build(ApplicationDbContext db, Guid assetId, CancellationToken token)
    {
        var asset = await db.Assets.AsNoTracking().Include(x => x.Branch).Include(x => x.Division).SingleAsync(x => x.Id == assetId, token);
        var bookings = await db.Bookings.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).ThenInclude(x => x.Asset)
            .Include(x => x.Charges).Include(x => x.Invoice).Include(x => x.Payments).AsSplitQuery()
            .Where(x => x.Items.Any(i => i.AssetId == assetId) && (x.Status == BookingStatus.Completed || x.Status == BookingStatus.ConvertedToRental)).ToListAsync(token);
        var maintenance = await db.MaintenanceJobs.AsNoTracking().Where(x => x.AssetId == assetId).OrderByDescending(x => x.ReportedAt).ToListAsync(token);
        var costs = await db.AssetCostEntries.AsNoTracking().Where(x => x.AssetId == assetId).OrderByDescending(x => x.OccurredOn).ToListAsync(token);
        var transfers = await db.AssetTransfers.AsNoTracking().Where(x => x.AssetId == assetId &&
            (x.Status == Domain.Corporate.TransferStatus.Received || x.Status == Domain.Corporate.TransferStatus.Inspected)).ToListAsync(token);
        var bookingIds = bookings.Select(x => x.Id).ToArray();
        var sheets = await db.PersonnelTimesheets.AsNoTracking().Include(x => x.Assignment).Where(x => x.Assignment != null && bookingIds.Contains(x.Assignment.BookingId)).ToListAsync(token);
        var invoiceIds = bookings.Where(x => x.Invoice != null).Select(x => x.Invoice!.Id).ToArray();
        var credits = await db.CreditNotes.AsNoTracking().Where(x => invoiceIds.Contains(x.InvoiceId) && x.Status == CreditNoteStatus.Applied).ToListAsync(token);
        var ledger = new List<Entry>();
        decimal Share(decimal value, Booking booking) => AssetProfitabilityReport.Allocate(value, booking.Items.Select(x => x.AssetId)).GetValueOrDefault(assetId);
        foreach (var booking in bookings)
        {
            foreach (var item in booking.Items.Where(x => x.AssetId == assetId))
                ledger.Add(new(item.Id, MaintenanceRules.LocalDate(item.StartAt), booking.BookingNumber, "Base hire", $"{booking.Customer?.Name} · {item.HireUnit}", HirePricing.Amount(item), 0, booking.Invoice?.InvoiceNumber, HirePricing.Quantity(item), item.DailyRate));
            foreach (var charge in booking.Charges.Where(x => x.AssetId == assetId || x.AssetId == null))
            {
                var revenue = RentalFinancials.Money(charge.Quantity * charge.UnitRate);
                var expense = RentalFinancials.Money(charge.Quantity * charge.UnitCost);
                var actualPersonnel = sheets.Any(x => x.Assignment!.BookingId == booking.Id) && charge.Category is Domain.Common.ChargeCategory.Operator or Domain.Common.ChargeCategory.Driver or Domain.Common.ChargeCategory.Labour;
                if (actualPersonnel) expense = 0; // Actual timesheets replace the quoted staffing cost below.
                ledger.Add(new(charge.Id, MaintenanceRules.LocalDate(charge.CreatedAt), booking.BookingNumber, charge.Category.ToString(), charge.Description + (charge.AssetId == null && booking.Items.Select(x => x.AssetId).Distinct().Count() > 1 ? " (shared equally across booking assets)" : ""), charge.AssetId == null ? Share(revenue, booking) : revenue, charge.AssetId == null ? Share(expense, booking) : expense, booking.Invoice?.InvoiceNumber, charge.Quantity, charge.UnitRate));
            }
            if (RentalFinancials.Unitemised(booking) > 0)
                ledger.Add(new(booking.Id, MaintenanceRules.LocalDate(booking.UpdatedAt ?? booking.CreatedAt), booking.BookingNumber, "Other charges", booking.AdditionalChargesDescription ?? "Unitemised agreed or return charges", Share(RentalFinancials.Unitemised(booking), booking), 0, booking.Invoice?.InvoiceNumber));
            if (booking.DiscountAmount > 0)
                ledger.Add(new(booking.Id, MaintenanceRules.LocalDate(booking.CreatedAt), booking.BookingNumber, "Discount", "Booking discount allocated across assets", -Share(booking.DiscountAmount, booking), 0, booking.Invoice?.InvoiceNumber));
            foreach (var credit in credits.Where(x => x.InvoiceId == booking.Invoice?.Id))
                ledger.Add(new(credit.Id, MaintenanceRules.LocalDate(credit.CreatedAt), booking.BookingNumber, "Credit note", credit.Reason, -Share(credit.Subtotal, booking), 0, credit.CreditNoteNumber));
            if (booking.Invoice?.Status == InvoiceStatus.Voided)
                ledger.Add(new(booking.Invoice.Id, MaintenanceRules.LocalDate(booking.Invoice.IssuedAt), booking.BookingNumber, "Voided invoice", "Revenue reversed for voided invoice", -ledger.Where(x => x.Reference == booking.BookingNumber).Sum(x => x.Revenue), 0, booking.Invoice.InvoiceNumber));
            foreach (var sheet in sheets.Where(x => x.Assignment!.BookingId == booking.Id))
                ledger.Add(new(sheet.Id, sheet.WorkDate, booking.BookingNumber, "Personnel", "Recorded staff time including overtime", 0, Share(RentalFinancials.Money(sheet.RegularHours * sheet.Assignment!.InternalHourlyCost + sheet.OvertimeHours * sheet.Assignment.InternalHourlyCost * 1.5m), booking)));
        }
        foreach (var job in maintenance)
            ledger.Add(new(job.Id, MaintenanceRules.LocalDate(job.ReportedAt), job.JobNumber, "Maintenance", job.ServiceType, 0, job.ActualCost ?? MaintenanceCosts.Total(job), job.InvoiceNumber));
        foreach (var cost in costs)
            ledger.Add(new(cost.Id, cost.OccurredOn, cost.ReferenceNumber ?? "Direct cost", cost.Category.ToString(), cost.Description, 0, cost.Amount, cost.Supplier, RecordedBy: cost.RecordedByName));
        foreach (var transfer in transfers)
            ledger.Add(new(transfer.Id, MaintenanceRules.LocalDate(transfer.CreatedAt), transfer.TransferNumber, "Transfer", "Received asset transfer", 0, transfer.TransferCost));
        var revenueTotal = ledger.Sum(x => x.Revenue); var expenseTotal = ledger.Sum(x => x.Expense);
        var maintenanceExpense = maintenance.Sum(x => x.ActualCost ?? MaintenanceCosts.Total(x));
        var transferExpense = transfers.Sum(x => x.TransferCost);
        return new { asset.Id, asset.AssetNumber, asset.Name, rentalRevenue = revenueTotal, maintenanceExpense,
            operatingExpense = expenseTotal - maintenanceExpense - transferExpense, transferExpense, totalExpense = expenseTotal,
            operatingProfit = revenueTotal - expenseTotal, asset.AcquisitionCost, asset.CurrentBookValue,
            lifetimeNetAfterAcquisition = revenueTotal - expenseTotal - asset.AcquisitionCost,
            rentalCount = bookings.Count, rentalDays = bookings.SelectMany(x => x.Items).Where(x => x.AssetId == assetId).Sum(x => Math.Max(1, Math.Ceiling((x.EndAt - x.StartAt).TotalDays))),
            inspectionCount = await db.RentalInspections.CountAsync(x => x.Booking!.Items.Any(i => i.AssetId == assetId), token),
            basis = "Lifetime contracted revenue for active and completed hires, excluding VAT and refundable bonds. Shared charges, discounts and personnel costs are allocated equally across booking assets. Maintenance includes recorded tax; acquisition is shown separately. Receipts are cash movements, not additional revenue. Actual timesheets replace quoted staffing costs when present.",
            ledger = ledger.OrderByDescending(x => x.Date).ThenBy(x => x.Reference),
            maintenance = maintenance.Select(x => new { x.Id, x.JobNumber, x.ServiceType, x.Status, x.ReportedAt, x.CompletedAt, actualCost = x.ActualCost ?? MaintenanceCosts.Total(x), x.PartsCost, x.LabourCost, x.TransportCost, x.ExternalServiceCost, x.FuelCost, x.OtherCost, x.TaxCost, x.TaxMode, x.TaxRate, x.DowntimeHours, x.InvoiceNumber }),
            costs = costs.Select(x => new { x.Id, x.Category, x.Description, x.Amount, x.OccurredOn, x.Supplier, x.ReferenceNumber }),
            rentals = bookings.Select(x => new { x.Id, x.BookingNumber, customerName = x.Customer?.Name, status = x.Status.ToString(), invoiceNumber = x.Invoice?.InvoiceNumber,
                assetRevenue = ledger.Where(entry => entry.Reference == x.BookingNumber).Sum(entry => entry.Revenue),
                rentalCharge = x.Items.Where(item => item.AssetId == assetId).Sum(HirePricing.Amount), services = x.Charges.Where(c => c.AssetId == assetId || c.AssetId == null).Sum(c => c.AssetId == null ? Share(RentalFinancials.Money(c.Quantity * c.UnitRate), x) : RentalFinancials.Money(c.Quantity * c.UnitRate)) + Share(RentalFinancials.Unitemised(x), x), discount = Share(x.DiscountAmount, x),
                // Full booking totals are explicitly marked; they are not added to the asset ledger.
                bookingNet = RentalFinancials.Net(x), bookingVat = HirePricing.Tax(x), bookingTotal = RentalFinancials.Total(x), invoiceTotal = x.Invoice?.Total,
                paid = RentalFinancials.Paid(x), balance = Math.Max(0, (x.Invoice?.Total ?? RentalFinancials.Total(x)) - RentalFinancials.Paid(x)), x.BondAmountHeld, x.BondRefundAmount, x.BondDeductionAmount }),
            payments = bookings.SelectMany(x => x.Payments.Select(p => new { p.Id, x.BookingNumber, p.ReceiptNumber, type = p.Type.ToString(), method = p.Method.ToString(), status = p.Status.ToString(), p.Amount, p.CreatedAt, p.RecordedByName, p.Note })) };
    }
}
