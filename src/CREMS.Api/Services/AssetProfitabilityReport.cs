using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class AssetProfitabilityReport
{
    private sealed record Entry(Guid? AssetId, DateOnly Date, decimal Revenue, decimal Maintenance = 0, decimal Direct = 0, decimal Component = 0, decimal Personnel = 0, decimal Transfer = 0)
    {
        public decimal Expense => Maintenance + Direct + Component + Personnel + Transfer;
    }

    // Allocate before applying visibility, so restricted views cannot inherit another asset's costs.
    public static Dictionary<Guid, decimal> Allocate(decimal amount, IEnumerable<Guid> assetIds)
    {
        var ids = assetIds.Distinct().Order().ToArray();
        if (ids.Length == 0) return [];
        var cents = decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero);
        var share = decimal.Truncate(cents / ids.Length);
        var remainder = (int)(cents - share * ids.Length);
        return ids.Select((id, index) => new { id, amount = (share + (index < Math.Abs(remainder) ? Math.Sign(remainder) : 0)) / 100 })
            .ToDictionary(x => x.id, x => x.amount);
    }

    public static async Task<object> Build(ApplicationDbContext db, IQueryable<Asset> assetScope, bool includeUnallocated, CancellationToken token)
    {
        var assets = await assetScope.AsNoTracking().ToListAsync(token);
        var ids = assets.Select(x => x.Id).ToHashSet();
        var bookings = await db.Bookings.AsNoTracking()
            .Where(x => (x.Status == BookingStatus.Completed || x.Status == BookingStatus.ConvertedToRental)
                && (includeUnallocated || x.Items.Any(i => ids.Contains(i.AssetId)) || x.Charges.Any(c => c.AssetId.HasValue && ids.Contains(c.AssetId.Value))))
            .Select(x => x.Id).ToListAsync(token);
        var items = await db.BookingItems.AsNoTracking().Where(x => bookings.Contains(x.BookingId)).ToListAsync(token);
        var bookingAssets = items.GroupBy(x => x.BookingId).ToDictionary(g => g.Key, g => g.Select(x => x.AssetId).Distinct().ToArray());
        var charges = await db.BookingCharges.AsNoTracking().Where(x => bookings.Contains(x.BookingId)).ToListAsync(token);
        var timesheets = await db.PersonnelTimesheets.AsNoTracking().Include(x => x.Assignment)
            .Where(x => x.Assignment != null && bookings.Contains(x.Assignment.BookingId)).ToListAsync(token);
        var maintenance = await db.MaintenanceJobs.AsNoTracking().Where(x => ids.Contains(x.AssetId)).ToListAsync(token);
        var costs = await db.AssetCostEntries.AsNoTracking().Where(x => ids.Contains(x.AssetId)).ToListAsync(token);
        var bookingAdjustments = await db.Bookings.AsNoTracking().Where(x => bookings.Contains(x.Id))
            .Select(x => new { x.Id, x.DiscountAmount, x.AdditionalCharges, x.AdditionalChargesDescription, x.CreatedAt }).ToListAsync(token);
        var invoices = await db.RentalInvoices.AsNoTracking().Where(x => bookings.Contains(x.BookingId)).Select(x => new { x.Id, x.BookingId, x.Status, x.IssuedAt }).ToListAsync(token);
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var credits = await db.CreditNotes.AsNoTracking().Where(x => invoiceIds.Contains(x.InvoiceId) && x.Status == CreditNoteStatus.Applied).ToListAsync(token);
        var transfers = await db.AssetTransfers.AsNoTracking().Where(x => ids.Contains(x.AssetId) &&
            (x.Status == Domain.Corporate.TransferStatus.Received || x.Status == Domain.Corporate.TransferStatus.Inspected)).ToListAsync(token);
        var entries = new List<Entry>();
        void Shared(Guid bookingId, DateOnly date, decimal revenue, decimal expense, bool personnel = false)
        {
            revenue = decimal.Round(revenue, 2, MidpointRounding.AwayFromZero);
            expense = decimal.Round(expense, 2, MidpointRounding.AwayFromZero);
            var targets = bookingAssets.GetValueOrDefault(bookingId, []);
            if (targets.Length == 0) {
                if (includeUnallocated) entries.Add(new(null, date, revenue, Component: personnel ? 0 : expense, Personnel: personnel ? expense : 0));
                return;
            }
            var revenues = Allocate(revenue, targets); var expenses = Allocate(expense, targets);
            foreach (var id in targets.Where(ids.Contains))
                entries.Add(new(id, date, revenues[id], Component: personnel ? 0 : expenses[id], Personnel: personnel ? expenses[id] : 0));
        }
        foreach (var item in items.Where(x => ids.Contains(x.AssetId)))
            entries.Add(new(item.AssetId, MaintenanceRules.LocalDate(item.CreatedAt), HirePricing.Amount(item)));
        foreach (var booking in bookingAdjustments)
        {
            var residual = Math.Max(0, booking.AdditionalCharges - charges.Where(x => x.BookingId == booking.Id).Sum(x => RentalFinancials.Money(x.Quantity * x.UnitRate)));
            Shared(booking.Id, MaintenanceRules.LocalDate(booking.CreatedAt), residual - booking.DiscountAmount, 0);
        }
        foreach (var charge in charges) {
            var date = MaintenanceRules.LocalDate(charge.CreatedAt);
            var expense = timesheets.Any(x => x.Assignment!.BookingId == charge.BookingId) && charge.Category is Domain.Common.ChargeCategory.Operator or Domain.Common.ChargeCategory.Driver or Domain.Common.ChargeCategory.Labour
                ? 0 : charge.Quantity * charge.UnitCost;
            if (charge.AssetId.HasValue) {
                if (ids.Contains(charge.AssetId.Value)) entries.Add(new(charge.AssetId, date, decimal.Round(charge.Quantity * charge.UnitRate, 2, MidpointRounding.AwayFromZero), Component: decimal.Round(expense, 2, MidpointRounding.AwayFromZero)));
            } else Shared(charge.BookingId, date, charge.Quantity * charge.UnitRate, expense);
        }
        foreach (var credit in credits)
        {
            var invoice = invoices.First(x => x.Id == credit.InvoiceId);
            if (invoice.Status != InvoiceStatus.Voided) Shared(invoice.BookingId, MaintenanceRules.LocalDate(credit.CreatedAt), -credit.Subtotal, 0);
        }
        foreach (var invoice in invoices.Where(x => x.Status == InvoiceStatus.Voided))
        {
            var targets = bookingAssets.GetValueOrDefault(invoice.BookingId, []);
            foreach (var id in targets.Where(ids.Contains))
            {
                var revenue = items.Where(x => x.BookingId == invoice.BookingId && x.AssetId == id).Sum(HirePricing.Amount)
                    + charges.Where(x => x.BookingId == invoice.BookingId && x.AssetId == id).Sum(x => RentalFinancials.Money(x.Quantity * x.UnitRate));
                revenue += Allocate(charges.Where(x => x.BookingId == invoice.BookingId && x.AssetId == null).Sum(x => RentalFinancials.Money(x.Quantity * x.UnitRate)), targets).GetValueOrDefault(id);
                var adjustment = bookingAdjustments.First(x => x.Id == invoice.BookingId);
                var residual = Math.Max(0, adjustment.AdditionalCharges - charges.Where(x => x.BookingId == invoice.BookingId).Sum(x => RentalFinancials.Money(x.Quantity * x.UnitRate)));
                revenue += Allocate(residual - adjustment.DiscountAmount, targets).GetValueOrDefault(id);
                entries.Add(new(id, MaintenanceRules.LocalDate(invoice.IssuedAt), -revenue));
            }
        }
        foreach (var sheet in timesheets)
            Shared(sheet.Assignment!.BookingId, sheet.WorkDate, 0, sheet.RegularHours * sheet.Assignment.InternalHourlyCost + sheet.OvertimeHours * sheet.Assignment.InternalHourlyCost * 1.5m, true);
        foreach (var job in maintenance) entries.Add(new(job.AssetId, MaintenanceRules.LocalDate(job.ReportedAt), 0, Maintenance: job.ActualCost ?? MaintenanceCosts.Total(job)));
        foreach (var cost in costs) entries.Add(new(cost.AssetId, cost.OccurredOn, 0, Direct: cost.Amount));
        foreach (var transfer in transfers) entries.Add(new(transfer.AssetId, MaintenanceRules.LocalDate(transfer.CreatedAt), 0, Transfer: transfer.TransferCost));
        var grouped = entries.Where(x => x.AssetId.HasValue).ToLookup(x => x.AssetId!.Value);
        var rows = assets.Select(asset => {
            var recorded = grouped[asset.Id].ToList();
            var revenue = recorded.Sum(x => x.Revenue); var expense = recorded.Sum(x => x.Expense); var profit = revenue - expense;
            var usage = items.Where(x => x.AssetId == asset.Id).Sum(x => Math.Max(0, (decimal)(x.EndAt - x.StartAt).TotalHours));
            return new { asset.Id, asset.AssetNumber, asset.Name, asset.BranchId, asset.DivisionId, revenue,
                maintenanceCost = recorded.Sum(x => x.Maintenance), directCost = recorded.Sum(x => x.Direct), componentCost = recorded.Sum(x => x.Component), personnelCost = recorded.Sum(x => x.Personnel), transferCost = recorded.Sum(x => x.Transfer),
                totalExpense = expense, grossProfit = profit, profitMargin = revenue == 0 ? 0 : Math.Round(profit * 100 / revenue, 1), asset.AcquisitionCost, asset.CurrentBookValue,
                costPerOperatingHour = usage == 0 ? 0 : Math.Round(expense / usage, 2), asset.CurrentMeterReading, asset.MeterUnit, lossMaking = profit < 0 };
        }).OrderBy(x => x.grossProfit).ToList();
        var today = MaintenanceRules.LocalDate(DateTimeOffset.UtcNow); var first = new DateOnly(today.Year, today.Month, 1);
        var monthlyTrend = Enumerable.Range(0,12).Select(offset => {
            var start = first.AddMonths(offset - 11); var end = start.AddMonths(1); var monthEntries = entries.Where(x => x.Date >= start && x.Date < end).ToList();
            var revenue = monthEntries.Sum(x => x.Revenue); var expense = monthEntries.Sum(x => x.Expense);
            return new { month = start.ToString("yyyy-MM"), revenue, expense, profit = revenue - expense };
        }).ToList();
        var unassigned = entries.Where(x => x.AssetId == null).ToList();
        return new { generatedAt = DateTimeOffset.UtcNow,
            totals = new { revenue = entries.Sum(x => x.Revenue), expense = entries.Sum(x => x.Expense), profit = entries.Sum(x => x.Revenue - x.Expense), lossMakingAssets = rows.Count(x => x.lossMaking) },
            unallocated = new { revenue = unassigned.Sum(x => x.Revenue), expense = unassigned.Sum(x => x.Expense), profit = unassigned.Sum(x => x.Revenue - x.Expense) },
            monthlyTrend, assets = rows };
    }
}
