using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class PeriodProfitabilityReport
{
    private sealed record Entry(Guid AssetId, DateOnly Date, decimal Revenue = 0, decimal Maintenance = 0,
        decimal Direct = 0, decimal BookingCost = 0, decimal Personnel = 0)
    {
        public decimal Expense => Maintenance + Direct + BookingCost + Personnel;
    }

    public static async Task<object> Build(ApplicationDbContext db, IQueryable<Asset> assetScope,
        DateOnly from, DateOnly to, CancellationToken token)
    {
        var (start, end) = ReportingKpis.FijiPeriod(from, to);
        var assets = await assetScope.AsNoTracking().Select(x => new
        {
            x.Id, x.AssetNumber, x.Name, x.BranchId, branchName = x.Branch!.Name,
            x.DivisionId, divisionName = x.Division!.Name
        }).ToListAsync(token);
        var visible = assets.Select(x => x.Id).ToHashSet();
        var bookingIds = await db.Bookings.AsNoTracking()
            .Where(x => x.Status != BookingStatus.Cancelled && x.Status != BookingStatus.Expired &&
                x.Items.Any(i => visible.Contains(i.AssetId)))
            .Select(x => x.Id).ToListAsync(token);
        var items = await db.BookingItems.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId))
            .Select(x => new { x.BookingId, x.AssetId, x.StartAt, x.EndAt, x.DailyRate, x.HireQuantity }).ToListAsync(token);
        var weights = items.GroupBy(x => x.BookingId).ToDictionary(g => g.Key, g => g.GroupBy(i => i.AssetId)
            .ToDictionary(a => a.Key, a => a.Sum(i => Math.Max(0, i.DailyRate * (i.HireQuantity ?? Math.Max(1, (decimal)Math.Ceiling((i.EndAt - i.StartAt).TotalDays)))))));
        var invoices = await db.RentalInvoices.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId) &&
            x.Status != InvoiceStatus.Voided && x.Status != InvoiceStatus.Draft && x.IssuedAt >= start && x.IssuedAt < end)
            .Select(x => new { x.Id, x.BookingId, x.IssuedAt, x.Subtotal }).ToListAsync(token);
        var invoiceIds = invoices.Select(x => x.Id).ToList();
        var credits = await db.CreditNotes.AsNoTracking().Where(x => invoiceIds.Contains(x.InvoiceId) && x.Status == CreditNoteStatus.Applied)
            .Select(x => new { x.InvoiceId, x.Subtotal }).ToListAsync(token);
        var credited = credits.GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Subtotal));
        var entries = new List<Entry>();
        decimal unallocatedRevenue = 0;
        foreach (var invoice in invoices)
        {
            var net = invoice.Subtotal - credited.GetValueOrDefault(invoice.Id);
            var allocation = AllocateWeighted(net, weights.GetValueOrDefault(invoice.BookingId));
            if (allocation.Count == 0) unallocatedRevenue += net;
            foreach (var (id, amount) in allocation.Where(x => visible.Contains(x.Key)))
                entries.Add(new Entry(id, MaintenanceRules.LocalDate(invoice.IssuedAt), Revenue: amount));
        }

        var jobs = await db.MaintenanceJobs.AsNoTracking().Where(x => visible.Contains(x.AssetId) &&
            x.ReportedAt >= start && x.ReportedAt < end && x.Status != MaintenanceStatus.Cancelled).ToListAsync(token);
        entries.AddRange(jobs.Select(x => new Entry(x.AssetId, MaintenanceRules.LocalDate(x.ReportedAt),
            Maintenance: x.ActualCost ?? MaintenanceCosts.Total(x))));
        var direct = await db.AssetCostEntries.AsNoTracking().Where(x => visible.Contains(x.AssetId) &&
            x.OccurredOn >= from && x.OccurredOn <= to).Select(x => new { x.AssetId, x.OccurredOn, x.Amount }).ToListAsync(token);
        entries.AddRange(direct.Select(x => new Entry(x.AssetId, x.OccurredOn, Direct: x.Amount)));
        var charges = await db.BookingCharges.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId) &&
            x.CreatedAt >= start && x.CreatedAt < end)
            .Select(x => new { x.BookingId, x.AssetId, x.CreatedAt, x.Quantity, x.UnitCost }).ToListAsync(token);
        foreach (var charge in charges)
        {
            var cost = charge.Quantity * charge.UnitCost;
            var allocation = charge.AssetId.HasValue
                ? new Dictionary<Guid, decimal> { [charge.AssetId.Value] = cost }
                : AllocateWeighted(cost, weights.GetValueOrDefault(charge.BookingId));
            foreach (var (id, amount) in allocation.Where(x => visible.Contains(x.Key)))
                entries.Add(new Entry(id, MaintenanceRules.LocalDate(charge.CreatedAt), BookingCost: amount));
        }
        var timesheets = await db.PersonnelTimesheets.AsNoTracking().Where(x => x.Assignment != null &&
            bookingIds.Contains(x.Assignment.BookingId) && x.WorkDate >= from && x.WorkDate <= to)
            .Select(x => new { x.WorkDate, x.Assignment!.BookingId, x.RegularHours, x.OvertimeHours,
                x.Assignment.InternalHourlyCost }).ToListAsync(token);
        foreach (var sheet in timesheets)
        {
            var cost = sheet.RegularHours * sheet.InternalHourlyCost + sheet.OvertimeHours * sheet.InternalHourlyCost * 1.5m;
            foreach (var (id, amount) in AllocateWeighted(cost, weights.GetValueOrDefault(sheet.BookingId)).Where(x => visible.Contains(x.Key)))
                entries.Add(new Entry(id, sheet.WorkDate, Personnel: amount));
        }

        var grouped = entries.ToLookup(x => x.AssetId);
        var rows = assets.Select(asset =>
        {
            var values = grouped[asset.Id].ToArray();
            var revenue = values.Sum(x => x.Revenue); var maintenance = values.Sum(x => x.Maintenance);
            var directCost = values.Sum(x => x.Direct); var bookingCost = values.Sum(x => x.BookingCost);
            var personnelCost = values.Sum(x => x.Personnel);
            var expense = maintenance + directCost + bookingCost + personnelCost;
            var profit = ReportingKpis.Profit(revenue, maintenance, directCost, bookingCost, personnelCost);
            return new { asset.Id, asset.AssetNumber, asset.Name, asset.BranchId, asset.branchName,
                asset.DivisionId, asset.divisionName, revenue, maintenanceCost = maintenance, directCost,
                bookingCost, personnelCost, totalExpense = expense, profit,
                margin = ReportingKpis.Margin(revenue, profit) };
        }).OrderBy(x => x.profit).ToList();
        var months = Enumerable.Range(0, (to.Year - from.Year) * 12 + to.Month - from.Month + 1)
            .Select(offset => new DateOnly(from.Year, from.Month, 1).AddMonths(offset)).ToArray();
        var trend = months.Select(month =>
        {
            var values = entries.Where(x => x.Date >= month && x.Date < month.AddMonths(1)).ToArray();
            var revenue = values.Sum(x => x.Revenue); var expense = values.Sum(x => x.Expense);
            return new { month = month.ToString("yyyy-MM"), revenue, expense, profit = revenue - expense,
                margin = ReportingKpis.Margin(revenue, revenue - expense) };
        }).ToArray();
        var scopedEntries = entries.Join(assets, entry => entry.AssetId, asset => asset.Id,
            (entry, asset) => new { entry, asset }).ToArray();
        var branchTrend = scopedEntries.GroupBy(x => new { month = x.entry.Date.ToString("yyyy-MM"), x.asset.BranchId, x.asset.branchName })
            .Select(g => new { g.Key.month, g.Key.BranchId, g.Key.branchName,
                revenue = g.Sum(x => x.entry.Revenue), expense = g.Sum(x => x.entry.Expense),
                profit = g.Sum(x => x.entry.Revenue - x.entry.Expense) })
            .OrderBy(x => x.month).ThenBy(x => x.branchName).ToArray();
        var divisionTrend = scopedEntries.GroupBy(x => new { month = x.entry.Date.ToString("yyyy-MM"), x.asset.DivisionId, x.asset.divisionName })
            .Select(g => new { g.Key.month, g.Key.DivisionId, g.Key.divisionName,
                revenue = g.Sum(x => x.entry.Revenue), expense = g.Sum(x => x.entry.Expense),
                profit = g.Sum(x => x.entry.Revenue - x.entry.Expense) })
            .OrderBy(x => x.month).ThenBy(x => x.divisionName).ToArray();
        return new
        {
            totals = new { revenue = rows.Sum(x => x.revenue), expense = rows.Sum(x => x.totalExpense),
                profit = rows.Sum(x => x.profit), unallocatedRevenue },
            assets = rows, monthlyTrend = trend, branchTrend, divisionTrend,
            note = "Pre-tax invoice revenue net of applied credits is allocated by booked asset value. Costs use their own recorded dates; overhead and depreciation are excluded."
        };
    }

    private static Dictionary<Guid, decimal> AllocateWeighted(decimal amount, Dictionary<Guid, decimal>? weights)
    {
        if (weights is null || weights.Count == 0) return [];
        var ordered = weights.OrderBy(x => x.Key).ToArray();
        var totalWeight = ordered.Sum(x => x.Value);
        var result = new Dictionary<Guid, decimal>();
        decimal allocated = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var share = index == ordered.Length - 1 ? amount - allocated :
                decimal.Round(amount * (totalWeight == 0 ? 1m / ordered.Length : ordered[index].Value / totalWeight),
                    2, MidpointRounding.AwayFromZero);
            result[ordered[index].Key] = share;
            allocated += share;
        }
        return result;
    }
}
