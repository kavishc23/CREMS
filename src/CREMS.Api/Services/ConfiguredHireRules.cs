using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class ConfiguredHireRules
{
    public static async Task<BookingConfigurationSnapshot> Capture(ApplicationDbContext db, Asset asset,
        IReadOnlyDictionary<string, string>? answers, CancellationToken token)
    {
        var service = asset.ServiceOffering;
        if (service is null && asset.ServiceOfferingId.HasValue) service = await db.ServiceOfferings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == asset.ServiceOfferingId, token);
        var division = asset.Division;
        if (division is null && asset.DivisionId.HasValue) division = await db.Divisions.AsNoTracking().SingleAsync(x => x.Id == asset.DivisionId, token);
        var category = asset.AssetCategory;
        if (category is null && asset.AssetCategoryId.HasValue) category = await db.AssetCategories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == asset.AssetCategoryId, token);
        var templates = await db.InspectionTemplates.AsNoTracking().Where(x => x.AssetCategoryId == asset.AssetCategoryId && x.IsActive && (x.Stage == InspectionStage.PreHire || x.Stage == InspectionStage.PostHire))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new InspectionTemplateSnapshot(x.Id, x.Name, x.Stage, x.ChecklistJson)).ToListAsync(token);
        return new(OrganizationConfiguration.Questions(DivisionBookingFlow.For(division), service, category),
            answers?.ToDictionary(x => x.Key, x => x.Value.Trim()) ?? new Dictionary<string, string>(), templates,
            !string.IsNullOrWhiteSpace(category?.DefaultMeterType) || (OrganizationConfiguration.Flag(service?.InspectionRequirementsJson, "tracksMeter") ?? (!string.IsNullOrWhiteSpace(service?.MeterType) || AssetCategoryPolicy.TracksMeter(asset))),
            OrganizationConfiguration.Flag(service?.InspectionRequirementsJson, "tracksFuel") ?? AssetCategoryPolicy.TracksFuel(asset),
            category?.DefaultMeterType ?? service?.MeterType ?? asset.MeterUnit, DateTimeOffset.UtcNow);
    }

    public static async Task<string?> EffectiveMeterUnit(ApplicationDbContext db, Asset asset, CancellationToken token)
    {
        if (asset.AssetCategoryId is { } categoryId)
        {
            var categoryUnit = await db.AssetCategories.AsNoTracking().Where(x => x.Id == categoryId).Select(x => x.DefaultMeterType).FirstOrDefaultAsync(token);
            if (!string.IsNullOrWhiteSpace(categoryUnit)) return categoryUnit.Trim();
        }
        if (asset.ServiceOfferingId is { } serviceId)
        {
            var serviceUnit = await db.ServiceOfferings.AsNoTracking().Where(x => x.Id == serviceId).Select(x => x.MeterType).FirstOrDefaultAsync(token);
            if (!string.IsNullOrWhiteSpace(serviceUnit)) return serviceUnit.Trim();
        }
        return asset.MeterUnit;
    }

    public static async Task<BookingConfigurationSnapshot?> Resolve(ApplicationDbContext db, Booking booking, CancellationToken token)
    {
        var saved = OrganizationConfiguration.Read<BookingConfigurationSnapshot>(booking.ConfigurationSnapshotJson);
        var asset = booking.Items.FirstOrDefault()?.Asset;
        var resolved = saved ?? (asset is null ? null : await Capture(db, asset, null, token));
        if (resolved is not null && asset is not null && (asset.Type is AssetType.Scaffolding or AssetType.PortableSanitation or AssetType.WasteContainer or AssetType.PropertyUnit || AssetCategoryPolicy.Code(asset) is "SCAFFOLD" or "PORTABLE_TOILET" or "BIG_BIN" or "PROPERTY_UNIT")) return resolved with { TracksFuel = false, TracksMeter = false, MeterUnit = null };
        return resolved;
    }

    public static async Task<string?> ValidateInspection(ApplicationDbContext db, Booking booking, InspectionStage stage,
        IReadOnlyList<string>? passed, IReadOnlyList<string>? failed, decimal? meter, decimal? fuel,
        bool preparation, CancellationToken token)
    {
        var config = await Resolve(db, booking, token);
        var template = config?.Templates.FirstOrDefault(t => t.Stage == stage);
        if (template is not null)
        {
            if (OrganizationConfiguration.ValidateChecklist(template.ChecklistJson) is not null) return "The configured checklist is invalid. Ask an administrator to correct the category template.";
            var expected = OrganizationConfiguration.Checklist(template.ChecklistJson);
            if (expected.Any(label => passed?.Contains(label) != true && (stage == InspectionStage.PreHire && !preparation || failed?.Contains(label) != true))) return "Record a result for every configured inspection check.";
            if (stage == InspectionStage.PreHire && !preparation && expected.Any(label => failed?.Contains(label) == true)) return "Resolve every failed inspection check before handing over the asset.";
        }
        var asset = booking.Items.FirstOrDefault()?.Asset;
        var serviceRules = asset?.ServiceOffering?.InspectionRequirementsJson;
        if (serviceRules is null && asset?.ServiceOfferingId is { } serviceId)
            serviceRules = await db.ServiceOfferings.Where(x => x.Id == serviceId).Select(x => x.InspectionRequirementsJson).FirstOrDefaultAsync(token);
        // Historical bookings without a snapshot retain their legacy optional readings unless an explicit rule exists.
        var enforceReadings = OrganizationConfiguration.Read<BookingConfigurationSnapshot>(booking.ConfigurationSnapshotJson) is not null ||
            OrganizationConfiguration.Flag(serviceRules, "tracksMeter").HasValue || OrganizationConfiguration.Flag(serviceRules, "tracksFuel").HasValue;
        if (enforceReadings && config?.TracksMeter == true && !meter.HasValue) return "Record the asset's required usage meter reading.";
        if (enforceReadings && config?.TracksFuel == true && !fuel.HasValue) return "Record the asset's required fuel or battery level.";
        if (meter < 0 || fuel is < 0 or > 100) return "Check the meter reading and fuel or battery level.";
        return null;
    }

    public static async Task CaptureIfMissing(ApplicationDbContext db, Booking booking, CancellationToken token)
    {
        if (OrganizationConfiguration.Read<BookingConfigurationSnapshot>(booking.ConfigurationSnapshotJson) is not null) return;
        var snapshot = await Resolve(db, booking, token);
        if (snapshot is not null) booking.ConfigurationSnapshotJson = JsonSerializer.Serialize(snapshot, OrganizationConfiguration.Json);
    }
}
