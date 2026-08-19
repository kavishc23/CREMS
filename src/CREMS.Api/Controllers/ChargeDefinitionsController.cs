using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/charge-definitions")]
[Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class ChargeDefinitionsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get([FromQuery] Guid? divisionId, [FromQuery] Guid? serviceOfferingId, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        var effectiveDivision = scope.IsAdministrator ? divisionId : scope.DivisionId;
        var query = db.ChargeDefinitions.AsNoTracking().AsQueryable();
        // Administrators need to see inactive definitions so they can restore them.
        // Operational users only receive charges that can currently be applied.
        if (!scope.IsAdministrator) query = query.Where(x => x.IsActive);
        if (effectiveDivision.HasValue) query = query.Where(x => x.DivisionId == effectiveDivision);
        if (serviceOfferingId.HasValue) query = query.Where(x => x.ServiceOfferingId == null || x.ServiceOfferingId == serviceOfferingId);
        return Ok(await query.OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync(token));
    }

    [HttpPost]
    [Authorize(Policy = SystemPolicies.AdministerSystem)]
    public async Task<ActionResult> Create(SaveChargeDefinitionRequest request, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        if (!await ValidScope(request.DivisionId, request.ServiceOfferingId, token)) return ValidationProblem("The service must belong to the selected division.");
        var code = request.Code.Trim().ToUpperInvariant(); if (await db.ChargeDefinitions.AnyAsync(x => x.DivisionId == request.DivisionId && x.Code == code, token)) return Conflict(new { message = "This charge code already exists in the division." });
        var item = Map(new ChargeDefinition { Code = code, Name = request.Name.Trim(), DivisionId = request.DivisionId }, request); db.ChargeDefinitions.Add(item);
        AuditWriter.Record(db, scope, "Charge definition created", nameof(ChargeDefinition), item.Id, $"{item.Name} ({item.Unit}) added.", null); await db.SaveChangesAsync(token); return Ok(item);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = SystemPolicies.AdministerSystem)]
    public async Task<ActionResult> Update(Guid id, SaveChargeDefinitionRequest request, CancellationToken token)
    {
        var item = await db.ChargeDefinitions.FirstOrDefaultAsync(x => x.Id == id, token); if (item is null) return NotFound();
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid(); if (!await ValidScope(request.DivisionId, request.ServiceOfferingId, token)) return BadRequest();
        Map(item, request); item.UpdatedAt = DateTimeOffset.UtcNow; AuditWriter.Record(db, scope, "Charge definition updated", nameof(ChargeDefinition), item.Id, $"{item.Name} rates or applicability changed.", null); await db.SaveChangesAsync(token); return Ok(item);
    }

    private async Task<bool> ValidScope(Guid divisionId, Guid? serviceId, CancellationToken token) => await db.Divisions.AnyAsync(x => x.Id == divisionId && x.IsActive, token) && (!serviceId.HasValue || await db.ServiceOfferings.AnyAsync(x => x.Id == serviceId && x.DivisionId == divisionId, token));
    private static ChargeDefinition Map(ChargeDefinition item, SaveChargeDefinitionRequest request) { item.Name = request.Name.Trim(); item.DivisionId = request.DivisionId; item.ServiceOfferingId = request.ServiceOfferingId; item.Category = request.Category; item.Unit = request.Unit; item.DefaultSellingRate = request.DefaultSellingRate; item.DefaultCostRate = request.DefaultCostRate; item.IsTaxable = request.IsTaxable; item.IsRequired = request.IsRequired; item.IsCustomerVisible = request.IsCustomerVisible; item.IsActive = request.IsActive; return item; }
}

public sealed record SaveChargeDefinitionRequest(Guid DivisionId, Guid? ServiceOfferingId, [Required, MaxLength(50)] string Code, [Required, MaxLength(150)] string Name, ChargeCategory Category, ChargeUnit Unit, [Range(0, 100000000)] decimal DefaultSellingRate, [Range(0, 100000000)] decimal DefaultCostRate, bool IsTaxable, bool IsRequired, bool IsCustomerVisible, bool IsActive);
