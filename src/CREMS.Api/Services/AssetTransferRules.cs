using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class AssetTransferRules
{
    public static async Task<string?> Validate(ApplicationDbContext db, Asset asset, Guid from, Guid to, Guid? transferId, CancellationToken token)
    {
        if (from == to || asset.BranchId != from) return "Choose different branches and the asset's current source branch.";
        if (await db.Branches.CountAsync(x => (x.Id == from || x.Id == to) && x.IsActive, token) != 2)
            return "Both branches must be active.";
        if (asset.DivisionId.HasValue && !await db.BranchDivisions.AnyAsync(x => x.BranchId == to && x.DivisionId == asset.DivisionId && x.IsActive, token))
            return "The destination must operate the asset's division.";
        var service = asset.ServiceOfferingId;
        if (!service.HasValue && asset.AssetCategoryId.HasValue)
            service = await db.AssetCategories.Where(x => x.Id == asset.AssetCategoryId).Select(x => x.ServiceOfferingId).FirstOrDefaultAsync(token);
        if (service.HasValue && !await db.BranchDivisionServices.AnyAsync(x => x.BranchId == to && x.DivisionId == asset.DivisionId && x.ServiceOfferingId == service && x.IsActive, token))
            return "Enable the asset's service at the destination branch first.";
        if (await db.AssetTransfers.AnyAsync(x => x.AssetId == asset.Id && x.Id != transferId && x.Status != TransferStatus.Cancelled && x.Status != TransferStatus.Rejected && x.Status != TransferStatus.Inspected, token))
            return "This asset already has an active transfer.";
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
        if (await db.BookingItems.AnyAsync(x => x.AssetId == asset.Id && (x.Booking!.Status == BookingStatus.Confirmed || x.Booking.Status == BookingStatus.ConvertedToRental || x.Booking.Status == BookingStatus.Draft && x.Booking.CreatedAt > cutoff), token))
            return "Resolve the asset's bookings before transferring it.";
        if (await db.MaintenanceJobs.AnyAsync(x => x.AssetId == asset.Id && (x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled || x.ReleasedAt == null), token))
            return "Complete maintenance and its safety release before transferring this asset.";
        return null;
    }

    public static async Task CheckRelease(ApplicationDbContext db, CancellationToken token)
    {
        var allocating = db.ChangeTracker.Entries<BookingItem>().Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity.AssetId).Distinct().ToArray();
        if (allocating.Length > 0 && await db.AssetTransfers.AnyAsync(x => allocating.Contains(x.AssetId) &&
            (x.Status == TransferStatus.Approved || x.Status == TransferStatus.InTransit || x.Status == TransferStatus.Received), token))
            throw new MaintenanceConflictException("This asset is unavailable while its transfer is active.");
        var ids = db.ChangeTracker.Entries<Asset>().Where(e => e.State == EntityState.Modified &&
            (e.Property(x => x.BranchId).IsModified || e.Property(x => x.DivisionId).IsModified ||
             e.Property(x => x.Status).IsModified && e.Entity.Status is AssetStatus.Available or AssetStatus.Reserved or AssetStatus.Rented))
            .Select(e => e.Entity.Id).ToArray();
        if (ids.Length == 0) return;
        var closing = db.ChangeTracker.Entries<AssetTransfer>().Where(e => e.State == EntityState.Modified &&
            e.Entity.Status is TransferStatus.Cancelled or TransferStatus.Inspected).Select(e => e.Entity.Id).ToArray();
        if (await db.AssetTransfers.AnyAsync(x => ids.Contains(x.AssetId) && !closing.Contains(x.Id) &&
            (x.Status == TransferStatus.Approved || x.Status == TransferStatus.InTransit || x.Status == TransferStatus.Received), token))
            throw new MaintenanceConflictException("Complete or cancel the active transfer before releasing or relocating this asset.");
    }
}
