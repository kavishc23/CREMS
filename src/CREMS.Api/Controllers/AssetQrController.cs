using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
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
        var value = request.Code.Trim();
        Guid? assetId = null;
        if (value.StartsWith("CREMS:ASSET:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value[12..], out var parsed)) assetId = parsed;
        else if (Guid.TryParse(value, out parsed)) assetId = parsed;
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).TryGetValue("code", out var codeValues);
            var code = codeValues.FirstOrDefault();
            if (code?.StartsWith("CREMS:ASSET:", StringComparison.OrdinalIgnoreCase) == true && Guid.TryParse(code[12..], out parsed)) assetId = parsed;
        }

        var asset = assetId.HasValue
            ? await db.Assets.Include(x => x.Branch).FirstOrDefaultAsync(x => x.Id == assetId, token)
            : await db.Assets.Include(x => x.Branch).FirstOrDefaultAsync(x => x.AssetNumber == value, token);
        if (asset is null) return NotFound(new { message = "This QR code does not match a CREMS asset." });
        if (!scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();

        var booking = await db.Bookings.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.Branch).Include(x => x.Items).Include(x => x.RentalAgreement)
            .Where(x => x.Items.Any(i => i.AssetId == asset.Id) &&
                (x.Status == BookingStatus.ConvertedToRental || x.Status == BookingStatus.Confirmed))
            .OrderByDescending(x => x.Status == BookingStatus.ConvertedToRental).ThenBy(x => x.Items.Min(i => i.StartAt))
            .FirstOrDefaultAsync(token);

        var action = booking?.Status switch { BookingStatus.Confirmed => "CheckOut", BookingStatus.ConvertedToRental => "CheckIn", _ => "ViewOnly" };
        var payments = booking is null ? 0 : await db.RentalPayments.Where(x => x.BookingId == booking.Id && x.Status == PaymentStatus.Recorded).SumAsync(x => (decimal?)x.Amount, token) ?? 0;
        var driverVerified = booking is not null && await db.AuthorizedDrivers.AnyAsync(x => x.BookingId == booking.Id && x.Verified && x.LicenceExpiry > DateOnly.FromDateTime(DateTime.UtcNow), token);
        AuditWriter.Record(db, scope, "Asset QR scanned", "Asset", asset.Id, $"{asset.AssetNumber} scanned for {action}.", asset.BranchId);
        await db.SaveChangesAsync(token);

        return Ok(new
        {
            asset = new { asset.Id, asset.AssetNumber, asset.Name, asset.Type, asset.Status, asset.RegistrationNumber, asset.SerialNumber, asset.BranchId, BranchName = asset.Branch!.Name },
            action,
            booking = booking is null ? null : new { booking.Id, booking.BookingNumber, booking.Status, CustomerName = booking.Customer!.Name, booking.DepositRequired, AmountPaid = payments, AgreementReady = booking.RentalAgreement is not null, DriverVerified = driverVerified, StartAt = booking.Items.Min(x => x.StartAt), EndAt = booking.Items.Max(x => x.EndAt), AssetCount = booking.Items.Count },
            message = booking is null ? "No confirmed booking or active rental was found for this asset." : null,
        });
    }
}

public sealed record ResolveQrRequest([Required] string Code);
