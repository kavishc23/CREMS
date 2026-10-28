using CREMS.Api.Data;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class CustomerRentalReminders
{
    public static async Task QueueAsync(ApplicationDbContext db, DateTimeOffset now, CancellationToken token)
    {
        var today = now.ToOffset(TimeSpan.FromHours(12)).Date;
        var rentals = await db.Bookings.AsNoTracking().Where(x => x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.ConvertedToRental)
            .Select(x => new { x.Id, x.CustomerId, x.BookingNumber, x.BranchId, x.Status,
                BranchName = x.Branch!.Name, DivisionId = x.Items.Select(i => i.Asset!.DivisionId).FirstOrDefault(),
                Start = x.Items.Min(i => (DateTimeOffset?)i.StartAt), End = x.Items.Max(i => (DateTimeOffset?)i.EndAt) }).ToListAsync(token);
        var ids = rentals.Select(x => x.Id).ToList();
        var keys = (await db.InAppNotifications.AsNoTracking().Where(x => x.Audience == "Customer" && x.RelatedEntityId.HasValue && ids.Contains(x.RelatedEntityId.Value))
            .Select(x => x.EventKey).ToListAsync(token)).Concat(db.InAppNotifications.Local.Select(x => x.EventKey)).ToHashSet();
        foreach (var rental in rentals)
        {
            var pickup = rental.Status == BookingStatus.Confirmed;
            var due = pickup ? rental.Start : rental.End;
            if (!due.HasValue || due.Value.ToOffset(TimeSpan.FromHours(12)).Date != today) continue;
            var key = $"customer-{(pickup ? "pickup" : "return")}-day:{rental.Id}:{due.Value:O}";
            if (!keys.Add(key)) continue;
            db.InAppNotifications.Add(new InAppNotification {
                Audience = "Customer", CustomerId = rental.CustomerId, BranchId = rental.BranchId, DivisionId = rental.DivisionId,
                Kind = "Booking", Title = pickup ? "Your rental starts today" : "Your rental return is due today",
                Message = pickup ? $"{rental.BookingNumber}: your hire starts today. Check your summary for collection or delivery arrangements with {rental.BranchName}."
                    : $"{rental.BookingNumber}: your return is due today. Check your summary or contact {rental.BranchName} if you need an extension.",
                Url = "/account?reference=" + Uri.EscapeDataString(rental.BookingNumber), EventKey = key,
                RelatedEntityType = nameof(Booking), RelatedEntityId = rental.Id, ActionType = "OpenBooking", ActionLabel = "View summary",
                Severity = "Info", ExpiresAt = now.AddDays(2)
            });
        }
    }
}
