using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Domain.Assets;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

/// <summary>Creates inbox events in the same transaction as the business change.</summary>
public static class NotificationEvents
{
    public static async Task CaptureAsync(ApplicationDbContext db, CancellationToken token)
    {
        var changes = db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList();
        foreach (var entry in changes)
        {
            bool Changed(string property) => entry.State == EntityState.Added || entry.Property(property).IsModified && !Equals(entry.Property(property).OriginalValue, entry.Property(property).CurrentValue);
            void Add(Entity source, string audience, Guid? customer, Guid? branch, Guid? division, string kind, string title, string message, string? url, string? role = null)
            {
                var key = $"{source.GetType().Name}:{source.Id}:{audience}:{title}";
                if (db.InAppNotifications.Local.Any(n => n.EventKey.StartsWith(key + ":"))) return;
                db.InAppNotifications.Add(new InAppNotification { Audience = audience, CustomerId = customer, BranchId = branch, DivisionId = division, Kind = kind, Title = title, Message = message, Url = url, RequiredRole = role, EventKey = key + ":" + Guid.NewGuid().ToString("N") });
            }
            async Task<Guid?> DivisionFor(Guid bookingId)
            {
                var item = db.BookingItems.Local.FirstOrDefault(x => x.BookingId == bookingId);
                if (item?.Asset is not null) return item.Asset.DivisionId;
                if (item is not null) return await db.Assets.Where(x => x.Id == item.AssetId).Select(x => x.DivisionId).FirstOrDefaultAsync(token);
                return await db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Asset!.DivisionId).FirstOrDefaultAsync(token);
            }
            switch (entry.Entity)
            {
                case Booking booking when Changed(nameof(Booking.Status)) || Changed(nameof(Booking.BondStatus)):
                    var division = booking.Items.FirstOrDefault()?.Asset?.DivisionId ?? await DivisionFor(booking.Id);
                    var bond = entry.State != EntityState.Added && Changed(nameof(Booking.BondStatus));
                    var title = bond ? $"Refundable bond: {booking.BondStatus}" : $"Booking: {booking.Status}";
                    var message = $"{booking.BookingNumber} — {(bond ? "Your refundable bond record has been updated." : "The booking status has changed.")}";
                    Add(booking, "Customer", booking.CustomerId, booking.BranchId, division, "Booking", title, message, "/account?reference=" + Uri.EscapeDataString(booking.BookingNumber));
                    Add(booking, "Staff", null, booking.BranchId, division, "Booking", title, message, "/staff/bookings?search=" + Uri.EscapeDataString(booking.BookingNumber));
                    break;
                case SalesQuote quote when Changed(nameof(SalesQuote.Status)) || Changed(nameof(SalesQuote.Version)):
                    var qMessage = $"{quote.QuoteNumber} · version {quote.Version}.";
                    Add(quote, "Staff", null, quote.BranchId, quote.DivisionId, "Booking", $"Quotation: {quote.Status}", qMessage, "/staff/bookings?search=" + Uri.EscapeDataString(quote.QuoteNumber));
                    if (quote.Status != QuoteStatus.Draft)
                        Add(quote, "Customer", quote.CustomerId, quote.BranchId, quote.DivisionId, "Booking", quote.Status == QuoteStatus.Sent ? "Quotation ready for your review" : $"Quotation: {quote.Status}", qMessage, "/account?reference=" + Uri.EscapeDataString(quote.QuoteNumber));
                    break;
                case ApprovalRequest approval when Changed(nameof(ApprovalRequest.Status)) || Changed(nameof(ApprovalRequest.CurrentStage)):
                    Guid? bookingId = approval.EntityType == nameof(Booking) ? approval.EntityId : approval.EntityType == nameof(SalesQuote) ? await db.SalesQuotes.Where(x => x.Id == approval.EntityId).Select(x => x.ConvertedBookingId).FirstOrDefaultAsync(token) : null;
                    if (bookingId is null) break;
                    var reference = await db.Bookings.Where(x => x.Id == bookingId).Select(x => x.BookingNumber).FirstOrDefaultAsync(token);
                    Add(approval, "Staff", null, approval.BranchId, await DivisionFor(bookingId.Value), "Booking", $"Approval {approval.Status} · stage {approval.CurrentStage}", $"{approval.RequestNumber} · {reference}", "/staff/bookings?search=" + Uri.EscapeDataString(reference ?? approval.RequestNumber), approval.Status == ApprovalStatus.Pending ? SystemRoles.BranchManager : null);
                    break;
                case CustomerCase customerCase when Changed(nameof(CustomerCase.Status)):
                    // Cases have a branch but no division; notify the customer only to avoid widening staff scope.
                    Add(customerCase, "Customer", customerCase.CustomerId, customerCase.BranchId, null, "Booking", $"Customer request: {customerCase.Status}", customerCase.Subject, "/account?reference=");
                    var related = await db.Bookings.Where(b => b.CustomerId == customerCase.CustomerId && b.BranchId == customerCase.BranchId && customerCase.Subject.Contains(b.BookingNumber)).Select(b => new { b.Id, b.BookingNumber }).FirstOrDefaultAsync(token);
                    if (related is not null) Add(customerCase, "Staff", null, customerCase.BranchId, await DivisionFor(related.Id), "Booking", $"Customer request: {customerCase.Status}", customerCase.Subject, "/staff/bookings?search=" + Uri.EscapeDataString(related.BookingNumber));
                    break;
                case MaintenanceJob job when Changed(nameof(MaintenanceJob.Status)):
                    var assetDivision = await db.Assets.Where(x => x.Id == job.AssetId).Select(x => x.DivisionId).FirstOrDefaultAsync(token);
                    Add(job, "Staff", null, job.BranchId, assetDivision, "Maintenance", $"Maintenance: {job.Status}", job.JobNumber, "/staff/maintenance?search=" + Uri.EscapeDataString(job.JobNumber));
                    break;
            }
        }
    }
}
