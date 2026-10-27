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

public sealed class ActionNotificationWorker(IServiceScopeFactory scopes, ILogger<ActionNotificationWorker> logger, IHostEnvironment environment) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        // Let startup migrations finish before opening notification tables.
        await Task.Delay(environment.IsDevelopment() ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(2), token);
        using var timer = new PeriodicTimer(environment.IsDevelopment() ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5));
        do
        {
            try { await Reminders(token); await CustomerReminders(token); await Emails(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Action notification cycle failed"); }
        } while (await timer.WaitForNextTickAsync(token));
    }

    private async Task CustomerReminders(CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (!scope.ServiceProvider.GetRequiredService<IOptions<EmailOptions>>().Value.Enabled) return;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailQueue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();
        var now = DateTimeOffset.UtcNow;
        var today = now.ToOffset(TimeSpan.FromHours(12)).Date;
        var quotationHours = int.Parse(await RentalSettings.Read(db, "reminders.quotationHours", token));
        var pickupHours = int.Parse(await RentalSettings.Read(db, "reminders.pickupHours", token));
        var invoiceDueDays = int.Parse(await RentalSettings.Read(db, "reminders.invoiceDueDays", token));
        var documentDays = int.Parse(await RentalSettings.Read(db, "reminders.documentDays", token));
        var overdueReturnDays = int.Parse(await RentalSettings.Read(db, "reminders.overdueReturnDays", token));
        var templates = await db.NotificationTemplates.AsNoTracking().Where(x => x.Key.StartsWith("reminder.") && x.IsActive && x.Channel == "Email")
            .ToDictionaryAsync(x => x.Key, token);
        async Task Send(string key, Guid recordId, string? recipient, Dictionary<string, string> values, bool daily = false)
        {
            if (string.IsNullOrWhiteSpace(recipient) || !templates.TryGetValue(key, out var template)) return;
            var category = $"{key}:{recordId:N}:{(daily ? today.ToString("yyyyMMdd") : "once")}";
            if (await db.OutboundEmails.AnyAsync(x => x.Category == category, token)) return;
            string Render(string input)
            {
                foreach (var value in values) input = input.Replace("{{" + value.Key + "}}", value.Value, StringComparison.OrdinalIgnoreCase);
                return input;
            }
            var subject = Render(template.Subject);
            var body = Render(template.Body);
            emailQueue.Queue(db, recipient, subject,
                EmailTemplate.Branded(template.Name, $"<p>{HtmlEncoder.Default.Encode(body)}</p>"), body, category);
            await db.SaveChangesAsync(token);
        }
        var quotes = await db.SalesQuotes.AsNoTracking().Where(x => x.Status == QuoteStatus.Sent && x.ValidUntil > now && x.ValidUntil <= now.AddHours(quotationHours))
            .Select(x => new { x.Id, x.QuoteNumber, x.ValidUntil, Email = db.Customers.Where(c => c.Id == x.CustomerId).Select(c => c.Email).FirstOrDefault(), BranchName = db.Branches.Where(b => b.Id == x.BranchId).Select(b => b.Name).FirstOrDefault() }).Take(200).ToListAsync(token);
        foreach (var quote in quotes) await Send("reminder.quotationExpiry", quote.Id, quote.Email, new() { ["quoteNumber"] = quote.QuoteNumber, ["dueAt"] = quote.ValidUntil.ToOffset(TimeSpan.FromHours(12)).ToString("dd MMM yyyy h:mm tt"), ["branchName"] = quote.BranchName ?? "your branch" });

        var bookings = await db.Bookings.AsNoTracking().Where(x => x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.ConvertedToRental)
            .Select(x => new { x.Id, x.BookingNumber, x.Status, Email = x.Customer!.Email, BranchName = x.Branch!.Name,
                Instructions = x.Branch.PickupInstructions, Start = x.Items.Min(i => (DateTimeOffset?)i.StartAt), End = x.Items.Max(i => (DateTimeOffset?)i.EndAt) }).ToListAsync(token);
        foreach (var booking in bookings)
        {
            if (booking.Status == BookingStatus.Confirmed && booking.Start > now && booking.Start <= now.AddHours(pickupHours))
                await Send("reminder.pickup", booking.Id, booking.Email, new() { ["bookingNumber"] = booking.BookingNumber,
                    ["dueAt"] = booking.Start.Value.ToOffset(TimeSpan.FromHours(12)).ToString("dd MMM yyyy h:mm tt"),
                    ["branchName"] = booking.BranchName, ["instructions"] = booking.Instructions ?? "Please bring your required identification and documents." });
            if (booking.Status == BookingStatus.ConvertedToRental && booking.End < now && booking.End >= now.AddDays(-overdueReturnDays))
                await Send("reminder.overdueReturn", booking.Id, booking.Email, new() { ["bookingNumber"] = booking.BookingNumber,
                    ["dueAt"] = booking.End.Value.ToOffset(TimeSpan.FromHours(12)).ToString("dd MMM yyyy h:mm tt"), ["branchName"] = booking.BranchName }, daily: true);
        }
        var invoices = await db.RentalInvoices.AsNoTracking().Where(x => (x.Status == InvoiceStatus.Issued || x.Status == InvoiceStatus.PartiallyPaid) &&
            x.BalanceDue > 0 && x.IssuedAt < now.AddDays(-invoiceDueDays) && x.IssuedAt >= now.AddDays(-invoiceDueDays - 31))
            .Select(x => new { x.Id, x.InvoiceNumber, x.BalanceDue, Email = x.Booking!.Customer!.Email, BranchName = x.Booking.Branch!.Name }).Take(200).ToListAsync(token);
        foreach (var invoice in invoices) await Send("reminder.invoiceDue", invoice.Id, invoice.Email, new() { ["invoiceNumber"] = invoice.InvoiceNumber,
            ["balanceDue"] = $"FJD {invoice.BalanceDue:N2}", ["branchName"] = invoice.BranchName });

        var releasedJobs = await db.MaintenanceJobs.AsNoTracking().Where(x => x.ReleasedAt > now.AddDays(-1))
            .Select(x => new { x.Id, x.AssetId, x.BranchId }).ToListAsync(token);
        foreach (var job in releasedJobs)
        {
            var related = await db.Bookings.AsNoTracking().Where(x => x.Status == BookingStatus.Confirmed && x.Items.Any(i => i.AssetId == job.AssetId))
                .Select(x => new { x.Id, x.BookingNumber, Email = x.Customer!.Email, BranchName = x.Branch!.Name }).Take(10).ToListAsync(token);
            foreach (var booking in related) await Send("reminder.maintenanceComplete", booking.Id, booking.Email,
                new() { ["bookingNumber"] = booking.BookingNumber, ["branchName"] = booking.BranchName });
        }
        var expiryDate = DateOnly.FromDateTime(today.AddDays(documentDays));
        var documents = await db.DocumentRecords.AsNoTracking().Where(x => x.EntityType == nameof(Domain.Customers.Customer) &&
            x.ExpiresOn.HasValue && x.ExpiresOn.Value >= DateOnly.FromDateTime(today) && x.ExpiresOn.Value <= expiryDate)
            .Select(x => new { x.Id, x.EntityId, x.Type, x.ExpiresOn, Email = db.Customers.Where(c => c.Id == x.EntityId).Select(c => c.Email).FirstOrDefault() }).Take(200).ToListAsync(token);
        foreach (var document in documents) await Send("reminder.documentExpiry", document.Id, document.Email,
            new() { ["documentType"] = document.Type, ["dueAt"] = document.ExpiresOn!.Value.ToString("dd MMM yyyy") });
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

        var today = MaintenanceRules.LocalDate(now);
        var assets = await MaintenanceRules.DueAssets(db, db.Assets.AsNoTracking(), today)
            .Select(x => new { x.Id, x.AssetNumber, x.BranchId, x.DivisionId }).ToListAsync(token);
        foreach (var a in assets) Add($"maintenance-due:{a.Id}:{today:yyyyMMdd}", a.BranchId, a.DivisionId, "Maintenance", "Asset maintenance due", a.AssetNumber,
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
