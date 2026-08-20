using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController, Route("api/asset-categories"), Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class AssetCategoriesController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get([FromQuery] Guid? divisionId, CancellationToken token)
    { var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid(); var query = db.AssetCategories.AsNoTracking().Include(x => x.AttributeDefinitions).Where(x => x.IsActive && x.Division!.IsActive).AsQueryable(); if (!scope.IsAdministrator) query = query.Where(x => scope.DivisionIds.Contains(x.DivisionId)); else if (divisionId.HasValue) query = query.Where(x => x.DivisionId == divisionId); return Ok(await query.OrderBy(x => x.Name).ToListAsync(token)); }

    [HttpPost, Authorize(Policy = SystemPermissions.AssetCategoriesConfigure)]
    public async Task<ActionResult> Create(CategoryRequest request, CancellationToken token)
    { if (!await db.Divisions.AnyAsync(x => x.Id == request.DivisionId && x.IsActive, token)) return BadRequest(); var item = new AssetCategory { DivisionId = request.DivisionId, ServiceOfferingId = request.ServiceOfferingId, Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim(), Description = Clean(request.Description), DefaultMeterType = Clean(request.DefaultMeterType), PersonnelRequirement = request.PersonnelRequirement }; db.AssetCategories.Add(item); await db.SaveChangesAsync(token); return Ok(item); }

    [HttpPut("{id:guid}"), Authorize(Policy = SystemPermissions.AssetCategoriesConfigure)]
    public async Task<ActionResult> Update(Guid id, CategoryRequest request, CancellationToken token)
    { var item = await db.AssetCategories.FindAsync([id], token); if (item is null) return NotFound(); item.ServiceOfferingId = request.ServiceOfferingId; item.Code = request.Code.Trim().ToUpperInvariant(); item.Name = request.Name.Trim(); item.Description = Clean(request.Description); item.DefaultMeterType = Clean(request.DefaultMeterType); item.PersonnelRequirement = request.PersonnelRequirement; item.IsActive = request.IsActive; await db.SaveChangesAsync(token); return Ok(item); }

    [HttpPut("{id:guid}/attributes"), Authorize(Policy = SystemPermissions.AssetCategoriesConfigure)]
    public async Task<ActionResult> Attributes(Guid id, IReadOnlyList<AttributeRequest> request, CancellationToken token)
    { if (!await db.AssetCategories.AnyAsync(x => x.Id == id, token)) return NotFound(); var current = await db.AssetAttributeDefinitions.Where(x => x.AssetCategoryId == id).ToListAsync(token); var incomingCodes=request.Select(x=>x.Code.Trim().ToUpperInvariant()).ToHashSet(); db.AssetAttributeDefinitions.RemoveRange(current.Where(x=>!incomingCodes.Contains(x.Code))); foreach (var input in request) { var item = current.FirstOrDefault(x => x.Code == input.Code.Trim().ToUpperInvariant()); if (item is null) { item = new AssetAttributeDefinition { AssetCategoryId = id, Code = input.Code.Trim().ToUpperInvariant(), Name = input.Name.Trim() }; db.AssetAttributeDefinitions.Add(item); } item.Name = input.Name.Trim(); item.DataType = input.DataType; item.Unit = Clean(input.Unit); item.IsRequired = input.IsRequired; item.IsSearchable = input.IsSearchable; item.IsCustomerVisible=input.IsCustomerVisible; item.IsReportable=input.IsReportable; item.DisplayOrder = input.DisplayOrder; item.OptionsJson = input.OptionsJson ?? "[]"; } await db.SaveChangesAsync(token); return NoContent(); }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
public sealed record CategoryRequest(Guid DivisionId, Guid? ServiceOfferingId, string Code, string Name, string? Description, string? DefaultMeterType, PersonnelRequirement PersonnelRequirement, bool IsActive = true);
public sealed record AttributeRequest(string Code, string Name, AttributeDataType DataType, string? Unit, bool IsRequired, bool IsSearchable, bool IsCustomerVisible, bool IsReportable, int DisplayOrder, string? OptionsJson);
