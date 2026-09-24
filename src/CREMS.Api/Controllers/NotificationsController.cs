using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Services;
using System.Text.Json;
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
    public Task<ActionResult> Staff([FromQuery] string? category, [FromQuery] bool unread, [FromQuery] string? severity, [FromQuery] bool history, [FromQuery] int page = 1, CancellationToken token = default) => Inbox(false, token, category, unread, severity, history, page);
    [HttpGet("api/customer-account/notifications"), Authorize(Policy = SystemPolicies.CustomerPortal)]
    public Task<ActionResult> Customer([FromQuery] int page = 1, CancellationToken token = default) => Inbox(true, token, page: page);
    [HttpPost("api/notifications/read"), Authorize(Policy = SystemPolicies.StaffPortal)]
    public Task<ActionResult> StaffRead(ReadNotifications request, CancellationToken token) => Read(false, request, token);
    [HttpPost("api/customer-account/notifications/read"), Authorize(Policy = SystemPolicies.CustomerPortal)]
    public Task<ActionResult> CustomerRead(ReadNotifications request, CancellationToken token) => Read(true, request, token);

    private async Task<IQueryable<InAppNotification>> Visible(ApplicationUser user, bool customer, bool history = false)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var now = DateTimeOffset.UtcNow;
        var query = db.InAppNotifications.AsNoTracking().Where(x => x.CreatedAt >= since && (history || x.ExpiresAt == null || x.ExpiresAt > now));
        // Customer notifications are already scoped to the authenticated customer's record.
        // Do not whitelist system titles here: staff announcements use the same audience and
        // must be visible in the customer's inbox.
        if (customer) return query.Where(x => x.Audience == "Customer" && user.CustomerId != null && x.CustomerId == user.CustomerId);
        return await NotificationAccess.StaffAsync(db, user, User, authorization, history);
    }
    private async Task<ActionResult> Inbox(bool customer, CancellationToken token, string? category = null, bool unread = false, string? severity = null, bool history = false, int page = 1)
    {
        var user = await users.GetUserAsync(User); if (user is null || !user.IsActive) return Unauthorized();
        var visible = await Visible(user, customer, history);
        if (!customer && !history) visible = visible.Where(x => !db.StaffNotificationPreferences.Any(p => p.UserId == user.Id && p.Category == x.Kind && !p.InAppEnabled));
        var now = DateTimeOffset.UtcNow;
        var unreadCount = await visible.CountAsync(x => (x.ExpiresAt == null || x.ExpiresAt > now) && !db.NotificationReads.Any(r => r.NotificationId == x.Id && r.UserId == user.Id), token);
        if (!string.IsNullOrWhiteSpace(category)) visible = visible.Where(x => x.Kind == category);
        if (!string.IsNullOrWhiteSpace(severity)) visible = visible.Where(x => x.Severity == severity);
        if (unread) visible = visible.Where(x => !db.NotificationReads.Any(r => r.NotificationId == x.Id && r.UserId == user.Id));
        var total = await visible.CountAsync(token);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 19) / 20));
        var items = await visible.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * 20).Take(20)
            .Select(x => new { x.Id, x.Title, x.Message, x.Kind, x.Url, x.CreatedAt, x.Severity, x.ActionType, x.ActionLabel,
                x.RelatedEntityType, x.RelatedEntityId, x.ExpiresAt, systemGenerated = x.SentByUserId == null,
                isExpired = x.ExpiresAt != null && x.ExpiresAt <= now,
                toastEnabled = customer || !db.StaffNotificationPreferences.Any(p => p.UserId == user.Id && p.Category == x.Kind && !p.ToastEnabled),
                isRead = db.NotificationReads.Any(r => r.NotificationId == x.Id && r.UserId == user.Id) }).ToListAsync(token);
        return Ok(new { unreadCount, items, total, page, pageSize = 20 });
    }
    private async Task<ActionResult> Read(bool customer, ReadNotifications request, CancellationToken token)
    {
        var user = await users.GetUserAsync(User); if (user is null || !user.IsActive) return Unauthorized();
        if (request.Ids.Length > 50) return BadRequest(new { message = "Mark up to 50 notifications at a time." });
        var ids = await (await Visible(user, customer, true)).Where(x => request.All || request.Ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(token);
        var json = JsonSerializer.Serialize(ids);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO NotificationReads (NotificationId, UserId, ReadAt)
            SELECT j.Id, {user.Id}, {DateTimeOffset.UtcNow}
            FROM OPENJSON({json}) WITH (Id uniqueidentifier '$') j
            WHERE NOT EXISTS (SELECT 1 FROM NotificationReads WITH (UPDLOCK, HOLDLOCK) WHERE NotificationId = j.Id AND UserId = {user.Id})
            """, token);
        return NoContent();
    }

    [HttpGet("api/notifications/preferences"), Authorize(Policy = SystemPolicies.StaffPortal)]
    public async Task<ActionResult> Preferences(CancellationToken token)
    {
        var user = await users.GetUserAsync(User); if (user is null || !user.IsActive) return Unauthorized();
        var rows = await db.StaffNotificationPreferences.AsNoTracking().Where(x => x.UserId == user.Id).ToListAsync(token);
        return Ok(NotificationAccess.Categories.Select(category => rows.FirstOrDefault(x => x.Category == category) ?? new StaffNotificationPreference { UserId = user.Id, Category = category }));
    }

    [HttpPut("api/notifications/preferences"), Authorize(Policy = SystemPolicies.StaffPortal)]
    public async Task<ActionResult> SavePreferences(IReadOnlyList<NotificationPreferenceRequest> request, CancellationToken token)
    {
        var user = await users.GetUserAsync(User); if (user is null || !user.IsActive) return Unauthorized();
        if (request.Count > NotificationAccess.Categories.Length || request.Select(x => x.Category).Distinct().Count() != request.Count ||
            request.Any(x => !NotificationAccess.Categories.Contains(x.Category) || x.EmailFrequency is not ("Immediate" or "Daily")))
            return BadRequest(new { message = "Choose valid, unique notification categories and delivery frequencies." });
        if (request.Any(x => x.EmailEnabled) && (string.IsNullOrWhiteSpace(user.Email) || !user.EmailConfirmed))
            return BadRequest(new { message = "Verify your staff email address before enabling email notifications." });
        var existing = await db.StaffNotificationPreferences.Where(x => x.UserId == user.Id).ToListAsync(token);
        foreach (var input in request)
        {
            var row = existing.FirstOrDefault(x => x.Category == input.Category);
            if (row is null) { row = new StaffNotificationPreference { UserId = user.Id, Category = input.Category }; db.StaffNotificationPreferences.Add(row); }
            row.InAppEnabled = input.InAppEnabled; row.ToastEnabled = input.ToastEnabled;
            row.EmailEnabled = input.EmailEnabled; row.EmailFrequency = input.EmailFrequency;
        }
        await db.SaveChangesAsync(token);
        return NoContent();
    }

    [HttpGet("api/notifications/targets"), Authorize(Policy = SystemPolicies.ManageRentals)]
    public async Task<ActionResult> Targets(CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        var staff = db.Users.AsNoTracking().Where(x => x.IsActive && db.UserRoles.Any(r => r.UserId == x.Id && db.Roles.Any(role => role.Id == r.RoleId && SystemRoles.Staff.Contains(role.Name!))));
        if (!scope.IsAdministrator) staff = staff.Where(x => x.BranchId.HasValue && scope.BranchIds.Contains(x.BranchId.Value) && x.DivisionId.HasValue && scope.DivisionIds.Contains(x.DivisionId.Value));
        return Ok(new {
            staff = await staff.OrderBy(x => x.FullName).Select(x => new { x.Id, x.FullName, x.BranchId, x.DivisionId }).ToListAsync(token),
            branches = await db.Branches.Where(x => x.IsActive && (scope.IsAdministrator || scope.BranchIds.Contains(x.Id))).Select(x => new { x.Id, x.Name }).ToListAsync(token),
            divisions = await db.Divisions.Where(x => x.IsActive && (scope.IsAdministrator || scope.DivisionIds.Contains(x.Id))).Select(x => new { x.Id, x.Name }).ToListAsync(token),
            roles = scope.IsAdministrator ? SystemRoles.Staff : SystemRoles.BranchScoped, scope.IsAdministrator,
        });
    }

    [HttpPost("api/notifications/staff"), Authorize(Policy = SystemPolicies.ManageRentals)]
    public async Task<ActionResult> SendStaff(SendStaffNotification request, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message) ||
            request.Severity is not ("Info" or "Warning" or "Urgent") || request.ExpiresAt <= DateTimeOffset.UtcNow ||
            request.RequiredRole != null && !SystemRoles.Staff.Contains(request.RequiredRole))
            return BadRequest(new { message = "Enter a title, message, valid priority and future expiry." });
        if (!request.RecipientUserId.HasValue && !request.BranchId.HasValue && !request.DivisionId.HasValue && request.RequiredRole == null)
            return BadRequest(new { message = "Choose a staff member, role, branch or division." });
        Guid? branch = request.BranchId, division = request.DivisionId;
        if (request.RecipientUserId.HasValue)
        {
            var recipient = await db.Users.FirstOrDefaultAsync(x => x.Id == request.RecipientUserId && x.IsActive, token);
            if (recipient is null || !(await users.GetRolesAsync(recipient)).Any(SystemRoles.Staff.Contains)) return NotFound();
            if (!scope.IsAdministrator && (!recipient.BranchId.HasValue || !scope.HasAssetAccess(recipient.BranchId.Value, recipient.DivisionId))) return Forbid();
            branch ??= recipient.BranchId; division ??= recipient.DivisionId;
        }
        if (!scope.IsAdministrator && (!branch.HasValue || !scope.HasAssetAccess(branch.Value, division))) return Forbid();
        if (branch.HasValue && !await db.Branches.AnyAsync(x => x.Id == branch && x.IsActive, token) ||
            division.HasValue && !await db.Divisions.AnyAsync(x => x.Id == division && x.IsActive, token))
            return BadRequest(new { message = "Select an active branch and division." });
        string? url = null;
        if (request.BookingId.HasValue)
        {
            var booking = await db.Bookings.AsNoTracking().Where(x => x.Id == request.BookingId)
                .Select(x => new { x.BranchId, DivisionId = x.Items.Select(i => i.Asset!.DivisionId).FirstOrDefault() }).FirstOrDefaultAsync(token);
            if (booking is null) return NotFound();
            if (!scope.HasAssetAccess(booking.BranchId, booking.DivisionId)) return Forbid();
            // A record-linked notice may never broadcast outside that record's scope.
            branch = booking.BranchId; division = booking.DivisionId;
            url = NotificationAccess.BookingUrl(request.BookingId.Value);
        }
        var key = $"staff:{scope.UserId}:{request.RequestId}";
        if (await db.InAppNotifications.AnyAsync(x => x.EventKey == key, token)) return Ok(new { message = "Notification already sent." });
        db.InAppNotifications.Add(new InAppNotification { Title = request.Title.Trim(), Message = request.Message.Trim(),
            Kind = "Announcement", RecipientUserId = request.RecipientUserId, RequiredRole = request.RequiredRole,
            BranchId = branch, DivisionId = division, Severity = request.Severity, SentByUserId = scope.UserId,
            ExpiresAt = request.ExpiresAt, Url = url, ActionType = url == null ? null : "OpenBooking",
            ActionLabel = url == null ? null : "Open booking", RelatedEntityId = request.BookingId,
            RelatedEntityType = request.BookingId.HasValue ? "Booking" : null, EventKey = key });
        await db.SaveChangesAsync(token);
        return Ok(new { message = "Staff notification sent to the selected audience." });
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
public sealed record ReadNotifications([Required] Guid[] Ids, bool All = false);
public sealed record SendNotification(Guid RequestId, Guid? CustomerId, bool AllCustomers, [Required, MaxLength(160)] string Title, [Required, MaxLength(2000)] string Message);
public sealed record NotificationPreferenceRequest(string Category, bool InAppEnabled, bool ToastEnabled, bool EmailEnabled, string EmailFrequency);
public sealed record SendStaffNotification(Guid RequestId, Guid? RecipientUserId, string? RequiredRole, Guid? BranchId, Guid? DivisionId,
    [Required, MaxLength(160)] string Title, [Required, MaxLength(2000)] string Message, string Severity = "Info", DateTimeOffset? ExpiresAt = null, Guid? BookingId = null);
