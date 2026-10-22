using CREMS.Api.Services;
using System.Globalization;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/administration")]
[Authorize(Policy = SystemPolicies.AdministerSystem)]
public sealed class AdministrationController(ApplicationDbContext db, CurrentStaffScope staffScope, IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("audit")]
    public async Task<ActionResult> Audit([FromQuery] string? search, [FromQuery] int take = 100, CancellationToken token = default)
    {
        var query = db.AuditEvents.AsNoTracking().Where(x => x.EntityType == "ApplicationUser" || x.Action.Contains("Staff") || x.Action.Contains("System"));
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Action.Contains(search) || x.Summary.Contains(search) || x.UserName.Contains(search));
        return Ok(await query.OrderByDescending(x => x.OccurredAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(token));
    }

    [HttpGet("settings")]
    public async Task<ActionResult> Settings(CancellationToken token)
    {
        var stored = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, token);
        return Ok(RentalSettings.Definitions.Select(entry => new {
            Id = stored.GetValueOrDefault(entry.Key)?.Id ?? Guid.Empty,
            Key = entry.Key, Value = stored.GetValueOrDefault(entry.Key)?.Value ?? entry.Value.Default,
            entry.Value.Category, entry.Value.Description, IsSecret = false
        }).OrderBy(x => x.Category).ThenBy(x => x.Key));
    }

    [HttpPut("settings/{key}")]
    public async Task<ActionResult> SaveSetting(string key, SettingRequest request, CancellationToken token)
    {
        if (!RentalSettings.Definitions.TryGetValue(key, out var definition)) return NotFound();
        var error = RentalSettings.Validate(key, request.Value);
        if (error is not null) return BadRequest(new { message = error });
        var value = request.Value.Trim();
        if (key.EndsWith("Prefix", StringComparison.Ordinal)) value = value.ToUpperInvariant();
        var setting = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key, token);
        if (setting is null)
        {
            setting = new SystemSetting { Key = key, Value = definition.Default, Category = definition.Category };
            db.SystemSettings.Add(setting);
        }
        var scope = await staffScope.GetAsync(User);
        if (key == "rentals.vatRate")
        {
            var rate = decimal.Parse(value, CultureInfo.InvariantCulture);
            var divisions = await db.Divisions.ToListAsync(token);
            foreach (var division in divisions.Where(x => x.DefaultTaxRate != rate))
            {
                var previousRate = division.DefaultTaxRate;
                division.DefaultTaxRate = rate;
                division.UpdatedAt = DateTimeOffset.UtcNow;
                if (scope is not null) AuditWriter.Record(db, scope, "Division VAT updated", "Division", division.Id,
                    "Global VAT applied to new rental requests.", null, previousRate.ToString(CultureInfo.InvariantCulture), rate.ToString(CultureInfo.InvariantCulture));
            }
        }
        var previous = setting.Value;
        setting.Value = value; setting.Description = definition.Description; setting.UpdatedAt = DateTimeOffset.UtcNow;
        if (scope is not null) AuditWriter.Record(db, scope, "System setting updated", "SystemSetting", setting.Id, $"{setting.Key} was updated.", null, previous, setting.Value);
        await db.SaveChangesAsync(token);
        return NoContent();
    }

    [HttpGet("retention-review")]
    public async Task<ActionResult> RetentionReview([FromQuery] int page = 1, CancellationToken token = default)
    {
        var years = int.Parse(await RentalSettings.Read(db, "data.retentionYears", token), CultureInfo.InvariantCulture);
        var cutoff = DateTimeOffset.UtcNow.AddYears(-years);
        var bookings = db.Bookings.AsNoTracking().Where(x =>
            (x.Status == CREMS.Api.Domain.Rentals.BookingStatus.Completed || x.Status == CREMS.Api.Domain.Rentals.BookingStatus.Cancelled || x.Status == CREMS.Api.Domain.Rentals.BookingStatus.Expired) &&
            (x.UpdatedAt ?? x.CreatedAt) < cutoff && !x.Items.Any(item => item.EndAt >= cutoff));
        var audits = db.AuditEvents.AsNoTracking().Where(x => x.OccurredAt < cutoff);
        page = Math.Clamp(page, 1, 1000000);
        const int pageSize = 25;
        return Ok(new {
            years, cutoff, page, pageSize,
            bookingCount = await bookings.CountAsync(token), auditCount = await audits.CountAsync(token),
            bookings = await bookings.OrderBy(x => x.UpdatedAt ?? x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new { x.Id, reference = x.BookingNumber, status = x.Status.ToString(), lastActivity = x.UpdatedAt ?? x.CreatedAt }).ToListAsync(token),
            audits = await audits.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new { x.Id, x.Action, x.EntityType, x.OccurredAt }).ToListAsync(token)
        });
    }

    [HttpGet("templates")]
    public async Task<ActionResult> Templates(CancellationToken token) => Ok(await db.NotificationTemplates.AsNoTracking().OrderBy(x => x.Name).ToListAsync(token));

    [HttpPost("templates")]
    public async Task<ActionResult> CreateTemplate(CreateTemplateRequest request, CancellationToken token)
    {
        var key = request.Key?.Trim().ToLowerInvariant() ?? "";
        if (!key.StartsWith("manual.", StringComparison.Ordinal) || key.Length <= 7 ||
            key.Length > 100 || key[7..].Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-') ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 150 ||
            request.Channel is not ("Email" or "InApp") ||
            string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 160 ||
            string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 2000)
            return BadRequest(new { message = "Use a unique manual.key, a name, Email or InApp channel, and a subject and message within the limits." });
        if (await db.NotificationTemplates.AnyAsync(x => x.Key == key, token)) return Conflict(new { message = "A template with this key already exists." });
        var template = new NotificationTemplate { Key = key, Name = request.Name.Trim(), Channel = request.Channel, Subject = request.Subject.Trim(), Body = request.Body.Trim(), IsActive = request.IsActive };
        db.NotificationTemplates.Add(template);
        var scope = await staffScope.GetAsync(User); if (scope is not null) AuditWriter.Record(db, scope, "Notification template created", "NotificationTemplate", template.Id, $"{key} was created.", null);
        await db.SaveChangesAsync(token);
        return Ok(template);
    }

    [HttpPut("templates/{id:guid}")]
    public async Task<ActionResult> SaveTemplate(Guid id, TemplateRequest request, CancellationToken token)
    {
        var template = await db.NotificationTemplates.FirstOrDefaultAsync(x => x.Id == id, token); if (template is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 150 || request.Channel is not ("Email" or "InApp" or "Sms") ||
            string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 160 || string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 2000)
            return BadRequest(new { message = "Enter a name, valid channel, subject and message within the allowed limits." });
        template.Name = request.Name.Trim(); template.Channel = request.Channel.Trim(); template.Subject = request.Subject.Trim(); template.Body = request.Body.Trim(); template.IsActive = request.IsActive; template.UpdatedAt = DateTimeOffset.UtcNow;
        var scope = await staffScope.GetAsync(User); if (scope is not null) AuditWriter.Record(db, scope, "Notification template updated", "NotificationTemplate", template.Id, $"{template.Key} was updated.", null);
        await db.SaveChangesAsync(token); return NoContent();
    }

    [HttpGet("health")]
    public async Task<ActionResult> Health(CancellationToken token)
    {
        var canConnect = await db.Database.CanConnectAsync(token); var pending = (await db.Database.GetPendingMigrationsAsync(token)).ToArray();
        var queued = await db.RentalNotifications.CountAsync(x => x.Status == Domain.Rentals.NotificationStatus.Queued, token) + await db.OutboundEmails.CountAsync(x => x.Status == EmailDeliveryStatus.Queued, token); var failed = await db.RentalNotifications.CountAsync(x => x.Status == Domain.Rentals.NotificationStatus.Failed, token) + await db.OutboundEmails.CountAsync(x => x.Status == EmailDeliveryStatus.Failed, token);
        return Ok(new { status = canConnect && pending.Length == 0 ? "Healthy" : "Attention", databaseConnected = canConnect, pendingMigrations = pending, queuedNotifications = queued, failedNotifications = failed, environment = environment.EnvironmentName, version = typeof(Program).Assembly.GetName().Version?.ToString(), serverTime = DateTimeOffset.UtcNow, emailProviderConfigured = (configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions()).IsProviderConfigured, smsProviderConfigured = !string.IsNullOrWhiteSpace(configuration["Notifications:SmsProvider"]) });
    }

    [HttpGet("emails")]
    public async Task<ActionResult> Emails([FromQuery] int take = 100, CancellationToken token = default) => Ok(await db.OutboundEmails.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(take, 1, 500)).Select(x => new { x.Id, x.Recipient, x.Subject, x.Category, x.Status, x.Attempts, x.CreatedAt, x.SentAt, x.FailureReason }).ToListAsync(token));

    [HttpPost("emails/{id:guid}/retry")]
    public async Task<ActionResult> RetryEmail(Guid id, CancellationToken token)
    {
        var email = await db.OutboundEmails.FirstOrDefaultAsync(x => x.Id == id, token); if (email is null) return NotFound(); if (email.Status == EmailDeliveryStatus.Sent) return BadRequest(new { message = "A delivered email cannot be retried." });
        email.Status = EmailDeliveryStatus.Queued; email.Attempts = 0; email.NextAttemptAt = DateTimeOffset.UtcNow; email.FailureReason = null; var scope = await staffScope.GetAsync(User); if (scope is not null) AuditWriter.Record(db, scope, "Email delivery retried", "OutboundEmail", email.Id, email.Subject, null); await db.SaveChangesAsync(token); return NoContent();
    }

    [HttpGet("permissions")]
    public ActionResult Permissions() => Ok(new[]
    {
        new { role = SystemRoles.SuperAdministrator, scope = "Carpenters group", permissions = new[] { "Divisions and branches", "All users and roles", "System settings", "All operational records", "Audit and system health" } },
        new { role = SystemRoles.Administrator, scope = "All active divisions and branches", permissions = new[] { "Operational staff accounts", "All rentals", "All assets", "Maintenance", "Finance and reports" } },
        new { role = SystemRoles.BranchManager, scope = "Assigned branch", permissions = new[] { "Branch rentals", "Branch assets", "Branch customers", "Maintenance", "Reports", "Branch staff operations" } },
        new { role = SystemRoles.RentalOfficer, scope = "Assigned branch", permissions = new[] { "Bookings", "Customer records", "Pickup and return", "Rental agreements", "Payments and incidents" } },
        new { role = SystemRoles.MaintenanceOfficer, scope = "Assigned division and branch", permissions = new[] { "Asset register", "Maintenance jobs", "Inspections", "Meter readings", "QR identification" } },
    });
}

public sealed record SettingRequest(string Value);
public sealed record TemplateRequest(string Name, string Channel, string Subject, string Body, bool IsActive);
public sealed record CreateTemplateRequest(string Key, string Name, string Channel, string Subject, string Body, bool IsActive);
