using System.ComponentModel.DataAnnotations;
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
    { var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid(); var query = db.AssetCategories.AsNoTracking().Where(x => x.IsActive && x.Division!.IsActive).AsQueryable(); if (!scope.IsAdministrator) query = query.Where(x => scope.DivisionIds.Contains(x.DivisionId)); else if (divisionId.HasValue) query = query.Where(x => x.DivisionId == divisionId); return Ok(await query.OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name, x.Description, x.DivisionId, x.ServiceOfferingId, x.DefaultMeterType, x.DefaultAssetType, x.PersonnelRequirement, x.IsActive, AttributeDefinitions = x.AttributeDefinitions.OrderBy(a => a.DisplayOrder).Select(a => new { a.Code, a.Name, a.DataType, a.Unit, a.IsRequired, a.IsSearchable, a.IsCustomerVisible, a.IsReportable, a.DisplayOrder, a.OptionsJson }).ToList() }).ToListAsync(token)); }

    [HttpPost, Authorize(Policy = SystemPermissions.AssetCategoriesConfigure)]
    public async Task<ActionResult> Create(CategoryRequest request, CancellationToken token)
    { var validation = await ValidateCategory(request, null, token); if (validation is not null) return validation; var item = new AssetCategory { DivisionId = request.DivisionId, ServiceOfferingId = request.ServiceOfferingId, Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim(), Description = Clean(request.Description), DefaultMeterType = Clean(request.DefaultMeterType), DefaultAssetType = request.DefaultAssetType, PersonnelRequirement = request.PersonnelRequirement }; db.AssetCategories.Add(item); await db.SaveChangesAsync(token); return Ok(item); }

    [HttpPut("{id:guid}"), Authorize(Policy = SystemPermissions.AssetCategoriesConfigure)]
    public async Task<ActionResult> Update(Guid id, CategoryRequest request, CancellationToken token)
    { var item = await db.AssetCategories.FindAsync([id], token); if (item is null) return NotFound(); if (item.DivisionId != request.DivisionId) return BadRequest(new { message = "An existing category cannot be moved to another division." }); var validation = await ValidateCategory(request, id, token); if (validation is not null) return validation; item.UpdatedAt = DateTimeOffset.UtcNow; item.ServiceOfferingId = request.ServiceOfferingId; item.Code = request.Code.Trim().ToUpperInvariant(); item.Name = request.Name.Trim(); item.Description = Clean(request.Description); item.DefaultMeterType = Clean(request.DefaultMeterType); item.DefaultAssetType = request.DefaultAssetType ?? item.DefaultAssetType; item.PersonnelRequirement = request.PersonnelRequirement; item.IsActive = request.IsActive; await db.SaveChangesAsync(token); return Ok(item); }

    [HttpPut("{id:guid}/attributes"), Authorize(Policy = SystemPermissions.AssetCategoriesConfigure)]
    public async Task<ActionResult> Attributes(Guid id, IReadOnlyList<AttributeRequest> request, CancellationToken token)
    {
        var category = await db.AssetCategories.SingleOrDefaultAsync(x => x.Id == id, token);
        if (category is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasDivisionAccess(category.DivisionId)) return Forbid();
        if (request.Count > 80 || request.Any(x => string.IsNullOrWhiteSpace(x.Code) || x.Code.Length > 40 || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 150 || !Enum.IsDefined(x.DataType)) || request.GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) return BadRequest(new { message = "Asset fields require unique codes, names and supported types." });
        foreach (var input in request.Where(x => x.DataType == AttributeDataType.Choice))
        {
            try { var options = System.Text.Json.JsonSerializer.Deserialize<string[]>(input.OptionsJson ?? "[]"); if (options is not { Length: > 0 and <= 100 } || options.Any(string.IsNullOrWhiteSpace) || options.Distinct().Count() != options.Length) return BadRequest(new { message = $"Provide unique choices for {input.Name}." }); }
            catch (System.Text.Json.JsonException) { return BadRequest(new { message = $"Check the choices for {input.Name}." }); }
        }
        var current = await db.AssetAttributeDefinitions.Where(x => x.AssetCategoryId == id).ToListAsync(token);
        var removed = current.Where(x => !request.Any(a => a.Code.Trim().Equals(x.Code, StringComparison.OrdinalIgnoreCase))).ToArray();
        var removedIds = removed.Select(x => x.Id).ToArray();
        if (await db.AssetAttributeValues.AnyAsync(x => removedIds.Contains(x.AttributeDefinitionId), token)) return Conflict(new { message = "A field contains asset data and cannot be removed. Keep it to preserve existing records." });
        foreach (var input in request)
        {
            var code = input.Code.Trim().ToUpperInvariant();
            var item = current.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (item is not null && (item.DataType != input.DataType || item.OptionsJson != (input.OptionsJson ?? "[]")) && await db.AssetAttributeValues.AnyAsync(x => x.AttributeDefinitionId == item.Id, token)) return Conflict(new { message = $"{item.Name} contains data. Keep its type and choices or add a new field." });
            item ??= new AssetAttributeDefinition { AssetCategoryId = id, Code = code, Name = input.Name.Trim() };
            if (!current.Contains(item)) db.AssetAttributeDefinitions.Add(item);
            item.Name = input.Name.Trim(); item.DataType = input.DataType; item.Unit = Clean(input.Unit); item.IsRequired = input.IsRequired; item.IsSearchable = input.IsSearchable; item.IsCustomerVisible = input.IsCustomerVisible; item.IsReportable = input.IsReportable; item.DisplayOrder = input.DisplayOrder; item.OptionsJson = input.OptionsJson ?? "[]";
        }
        db.AssetAttributeDefinitions.RemoveRange(removed); category.UpdatedAt = DateTimeOffset.UtcNow;
        AuditWriter.Record(db, scope, "Asset fields configured", nameof(AssetCategory), id, $"{category.Name} field definitions updated.", null);
        await db.SaveChangesAsync(token);
        return NoContent();
    }
    private async Task<ActionResult?> ValidateCategory(CategoryRequest request, Guid? id, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.IsAdministrator && !scope.DivisionIds.Contains(request.DivisionId)) return Forbid();
        if (!await db.Divisions.AnyAsync(x => x.Id == request.DivisionId && x.IsActive, token)) return BadRequest(new { message = "Select an active division." });
        if (request.ServiceOfferingId.HasValue && !await db.ServiceOfferings.AnyAsync(x => x.Id == request.ServiceOfferingId && x.DivisionId == request.DivisionId && x.IsActive, token)) return BadRequest(new { message = "Select an active service belonging to this division." });
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.AssetCategories.AnyAsync(x => x.Id != id && x.DivisionId == request.DivisionId && x.Code == code, token)) return Conflict(new { message = "This category code is already used in the division." });
        return null;
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
public sealed record CategoryRequest(Guid DivisionId, Guid? ServiceOfferingId, [Required, MaxLength(40)] string Code, [Required, MaxLength(150)] string Name, string? Description, string? DefaultMeterType, [EnumDataType(typeof(PersonnelRequirement))] PersonnelRequirement PersonnelRequirement, bool IsActive = true, [EnumDataType(typeof(CREMS.Api.Domain.Assets.AssetType))] CREMS.Api.Domain.Assets.AssetType? DefaultAssetType = null);
public sealed record AttributeRequest(string Code, string Name, AttributeDataType DataType, string? Unit, bool IsRequired, bool IsSearchable, bool IsCustomerVisible, bool IsReportable, int DisplayOrder, string? OptionsJson);
