using System.Security.Claims;
using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class NotificationAccess
{
    public static readonly string[] Categories = ["Booking", "Approval", "Maintenance", "Finance", "Administration", "Announcement"];

    // Inbox, read actions and email delivery use the same live authorization rules.
    public static async Task<IQueryable<InAppNotification>> StaffAsync(ApplicationDbContext db, ApplicationUser user,
        ClaimsPrincipal principal, IAuthorizationService authorization, bool history = false)
    {
        var scope = await new CurrentStaffScope(db).GetAsync(principal);
        var now = DateTimeOffset.UtcNow;
        var query = db.InAppNotifications.AsNoTracking().Where(x => x.Audience == "Staff" && x.CreatedAt >= now.AddDays(-90));
        if (scope is null || !user.IsActive) return query.Where(x => false);
        if (!history) query = query.Where(x => x.ExpiresAt == null || x.ExpiresAt > now);
        var roles = principal.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();
        var rentals = (await authorization.AuthorizeAsync(principal, SystemPolicies.ManageRentals)).Succeeded;
        var maintenance = (await authorization.AuthorizeAsync(principal, SystemPolicies.ManageMaintenance)).Succeeded;
        var finance = (await authorization.AuthorizeAsync(principal, SystemPolicies.ManageFinance)).Succeeded;
        var administration = (await authorization.AuthorizeAsync(principal, SystemPolicies.ManageUsers)).Succeeded;
        return query.Where(x =>
            (x.RecipientUserId == null || x.RecipientUserId == user.Id) &&
            (x.RequiredRole == null || roles.Contains(x.RequiredRole)) &&
            (scope.IsAdministrator ||
                (!x.BranchId.HasValue || scope.BranchIds.Contains(x.BranchId.Value)) &&
                (!x.DivisionId.HasValue || scope.DivisionIds.Contains(x.DivisionId.Value))) &&
            ((x.Kind == "Booking" || x.Kind == "Approval") && rentals ||
             x.Kind == "Maintenance" && maintenance || x.Kind == "Finance" && finance ||
             x.Kind == "Administration" && administration || x.Kind == "Announcement"));
    }

    public static string BookingUrl(Guid bookingId, bool approval = false) =>
        $"/staff/bookings?bookingId={bookingId}" + (approval ? "&section=approval" : "");
}
