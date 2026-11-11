using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public sealed record AssetTimeTotal(string Activity, decimal Hours, decimal Days, decimal Percent);
public sealed record AssetUsageResult(DateTimeOffset From, DateTimeOffset To, int RequestedDays,
    decimal TotalHours, decimal InServiceHours, decimal RentedHours, decimal AvailableHours,
    decimal MaintenanceHours, decimal ReservedHours, decimal OtherUnavailableHours,
    decimal UtilisationPercent, decimal DowntimePercent, int RentalCount, int MaintenanceCount,
    int EstimatedRentalCount, decimal ConflictingHours, string Basis, IReadOnlyList<AssetTimeTotal> Breakdown);

/// <summary>Elapsed asset time, independent of financial permissions and history display limits.</summary>
public static class AssetUsageSummary
{
    public const string Basis = "Calendar hours, not engine-running hours. In service = rented + available. " +
        "Pickup and return records determine hire time; completed hires without pickup records use booked dates. " +
        "Maintenance includes the wait for safety release. Overlaps count once, with maintenance taking priority. " +
        "Other holds include inspection, transfer and out-of-service time. Unrecorded time is treated as available; " +
        "branch opening hours and turnaround buffers are not deducted. Days are 24-hour equivalents.";

    private sealed record Span(DateTimeOffset Start, DateTimeOffset End, string Kind);

    public static async Task<AssetUsageResult> Build(ApplicationDbContext db, Asset asset, int days,
        DateTimeOffset now, CancellationToken token)
    {
        var items = await db.BookingItems.AsNoTracking().Where(x => x.AssetId == asset.Id)
            .Include(x => x.Booking).ThenInclude(x => x!.Inspections).AsSplitQuery().ToListAsync(token);
        var jobs = await db.MaintenanceJobs.AsNoTracking().Where(x => x.AssetId == asset.Id).ToListAsync(token);
        var history = await db.AssetLifecycleEvents.AsNoTracking().Where(x => x.AssetId == asset.Id)
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToListAsync(token);
        var transfers = await db.AssetTransfers.AsNoTracking().Where(x => x.AssetId == asset.Id).ToListAsync(token);
        return Calculate(asset, items, jobs, history, transfers, days, now);
    }

