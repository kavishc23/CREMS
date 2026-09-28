using System.Net.Mail;
using System.Text.Encodings.Web;
using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class MessageEmailDelivery
{
    // Queue entries and in-app messages are committed by the caller in one SaveChanges.
    public static async Task<string> QueueAsync(ApplicationDbContext db, IReadOnlyList<InAppNotification> notices,
        bool enabled, CancellationToken token)
    {
        if (!enabled) return " Email delivery is disabled; messages are available in CREMS only.";
        var customerIds = notices.Where(x => x.CustomerId.HasValue).Select(x => x.CustomerId!.Value).ToArray();
        var staffIds = notices.Where(x => x.RecipientUserId.HasValue).Select(x => x.RecipientUserId!.Value).ToArray();
        var accounts = await db.Users.Where(x => x.IsActive && x.EmailConfirmed && x.Email != null &&
            (staffIds.Contains(x.Id) || x.CustomerId.HasValue && customerIds.Contains(x.CustomerId.Value))).ToListAsync(token);
        var preferences = await db.StaffNotificationPreferences.Where(x => staffIds.Contains(x.UserId) && x.Category == "Announcement").ToListAsync(token);
        var queued = 0; var daily = 0; var skipped = 0;
        var queue = new EmailQueue();
        foreach (var notice in notices)
        {
            var recipients = accounts.Where(x => notice.Audience == "Customer" ? x.CustomerId == notice.CustomerId : x.Id == notice.RecipientUserId).ToList();
            if (recipients.Count == 0) { skipped++; continue; }
            foreach (var account in recipients)
            {
                if (!MailAddress.TryCreate(account.Email, out var address)) { skipped++; continue; }
                if (notice.Audience == "Staff")
                {
                    var preference = preferences.FirstOrDefault(x => x.UserId == account.Id);
                    if (preference?.EmailEnabled != true) { skipped++; continue; }
                    // The existing staff worker handles daily summaries and records its own marker.
                    if (preference.EmailFrequency == "Daily") { daily++; continue; }
                }
                var content = $"<p style=\"white-space:pre-wrap;line-height:1.6\">{HtmlEncoder.Default.Encode(notice.Message)}</p><p>This message is also available in your CREMS notification centre.</p>";
                queue.Queue(db, address.Address, notice.Title, EmailTemplate.Branded(notice.Title, content), notice.Message, "ManualNotification");
                db.NotificationEmailDeliveries.Add(new NotificationEmailDelivery { NotificationId = notice.Id, UserId = account.Id, QueuedAt = DateTimeOffset.UtcNow });
                queued++;
            }
        }
        var summary = $" {queued} email{(queued == 1 ? "" : "s")} queued.";
        if (daily > 0) summary += $" {daily} scheduled for staff daily summaries.";
        if (skipped > 0) summary += $" {skipped} skipped because no verified email is available or staff email preferences are off.";
        return summary;
    }
}
