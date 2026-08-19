using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public sealed class RentalPricingService(ApplicationDbContext db)
{
    public async Task<PricingResult> CalculateAsync(Asset asset, Guid? customerId, DateTimeOffset start, DateTimeOffset end, decimal operatorHours, decimal deliveryKilometres, Guid? deliveryZoneId, bool taxInclusive, decimal taxRate, CancellationToken token)
    {
        if (end <= start) throw new ArgumentException("The rental end must be after its start.");
        var customerType = customerId.HasValue ? await db.Customers.Where(x => x.Id == customerId).Select(x => (CustomerType?)x.Type).FirstOrDefaultAsync(token) : null;
        var now = DateTimeOffset.UtcNow;
        var rules = await db.PricingRules.AsNoTracking().Where(x => x.IsActive && (x.EffectiveFrom == null || x.EffectiveFrom <= now) && (x.EffectiveTo == null || x.EffectiveTo >= now) && (x.AssetType == null || x.AssetType == asset.Type.ToString()) && (x.DivisionId == null || x.DivisionId == asset.DivisionId) && (x.ServiceOfferingId == null || x.ServiceOfferingId == asset.ServiceOfferingId) && (x.AssetCategoryId == null || x.AssetCategoryId == asset.AssetCategoryId) && (x.AssetId == null || x.AssetId == asset.Id) && (x.BranchId == null || x.BranchId == asset.BranchId) && (x.CustomerId == null || x.CustomerId == customerId) && (x.CustomerType == null || x.CustomerType == customerType.ToString())).ToListAsync(token);
        // Deterministic precedence: negotiated customer contract, branch override, division,
        // asset/category/service rate, then the asset's standard daily rate.
        var rule = rules.OrderByDescending(x => x.CustomerId.HasValue)
            .ThenByDescending(x => x.BranchId.HasValue)
            .ThenByDescending(x => x.DivisionId.HasValue)
            .ThenByDescending(x => x.AssetId.HasValue)
            .ThenByDescending(x => x.AssetCategoryId.HasValue)
            .ThenByDescending(x => x.ServiceOfferingId.HasValue)
            .FirstOrDefault();
        var period = rule?.Period ?? RatePeriod.Daily; var rate = rule?.Rate ?? asset.DailyRate; var rawDuration = period switch { RatePeriod.Hourly => (decimal)(end-start).TotalHours, RatePeriod.Weekly => (decimal)(end-start).TotalDays/7m, RatePeriod.Monthly => (decimal)(end-start).TotalDays/30m, _ => (decimal)(end-start).TotalDays }; var units = Math.Max(rule?.MinimumDuration ?? 1, (int)Math.Ceiling(rawDuration));
        var weekendDays = Enumerable.Range(0, Math.Max(1,(int)Math.Ceiling((end-start).TotalDays))).Count(i => start.AddDays(i).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday); var weekendAdjustment = period == RatePeriod.Daily && rule is not null ? weekendDays * rate * (rule.WeekendMultiplier-1) : 0;
        var operatorDefinition = await db.ChargeDefinitions.AsNoTracking().Where(x => x.IsActive && x.DivisionId == asset.DivisionId && x.Category == Domain.Common.ChargeCategory.Operator).OrderByDescending(x => x.ServiceOfferingId == asset.ServiceOfferingId).FirstOrDefaultAsync(token);
        var zone = deliveryZoneId.HasValue ? await db.DeliveryZones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == deliveryZoneId && x.IsActive, token) : null;
        var holidays=await db.BranchCalendarExceptions.AsNoTracking().CountAsync(x=>x.BranchId==asset.BranchId&&!x.IsClosed&&x.Date>=DateOnly.FromDateTime(start.Date)&&x.Date<=DateOnly.FromDateTime(end.Date),token);
        var holidayAdjustment=rule is null?0:holidays*rate*(rule.HolidayMultiplier-1);
        var baseHire = units*rate+weekendAdjustment+holidayAdjustment; var operatorCharge=operatorHours*(operatorDefinition?.DefaultSellingRate??0); var deliveryCharge=zone is null?0:zone.BaseCharge+deliveryKilometres*zone.ChargePerKilometre; var subtotal=baseHire+operatorCharge+deliveryCharge; var tax=taxInclusive?subtotal-subtotal/(1+taxRate/100m):subtotal*taxRate/100m;
        return new PricingResult(rule?.Id, rule?.Name??"Standard asset rate", period, units, rate, rule?.MinimumDuration??1, rule?.IncludedUsage??0, rule?.ExcessUsageRate??0, baseHire, weekendAdjustment, holidayAdjustment, operatorCharge, deliveryCharge, subtotal, tax, taxInclusive?subtotal:subtotal+tax);
    }
}
public sealed record PricingResult(Guid? PricingRuleId, string PricingSource, RatePeriod Period, int Units, decimal Rate, int MinimumDuration, decimal IncludedUsage, decimal ExcessUsageRate, decimal BaseHire, decimal WeekendAdjustment, decimal HolidayAdjustment, decimal OperatorCharge, decimal DeliveryCharge, decimal Subtotal, decimal Tax, decimal Total);
