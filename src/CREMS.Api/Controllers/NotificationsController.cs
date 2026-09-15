using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
public sealed class NotificationsController(ApplicationDbContext db, UserManager<ApplicationUser> users, CurrentStaffScope staffScope, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet("api/notifications"), Authorize(Policy = SystemPolicies.StaffPortal)]
    public Task<ActionResult> Staff(CancellationToken token) => Inbox(false, token);
    [HttpGet("api/customer-account/notifications"), Authorize(Policy = SystemPolicies.CustomerPortal)]
    public Task<ActionResult> Customer(CancellationToken token) => Inbox(true, token);
    [HttpPost("api/notifications/read"), Authorize(Policy = SystemPolicies.StaffPortal)]
    public Task<ActionResult> StaffRead(ReadNotifications request, CancellationToken token) => Read(false, request, token);
    [HttpPost("api/customer-account/notifications/read"), Authorize(Policy = SystemPolicies.CustomerPortal)]
    public Task<ActionResult> CustomerRead(ReadNotifications request, CancellationToken token) => Read(true, request, token);

    private async Task<IQueryable<InAppNotification>> Visible(ApplicationUser user, bool customer)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var query = db.InAppNotifications.AsNoTracking().Where(x => x.CreatedAt >= since);
        if (customer) return query.Where(x => x.Audience == "Customer" && user.CustomerId != null && x.CustomerId == user.CustomerId);
        var scope = await staffScope.GetAsync(User);
        if (scope is null) return query.Where(x => false);
        var rentals = (await authorization.AuthorizeAsync(User, SystemPolicies.ManageRentals)).Succeeded;
        var maintenance = (await authorization.AuthorizeAsync(User, SystemPolicies.ManageMaintenance)).Succeeded;
        var manager = User.IsInRole(SystemRoles.BranchManager);
        return query.Where(x => x.Audience == "Staff" &&
            (scope.IsAdministrator || x.BranchId.HasValue && scope.BranchIds.Contains(x.BranchId.Value) && x.DivisionId.HasValue && scope.DivisionIds.Contains(x.DivisionId.Value)) &&
            (x.RequiredRole == null || scope.IsAdministrator || x.RequiredRole == SystemRoles.BranchManager && manager) &&
            (x.Kind == "Booking" && rentals || x.Kind == "Maintenance" && maintenance));
    }
    private async Task<ActionResult> Inbox(bool customer, CancellationToken token)
    {
        var user = await users.GetUserAsync(User); if (user is null || !user.IsActive) return Unauthorized();
        var visible = await Visible(user, customer);
        var unreadCount = await visible.CountAsync(x => !db.NotificationReads.Any(r => r.NotificationId == x.Id && r.UserId == user.Id), token);
        var items = await visible.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(50)
            .Select(x => new { x.Id, x.Title, x.Message, x.Kind, x.Url, x.CreatedAt, isRead = db.NotificationReads.Any(r => r.NotificationId == x.Id && r.UserId == user.Id) }).ToListAsync(token);
        return Ok(new { unreadCount, items });
    }
    private async Task<ActionResult> Read(bool customer, ReadNotifications request, CancellationToken token)
    {
        var user = await users.GetUserAsync(User); if (user is null || !user.IsActive) return Unauthorized();
        if (request.Ids.Length > 50) return BadRequest(new { message = "Mark up to 50 notifications at a time." });
        var ids = await (await Visible(user, customer)).Where(x => request.Ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(token);
        foreach (var id in ids)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO NotificationReads (NotificationId, UserId, ReadAt) SELECT {id}, {user.Id}, {DateTimeOffset.UtcNow} WHERE NOT EXISTS (SELECT 1 FROM NotificationReads WITH (UPDLOCK, HOLDLOCK) WHERE NotificationId = {id} AND UserId = {user.Id})", token);
        return NoContent();
    }

    [HttpPost("api/notifications/send"), Authorize(Policy = SystemPolicies.ManageRentals)]
    public async Task<ActionResult> Send(SendNotification request, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message) || request.RequestId == Guid.Empty || request.AllCustomers == request.CustomerId.HasValue)
            return BadRequest(new { message = "Choose one customer or all accessible customers, and enter a title and message." });
        var customers = db.Customers.AsNoTracking().AsQueryable();
        if (!scope.IsAdministrator) customers = customers.Where(c => db.Bookings.Any(b => b.CustomerId == c.Id && scope.BranchIds.Contains(b.BranchId) && b.Items.Any() && b.Items.All(i => i.Asset != null && i.Asset.DivisionId.HasValue && scope.DivisionIds.Contains(i.Asset.DivisionId.Value))));
        if (request.CustomerId.HasValue) customers = customers.Where(c => c.Id == request.CustomerId);
        var ids = await customers.Select(c => c.Id).ToListAsync(token);
        if (ids.Count == 0) return NotFound(new { message = "No accessible customers match this selection." });
        var prefix = $"custom:{scope.UserId}:{request.RequestId}:";
        var existing = await db.InAppNotifications.CountAsync(x => x.EventKey.StartsWith(prefix), token);
        if (existing > 0) return Ok(new { sent = existing, message = "This notification was already sent." });
        foreach (var id in ids) db.InAppNotifications.Add(new InAppNotification { Audience = "Customer", CustomerId = id, Kind = "Announcement", Title = request.Title.Trim(), Message = request.Message.Trim(), SentByUserId = scope.UserId, EventKey = prefix + id });
        await db.SaveChangesAsync(token);
        return Ok(new { sent = ids.Count, message = $"Notification sent to {ids.Count} customer inbox(es)." });
    }
}
public sealed record ReadNotifications([Required] Guid[] Ids);
public sealed record SendNotification(Guid RequestId, Guid? CustomerId, bool AllCustomers, [Required, MaxLength(160)] string Title, [Required, MaxLength(2000)] string Message);
