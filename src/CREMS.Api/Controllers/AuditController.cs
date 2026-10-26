using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class AuditController(ApplicationDbContext db, CurrentStaffScope staffScope, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = SystemPolicies.ViewReports)]
    public async Task<ActionResult> GetAll([FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var query = db.AuditEvents.AsNoTracking();
        if (!scope.IsAdministrator) query = query.Where(item => item.BranchId == scope.BranchId);
        var events = await query.OrderByDescending(item => item.OccurredAt).Take(Math.Clamp(limit, 1, 500))
            .Select(item => new { item.Id, item.OccurredAt, item.UserName, item.Action, item.EntityType,
                item.EntityId, item.Summary, item.BranchId, BranchName = item.Branch != null ? item.Branch.Name : null,
                item.PreviousValues, item.NewValues })
            .ToListAsync(cancellationToken);
        return Ok(events);
    }

    [HttpGet("timeline/{entityType}/{id:guid}")]
    public async Task<ActionResult> Timeline(string entityType, Guid id, CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null) return Forbid();
        var normalized = entityType.ToLowerInvariant();
        var requiredPolicy = normalized switch
        {
            "asset" => SystemPolicies.ViewAssets,
            "maintenancejob" => SystemPolicies.ManageMaintenance,
            "invoice" or "rentalinvoice" => SystemPolicies.ManageFinance,
            "booking" or "customer" => SystemPolicies.ManageRentals,
            _ => null
        };
        if (requiredPolicy is null) return BadRequest(new { message = "Choose a booking, asset, invoice, customer or maintenance job." });
        if (!(await authorization.AuthorizeAsync(User, requiredPolicy)).Succeeded) return Forbid();

        Guid? branchId = null;
        Guid? divisionId = null;
        DateTimeOffset? createdAt = null;
        string? reference = null;
        var auditType = normalized switch { "maintenancejob" => nameof(MaintenanceJob), "invoice" or "rentalinvoice" => nameof(RentalInvoice), "booking" => nameof(Booking), "asset" => nameof(Asset), _ => nameof(Customer) };
        switch (normalized)
        {
            case "asset":
                var asset = await db.Assets.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.BranchId, x.DivisionId, x.CreatedAt, x.AssetNumber }).FirstOrDefaultAsync(cancellationToken);
                if (asset is null) return NotFound();
                branchId = asset.BranchId; divisionId = asset.DivisionId; createdAt = asset.CreatedAt; reference = asset.AssetNumber;
                break;
            case "maintenancejob":
                var job = await db.MaintenanceJobs.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.BranchId, x.Asset!.DivisionId, x.CreatedAt, x.JobNumber }).FirstOrDefaultAsync(cancellationToken);
                if (job is null) return NotFound();
                branchId = job.BranchId; divisionId = job.DivisionId; createdAt = job.CreatedAt; reference = job.JobNumber;
                break;
            case "booking":
                var booking = await db.Bookings.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.BranchId, DivisionId = x.Items.Select(i => i.Asset!.DivisionId).FirstOrDefault(), x.CreatedAt, x.BookingNumber }).FirstOrDefaultAsync(cancellationToken);
                if (booking is null) return NotFound();
                branchId = booking.BranchId; divisionId = booking.DivisionId; createdAt = booking.CreatedAt; reference = booking.BookingNumber;
                break;
            case "invoice":
            case "rentalinvoice":
                var invoice = await db.RentalInvoices.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.BookingId, x.Booking!.BranchId, DivisionId = x.Booking.Items.Select(i => i.Asset!.DivisionId).FirstOrDefault(), x.CreatedAt, x.InvoiceNumber }).FirstOrDefaultAsync(cancellationToken);
                if (invoice is null) return NotFound();
                branchId = invoice.BranchId; divisionId = invoice.DivisionId; createdAt = invoice.CreatedAt; reference = invoice.InvoiceNumber;
                break;
            case "customer":
                var customer = await db.Customers.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.CreatedAt, x.CustomerNumber }).FirstOrDefaultAsync(cancellationToken);
                if (customer is null) return NotFound();
                if (!scope.IsAdministrator && !await db.Bookings.AsNoTracking().AnyAsync(x => x.CustomerId == id && scope.BranchIds.Contains(x.BranchId) && x.Items.Any(i => i.Asset!.DivisionId.HasValue && scope.DivisionIds.Contains(i.Asset.DivisionId.Value)), cancellationToken)) return Forbid();
                createdAt = customer.CreatedAt; reference = customer.CustomerNumber;
                break;
        }
        if (branchId.HasValue && (!divisionId.HasValue || !scope.HasAssetAccess(branchId.Value, divisionId))) return Forbid();
        var events = new List<RecordTimelineItem> { new(createdAt!.Value, "Created", $"{reference} was created.", "System", "Record") };
        var audit = await db.AuditEvents.AsNoTracking().Where(x => x.EntityType == auditType && x.EntityId == id)
            .OrderByDescending(x => x.OccurredAt).Take(100)
            .Select(x => new { x.OccurredAt, x.Action, x.Summary, x.UserName }).ToListAsync(cancellationToken);
        events.AddRange(audit.Select(x => new RecordTimelineItem(x.OccurredAt, x.Action, x.Summary, x.UserName, "Activity")));
        var documents = await db.DocumentRecords.AsNoTracking().Where(x => x.EntityType == auditType && x.EntityId == id)
            .OrderByDescending(x => x.CreatedAt).Take(50)
            .Select(x => new { x.CreatedAt, x.Type, x.FileName }).ToListAsync(cancellationToken);
        events.AddRange(documents.Select(x => new RecordTimelineItem(x.CreatedAt, "Document added", $"{x.Type}: {x.FileName}", "System", "Document")));
        if (normalized == "booking")
        {
            var inspections = await db.RentalInspections.AsNoTracking().Where(x => x.BookingId == id)
                .Select(x => new { x.CompletedAt, x.Type, x.CompletedByName }).ToListAsync(cancellationToken);
            events.AddRange(inspections.Select(x => new RecordTimelineItem(x.CompletedAt, x.Type + " inspection", "Inspection completed.", x.CompletedByName ?? "Staff", "Inspection")));
            var notices = await db.RentalNotifications.AsNoTracking().Where(x => x.BookingId == id)
                .Select(x => new { x.CreatedAt, x.SentAt, x.Status, x.Subject, x.Recipient }).ToListAsync(cancellationToken);
            events.AddRange(notices.Select(x => new RecordTimelineItem(x.SentAt ?? x.CreatedAt, "Customer notification " + x.Status,
                $"{x.Subject} · {x.Recipient}", "System", "Notification")));
        }
        return Ok(new { entityType = auditType, id, reference, items = events.OrderByDescending(x => x.OccurredAt).Take(150) });
    }
}

public sealed record RecordTimelineItem(DateTimeOffset OccurredAt, string Title, string Detail, string Actor, string Kind);
