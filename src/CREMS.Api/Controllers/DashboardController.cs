using CREMS.Api.Data;
using CREMS.Api.Services;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Domain.Corporate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class DashboardController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryResponse>> GetSummary(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var now = DateTimeOffset.UtcNow;
        var today = MaintenanceRules.LocalDate(now);
        var assetQuery = db.Assets.AsNoTracking().Where(asset => asset.IsActive);
        var bookingQuery = db.Bookings.AsNoTracking();
        if (!scope.IsAdministrator)
        {
            assetQuery = assetQuery.Where(asset => asset.BranchId == scope.BranchId && asset.DivisionId == scope.DivisionId);
            bookingQuery = bookingQuery.Where(booking => booking.BranchId == scope.BranchId && booking.Items.Any(item => item.Asset!.DivisionId == scope.DivisionId));
        }
        var assetCounts = await assetQuery
            .GroupBy(asset => new { asset.Type, asset.Status })
            .Select(group => new { group.Key.Type, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var servicesDueSoon = await MaintenanceRules.DueAssets(db, assetQuery, today.AddDays(30)).CountAsync(cancellationToken);

        var bookingCounts = await bookingQuery
            .GroupBy(booking => booking.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var overdueRentals = await bookingQuery
            .CountAsync(booking => booking.Status == BookingStatus.ConvertedToRental &&
                booking.Items.Any(item => item.EndAt < now), cancellationToken);
        var upcomingBookings = await bookingQuery
            .CountAsync(booking => booking.Status == BookingStatus.Confirmed &&
                booking.Items.Any(item => item.StartAt >= now), cancellationToken);

        int AssetCount(AssetStatus? status = null, AssetType? type = null) => assetCounts
            .Where(item => (!status.HasValue || item.Status == status) && (!type.HasValue || item.Type == type))
            .Sum(item => item.Count);
        var vehicleTotal = assetCounts.Where(x => AssetCategoryPolicy.IsVehicle(x.Type)).Sum(x => x.Count);
        var equipmentTotal = assetCounts.Where(x => AssetCategoryPolicy.IsEquipment(x.Type)).Sum(x => x.Count);
        var vehicleRented = assetCounts.Where(x => x.Status == AssetStatus.Rented && AssetCategoryPolicy.IsVehicle(x.Type)).Sum(x => x.Count);
        var equipmentRented = assetCounts.Where(x => x.Status == AssetStatus.Rented && AssetCategoryPolicy.IsEquipment(x.Type)).Sum(x => x.Count);

        return Ok(new DashboardSummaryResponse(
            AssetCount(AssetStatus.Available),
            bookingCounts.GetValueOrDefault(BookingStatus.ConvertedToRental),
            AssetCount(AssetStatus.Maintenance),
            overdueRentals,
            bookingCounts.GetValueOrDefault(BookingStatus.Draft),
            upcomingBookings,
            servicesDueSoon,
            vehicleTotal == 0 ? 0 : (int)Math.Round(vehicleRented * 100d / vehicleTotal),
            equipmentTotal == 0 ? 0 : (int)Math.Round(equipmentRented * 100d / equipmentTotal),
            AssetCount()));
    }

    [HttpGet("operations")]
    [Authorize(Policy = SystemPolicies.ViewReports)]
    public async Task<ActionResult> GetOperations(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null) return Forbid();
        var now = DateTimeOffset.UtcNow;
        var today = MaintenanceRules.LocalDate(now);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var previousMonth = currentMonth.AddMonths(-1);
        var (monthStart, monthEnd) = ReportingKpis.FijiPeriod(currentMonth, today);
        var (previousMonthStart, _) = ReportingKpis.FijiPeriod(previousMonth, previousMonth);
        var assets = db.Assets.AsNoTracking().Where(asset => asset.IsActive);
        var bookings = db.Bookings.AsNoTracking();
        var invoices = db.RentalInvoices.AsNoTracking().Where(invoice => invoice.Booking != null);
        var maintenance = db.MaintenanceJobs.AsNoTracking();
        var approvals = db.ApprovalRequests.AsNoTracking();
        var audit = db.AuditEvents.AsNoTracking();
        if (!scope.IsAdministrator)
        {
            assets = assets.Where(asset => scope.BranchIds.Contains(asset.BranchId) && asset.DivisionId.HasValue && scope.DivisionIds.Contains(asset.DivisionId.Value));
            bookings = bookings.Where(booking => scope.BranchIds.Contains(booking.BranchId));
            invoices = invoices.Where(invoice => scope.BranchIds.Contains(invoice.Booking!.BranchId));
            maintenance = maintenance.Where(job => scope.BranchIds.Contains(job.BranchId) && job.Asset!.DivisionId.HasValue && scope.DivisionIds.Contains(job.Asset.DivisionId.Value));
            approvals = approvals.Where(item => scope.BranchIds.Contains(item.BranchId));
            audit = audit.Where(item => !item.BranchId.HasValue || scope.BranchIds.Contains(item.BranchId.Value));
        }
        var monthlyInvoices = await invoices.Where(item => item.IssuedAt >= previousMonthStart && item.IssuedAt < monthEnd).ToListAsync(cancellationToken);
        var monthlyInvoiceIds = monthlyInvoices.Select(x => x.Id).ToArray();
        var monthlyCredits = await db.CreditNotes.AsNoTracking().Where(x => monthlyInvoiceIds.Contains(x.InvoiceId) && x.Status == CreditNoteStatus.Applied).ToListAsync(cancellationToken);
        var currentRevenue = ReportingKpis.Revenue(monthlyInvoices.Where(x => x.IssuedAt >= monthStart), monthlyCredits);
        var previousRevenue = ReportingKpis.Revenue(monthlyInvoices.Where(x => x.IssuedAt < monthStart), monthlyCredits);
        var monthlyJobs = await maintenance.Where(item => item.ReportedAt >= previousMonthStart && item.ReportedAt < monthEnd).ToListAsync(cancellationToken);
        var currentCosts = ReportingKpis.MaintenanceExpense(monthlyJobs.Where(x => x.ReportedAt >= monthStart));
        var previousCosts = ReportingKpis.MaintenanceExpense(monthlyJobs.Where(x => x.ReportedAt < monthStart));
        var fleet = await assets.GroupBy(item => new { Division = item.Division != null ? item.Division.Name : "Unassigned", item.Status }).Select(group => new { group.Key.Division, Status = group.Key.Status.ToString(), Count = group.Count() }).ToListAsync(cancellationToken);
        var alerts = new List<object>();
        alerts.AddRange(await bookings.Where(item => item.Status == BookingStatus.ConvertedToRental && item.Items.Any(line => line.EndAt < now)).OrderBy(item => item.BookingNumber).Take(5).Select(item => new { Type = "Overdue return", Detail = item.BookingNumber + " requires return follow-up", Priority = "High", Page = "rentals" }).ToListAsync(cancellationToken));
        alerts.AddRange(await bookings.Where(item => item.Status == BookingStatus.Completed &&
            !item.Inspections.Any(inspection => inspection.Type == InspectionType.Return))
            .OrderByDescending(item => item.UpdatedAt).Take(5)
            .Select(item => new { Type = "Return inspection missing", Detail = item.BookingNumber + " has no post-hire inspection", Priority = "High", Page = "rentals" }).ToListAsync(cancellationToken));
        alerts.AddRange(await bookings.Where(item => item.Status == BookingStatus.Completed &&
            !db.RentalInvoices.Any(invoice => invoice.BookingId == item.Id && invoice.Status != InvoiceStatus.Voided))
            .OrderByDescending(item => item.UpdatedAt).Take(5)
            .Select(item => new { Type = "Invoice missing", Detail = item.BookingNumber + " was completed without an invoice", Priority = "High", Page = "finance" }).ToListAsync(cancellationToken));
        alerts.AddRange(await maintenance.Where(item => item.Status == MaintenanceStatus.Completed && item.ReleasedAt == null)
            .OrderByDescending(item => item.CompletedAt).Take(5)
            .Select(item => new { Type = "Safety release pending", Detail = item.JobNumber + " repair is complete", Priority = "High", Page = "maintenance" }).ToListAsync(cancellationToken));
        alerts.AddRange(await assets.Where(item => item.Status == AssetStatus.Available &&
            db.BookingItems.Any(line => line.AssetId == item.Id && line.Booking!.Status == BookingStatus.ConvertedToRental && line.StartAt <= now && line.EndAt >= now))
            .OrderBy(item => item.AssetNumber).Take(5)
            .Select(item => new { Type = "Asset status conflict", Detail = item.AssetNumber + " is available while on hire", Priority = "High", Page = "assets" }).ToListAsync(cancellationToken));
        alerts.AddRange(await MaintenanceRules.DueAssets(db, assets, MaintenanceRules.LocalDate(now).AddDays(14)).OrderBy(item => item.NextServiceDate).Take(5).Select(item => new { Type = "Maintenance due", Detail = item.AssetNumber + " · " + item.Name, Priority = "Medium", Page = "maintenance" }).ToListAsync(cancellationToken));
        alerts.AddRange(await approvals.Where(item => item.Status == ApprovalStatus.Pending).OrderBy(item => item.CreatedAt).Take(5).Select(item => new { Type = "Approval pending", Detail = item.RequestNumber + " · " + item.Reason, Priority = "Medium", Page = "operations" }).ToListAsync(cancellationToken));
        var activity = await audit.OrderByDescending(item => item.OccurredAt).Take(8).Select(item => new { item.Id, item.Action, item.Summary, item.UserName, item.OccurredAt, BranchName = item.Branch != null ? item.Branch.Name : null }).ToListAsync(cancellationToken);
        return Ok(new { currentRevenue, previousRevenue, currentCosts, previousCosts, fleet, alerts = alerts.Take(8), activity, updatedAt = now });
    }
}

public sealed record DashboardSummaryResponse(
    int AvailableAssets, int ActiveRentals, int UnderMaintenance, int OverdueRentals,
    int PendingRequests, int UpcomingBookings, int ServicesDueSoon,
    int VehicleUtilization, int EquipmentUtilization, int TotalAssets);
