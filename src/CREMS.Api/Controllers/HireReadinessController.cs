using System.Text.Json;
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
[Route("api/hire-readiness")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class HireReadinessController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null) return Forbid();
        var now = DateTimeOffset.UtcNow;
        var today = new DateTimeOffset(now.ToOffset(TimeSpan.FromHours(12)).Date, TimeSpan.FromHours(12));
        var end = today.AddDays(1);
        var query = db.Bookings.AsNoTracking().Where(x =>
            x.Status == BookingStatus.Confirmed && x.Items.Any(i => i.StartAt < end && i.EndAt >= today) ||
            x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt < end));
        if (!scope.IsAdministrator) query = query.Where(x => scope.BranchIds.Contains(x.BranchId) &&
            x.Items.Any(i => i.Asset != null && i.Asset.DivisionId.HasValue && scope.DivisionIds.Contains(i.Asset.DivisionId.Value)));
        var bookings = await query.Include(x => x.Customer).Include(x => x.Branch).Include(x => x.Items).ThenInclude(x => x.Asset).ThenInclude(x => x!.ServiceOffering)
            .Include(x => x.Inspections).Include(x => x.RentalAgreement).Include(x => x.Invoice)
            .OrderBy(x => x.CreatedAt).Take(200).ToListAsync(token);
        var ids = bookings.Select(x => x.Id).ToArray();
        var customerIds = bookings.Select(x => x.CustomerId).Distinct().ToArray();
        var documents = await db.DocumentRecords.AsNoTracking().Where(x => x.EntityType == nameof(Booking) && ids.Contains(x.EntityId) ||
            x.EntityType == nameof(Customer) && customerIds.Contains(x.EntityId))
            .Select(x => new { x.EntityId, x.Type, x.FileName }).ToListAsync(token);
        var approvals = await db.ApprovalRequests.AsNoTracking().Where(x => x.EntityType == nameof(Booking) && ids.Contains(x.EntityId))
            .Select(x => new { x.EntityId, x.Status, x.CreatedAt }).ToListAsync(token);
        var dispatches = await db.DispatchJobs.AsNoTracking().Where(x => ids.Contains(x.BookingId))
            .Select(x => new { x.BookingId, x.Type, x.Status }).ToListAsync(token);
        var rows = bookings.Select(booking =>
        {
            var item = booking.Items.OrderBy(x => x.StartAt).FirstOrDefault();
            var pickup = booking.Status == BookingStatus.Confirmed;
            var required = booking.Items.SelectMany(x => RequiredDocuments(x.Asset?.ServiceOffering?.RequiredDocumentsJson))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var bookingDocuments = documents.Where(x => x.EntityId == booking.Id || x.EntityId == booking.CustomerId).ToList();
            var missingDocuments = required.Where(name => !bookingDocuments.Any(x => x.Type.Contains(name, StringComparison.OrdinalIgnoreCase) || x.FileName.Contains(name, StringComparison.OrdinalIgnoreCase))).ToList();
            var latestApproval = approvals.Where(x => x.EntityId == booking.Id).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            var delivery = dispatches.Where(x => x.BookingId == booking.Id && x.Type == DispatchType.Delivery).ToList();
            var handover = booking.Inspections.Any(x => x.Type == InspectionType.Handover);
            var checks = pickup ? new List<ReadinessCheck>
            {
                new("Approval", latestApproval is null || latestApproval.Status == ApprovalStatus.Approved, latestApproval?.Status.ToString() ?? "No approval request"),
                new("Customer", booking.Customer is { IsActive: true, IsBlocked: false }, booking.Customer?.IsBlocked == true ? "Blocked" : "Eligible"),
                new("Asset", item?.Asset is { IsActive: true } asset && asset.Status is AssetStatus.Available or AssetStatus.Reserved, item?.Asset?.Status.ToString() ?? "Not allocated"),
                new("Documents", missingDocuments.Count == 0, missingDocuments.Count == 0 ? "Complete" : "Missing: " + string.Join(", ", missingDocuments)),
                new("Bond", booking.DepositRequired <= 0 || booking.BondAmountHeld >= booking.DepositRequired, $"Held {booking.BondAmountHeld:0.00} / required {booking.DepositRequired:0.00}"),
                new("Pre-hire inspection", handover, handover ? "Complete" : "Not complete"),
                new("Agreement", booking.RentalAgreement is { Status: not AgreementStatus.Draft }, booking.RentalAgreement?.Status.ToString() ?? "Not signed"),
                new("Delivery", delivery.Count == 0 || delivery.Any(x => x.Status is DispatchStatus.Assigned or DispatchStatus.EnRoute or DispatchStatus.Arrived or DispatchStatus.Completed), delivery.Count == 0 ? "Not required" : string.Join(", ", delivery.Select(x => x.Status.ToString())))
            } : new List<ReadinessCheck>
            {
                new("On hire", booking.Status == BookingStatus.ConvertedToRental, booking.Status.ToString()),
                new("Post-hire inspection", booking.Inspections.Any(x => x.Type == InspectionType.Return), booking.Inspections.Any(x => x.Type == InspectionType.Return) ? "Complete" : "Not complete"),
                new("Invoice", booking.Invoice is not null, booking.Invoice?.InvoiceNumber ?? "Not issued")
            };
            return new { booking.Id, booking.BookingNumber, customerName = booking.Customer?.Name ?? "Unknown customer", branchName = booking.Branch?.Name ?? "Unknown branch",
                assetName = item?.Asset?.Name ?? "No asset", startAt = item?.StartAt, endAt = item?.EndAt,
                stage = pickup ? "Pickup" : "Return", status = checks.All(x => x.Ready) ? "Ready" : "Needs attention", checks };
        }).OrderBy(x => x.stage).ThenBy(x => x.startAt).ToList();
        return Ok(rows);
    }

    private static IEnumerable<string> RequiredDocuments(string? json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }
    private sealed record ReadinessCheck(string Name, bool Ready, string Detail);
}
