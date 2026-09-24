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
    public async Task<ActionResult> Settings(CancellationToken token) => Ok(await db.SystemSettings.AsNoTracking().Where(x => !x.Key.StartsWith("rentals.fijiPricingDefaults.")).OrderBy(x => x.Category).ThenBy(x => x.Key).Select(x => new { x.Id, x.Key, x.Value, x.Category, x.Description, x.IsSecret }).ToListAsync(token));

    [HttpPut("settings/{key}")]
    public async Task<ActionResult> SaveSetting(string key, SettingRequest request, CancellationToken token)
    {
        if (key.StartsWith("rentals.fijiPricingDefaults.", StringComparison.Ordinal)) return NotFound();
        var setting = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key, token); if (setting is null) return NotFound();
        if (key == "rentals.vatRate")
        {
            if (!decimal.TryParse(request.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) || rate < 0 || rate > 100)
                return BadRequest(new { message = "Enter a VAT percentage between 0 and 100." });
            var divisions = await db.Divisions.ToListAsync(token);
            var actor = await staffScope.GetAsync(User);
            foreach (var division in divisions.Where(x => x.DefaultTaxRate != rate))
            {
                var previousRate = division.DefaultTaxRate;
                division.DefaultTaxRate = rate;
                division.UpdatedAt = DateTimeOffset.UtcNow;
                if (actor is not null) AuditWriter.Record(db, actor, "Division VAT updated", "Division", division.Id,
                    "Global VAT applied to new rental requests.", null, previousRate.ToString(CultureInfo.InvariantCulture), rate.ToString(CultureInfo.InvariantCulture));
            }
        }
        var previous = setting.IsSecret ? "[protected]" : setting.Value; setting.Value = request.Value.Trim(); setting.UpdatedAt = DateTimeOffset.UtcNow;
        var scope = await staffScope.GetAsync(User); if (scope is not null) AuditWriter.Record(db, scope, "System setting updated", "SystemSetting", setting.Id, $"{setting.Key} was updated.", null, previous, setting.IsSecret ? "[protected]" : setting.Value);
        await db.SaveChangesAsync(token); return NoContent();
    }

    [HttpGet("templates")]
    public async Task<ActionResult> Templates(CancellationToken token) => Ok(await db.NotificationTemplates.AsNoTracking().OrderBy(x => x.Name).ToListAsync(token));

    [HttpPut("templates/{id:guid}")]
    public async Task<ActionResult> SaveTemplate(Guid id, TemplateRequest request, CancellationToken token)
    {
        var template = await db.NotificationTemplates.FirstOrDefaultAsync(x => x.Id == id, token); if (template is null) return NotFound();
        template.Name = request.Name.Trim(); template.Channel = request.Channel.Trim(); template.Subject = request.Subject.Trim(); template.Body = request.Body.Trim(); template.IsActive = request.IsActive; template.UpdatedAt = DateTimeOffset.UtcNow;
        var scope = await staffScope.GetAsync(User); if (scope is not null) AuditWriter.Record(db, scope, "Notification template updated", "NotificationTemplate", template.Id, $"{template.Key} was updated.", null);
        await db.SaveChangesAsync(token); return NoContent();
    }

    [HttpGet("health")]
    public async Task<ActionResult> Health(CancellationToken token)
    {
        var canConnect = await db.Database.CanConnectAsync(token); var pending = (await db.Database.GetPendingMigrationsAsync(token)).ToArray();
        var queued = await db.RentalNotifications.CountAsync(x => x.Status == Domain.Rentals.NotificationStatus.Queued, token) + await db.OutboundEmails.CountAsync(x => x.Status == EmailDeliveryStatus.Queued, token); var failed = await db.RentalNotifications.CountAsync(x => x.Status == Domain.Rentals.NotificationStatus.Failed, token) + await db.OutboundEmails.CountAsync(x => x.Status == EmailDeliveryStatus.Failed, token);
        return Ok(new { status = canConnect && pending.Length == 0 ? "Healthy" : "Attention", databaseConnected = canConnect, pendingMigrations = pending, queuedNotifications = queued, failedNotifications = failed, environment = environment.EnvironmentName, version = typeof(Program).Assembly.GetName().Version?.ToString(), serverTime = DateTimeOffset.UtcNow, emailProviderConfigured = configuration.GetValue<bool>("Email:Enabled") && !string.IsNullOrWhiteSpace(configuration["Email:Host"]) && !string.IsNullOrWhiteSpace(configuration["Email:FromAddress"]), smsProviderConfigured = !string.IsNullOrWhiteSpace(configuration["Notifications:SmsProvider"]) });
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
