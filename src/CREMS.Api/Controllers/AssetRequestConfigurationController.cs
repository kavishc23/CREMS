using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController, Route("api/assets/{assetId:guid}/request-configuration"), Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class AssetRequestConfigurationController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(Guid assetId, CancellationToken token)
    {
        var asset = await db.Assets.AsNoTracking().Include(x => x.Division).Include(x => x.ServiceOffering).Include(x => x.AssetCategory).SingleOrDefaultAsync(x => x.Id == assetId, token);
        if (asset is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();
        return Ok(new { questions = OrganizationConfiguration.Questions(DivisionBookingFlow.For(asset.Division), asset.ServiceOffering, asset.AssetCategory) });
    }
}
