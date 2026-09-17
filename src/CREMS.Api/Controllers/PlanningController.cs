using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;
[ApiController, Route("api/planning"), Authorize(Policy=SystemPolicies.StaffPortal)]
public sealed class PlanningController(ApplicationDbContext db, CurrentStaffScope staffScope, RentalPricingService pricing):ControllerBase
{
 [HttpPost("price")] public async Task<ActionResult> Price(PricingPreviewRequest request,CancellationToken token){var asset=await db.Assets.FirstOrDefaultAsync(x=>x.Id==request.AssetId,token);if(asset is null)return NotFound();var scope=await staffScope.GetAsync(User);if(scope is null||!scope.HasAssetAccess(asset.BranchId,asset.DivisionId))return Forbid();return Ok(await pricing.CalculateAsync(asset,request.CustomerId,request.StartAt,request.EndAt,request.OperatorHours,request.DeliveryKilometres,request.DeliveryZoneId,request.TaxInclusive,request.TaxRate,token));}
 [HttpGet("calendar")]
 public async Task<ActionResult> Calendar([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken token)
 {
  if (to <= from || to - from > TimeSpan.FromDays(120))
   return BadRequest(new { message = "Choose a planning period between 1 and 120 days." });

  var scope = await staffScope.GetAsync(User);
  if (scope is null) return Forbid();

  var assets = db.Assets.AsNoTracking().Where(x => x.IsActive &&
   (scope.IsAdministrator || scope.BranchIds.Contains(x.BranchId) &&
    (!x.DivisionId.HasValue || scope.DivisionIds.Contains(x.DivisionId.Value))));
  var ids = assets.Select(x => x.Id);
  var activeDraftCutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
  var events = await db.BookingItems.AsNoTracking().Where(x => ids.Contains(x.AssetId) &&
   x.StartAt < to && x.EndAt > from &&
   (x.Booking!.Status == BookingStatus.Confirmed ||
    x.Booking.Status == BookingStatus.ConvertedToRental ||
    x.Booking.Status == BookingStatus.Completed ||
    x.Booking.Status == BookingStatus.Draft && x.Booking.CreatedAt > activeDraftCutoff))
   .Select(x => new { x.AssetId, x.StartAt, x.EndAt, type = x.Booking!.Status.ToString(), reference = x.Booking.BookingNumber })
   .ToListAsync(token);
  var maintenance = await db.MaintenanceJobs.AsNoTracking().Where(x => ids.Contains(x.AssetId) &&
   x.Status != Domain.Assets.MaintenanceStatus.Cancelled && x.ReportedAt < to && (x.CompletedAt ?? to) > from)
   .Select(x => new { x.AssetId, startAt = x.ReportedAt, endAt = x.CompletedAt ?? to, type = "Maintenance", reference = x.JobNumber })
   .ToListAsync(token);
  var transfers = await db.AssetTransfers.AsNoTracking().Where(x => ids.Contains(x.AssetId) &&
   x.DepartedAt < to && (x.ReceivedAt ?? to) > from)
   .Select(x => new { x.AssetId, startAt = x.DepartedAt!.Value, endAt = x.ReceivedAt ?? to, type = "Transfer", reference = x.TransferNumber })
   .ToListAsync(token);
  var personnel = await db.BookingPersonnelAssignments.AsNoTracking().Where(x =>
   x.Status != Domain.Operations.AssignmentStatus.Cancelled && x.StartAt < to && x.EndAt > from &&
   (scope.IsAdministrator || scope.BranchIds.Contains(x.Personnel!.BranchId)))
   .Select(x => new { x.PersonnelId, x.Personnel!.FullName, x.StartAt, x.EndAt, type = "Personnel", reference = x.Role })
   .ToListAsync(token);
  var closures = await db.BranchCalendarExceptions.AsNoTracking().Where(x =>
   x.Date >= DateOnly.FromDateTime(from.Date) && x.Date <= DateOnly.FromDateTime(to.Date) &&
   (scope.IsAdministrator || scope.BranchIds.Contains(x.BranchId)))
   .OrderBy(x => x.Date).Select(x => new { x.Id, x.BranchId, x.Date, x.Name, x.IsClosed })
   .ToListAsync(token);
  var assetRows = await assets.OrderBy(x => x.AssetNumber)
   .Select(x => new { x.Id, x.AssetNumber, x.Name, x.Status, x.BranchId, x.DivisionId }).ToListAsync(token);

  return Ok(new { assets = assetRows, events, maintenance, transfers, personnel, closures });
 }
}
public sealed record PricingPreviewRequest(Guid AssetId,Guid? CustomerId,DateTimeOffset StartAt,DateTimeOffset EndAt,decimal OperatorHours,decimal DeliveryKilometres,Guid? DeliveryZoneId,bool TaxInclusive,decimal TaxRate);
