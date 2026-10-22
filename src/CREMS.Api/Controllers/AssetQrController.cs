using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/asset-qr")]
[Authorize(Policy = SystemPolicies.UseAssetQr)]
public sealed class AssetQrController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpPost("resolve")]
    public async Task<ActionResult> Resolve(ResolveQrRequest request, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null) return Forbid();
        var value = request.Code?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value)) return BadRequest(new { message = "Enter the asset number printed on the label." });
        if (Uri.TryCreate(value, UriKind.Absolute, out var labelUri) && labelUri.Scheme is "http" or "https")
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(labelUri.Query);
            value = query.TryGetValue("code", out var values) ? values.FirstOrDefault()?.Trim() ?? value : value;
        }
        Guid? assetId = null;
        if (value.StartsWith("CREMS:ASSET:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value[12..], out var parsed)) assetId = parsed;
        else if (Guid.TryParse(value, out parsed)) assetId = parsed;
        var normalized = value.ToUpperInvariant();
        var asset = assetId.HasValue
            ? await db.Assets.Include(x => x.Branch).Include(x => x.Division).FirstOrDefaultAsync(x => x.Id == assetId, token)
            : await db.Assets.Include(x => x.Branch).Include(x => x.Division).FirstOrDefaultAsync(x => x.AssetNumber.ToUpper() == normalized, token);
        if (asset is null) return NotFound(new { message = "No asset matches this code. Enter its full asset number, for example SHP-SUV-3001." });
        if (!scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();

        var now = DateTimeOffset.UtcNow;
        var upcoming = await db.BookingItems.AsNoTracking().Where(x => x.AssetId == asset.Id && (x.EndAt > now || x.Booking!.Status == BookingStatus.ConvertedToRental) &&
            (x.Booking!.Status == BookingStatus.Confirmed || x.Booking.Status == BookingStatus.ConvertedToRental))
            .OrderBy(x => x.StartAt).Select(x => new { x.StartAt, x.EndAt, x.Booking!.BookingNumber, x.Booking.Status }).Take(5).ToListAsync(token);
        var maintenance = await db.MaintenanceJobs.AsNoTracking().Where(x => x.AssetId == asset.Id &&
            (x.Status == MaintenanceStatus.Open || x.Status == MaintenanceStatus.InProgress || x.Status == MaintenanceStatus.WaitingForParts || x.Status == MaintenanceStatus.Completed && x.ReleasedAt == null))
            .Select(x => new { x.JobNumber, x.ServiceType, x.Status, x.ExpectedReleaseAt }).ToListAsync(token);
        var inTransit = await db.AssetTransfers.AsNoTracking().AnyAsync(x => x.AssetId == asset.Id && x.DepartedAt != null && x.ReceivedAt == null, token);
        var booking = await db.Bookings.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.Branch).Include(x => x.Items).Include(x => x.RentalAgreement)
            .Where(x => x.Items.Any(i => i.AssetId == asset.Id) &&
                (x.Status == BookingStatus.ConvertedToRental || x.Status == BookingStatus.Confirmed))
            .OrderByDescending(x => x.Status == BookingStatus.ConvertedToRental).ThenBy(x => x.Items.Min(i => i.StartAt))
            .FirstOrDefaultAsync(token);

        var fiji = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Fiji");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, fiji).DateTime);
        var pickupDue = booking?.Items.Any(x => x.AssetId == asset.Id && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.StartAt, fiji).DateTime) <= today) == true;
        var action = booking?.Status switch { BookingStatus.Confirmed when pickupDue && asset.IsActive && (asset.Status is AssetStatus.Available or AssetStatus.Reserved) && maintenance.Count == 0 && !inTransit => "CheckOut", BookingStatus.ConvertedToRental => "CheckIn", _ => "ViewOnly" };
        var bookedNow = booking?.Status == BookingStatus.ConvertedToRental || upcoming.Any(x => x.StartAt <= now && x.EndAt > now);
        var availableNow = asset.IsActive && (asset.Status is AssetStatus.Available or AssetStatus.Reserved) && maintenance.Count == 0 && !inTransit && !bookedNow;
        var payments = booking is null ? 0 : await db.RentalPayments.Where(x => x.BookingId == booking.Id && x.Status == PaymentStatus.Recorded).SumAsync(x => (decimal?)x.Amount, token) ?? 0;
        var driverVerified = booking is not null && await db.AuthorizedDrivers.AnyAsync(x => x.BookingId == booking.Id && x.Verified && x.LicenceExpiry > DateOnly.FromDateTime(DateTime.UtcNow), token);
        AuditWriter.Record(db, scope, "Asset QR scanned", "Asset", asset.Id, $"{asset.AssetNumber} scanned for {action}.", asset.BranchId);
        await db.SaveChangesAsync(token);

        return Ok(new
        {
            asset = new { asset.Id, asset.AssetNumber, asset.Name, asset.Type, asset.Status, asset.IsActive, asset.RegistrationNumber, asset.SerialNumber, asset.BranchId, BranchName = asset.Branch!.Name, DivisionName = asset.Division?.Name },
            availability = new { availableNow, checkedAt = now, inTransit, maintenance, upcoming, reason = availableNow ? "Available now. Review upcoming bookings before arranging a hire." : !asset.IsActive ? "Asset is inactive." : maintenance.Count > 0 ? "Maintenance or a safety release is outstanding." : inTransit ? "Asset is in transit." : bookedNow ? "Asset has an active hire or current booking." : $"Asset status is {asset.Status}." },
            action,
            booking = booking is null ? null : new { booking.Id, booking.BookingNumber, booking.Status, CustomerName = booking.Customer!.Name, booking.DepositRequired, AmountPaid = payments, AgreementReady = booking.RentalAgreement is not null, DriverVerified = driverVerified, StartAt = booking.Items.Min(x => x.StartAt), EndAt = booking.Items.Max(x => x.EndAt), AssetCount = booking.Items.Count },
            message = booking is null ? "No confirmed booking or active rental was found for this asset." : null,
        });
    }
}

public sealed record ResolveQrRequest([Required, MaxLength(2048)] string Code);
