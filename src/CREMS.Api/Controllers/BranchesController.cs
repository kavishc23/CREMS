using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/branches")]
[Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class BranchesController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BranchResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.DivisionId.HasValue)) return Forbid();
        var query = db.Branches.AsNoTracking();
        if (!scope.IsAdministrator) query = query.Where(branch => branch.Divisions.Any(link => link.DivisionId == scope.DivisionId && link.IsActive));
        var branches = await query
            .OrderBy(branch => branch.Name)
            .Select(branch => new BranchResponse(
                branch.Id, branch.Code, branch.Name, branch.Address, branch.Phone, branch.IsActive,
                branch.Divisions.Where(link => link.IsActive).Select(link => link.DivisionId).ToList()))
            .ToListAsync(cancellationToken);
        return Ok(branches);
    }

    [HttpPost]
    [Authorize(Policy = SystemPolicies.AdministerSystem)]
    public async Task<ActionResult<BranchResponse>> Create(
        SaveBranchRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Branches.AnyAsync(branch => branch.Code == code, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.Code), "A branch with this code already exists.");
            return ValidationProblem(ModelState);
        }

        var branch = new Branch
        {
            Code = code,
            Name = request.Name.Trim(),
            Address = Normalize(request.Address),
            Phone = Normalize(request.Phone),
            IsActive = true,
        };
        db.Branches.Add(branch);
        foreach (var divisionId in request.DivisionIds ?? [])
            branch.Divisions.Add(new BranchDivision { DivisionId = divisionId, IsActive = true });
        var scope = await staffScope.GetAsync(User);
        if (scope is not null) AuditWriter.Record(db, scope, "Branch created", "Branch", branch.Id,
            $"{branch.Code} — {branch.Name} was created.", branch.Id);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), ToResponse(branch));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult<BranchResponse>> Update(
        Guid id,
        SaveBranchRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasBranchAccess(id)) return Forbid();
        var branch = await db.Branches.FindAsync([id], cancellationToken);
        if (branch is null) return NotFound();

        var code = scope.IsAdministrator ? request.Code.Trim().ToUpperInvariant() : branch.Code;
        if (await db.Branches.AnyAsync(other => other.Id != id && other.Code == code, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.Code), "A branch with this code already exists.");
            return ValidationProblem(ModelState);
        }

        branch.Code = code;
        branch.Name = request.Name.Trim();
        branch.Address = Normalize(request.Address);
        branch.Phone = Normalize(request.Phone);
        if (scope.IsAdministrator && request.DivisionIds is not null)
        {
            var existing = await db.BranchDivisions.Where(x => x.BranchId == id).ToListAsync(cancellationToken);
            foreach (var link in existing) link.IsActive = request.DivisionIds.Contains(link.DivisionId);
            foreach (var divisionId in request.DivisionIds.Where(divisionId => existing.All(x => x.DivisionId != divisionId)))
                db.BranchDivisions.Add(new BranchDivision { BranchId = id, DivisionId = divisionId, IsActive = true });
        }
        AuditWriter.Record(db, scope, "Branch updated", "Branch", branch.Id,
            $"{branch.Code} contact details were updated.", branch.Id);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(branch));
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = SystemPolicies.AdministerSystem)]
    public async Task<ActionResult<BranchResponse>> SetStatus(
        Guid id,
        SetBranchStatusRequest request,
        CancellationToken cancellationToken)
    {
        var branch = await db.Branches.FindAsync([id], cancellationToken);
        if (branch is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null) return Forbid();
        branch.IsActive = request.IsActive;
        AuditWriter.Record(db, scope, "Branch status changed", "Branch", branch.Id,
            $"{branch.Code} active={request.IsActive}.", branch.Id);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(branch));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BranchResponse ToResponse(Branch branch) =>
        new(branch.Id, branch.Code, branch.Name, branch.Address, branch.Phone, branch.IsActive,
            branch.Divisions.Where(x => x.IsActive).Select(x => x.DivisionId).ToList());
}

public sealed record SaveBranchRequest(
    [Required, MaxLength(20)] string Code,
    [Required, MaxLength(150)] string Name,
    [MaxLength(500)] string? Address,
    [MaxLength(50)] string? Phone,
    IReadOnlyCollection<Guid>? DivisionIds = null);
public sealed record SetBranchStatusRequest(bool IsActive);
public sealed record BranchResponse(
    Guid Id, string Code, string Name, string? Address, string? Phone, bool IsActive, IReadOnlyCollection<Guid> DivisionIds);
