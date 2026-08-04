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
[Authorize(Policy = SystemPolicies.ManageBranch)]
public sealed class BranchesController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BranchResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var branches = await db.Branches.AsNoTracking()
            .OrderBy(branch => branch.Name)
            .Select(branch => new BranchResponse(
                branch.Id, branch.Code, branch.Name, branch.Address, branch.Phone, branch.IsActive))
            .ToListAsync(cancellationToken);
        return Ok(branches);
    }

    [HttpPost]
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
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), ToResponse(branch));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BranchResponse>> Update(
        Guid id,
        SaveBranchRequest request,
        CancellationToken cancellationToken)
    {
        var branch = await db.Branches.FindAsync([id], cancellationToken);
        if (branch is null) return NotFound();

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Branches.AnyAsync(other => other.Id != id && other.Code == code, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.Code), "A branch with this code already exists.");
            return ValidationProblem(ModelState);
        }

        branch.Code = code;
        branch.Name = request.Name.Trim();
        branch.Address = Normalize(request.Address);
        branch.Phone = Normalize(request.Phone);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(branch));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<BranchResponse>> SetStatus(
        Guid id,
        SetBranchStatusRequest request,
        CancellationToken cancellationToken)
    {
        var branch = await db.Branches.FindAsync([id], cancellationToken);
        if (branch is null) return NotFound();
        branch.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(branch));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BranchResponse ToResponse(Branch branch) =>
        new(branch.Id, branch.Code, branch.Name, branch.Address, branch.Phone, branch.IsActive);
}

public sealed record SaveBranchRequest(
    [Required, MaxLength(20)] string Code,
    [Required, MaxLength(150)] string Name,
    [MaxLength(500)] string? Address,
    [MaxLength(50)] string? Phone);
public sealed record SetBranchStatusRequest(bool IsActive);
public sealed record BranchResponse(
    Guid Id, string Code, string Name, string? Address, string? Phone, bool IsActive);
