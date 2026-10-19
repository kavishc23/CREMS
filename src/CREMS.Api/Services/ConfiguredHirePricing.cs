using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using Microsoft.EntityFrameworkCore;
namespace CREMS.Api.Services;

public sealed record HireRate(decimal Rate, ChargeUnit Unit, bool IsTaxable, string Source);
public static class ConfiguredHirePricing
{
    public static async Task<HireRate> Resolve(ApplicationDbContext db, Asset asset, CancellationToken token)
    {
        if(!asset.UseServiceHireRate) return new(asset.DailyRate,asset.HireUnit,true,"Asset rate");
        var rates=await db.ChargeDefinitions.AsNoTracking().Where(x=>x.IsActive&&x.Category==ChargeCategory.BaseHire&&x.DivisionId==asset.DivisionId&&(x.ServiceOfferingId==null||x.ServiceOfferingId==asset.ServiceOfferingId)).ToListAsync(token);
        var specific=rates.Where(x=>x.ServiceOfferingId.HasValue&&x.ServiceOfferingId==asset.ServiceOfferingId).ToList();
        var applicable=specific.Count>0?specific:rates.Where(x=>x.ServiceOfferingId==null).ToList();
        if(applicable.Count!=1) throw new InvalidOperationException("Configure exactly one active base-hire charge for this service (or its division), or select the asset's own rate.");
        var rate=applicable.Single();return new(rate.DefaultSellingRate,rate.Unit,rate.IsTaxable,rate.Name);
    }
}
