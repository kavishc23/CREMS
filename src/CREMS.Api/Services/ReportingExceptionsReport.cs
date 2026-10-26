using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class ReportingExceptionsReport
{
    public sealed record Row(string Kind, string Severity, string Reference, string Detail,
        decimal? Amount, DateTimeOffset? OccurredAt, string Url);

    public static async Task<object> Build(ApplicationDbContext db, IQueryable<Asset> assetScope,
        DateOnly from, DateOnly to, CancellationToken token)
    {
        var (start, end) = ReportingKpis.FijiPeriod(from, to);
        var now = DateTimeOffset.UtcNow;
        var assetIds = assetScope.Select(x => x.Id);
        var bookingIds = db.Bookings.AsNoTracking().Where(x => x.Items.Any(i => assetIds.Contains(i.AssetId))).Select(x => x.Id);
        var rows = new List<Row>();
        var invoices = await db.RentalInvoices.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId) &&
            x.Status != InvoiceStatus.Voided && x.Status != InvoiceStatus.Draft && x.BalanceDue > 0)
            .Select(x => new { x.InvoiceNumber, x.BalanceDue, x.IssuedAt }).ToListAsync(token);
        rows.AddRange(invoices.Select(x => new Row("Unpaid invoice", "High", x.InvoiceNumber,
            "Current invoice balance requires collection or reconciliation.", x.BalanceDue, x.IssuedAt,
            "/staff/finance?search=" + Uri.EscapeDataString(x.InvoiceNumber))));

        var payments = await db.RentalPayments.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId) &&
            x.Status == PaymentStatus.Recorded && x.Type != PaymentType.Deposit && x.Type != PaymentType.BondCollection &&
            x.Type != PaymentType.BondRefund && x.Type != PaymentType.Refund)
            .Select(x => new { x.Id, x.ReceiptNumber, x.Amount, x.CreatedAt }).ToListAsync(token);
        var paymentIds = payments.Select(x => x.Id).ToList();
        var allocations = await db.PaymentAllocations.AsNoTracking().Where(x => paymentIds.Contains(x.PaymentId))
            .GroupBy(x => x.PaymentId).Select(g => new { PaymentId = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(token);
        var allocated = allocations.ToDictionary(x => x.PaymentId, x => x.Amount);
        rows.AddRange(payments.Where(x => x.Amount - allocated.GetValueOrDefault(x.Id) > 0.01m).Select(x =>
            new Row("Unallocated payment", "High", x.ReceiptNumber,
                "Recorded payment has an amount not allocated to an invoice.", x.Amount - allocated.GetValueOrDefault(x.Id),
                x.CreatedAt, "/staff/finance?search=" + Uri.EscapeDataString(x.ReceiptNumber))));

        var bookings = await db.Bookings.AsNoTracking().Where(x => bookingIds.Contains(x.Id) &&
            ((x.Status == BookingStatus.Completed && x.Items.Any(i => i.EndAt >= start && i.EndAt < end)) ||
             (x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt < now)) ||
             (x.DiscountAmount > 0 && x.CreatedAt >= start && x.CreatedAt < end)))
            .Select(x => new { x.Id, x.BookingNumber, x.Status, x.DiscountAmount, x.CreatedAt,
                HasInvoice = db.RentalInvoices.Any(i => i.BookingId == x.Id && i.Status != InvoiceStatus.Voided),
                ReturnAt = x.Items.Max(i => i.EndAt) }).ToListAsync(token);
        var selectedBookingIds = bookings.Select(x => x.Id).ToArray();
        var bookedItems = await db.BookingItems.AsNoTracking().Where(x => selectedBookingIds.Contains(x.BookingId))
            .Select(x => new { x.BookingId, x.DailyRate, x.HireQuantity, x.StartAt, x.EndAt }).ToListAsync(token);
        var bookedValues = bookedItems.GroupBy(x => x.BookingId).ToDictionary(x => x.Key,
            x => x.Sum(i => i.DailyRate * (i.HireQuantity ?? Math.Max(1, (decimal)Math.Ceiling((i.EndAt - i.StartAt).TotalDays)))));
        foreach (var booking in bookings)
        {
            var url = "/staff/hire-operations?bookingId=" + booking.Id;
            if (booking.Status == BookingStatus.Completed && !booking.HasInvoice)
                rows.Add(new Row("Missing invoice", "High", booking.BookingNumber,
                    "Completed hire has no non-voided invoice.", null, booking.ReturnAt, url));
            if (booking.Status == BookingStatus.ConvertedToRental && booking.ReturnAt < now)
                rows.Add(new Row("Overdue return", "High", booking.BookingNumber,
                    "Active hire is past its scheduled return.", null, booking.ReturnAt, url));
            var scheduledValue = bookedValues.GetValueOrDefault(booking.Id);
            if (scheduledValue > 0 && booking.DiscountAmount * 100 / scheduledValue >= ReportingKpis.UnusualDiscountPercent)
                rows.Add(new Row("Unusual discount", "Review", booking.BookingNumber,
                    $"Discount is at least {ReportingKpis.UnusualDiscountPercent}% of scheduled hire value; review the booking terms.",
                    booking.DiscountAmount, booking.CreatedAt, url));
        }
        var jobs = await db.MaintenanceJobs.AsNoTracking().Where(x => assetIds.Contains(x.AssetId) &&
            x.ReportedAt >= start && x.ReportedAt < end && x.Status != MaintenanceStatus.Cancelled && x.HasEstimate)
            .ToListAsync(token);
        rows.AddRange(jobs.Where(x => (x.ActualCost ?? MaintenanceCosts.Total(x)) > x.EstimatedCost).Select(x =>
            new Row("Maintenance over estimate", "Review", x.JobNumber,
                $"Recorded cost exceeds the {x.EstimatedCost:N2} estimate.",
                (x.ActualCost ?? MaintenanceCosts.Total(x)) - x.EstimatedCost, x.ReportedAt,
                "/staff/maintenance?search=" + Uri.EscapeDataString(x.JobNumber))));

        // These are current integrity checks, independent of the selected reporting period.
        var activeItems = await db.BookingItems.AsNoTracking()
            .Where(x => assetIds.Contains(x.AssetId) &&
                (x.Booking!.Status == BookingStatus.Confirmed || x.Booking.Status == BookingStatus.ConvertedToRental))
            .Select(x => new { x.AssetId, x.Asset!.AssetNumber, AssetStatus = x.Asset.Status, x.StartAt, x.EndAt,
                x.BookingId, x.Booking!.BookingNumber, BookingStatus = x.Booking.Status })
            .ToListAsync(token);
        foreach (var group in activeItems.GroupBy(x => x.AssetId))
        {
            var ordered = group.OrderBy(x => x.StartAt).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            for (var next = index + 1; next < ordered.Length && ordered[next].StartAt < ordered[index].EndAt; next++)
            {
                if (ordered[index].BookingId == ordered[next].BookingId) continue;
                rows.Add(new Row("Double-booked asset", "High", ordered[index].AssetNumber,
                    $"{ordered[index].BookingNumber} overlaps {ordered[next].BookingNumber}.", null,
                    ordered[next].StartAt, "/staff/assets?search=" + Uri.EscapeDataString(ordered[index].AssetNumber)));
            }
            if (ordered[0].AssetStatus == AssetStatus.Available)
                foreach (var hire in ordered.Where(x => x.BookingStatus == BookingStatus.ConvertedToRental && x.StartAt <= now && x.EndAt >= now))
                    rows.Add(new Row("Asset status mismatch", "High", hire.AssetNumber,
                        $"Marked available while {hire.BookingNumber} is on hire.", null, now,
                        "/staff/assets?search=" + Uri.EscapeDataString(hire.AssetNumber)));
        }
        var completedWithoutInspection = await db.Bookings.AsNoTracking()
            .Where(x => bookingIds.Contains(x.Id) && x.Status == BookingStatus.Completed &&
                !x.Inspections.Any(i => i.Type == InspectionType.Return))
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).Take(200)
            .Select(x => new { x.Id, x.BookingNumber, x.UpdatedAt, x.CreatedAt }).ToListAsync(token);
        rows.AddRange(completedWithoutInspection.Select(x => new Row("Missing post-hire inspection", "High", x.BookingNumber,
            "Hire was completed without a recorded return inspection.", null, x.UpdatedAt ?? x.CreatedAt,
            "/staff/hire-operations?bookingId=" + x.Id)));
        var unreleasedJobs = await db.MaintenanceJobs.AsNoTracking().Where(x => assetIds.Contains(x.AssetId) &&
            x.Status == MaintenanceStatus.Completed && !x.ReleasedAt.HasValue)
            .OrderByDescending(x => x.CompletedAt).Take(200)
            .Select(x => new { x.JobNumber, x.CompletedAt }).ToListAsync(token);
        rows.AddRange(unreleasedJobs.Select(x => new Row("Safety release pending", "High", x.JobNumber,
            "Repair is complete, but the asset has not passed the safety release.", null, x.CompletedAt,
            "/staff/maintenance?search=" + Uri.EscapeDataString(x.JobNumber))));
        var expiredDocuments = await db.DocumentRecords.AsNoTracking().Where(x => x.ExpiresOn.HasValue &&
            x.ExpiresOn.Value < DateOnly.FromDateTime(now.UtcDateTime) &&
            ((x.EntityType == nameof(Asset) && assetIds.Contains(x.EntityId)) ||
             (x.EntityType == nameof(Booking) && bookingIds.Contains(x.EntityId))))
            .OrderByDescending(x => x.ExpiresOn).Take(200)
            .Select(x => new { x.EntityType, x.EntityId, x.DocumentNumber, x.Type, x.ExpiresOn,
                AssetNumber = db.Assets.Where(a => a.Id == x.EntityId).Select(a => a.AssetNumber).FirstOrDefault(),
                BookingNumber = db.Bookings.Where(b => b.Id == x.EntityId).Select(b => b.BookingNumber).FirstOrDefault() }).ToListAsync(token);
        rows.AddRange(expiredDocuments.Select(x => new Row("Expired document", "Review", x.DocumentNumber,
            $"{x.Type} expired on {x.ExpiresOn:dd MMM yyyy}.", null, null,
            x.EntityType == nameof(Asset) ? "/staff/assets?search=" + Uri.EscapeDataString(x.AssetNumber ?? x.DocumentNumber) : "/staff/bookings?search=" + Uri.EscapeDataString(x.BookingNumber ?? x.DocumentNumber))));
        var expiredDrivers = await db.AuthorizedDrivers.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId) &&
            x.LicenceExpiry < DateOnly.FromDateTime(now.UtcDateTime) &&
            (x.Booking!.Status == BookingStatus.Confirmed || x.Booking.Status == BookingStatus.ConvertedToRental))
            .Select(x => new { x.BookingId, x.FullName, x.LicenceExpiry, x.Booking!.BookingNumber }).ToListAsync(token);
        rows.AddRange(expiredDrivers.Select(x => new Row("Expired driver licence", "High", x.BookingNumber,
            $"{x.FullName}'s licence expired on {x.LicenceExpiry:dd MMM yyyy}.", null, null,
            "/staff/bookings?search=" + Uri.EscapeDataString(x.BookingNumber))));
        var upcoming = await db.Bookings.AsNoTracking().Where(x => bookingIds.Contains(x.Id) && x.Status == BookingStatus.Confirmed &&
            x.Items.Any(i => i.StartAt <= now.AddDays(7)))
            .Include(x => x.Items).ThenInclude(x => x.Asset).ThenInclude(x => x!.ServiceOffering)
            .OrderBy(x => x.CreatedAt).Take(200).ToListAsync(token);
        var upcomingIds = upcoming.Select(x => x.Id).ToArray();
        var upcomingCustomerIds = upcoming.Select(x => x.CustomerId).Distinct().ToArray();
        var attached = await db.DocumentRecords.AsNoTracking().Where(x =>
            x.EntityType == nameof(Booking) && upcomingIds.Contains(x.EntityId) ||
            x.EntityType == "Customer" && upcomingCustomerIds.Contains(x.EntityId))
            .Select(x => new { x.EntityId, x.Type, x.FileName, x.ExpiresOn }).ToListAsync(token);
        foreach (var booking in upcoming)
        {
            var required = booking.Items.SelectMany(x =>
            {
                try { return JsonSerializer.Deserialize<string[]>(x.Asset?.ServiceOffering?.RequiredDocumentsJson ?? "[]") ?? []; }
                catch (JsonException) { return Array.Empty<string>(); }
            }).Distinct(StringComparer.OrdinalIgnoreCase);
            var missing = required.Where(name => !attached.Any(x => (x.EntityId == booking.Id || x.EntityId == booking.CustomerId) &&
                (!x.ExpiresOn.HasValue || x.ExpiresOn.Value >= DateOnly.FromDateTime(now.UtcDateTime)) &&
                (x.Type.Contains(name, StringComparison.OrdinalIgnoreCase) || x.FileName.Contains(name, StringComparison.OrdinalIgnoreCase)))).ToArray();
            if (missing.Length > 0) rows.Add(new Row("Missing required documents", "High", booking.BookingNumber,
                "Required before pickup: " + string.Join(", ", missing), null, booking.Items.Min(x => x.StartAt),
                "/staff/bookings?search=" + Uri.EscapeDataString(booking.BookingNumber)));
        }
        return new { rows = rows.OrderBy(x => x.Severity == "High" ? 0 : 1).ThenByDescending(x => x.OccurredAt).ToArray(),
            note = "Current integrity checks include booking conflicts, inspection and safety-release gaps, status mismatches, and expired documents. Discounts and maintenance overruns use the selected period." };
    }
}
