using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class PickupReadiness
{
    public static async Task<IReadOnlyList<string>> Check(ApplicationDbContext db, Booking booking, CancellationToken token)
    {
        var reasons = new List<string>();
        if (booking.Status != BookingStatus.Confirmed) reasons.Add("Only a confirmed booking can be picked up.");
        if (booking.Customer is null || !booking.Customer.IsActive || booking.Customer.IsBlocked) reasons.Add("Resolve the customer account restriction before pickup.");
        if (booking.Items.Count == 0) reasons.Add("Allocate an asset before pickup.");
        foreach (var item in booking.Items)
        {
            var asset = item.Asset;
            if (asset is null) { reasons.Add("The allocated asset could not be found."); continue; }
            if (!asset.IsActive || asset.Status is AssetStatus.OutOfService or AssetStatus.Retired) reasons.Add($"{asset.AssetNumber} is unavailable for hire.");
            if (asset.Status == AssetStatus.Inspection) reasons.Add($"{asset.AssetNumber} is awaiting inspection. Complete its safety release in Maintenance work before pickup.");
            if (asset.BranchId != booking.BranchId) reasons.Add($"{asset.AssetNumber} is at another branch. Complete its transfer to the pickup branch first.");
            var work = await db.MaintenanceJobs.Where(job => job.AssetId == asset.Id && (job.Status != MaintenanceStatus.Completed && job.Status != MaintenanceStatus.Cancelled || job.ReleasedAt == null)).Select(job => job.JobNumber).ToListAsync(token);
            if (work.Count > 0) reasons.Add($"Complete the work and safety release for {string.Join(", ", work)} before pickup.");
            else if (asset.Status == AssetStatus.Maintenance) reasons.Add($"{asset.AssetNumber} still has maintenance status. Review its maintenance release before pickup.");
            var unreturned = await db.BookingItems.Where(other => other.AssetId == asset.Id && other.BookingId != booking.Id && (other.Booking!.Status == BookingStatus.ConvertedToRental || other.Booking.Status == BookingStatus.Completed && !other.Booking.Inspections.Any(inspection => inspection.Type == InspectionType.Return))).Select(other => other.Booking!.BookingNumber).Distinct().ToListAsync(token);
            if (unreturned.Count > 0) reasons.Add($"Complete the outstanding return for {string.Join(", ", unreturned)} before releasing {asset.AssetNumber}.");
            else if (asset.Status == AssetStatus.Rented) reasons.Add($"{asset.AssetNumber} is recorded as on hire. Review its latest return before pickup.");
            var conflicts = await db.BookingItems.Where(other => other.AssetId == asset.Id && other.BookingId != booking.Id && other.Booking!.Status == BookingStatus.Confirmed && other.StartAt < item.EndAt && other.EndAt > item.StartAt).Select(other => other.Booking!.BookingNumber).Distinct().ToListAsync(token);
            if (conflicts.Count > 0) reasons.Add($"The hire period overlaps confirmed booking {string.Join(", ", conflicts)}. Resolve the allocation before pickup.");
            if (await db.AssetTransfers.AnyAsync(transfer => transfer.AssetId == asset.Id && (transfer.Status == TransferStatus.Approved || transfer.Status == TransferStatus.InTransit || transfer.Status == TransferStatus.Received), token)) reasons.Add($"Complete the transfer and receiving inspection for {asset.AssetNumber} before pickup.");
        }
        return reasons.Distinct().ToArray();
    }
}
