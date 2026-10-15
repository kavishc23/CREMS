using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class HistoricalUtilizationReport
{
    private sealed record Interval(DateTimeOffset Start, DateTimeOffset End);

    public static async Task<object> Build(ApplicationDbContext db, IQueryable<Asset> assetScope,
        DateOnly from, DateOnly to, CancellationToken token)
    {
        var (start, requestedEnd) = ReportingKpis.FijiPeriod(from, to);
        var end = requestedEnd < DateTimeOffset.UtcNow ? requestedEnd : DateTimeOffset.UtcNow;
        var assets = await assetScope.AsNoTracking().Where(x => x.IsActive).Select(x => new
        {
            x.Id, x.AssetNumber, x.Name, x.BranchId, branchName = x.Branch!.Name,
            x.DivisionId, divisionName = x.Division!.Name, x.AcquisitionDate
        }).ToListAsync(token);
        var ids = assets.Select(x => x.Id).ToHashSet();
        var hires = await db.BookingItems.AsNoTracking().Where(x => ids.Contains(x.AssetId) &&
            x.Booking != null && (x.Booking.Status == BookingStatus.Completed || x.Booking.Status == BookingStatus.ConvertedToRental) &&
            x.StartAt < end && x.EndAt > start)
            .Select(x => new { x.AssetId, x.StartAt, x.EndAt }).ToListAsync(token);
        var jobs = await db.MaintenanceJobs.AsNoTracking().Where(x => ids.Contains(x.AssetId) &&
            x.Status != MaintenanceStatus.Cancelled && (x.StartedAt ?? x.ReportedAt) < end &&
            (x.ReleasedAt ?? x.CompletedAt ?? end) > start)
            .Select(x => new { x.AssetId, x.ReportedAt, x.StartedAt, x.CompletedAt, x.ReleasedAt }).ToListAsync(token);
        var hireLookup = hires.GroupBy(x => x.AssetId).ToDictionary(g => g.Key,
            g => g.Select(x => new Interval(x.StartAt, x.EndAt)).ToArray());
        var jobLookup = jobs.GroupBy(x => x.AssetId).ToDictionary(g => g.Key,
            g => g.Select(x => new Interval(x.StartedAt ?? x.ReportedAt, x.ReleasedAt ?? x.CompletedAt ?? end)).ToArray());
        object[] Compute(DateTimeOffset rangeStart, DateTimeOffset rangeEnd) => assets.Select(asset =>
        {
            var acquisition = asset.AcquisitionDate.HasValue
                ? ReportingKpis.FijiPeriod(asset.AcquisitionDate.Value, asset.AcquisitionDate.Value).Start : rangeStart;
            var capacityStart = acquisition > rangeStart ? acquisition : rangeStart;
            var capacity = Days(capacityStart, rangeEnd);
            var hired = UnionDays(hireLookup.GetValueOrDefault(asset.Id, []), capacityStart, rangeEnd);
            var downtime = UnionDays(jobLookup.GetValueOrDefault(asset.Id, []), capacityStart, rangeEnd);
            var occupied = UnionDays(hireLookup.GetValueOrDefault(asset.Id, []).Concat(jobLookup.GetValueOrDefault(asset.Id, [])), capacityStart, rangeEnd);
            var available = Math.Max(0, capacity - occupied);
            return (object)new { asset.Id, asset.AssetNumber, asset.Name, asset.BranchId, asset.branchName,
                asset.DivisionId, asset.divisionName, capacityDays = Round(capacity), hiredDays = Round(hired),
                downtimeDays = Round(downtime), availableDays = Round(available),
                utilizationPercent = ReportingKpis.Utilization(hired, capacity) };
        }).ToArray();
        var rows = Compute(start, end);
        // Use typed values for aggregates without relying on anonymous reflection.
        var totals = assets.Select(asset =>
        {
            var acquisition = asset.AcquisitionDate.HasValue
                ? ReportingKpis.FijiPeriod(asset.AcquisitionDate.Value, asset.AcquisitionDate.Value).Start : start;
            var capacityStart = acquisition > start ? acquisition : start;
            var capacity = Days(capacityStart, end);
            var hired = UnionDays(hireLookup.GetValueOrDefault(asset.Id, []), capacityStart, end);
            var downtime = UnionDays(jobLookup.GetValueOrDefault(asset.Id, []), capacityStart, end);
            var occupied = UnionDays(hireLookup.GetValueOrDefault(asset.Id, []).Concat(jobLookup.GetValueOrDefault(asset.Id, [])), capacityStart, end);
            return new { capacity, hired, downtime, available = Math.Max(0, capacity - occupied) };
        }).ToArray();
        var capacityTotal = totals.Sum(x => x.capacity); var hiredTotal = totals.Sum(x => x.hired);
        var months = Enumerable.Range(0, (to.Year - from.Year) * 12 + to.Month - from.Month + 1)
            .Select(offset => new DateOnly(from.Year, from.Month, 1).AddMonths(offset)).ToArray();
        var trend = months.Select(month =>
        {
            var (monthStart, monthEnd) = ReportingKpis.FijiPeriod(month, month.AddMonths(1).AddDays(-1));
            var boundedStart = monthStart > start ? monthStart : start;
            var boundedEnd = monthEnd < end ? monthEnd : end;
            var figures = assets.Select(asset =>
            {
                var acquisition = asset.AcquisitionDate.HasValue
                    ? ReportingKpis.FijiPeriod(asset.AcquisitionDate.Value, asset.AcquisitionDate.Value).Start : boundedStart;
                var actualStart = acquisition > boundedStart ? acquisition : boundedStart;
                return new { capacity = Days(actualStart, boundedEnd), hired = UnionDays(hireLookup.GetValueOrDefault(asset.Id, []), actualStart, boundedEnd),
                    downtime = UnionDays(jobLookup.GetValueOrDefault(asset.Id, []), actualStart, boundedEnd) };
            }).ToArray();
            var monthCapacity = figures.Sum(x => x.capacity); var monthHired = figures.Sum(x => x.hired);
            return new { month = month.ToString("yyyy-MM"), capacityDays = Round(monthCapacity),
                hiredDays = Round(monthHired), downtimeDays = Round(figures.Sum(x => x.downtime)),
                utilizationPercent = ReportingKpis.Utilization(monthHired, monthCapacity) };
        }).ToArray();
        return new { totals = new { capacityDays = Round(capacityTotal), hiredDays = Round(hiredTotal),
            downtimeDays = Round(totals.Sum(x => x.downtime)), availableDays = Round(totals.Sum(x => x.available)),
            utilizationPercent = ReportingKpis.Utilization(hiredTotal, capacityTotal) }, assets = rows, monthlyTrend = trend,
            note = "Intervals are clipped to the selected period and now. Overlapping hires or maintenance jobs count once; available days exclude the union of hire and maintenance time." };
    }

    private static decimal Days(DateTimeOffset start, DateTimeOffset end) => end <= start ? 0 : (decimal)(end - start).TotalDays;
    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal UnionDays(IEnumerable<Interval> intervals, DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start) return 0;
        var ordered = intervals.Select(x => new Interval(x.Start < start ? start : x.Start, x.End > end ? end : x.End))
            .Where(x => x.End > x.Start).OrderBy(x => x.Start).ToArray();
        if (ordered.Length == 0) return 0;
        var total = TimeSpan.Zero; var currentStart = ordered[0].Start; var currentEnd = ordered[0].End;
        foreach (var interval in ordered.Skip(1))
        {
            if (interval.Start <= currentEnd) { if (interval.End > currentEnd) currentEnd = interval.End; }
            else { total += currentEnd - currentStart; currentStart = interval.Start; currentEnd = interval.End; }
        }
        total += currentEnd - currentStart;
        return (decimal)total.TotalDays;
    }
}
