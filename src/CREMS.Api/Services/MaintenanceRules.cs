using System.Data;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CREMS.Api.Services;

public static class MaintenanceRules
{
    private static readonly TimeZoneInfo Fiji = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Fiji");
    public static DateOnly LocalDate(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Fiji).DateTime);

    // Range locks protect open-job checks and stock read/modify/write operations across server instances.
    public static async Task<IDbContextTransaction?> BeginAsync(ApplicationDbContext db, CancellationToken token)
        => db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;

    public static IQueryable<Asset> DueAssets(ApplicationDbContext db, IQueryable<Asset> assets, DateOnly through)
        => assets.Where(asset => asset.IsActive && asset.Status != AssetStatus.Retired
            && ((asset.NextServiceDate.HasValue && asset.NextServiceDate <= through)
                || (asset.CurrentMeterReading.HasValue && db.MaintenanceJobs
                    .Where(job => job.AssetId == asset.Id && job.Status == MaintenanceStatus.Completed && job.NextServiceMeter.HasValue)
                    .OrderByDescending(job => job.CompletedAt).ThenByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id)
                    .Select(job => job.NextServiceMeter).FirstOrDefault() <= asset.CurrentMeterReading)));

    public static async Task<int> GeneratePreventiveJobsAsync(ApplicationDbContext db, DateTimeOffset now, CancellationToken token)
    {
        await using var transaction = await BeginAsync(db, token);
        var today = LocalDate(now);
        var assets = await DueAssets(db, db.Assets.Where(asset => asset.Status == AssetStatus.Available || asset.Status == AssetStatus.Maintenance), today)
            .OrderBy(asset => asset.Id).ToListAsync(token);
        var created = 0;
        foreach (var asset in assets)
        {
            if (await db.MaintenanceJobs.AnyAsync(job => job.AssetId == asset.Id
                && job.Status != MaintenanceStatus.Completed && job.Status != MaintenanceStatus.Cancelled, token)) continue;
            var meter = await db.MaintenanceJobs.Where(job => job.AssetId == asset.Id && job.Status == MaintenanceStatus.Completed && job.NextServiceMeter.HasValue)
                .OrderByDescending(job => job.CompletedAt).ThenByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id)
                .Select(job => job.NextServiceMeter).FirstOrDefaultAsync(token);
            var meterDue = meter.HasValue && asset.CurrentMeterReading >= meter;
            var dateDue = asset.NextServiceDate.HasValue && asset.NextServiceDate <= today;
            var job = new MaintenanceJob { JobNumber = $"MNT-AUTO-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..29],
                AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = "Preventive maintenance",
                FaultDescription = dateDue && meterDue ? "Date and meter preventive maintenance thresholds reached."
                    : meterDue ? "Meter-based preventive maintenance threshold reached." : "Date-based preventive maintenance interval reached.",
                IsPreventive = true, NextServiceDate = asset.NextServiceDate, NextServiceMeter = meter, Status = MaintenanceStatus.Open };
            db.MaintenanceJobs.Add(job);
            db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, Type = AssetLifecycleEventType.MaintenanceStarted,
                FromStatus = asset.Status, ToStatus = AssetStatus.Maintenance, Notes = $"Automatic preventive job {job.JobNumber}", RecordedByName = "System" });
            asset.Status = AssetStatus.Maintenance;
            created++;
        }
        var configuredAssets = await db.Assets.Where(asset => asset.IsActive && asset.ServiceOfferingId.HasValue
            && (asset.Status == AssetStatus.Available || asset.Status == AssetStatus.Maintenance)).ToListAsync(token);
        foreach (var asset in configuredAssets)
            if (await GenerateMeterJobAsync(db, asset, token)) created++;
        await db.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return created;
    }

    public static async Task<bool> GenerateMeterJobAsync(ApplicationDbContext db, Asset asset, CancellationToken token)
    {
        if (!asset.IsActive || !asset.ServiceOfferingId.HasValue || !asset.CurrentMeterReading.HasValue
            || asset.Status is not (AssetStatus.Available or AssetStatus.Maintenance)) return false;
        if (db.ChangeTracker.Entries<MaintenanceJob>().Any(entry => entry.State == EntityState.Added
                && entry.Entity.AssetId == asset.Id && entry.Entity.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled))
            || await db.MaintenanceJobs.AnyAsync(job => job.AssetId == asset.Id
                && job.Status != MaintenanceStatus.Completed && job.Status != MaintenanceStatus.Cancelled, token)) return false;
        var json = await db.ServiceOfferings.Where(service => service.Id == asset.ServiceOfferingId)
            .Select(service => service.MaintenanceRulesJson).FirstOrDefaultAsync(token);
        if (string.IsNullOrWhiteSpace(json)) return false;
        decimal interval;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("meterInterval", out var value)
                || value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out interval)
                || interval <= 0 || interval > 100000000) return false;
        }
        catch (JsonException) { return false; }
        var latest = await db.MaintenanceJobs.Where(job => job.AssetId == asset.Id && job.Status == MaintenanceStatus.Completed
                && (job.NextServiceMeter.HasValue || job.IsPreventive))
            .OrderByDescending(job => job.CompletedAt).ThenByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id)
            .FirstOrDefaultAsync(token);
        var threshold = latest?.NextServiceMeter ?? (latest?.MeterReading ?? 0) + interval;
        if (asset.CurrentMeterReading < threshold) return false;
        db.MaintenanceJobs.Add(new MaintenanceJob { JobNumber = $"MNT-{Guid.NewGuid():N}"[..29],
            AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = "Preventive meter service", IsPreventive = true,
            NextServiceMeter = Math.Min(100000000, asset.CurrentMeterReading.Value + interval), MeterReading = asset.CurrentMeterReading,
            FaultDescription = $"Meter service threshold {threshold} reached at {asset.CurrentMeterReading} {asset.MeterUnit}.",
            Status = MaintenanceStatus.Open });
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, Type = AssetLifecycleEventType.MaintenanceStarted,
            FromStatus = asset.Status, ToStatus = AssetStatus.Maintenance, Notes = "Meter service threshold reached", RecordedByName = "System" });
        asset.Status = AssetStatus.Maintenance;
        return true;
    }

    public static async Task CheckAssetReleaseAsync(ApplicationDbContext db, CancellationToken token)
    {
        var transferring = db.ChangeTracker.Entries<Asset>().Where(entry => entry.State == EntityState.Modified
            && entry.Property(asset => asset.BranchId).IsModified).Select(entry => entry.Entity.Id).ToArray();
        if (transferring.Length > 0 && (db.ChangeTracker.Entries<MaintenanceJob>().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified && transferring.Contains(entry.Entity.AssetId)
                && entry.Entity.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled))
            || await db.MaintenanceJobs.AnyAsync(job => transferring.Contains(job.AssetId)
                && job.Status != MaintenanceStatus.Completed && job.Status != MaintenanceStatus.Cancelled, token)))
            throw new MaintenanceConflictException("Close all open maintenance jobs before transferring this asset to another branch.");
        var released = db.ChangeTracker.Entries<Asset>().Where(entry => entry.State == EntityState.Modified
            && entry.Property(asset => asset.Status).IsModified
            && entry.Entity.Status is AssetStatus.Available or AssetStatus.Reserved or AssetStatus.Rented)
            .Select(entry => entry.Entity.Id).ToArray();
        if (released.Length == 0) return;
        var closing = db.ChangeTracker.Entries<MaintenanceJob>().Where(entry => entry.State == EntityState.Modified
            && entry.Entity.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled).Select(entry => entry.Entity.Id).ToArray();
        var pending = db.ChangeTracker.Entries<MaintenanceJob>().Any(entry => entry.State is EntityState.Added or EntityState.Modified
            && released.Contains(entry.Entity.AssetId) && entry.Entity.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled));
        if (pending || await db.MaintenanceJobs.AnyAsync(job => released.Contains(job.AssetId) && !closing.Contains(job.Id)
            && job.Status != MaintenanceStatus.Completed && job.Status != MaintenanceStatus.Cancelled, token))
            throw new MaintenanceConflictException("Close all open maintenance jobs before returning this asset to service.");
    }
}

public sealed class MaintenanceConflictException(string message) : Exception(message);
