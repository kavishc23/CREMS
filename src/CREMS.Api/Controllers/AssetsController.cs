using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/assets")]
[Authorize(Policy = SystemPolicies.ViewAssets)]
public sealed class AssetsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AssetResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var assets = await db.Assets.AsNoTracking()
            .OrderBy(asset => asset.AssetNumber)
            .Select(asset => new AssetResponse(
                asset.Id, asset.AssetNumber, asset.Name, asset.Type, asset.Status,
                asset.BranchId, asset.Branch!.Name, asset.RegistrationNumber,
                asset.SerialNumber, asset.DailyRate, asset.NextServiceDate, asset.IsActive))
            .ToListAsync(cancellationToken);
        return Ok(assets);
    }

    [HttpPost]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult<AssetResponse>> Create(
        SaveAssetRequest request,
        CancellationToken cancellationToken)
    {
        var assetNumber = request.AssetNumber.Trim().ToUpperInvariant();
        if (await db.Assets.AnyAsync(asset => asset.AssetNumber == assetNumber, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.AssetNumber), "An asset with this number already exists.");
            return ValidationProblem(ModelState);
        }

        var branch = await db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.BranchId && item.IsActive, cancellationToken);
        if (branch is null)
        {
            ModelState.AddModelError(nameof(request.BranchId), "Select an active branch.");
            return ValidationProblem(ModelState);
        }

        var asset = new Asset
        {
            AssetNumber = assetNumber,
            Name = request.Name.Trim(),
            Type = request.Type,
            Status = request.Status,
            BranchId = branch.Id,
            RegistrationNumber = Normalize(request.RegistrationNumber),
            SerialNumber = Normalize(request.SerialNumber),
            DailyRate = request.DailyRate,
            NextServiceDate = request.NextServiceDate,
            IsActive = true,
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetAll), ToResponse(asset, branch.Name));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult<AssetResponse>> Update(
        Guid id,
        SaveAssetRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FindAsync([id], cancellationToken);
        if (asset is null) return NotFound();

        var assetNumber = request.AssetNumber.Trim().ToUpperInvariant();
        if (await db.Assets.AnyAsync(
            other => other.Id != id && other.AssetNumber == assetNumber, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.AssetNumber), "An asset with this number already exists.");
            return ValidationProblem(ModelState);
        }

        var branch = await db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.BranchId && item.IsActive, cancellationToken);
        if (branch is null)
        {
            ModelState.AddModelError(nameof(request.BranchId), "Select an active branch.");
            return ValidationProblem(ModelState);
        }

        asset.AssetNumber = assetNumber;
        asset.Name = request.Name.Trim();
        asset.Type = request.Type;
        asset.Status = request.Status;
        asset.BranchId = branch.Id;
        asset.RegistrationNumber = Normalize(request.RegistrationNumber);
        asset.SerialNumber = Normalize(request.SerialNumber);
        asset.DailyRate = request.DailyRate;
        asset.NextServiceDate = request.NextServiceDate;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(asset, branch.Name));
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> SetStatus(
        Guid id,
        SetAssetStatusRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FindAsync([id], cancellationToken);
        if (asset is null) return NotFound();
        asset.Status = request.Status;
        asset.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AssetResponse ToResponse(Asset asset, string branchName) => new(
        asset.Id, asset.AssetNumber, asset.Name, asset.Type, asset.Status,
        asset.BranchId, branchName, asset.RegistrationNumber, asset.SerialNumber,
        asset.DailyRate, asset.NextServiceDate, asset.IsActive);
}

public sealed record SaveAssetRequest(
    [Required, MaxLength(50)] string AssetNumber,
    [Required, MaxLength(150)] string Name,
    AssetType Type,
    AssetStatus Status,
    Guid BranchId,
    [MaxLength(50)] string? RegistrationNumber,
    [MaxLength(100)] string? SerialNumber,
    [Range(0, 1_000_000)] decimal DailyRate,
    DateOnly? NextServiceDate);

public sealed record SetAssetStatusRequest(AssetStatus Status, bool IsActive);
public sealed record AssetResponse(
    Guid Id, string AssetNumber, string Name, AssetType Type, AssetStatus Status,
    Guid BranchId, string BranchName, string? RegistrationNumber, string? SerialNumber,
    decimal DailyRate, DateOnly? NextServiceDate, bool IsActive);
