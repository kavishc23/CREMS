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

    public static (decimal? Interval, string? Warning) ReadMeterRule(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return (null, "The service rule must be an object. Ask an administrator to correct it.");
            var unknown = document.RootElement.EnumerateObject().Any(x => x.Name != "meterInterval");
            if (!document.RootElement.TryGetProperty("meterInterval", out var value)) return (null, unknown ? "Additional configured rules are not automatic triggers. Use explicit service targets." : null);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var interval) || interval <= 0 || interval > 100000000)
                return (null, "The meter interval must be greater than zero and no more than 100,000,000. Ask an administrator to correct it.");
            return (interval, unknown ? "Additional configured rules are not automatic triggers. Use explicit service targets." : null);
        } catch (JsonException) { return (null, "The configured service rule is invalid. Ask an administrator to correct it."); }
    }

    public static ServicePlan Plan(Asset asset, MaintenanceJob? latest, string? rules, DateOnly today)
    {
        var (interval, warning) = ReadMeterRule(rules);
        var target = latest?.NextServiceMeter ?? (interval.HasValue ? Math.Min(100000000, (latest?.MeterReading ?? 0) + interval.Value) : (decimal?)null);
        var source = latest?.NextServiceMeter != null ? "Completed service target" : interval != null ? latest == null ? "Initial configured interval from zero" : "Configured interval after completed service" : "Not configured";
        return new(asset.Id, asset.AssetNumber, asset.Name, asset.BranchId, asset.DivisionId, asset.Branch?.Name ?? "", asset.Status, asset.MeterUnit, asset.CurrentMeterReading,
            asset.NextServiceDate, target, interval, latest?.ServiceIntervalMonths, source, warning,
            asset.NextServiceDate <= today || target.HasValue && asset.CurrentMeterReading >= target,
            asset.NextServiceDate < today || target.HasValue && asset.CurrentMeterReading > target);
    }

    // Resolve configured JSON intervals in one scoped batch; no per-row database calls.
    public static async Task<List<ServicePlan>> PlansAsync(ApplicationDbContext db, IQueryable<Asset> assets, DateOnly today, CancellationToken token)
    {
        var rows = await assets.Include(x => x.Branch).Select(x => new {
            Asset = x, BranchName = x.Branch!.Name, Rules = x.ServiceOffering == null ? null : x.ServiceOffering.MaintenanceRulesJson,
            Latest = db.MaintenanceJobs.Where(j => j.AssetId == x.Id && j.Status == MaintenanceStatus.Completed && (j.NextServiceMeter != null || j.IsPreventive))
                .OrderByDescending(j => j.CompletedAt).ThenByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id).FirstOrDefault(),
            Months = db.MaintenanceJobs.Where(j => j.AssetId == x.Id && j.Status == MaintenanceStatus.Completed).OrderByDescending(j => j.CompletedAt).ThenByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id).Select(j => j.ServiceIntervalMonths).FirstOrDefault(),
            ActiveJobId = db.MaintenanceJobs.Where(j => j.AssetId == x.Id && j.Status != MaintenanceStatus.Completed && j.Status != MaintenanceStatus.Cancelled).OrderByDescending(j => j.ReportedAt).Select(j => (Guid?)j.Id).FirstOrDefault()
        }).ToListAsync(token);
        return rows.Select(x => Plan(x.Asset, x.Latest, x.Rules, today) with { BranchName = x.BranchName, ActiveJobId = x.ActiveJobId, ServiceIntervalMonths = x.Months }).ToList();
    }

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
            var latestService = await db.MaintenanceJobs.Where(job => job.AssetId == asset.Id && job.Status == MaintenanceStatus.Completed)
                .OrderByDescending(job => job.CompletedAt).ThenByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id)
                .Select(job => new { job.ServiceIntervalMonths }).FirstOrDefaultAsync(token);
            var meter = await db.MaintenanceJobs.Where(job => job.AssetId == asset.Id && job.Status == MaintenanceStatus.Completed && job.NextServiceMeter.HasValue)
                .OrderByDescending(job => job.CompletedAt).ThenByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id)
                .Select(job => job.NextServiceMeter).FirstOrDefaultAsync(token);
            var meterDue = meter.HasValue && asset.CurrentMeterReading >= meter;
            var dateDue = asset.NextServiceDate.HasValue && asset.NextServiceDate <= today;
            var job = new MaintenanceJob { JobNumber = $"MNT-AUTO-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..29],
                AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = "Preventive maintenance",
                FaultDescription = dateDue && meterDue ? "Date and meter preventive maintenance thresholds reached."
                    : meterDue ? "Meter-based preventive maintenance threshold reached." : "Date-based preventive maintenance interval reached.",
                SourceType = "Preventive", ReportedByName = "System", IsPreventive = true, ServiceIntervalMonths = latestService?.ServiceIntervalMonths, NextServiceDate = null, NextServiceMeter = meter, Status = MaintenanceStatus.Open };
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
        var (configuredInterval, _) = ReadMeterRule(json);
        if (!configuredInterval.HasValue) return false;
        var interval = configuredInterval.Value;
        var latest = await db.MaintenanceJobs.Where(job => job.AssetId == asset.Id && job.Status == MaintenanceStatus.Completed
                && (job.NextServiceMeter.HasValue || job.IsPreventive))
            .OrderByDescending(job => job.CompletedAt).ThenByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id)
            .FirstOrDefaultAsync(token);
        var threshold = Plan(asset, latest, json, LocalDate(DateTimeOffset.UtcNow)).NextServiceMeter!.Value;
        if (asset.CurrentMeterReading < threshold) return false;
        db.MaintenanceJobs.Add(new MaintenanceJob { JobNumber = $"MNT-{Guid.NewGuid():N}"[..29],
            AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = "Preventive meter service", SourceType = "Preventive", ReportedByName = "System", IsPreventive = true,
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
                && (entry.Entity.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled) || !entry.Entity.ReleasedAt.HasValue))
            || await db.MaintenanceJobs.AnyAsync(job => transferring.Contains(job.AssetId)
                && (job.Status != MaintenanceStatus.Completed && job.Status != MaintenanceStatus.Cancelled || job.ReleasedAt == null), token)))
            throw new MaintenanceConflictException("Complete maintenance and pass the safety check before transferring this asset to another branch.");
        var released = db.ChangeTracker.Entries<Asset>().Where(entry => entry.State == EntityState.Modified
            && entry.Property(asset => asset.Status).IsModified
            && entry.Entity.Status is AssetStatus.Available or AssetStatus.Reserved or AssetStatus.Rented)
            .Select(entry => entry.Entity.Id).ToArray();
        if (released.Length == 0) return;
        var closing = db.ChangeTracker.Entries<MaintenanceJob>().Where(entry => entry.State == EntityState.Modified
            && entry.Entity.ReleasedAt.HasValue && entry.Entity.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled).Select(entry => entry.Entity.Id).ToArray();
        var pending = db.ChangeTracker.Entries<MaintenanceJob>().Any(entry => entry.State is EntityState.Added or EntityState.Modified
            && released.Contains(entry.Entity.AssetId) && entry.Entity.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled));
        if (pending || await db.MaintenanceJobs.AnyAsync(job => released.Contains(job.AssetId) && !closing.Contains(job.Id)
            && (job.Status != MaintenanceStatus.Completed && job.Status != MaintenanceStatus.Cancelled || job.ReleasedAt == null), token))
            throw new MaintenanceConflictException("Complete maintenance and pass the return-to-service inspection before releasing this asset.");
    }
}

public sealed class MaintenanceConflictException(string message) : Exception(message);

public sealed record ServicePlan(Guid Id, string AssetNumber, string Name, Guid BranchId, Guid? DivisionId, string BranchName, AssetStatus Status,
    string? MeterUnit, decimal? CurrentMeterReading, DateOnly? NextServiceDate, decimal? NextServiceMeter, decimal? MeterInterval,
    int? ServiceIntervalMonths, string MeterTargetSource, string? RuleWarning, bool IsDue, bool IsOverdue)
{
    public Guid? ActiveJobId { get; init; }
}
