using System.Security.Claims;
using System.Text.Encodings.Web;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CREMS.Api.Services;

public sealed class ActionNotificationWorker(IServiceScopeFactory scopes, ILogger<ActionNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        // Let startup migrations finish before opening notification tables.
        await Task.Delay(TimeSpan.FromSeconds(15), token);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await Reminders(token); await Emails(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Action notification cycle failed"); }
        } while (await timer.WaitForNextTickAsync(token));
    }

    private async Task Reminders(CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var keys = (await db.InAppNotifications.Where(x => x.CreatedAt >= now.AddDays(-2)).Select(x => x.EventKey).ToListAsync(token)).ToHashSet();
        void Add(string key, Guid branch, Guid? division, string category, string title, string message, string url,
            string action, string entity, Guid id, DateTimeOffset expires, string severity = "Warning", string? role = null, Guid? recipient = null)
        {
            if (!keys.Add(key)) return;
            db.InAppNotifications.Add(new InAppNotification { EventKey = key, BranchId = branch, DivisionId = division,
                Kind = category, Title = title, Message = message, Url = url, ActionLabel = action,
                ActionType = category == "Approval" ? "ReviewApproval" : category == "Maintenance" ? "OpenAsset" : "OpenBooking",
                RelatedEntityType = entity, RelatedEntityId = id, Severity = severity, RequiredRole = role,
                RecipientUserId = recipient, ExpiresAt = expires });
        }

        var bookings = await db.Bookings.AsNoTracking().Where(x => x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.ConvertedToRental)
            .Select(x => new { x.Id, x.BookingNumber, x.Status, x.BranchId, x.DepositRequired, x.BondAmountHeld,
                DivisionId = x.Items.Select(i => i.Asset!.DivisionId).FirstOrDefault(),
                Start = x.Items.Min(i => (DateTimeOffset?)i.StartAt), End = x.Items.Max(i => (DateTimeOffset?)i.EndAt),
                Unavailable = x.Items.Any(i => !i.Asset!.IsActive || i.Asset.Status == AssetStatus.Maintenance || i.Asset.Status == AssetStatus.OutOfService) }).ToListAsync(token);
        foreach (var b in bookings)
        {
            if (b.Status == BookingStatus.Confirmed && b.Start > now && b.Start <= now.AddHours(2))
                Add($"pickup:{b.Id}:{b.Start:O}", b.BranchId, b.DivisionId, "Booking", "Pickup due within two hours", b.BookingNumber,
                    $"/staff/hire-operations?bookingId={b.Id}", "Prepare pickup", nameof(Booking), b.Id, b.Start.Value);
            if (b.Status == BookingStatus.ConvertedToRental && b.End < now)
                Add($"return:{b.Id}:{now:yyyyMMdd}", b.BranchId, b.DivisionId, "Booking", "Rental return overdue", b.BookingNumber,
                    $"/staff/hire-operations?bookingId={b.Id}", "Review overdue rental", nameof(Booking), b.Id, now.AddDays(1), "Urgent");
            if (b.Status == BookingStatus.Confirmed && b.Start <= now.AddHours(2) && b.BondAmountHeld < b.DepositRequired)
                Add($"bond:{b.Id}:{now:yyyyMMdd}", b.BranchId, b.DivisionId, "Booking", "Refundable bond outstanding",
                    $"{b.BookingNumber}: FJD {b.DepositRequired - b.BondAmountHeld:N2} remains to be collected before handover.", NotificationAccess.BookingUrl(b.Id), "Review bond", nameof(Booking), b.Id, now.AddDays(1));
            if (b.Status == BookingStatus.Confirmed && b.Unavailable)
                Add($"unavailable:{b.Id}:{now:yyyyMMdd}", b.BranchId, b.DivisionId, "Booking", "Allocated asset unavailable", b.BookingNumber,
                    NotificationAccess.BookingUrl(b.Id), "Review allocation", nameof(Booking), b.Id, now.AddDays(1), "Urgent");
        }

        var approvals = await db.ApprovalRequests.AsNoTracking().Include(x => x.StageDecisions).Where(x => x.Status == ApprovalStatus.Pending).ToListAsync(token);
        var workflowIds = approvals.Where(x => x.WorkflowId.HasValue).Select(x => x.WorkflowId!.Value).Distinct().ToArray();
        var stages = await db.ApprovalWorkflowStages.AsNoTracking().Where(x => workflowIds.Contains(x.WorkflowId)).ToListAsync(token);
        foreach (var a in approvals)
        {
            var stage = a.StageDecisions.FirstOrDefault(x => x.StageNumber == a.CurrentStage);
            if (stage is null) continue;
            var settings = stages.FirstOrDefault(x => x.WorkflowId == a.WorkflowId && x.Sequence == a.CurrentStage);
            var began = a.StageDecisions.Where(x => x.StageNumber < a.CurrentStage && x.DecidedAt.HasValue).Max(x => x.DecidedAt) ?? a.CreatedAt;
            if (now < began.AddHours(Math.Max(1, settings?.EscalateAfterHours ?? 24))) continue;
            Guid? bookingId = a.EntityType == nameof(Booking) ? a.EntityId : a.EntityType == nameof(SalesQuote)
                ? await db.SalesQuotes.Where(x => x.Id == a.EntityId).Select(x => x.ConvertedBookingId).FirstOrDefaultAsync(token) : null;
            if (!bookingId.HasValue) continue;
            var division = await db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Asset!.DivisionId).FirstOrDefaultAsync(token);
            Add($"approval-due:{a.Id}:{a.CurrentStage}:{now:yyyyMMdd}", a.BranchId, division, "Approval", "Approval deadline passed",
                $"{a.RequestNumber}: {stage.StageName} is overdue. The assigned approver must review it.", NotificationAccess.BookingUrl(bookingId.Value, true),
                "Review approval", nameof(ApprovalRequest), a.Id, now.AddDays(1), "Urgent", stage.AssignedUserId.HasValue ? null : stage.AssignedRole ?? SystemRoles.BranchManager, stage.AssignedUserId);
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var assets = await db.Assets.AsNoTracking().Where(x => x.IsActive && x.NextServiceDate < today && x.Status != AssetStatus.Retired)
            .Select(x => new { x.Id, x.AssetNumber, x.BranchId, x.DivisionId }).ToListAsync(token);
        foreach (var a in assets) Add($"maintenance-due:{a.Id}:{now:yyyyMMdd}", a.BranchId, a.DivisionId, "Maintenance", "Asset maintenance overdue", a.AssetNumber,
            "/staff/assets?search=" + Uri.EscapeDataString(a.AssetNumber), "Open asset", nameof(Asset), a.Id, now.AddDays(1));
        await db.SaveChangesAsync(token);
    }

    private async Task Emails(CancellationToken token)
    {
        await using var listScope = scopes.CreateAsyncScope();
        if (!listScope.ServiceProvider.GetRequiredService<IOptions<EmailOptions>>().Value.Enabled) return;
        var listDb = listScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recipients = await listDb.StaffNotificationPreferences.Where(x => x.EmailEnabled).Select(x => x.UserId).Distinct().ToListAsync(token);
        foreach (var userId in recipients)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            if (user is not { IsActive: true, EmailConfirmed: true } || string.IsNullOrWhiteSpace(user.Email)) continue;
            var roles = await users.GetRolesAsync(user);
            if (!roles.Any(SystemRoles.Staff.Contains)) continue;
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) }
                .Concat(roles.Select(r => new Claim(ClaimTypes.Role, r))), "NotificationWorker"));
            var visible = await NotificationAccess.StaffAsync(db, user, principal, scope.ServiceProvider.GetRequiredService<IAuthorizationService>());
            var now = DateTimeOffset.UtcNow;
            var prefs = await db.StaffNotificationPreferences.Where(x => x.UserId == user.Id && x.EmailEnabled).ToListAsync(token);
            foreach (var frequency in new[] { "Immediate", "Daily" })
            {
                var categories = prefs.Where(x => x.EmailFrequency == frequency).Select(x => x.Category).ToArray();
                if (categories.Length == 0) continue;
                var fijiNow = now.ToOffset(TimeSpan.FromHours(12));
                var category = frequency == "Daily" ? $"StaffDigest:{user.Id}:{fijiNow:yyyyMMdd}" : "StaffActionNotifications";
                if (frequency == "Daily" && (fijiNow.Hour < 8 || await db.OutboundEmails.AnyAsync(x => x.Category == category, token))) continue;
                // Queue and deduplication markers commit atomically; email delivery is handled by the existing outbox.
                var pending = await visible.Where(x => categories.Contains(x.Kind) && x.CreatedAt >= now.AddDays(-7) &&
                    !db.NotificationEmailDeliveries.Any(d => d.UserId == user.Id && d.NotificationId == x.Id))
                    .OrderBy(x => x.CreatedAt).Take(100).ToListAsync(token);
                if (pending.Count == 0) continue;
                string Encode(string value) => HtmlEncoder.Default.Encode(value);
                var content = string.Join("", pending.Select(x => $"<h3>{Encode(x.Title)}</h3><p>{Encode(x.Message)}</p>"));
                content += "<p>Sign in to CREMS and open the notification bell to review and act on these records.</p>";
                scope.ServiceProvider.GetRequiredService<IEmailQueue>().Queue(db, user.Email,
                    frequency == "Daily" ? "CREMS daily staff notification summary" : $"CREMS: {pending.Count} staff update(s)",
                    EmailTemplate.Branded("Staff notifications", content), category: category);
                foreach (var notice in pending) db.NotificationEmailDeliveries.Add(new NotificationEmailDelivery { NotificationId = notice.Id, UserId = user.Id, QueuedAt = now });
                await db.SaveChangesAsync(token);
            }
        }
    }
}