    public static AssetUsageResult Calculate(Asset asset, IReadOnlyList<BookingItem> items,
        IReadOnlyList<MaintenanceJob> jobs, IReadOnlyList<AssetLifecycleEvent> history,
        IReadOnlyList<AssetTransfer> transfers, int days, DateTimeOffset now)
    {
        if (days is < 1 or > 3660) throw new ArgumentOutOfRangeException(nameof(days));
        // Imported operational records can legitimately predate the asset's CREMS creation timestamp.
        var evidence = items.Where(x => x.Booking?.Status is BookingStatus.Completed or BookingStatus.ConvertedToRental)
            .Select(x => x.StartAt).Concat(jobs.Select(x => x.ReportedAt)).Concat(history.Select(x => x.OccurredAt))
            .Append(asset.CreatedAt).Min();
        var from = now.AddDays(-days);
        if (evidence > from) from = evidence < now ? evidence : now;
        var spans = new List<Span>();
        bool Add(DateTimeOffset start, DateTimeOffset end, string kind)
        {
            start = start < from ? from : start; end = end > now ? now : end;
            if (end <= start) return false;
            spans.Add(new(start, end, kind)); return true;
        }
        var rentals = new HashSet<Guid>(); var estimated = new HashSet<Guid>(); var maintenance = 0;
        foreach (var item in items)
        {
            var booking = item.Booking;
            if (booking is null || booking.Status is BookingStatus.Cancelled or BookingStatus.Expired or BookingStatus.Draft) continue;
            var pickup = booking.Inspections.Where(x => x.Type == InspectionType.Handover).Select(x => (DateTimeOffset?)x.CompletedAt).Min();
            var returned = booking.Inspections.Where(x => x.Type == InspectionType.Return).Select(x => (DateTimeOffset?)x.CompletedAt).Max();
            // Use asset-specific checkout evidence before booking-wide inspection timestamps.
            pickup = history.Where(x => x.BookingId == booking.Id && x.Type is AssetLifecycleEventType.CheckedOut or AssetLifecycleEventType.OnHire)
                .Select(x => (DateTimeOffset?)x.OccurredAt).Min() ?? pickup;
            if (booking.Status == BookingStatus.Confirmed && pickup is null) { Add(item.StartAt, item.EndAt, "Reserved"); continue; }
            var start = pickup ?? item.StartAt;
            // Without a return inspection an active/overdue hire remains on rent up to now.
            var end = returned ?? (pickup.HasValue || booking.Status == BookingStatus.ConvertedToRental ? now : item.EndAt);
            if (Add(start, end, "Rented")) { rentals.Add(booking.Id); if (!pickup.HasValue) estimated.Add(booking.Id); }
        }
        foreach (var job in jobs)
        {
            if (job.Status == MaintenanceStatus.Cancelled && job.ReleasedAt is null && job.StartedAt is null) continue;
            if (Add(job.ReportedAt, job.ReleasedAt ?? now, "Maintenance")) maintenance++;
        }
        var ordered = history.Where(x => x.OccurredAt <= now).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var status = ordered[i].ToStatus;
            if (status is AssetStatus.Inspection or AssetStatus.OutOfService or AssetStatus.Retired)
                Add(ordered[i].OccurredAt, i + 1 < ordered.Length ? ordered[i + 1].OccurredAt : now, "Other unavailable");
            // Lifecycle evidence also supports older maintenance records with no matching job.
            if (status == AssetStatus.Maintenance)
                Add(ordered[i].OccurredAt, i + 1 < ordered.Length ? ordered[i + 1].OccurredAt : now, "Maintenance");
        }
        if (ordered.Length == 0 && (!asset.IsActive || asset.Status is AssetStatus.Inspection or AssetStatus.OutOfService or AssetStatus.Retired))
            Add(from, now, "Other unavailable");
        foreach (var transfer in transfers.Where(x => x.Status != TransferStatus.Cancelled))
            if (transfer.DepartedAt.HasValue) Add(transfer.DepartedAt.Value,
                transfer.Status == TransferStatus.Inspected ? transfer.UpdatedAt ?? transfer.ReceivedAt ?? now : now, "Other unavailable");

        var totals = new Dictionary<string, decimal> { ["Rented"] = 0, ["Available"] = 0, ["Maintenance"] = 0, ["Reserved"] = 0, ["Other unavailable"] = 0 };
        var points = spans.SelectMany(x => new[] { x.Start, x.End }).Append(from).Append(now).Distinct().Order().ToArray();
        decimal conflict = 0;
        for (var i = 1; i < points.Length; i++)
        {
            var active = spans.Where(x => x.Start < points[i] && x.End > points[i - 1]).Select(x => x.Kind).ToHashSet();
            var hours = (decimal)(points[i] - points[i - 1]).Ticks / TimeSpan.TicksPerHour;
            if (active.Contains("Maintenance") && active.Contains("Rented")) conflict += hours;
            var kind = active.Contains("Maintenance") ? "Maintenance" : active.Contains("Other unavailable") ? "Other unavailable" :
                active.Contains("Rented") ? "Rented" : active.Contains("Reserved") ? "Reserved" : "Available";
            totals[kind] += hours;
        }
        var total = totals.Values.Sum();
        decimal Round(decimal n) => Math.Round(n, 2, MidpointRounding.AwayFromZero);
        decimal Percent(decimal n) => total == 0 ? 0 : Round(n * 100 / total);
        return new(from, now, days, Round(total), Round(totals["Rented"] + totals["Available"]), Round(totals["Rented"]),
            Round(totals["Available"]), Round(totals["Maintenance"]), Round(totals["Reserved"]), Round(totals["Other unavailable"]),
            Percent(totals["Rented"]), Percent(totals["Maintenance"]), rentals.Count, maintenance, estimated.Count, Round(conflict), Basis,
            totals.Select(x => new AssetTimeTotal(x.Key, Round(x.Value), Round(x.Value / 24), Percent(x.Value))).ToArray());
    }
}
