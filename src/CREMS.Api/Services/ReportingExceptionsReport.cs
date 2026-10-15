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
            .Select(x => new { x.BookingId, x.DailyRate, x.StartAt, x.EndAt }).ToListAsync(token);
        var bookedValues = bookedItems.GroupBy(x => x.BookingId).ToDictionary(x => x.Key,
            x => x.Sum(i => i.DailyRate * Math.Max(1, (decimal)Math.Ceiling((i.EndAt - i.StartAt).TotalDays))));
        foreach (var booking in bookings)
        {
            var url = "/staff/rentals?bookingId=" + booking.Id;
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
        return new { rows = rows.OrderBy(x => x.Severity == "High" ? 0 : 1).ThenByDescending(x => x.OccurredAt).ToArray(),
            note = "Unpaid invoices, unallocated payments and overdue returns are current open worklists; missing invoices, discounts and maintenance overruns use the selected period." };
    }
}
