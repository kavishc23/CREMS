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
                branch.Divisions.Where(link => link.IsActive).Select(link => link.DivisionId).ToList(), branch.PostalAddress, branch.Email, branch.Latitude, branch.Longitude, branch.BranchManagerUserId, branch.PickupInstructions, branch.ReturnInstructions, branch.DeliveryCoverage, branch.IsPublic))
            .ToListAsync(cancellationToken);
        return Ok(branches);
    }

    [HttpPost]
    [Authorize(Policy = SystemPermissions.BranchesConfigure)]
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
            PostalAddress = Normalize(request.PostalAddress), Email = Normalize(request.Email)?.ToLowerInvariant(), Latitude = request.Latitude, Longitude = request.Longitude, BranchManagerUserId = request.BranchManagerUserId, PickupInstructions = Normalize(request.PickupInstructions), ReturnInstructions = Normalize(request.ReturnInstructions), DeliveryCoverage = Normalize(request.DeliveryCoverage), IsPublic = request.IsPublic,
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
    [Authorize(Policy = SystemPermissions.BranchesConfigure)]
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
        branch.PostalAddress = Normalize(request.PostalAddress); branch.Email = Normalize(request.Email)?.ToLowerInvariant(); branch.Latitude = request.Latitude; branch.Longitude = request.Longitude; branch.BranchManagerUserId = request.BranchManagerUserId; branch.PickupInstructions = Normalize(request.PickupInstructions); branch.ReturnInstructions = Normalize(request.ReturnInstructions); branch.DeliveryCoverage = Normalize(request.DeliveryCoverage); branch.IsPublic = request.IsPublic;
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
    [Authorize(Policy = SystemPermissions.BranchesConfigure)]
    public async Task<ActionResult<BranchResponse>> SetStatus(
        Guid id,
        SetBranchStatusRequest request,
        CancellationToken cancellationToken)
    {
        var branch = await db.Branches.FindAsync([id], cancellationToken);
        if (branch is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null) return Forbid();
        if (!request.IsActive && (await db.Bookings.AnyAsync(x => x.BranchId == id && x.Status != Domain.Rentals.BookingStatus.Completed && x.Status != Domain.Rentals.BookingStatus.Cancelled && x.Status != Domain.Rentals.BookingStatus.Expired, cancellationToken) || await db.Assets.AnyAsync(x => x.BranchId == id && x.IsActive && x.Status != Domain.Assets.AssetStatus.Retired, cancellationToken))) return Conflict(new { message = "Close active rentals and transfer or retire all assets before deactivating this branch." });
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
            branch.Divisions.Where(x => x.IsActive).Select(x => x.DivisionId).ToList(), branch.PostalAddress, branch.Email, branch.Latitude, branch.Longitude, branch.BranchManagerUserId, branch.PickupInstructions, branch.ReturnInstructions, branch.DeliveryCoverage, branch.IsPublic);

    [HttpGet("{id:guid}/configuration")]
    [Authorize(Policy = SystemPermissions.BranchesConfigure)]
    public async Task<ActionResult> Configuration(Guid id, CancellationToken token)
    { var scope = await staffScope.GetAsync(User); if (scope is null || !scope.HasBranchAccess(id)) return Forbid(); return Ok(new { periods = await db.BranchOperatingPeriods.AsNoTracking().Where(x => x.BranchId == id).OrderBy(x => x.DayOfWeek).ToListAsync(token), exceptions = await db.BranchCalendarExceptions.AsNoTracking().Where(x => x.BranchId == id && x.Date >= DateOnly.FromDateTime(DateTime.UtcNow)).OrderBy(x => x.Date).ToListAsync(token), services = await db.BranchDivisionServices.AsNoTracking().Where(x => x.BranchId == id).ToListAsync(token), divisions = await db.BranchDivisions.AsNoTracking().Where(x => x.BranchId == id).ToListAsync(token) }); }

    [HttpPut("{id:guid}/calendar")]
    [Authorize(Policy = SystemPermissions.BranchesConfigure)]
    public async Task<ActionResult> SaveCalendar(Guid id, CalendarRequest request, CancellationToken token)
    { var scope = await staffScope.GetAsync(User); if (scope is null || !scope.HasBranchAccess(id)) return Forbid(); db.BranchOperatingPeriods.RemoveRange(await db.BranchOperatingPeriods.Where(x => x.BranchId == id).ToListAsync(token)); db.BranchCalendarExceptions.RemoveRange(await db.BranchCalendarExceptions.Where(x => x.BranchId == id).ToListAsync(token)); foreach (var x in request.Periods) db.BranchOperatingPeriods.Add(new BranchOperatingPeriod { BranchId = id, DayOfWeek = x.DayOfWeek, OpensAt = x.OpensAt, ClosesAt = x.ClosesAt, PickupCutoff = x.PickupCutoff, ReturnCutoff = x.ReturnCutoff, IsClosed = x.IsClosed, AfterHoursCharge = x.AfterHoursCharge }); foreach (var x in request.Exceptions) db.BranchCalendarExceptions.Add(new BranchCalendarException { BranchId = id, Date = x.Date, Name = x.Name.Trim(), IsClosed = x.IsClosed, OpensAt = x.OpensAt, ClosesAt = x.ClosesAt, AfterHoursCharge = x.AfterHoursCharge }); await db.SaveChangesAsync(token); return NoContent(); }

    [HttpPut("{id:guid}/services")]
    [Authorize(Policy = SystemPermissions.BranchesConfigure)]
    public async Task<ActionResult> SaveServices(Guid id, IReadOnlyList<BranchServiceRequest> items, CancellationToken token)
    { var scope = await staffScope.GetAsync(User); if (scope is null || !scope.HasBranchAccess(id)) return Forbid(); var existing = await db.BranchDivisionServices.Where(x => x.BranchId == id).ToListAsync(token); db.BranchDivisionServices.RemoveRange(existing); foreach (var x in items) db.BranchDivisionServices.Add(new BranchDivisionService { BranchId = id, DivisionId = x.DivisionId, ServiceOfferingId = x.ServiceOfferingId, IsActive = x.IsActive, IsBookable = x.IsBookable }); await db.SaveChangesAsync(token); return NoContent(); }

    [HttpPut("{branchId:guid}/divisions/{divisionId:guid}")]
    [Authorize(Policy = SystemPermissions.BranchesConfigure)]
    public async Task<ActionResult> SaveDivisionConfiguration(Guid branchId, Guid divisionId, BranchDivisionConfigurationRequest request, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasBranchAccess(branchId) || !scope.HasDivisionAccess(divisionId)) return Forbid();
        var link = await db.BranchDivisions.SingleOrDefaultAsync(x => x.BranchId == branchId && x.DivisionId == divisionId, token);
        if (link is null) return NotFound(new { message = "This division does not operate at the selected branch." });
        link.IsActive = request.IsActive;
        link.OpenedOn = request.OpenedOn;
        link.LocalContactEmail = Normalize(request.LocalContactEmail)?.ToLowerInvariant();
        link.LocalContactPhone = Normalize(request.LocalContactPhone);
        link.DivisionManagerUserId = request.DivisionManagerUserId;
        link.AcceptsBookings = request.AcceptsBookings;
        link.HasMaintenanceCapability = request.HasMaintenanceCapability;
        link.DefaultApprovalWorkflowId = request.DefaultApprovalWorkflowId;
        link.LocalTermsJson = string.IsNullOrWhiteSpace(request.LocalTermsJson) ? "{}" : request.LocalTermsJson;
        link.PricingOverridesJson = string.IsNullOrWhiteSpace(request.PricingOverridesJson) ? "{}" : request.PricingOverridesJson;
        AuditWriter.Record(db, scope, "Branch division configured", "BranchDivision", branchId,
            $"Division {divisionId} configuration was updated for branch {branchId}.", branchId,
            newValues: $"{{\"divisionId\":\"{divisionId}\"}}");
        await db.SaveChangesAsync(token);
        return NoContent();
    }
}

public sealed record SaveBranchRequest(
    [Required, MaxLength(20)] string Code,
    [Required, MaxLength(150)] string Name,
    [MaxLength(500)] string? Address,
    [MaxLength(50)] string? Phone,
    IReadOnlyCollection<Guid>? DivisionIds = null, string? PostalAddress = null, string? Email = null, decimal? Latitude = null, decimal? Longitude = null, Guid? BranchManagerUserId = null, string? PickupInstructions = null, string? ReturnInstructions = null, string? DeliveryCoverage = null, bool IsPublic = true);
public sealed record SetBranchStatusRequest(bool IsActive);
public sealed record BranchResponse(
    Guid Id, string Code, string Name, string? Address, string? Phone, bool IsActive, IReadOnlyCollection<Guid> DivisionIds, string? PostalAddress, string? Email, decimal? Latitude, decimal? Longitude, Guid? BranchManagerUserId, string? PickupInstructions, string? ReturnInstructions, string? DeliveryCoverage, bool IsPublic);
public sealed record OperatingPeriodRequest(DayOfWeek DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, TimeOnly? PickupCutoff, TimeOnly? ReturnCutoff, bool IsClosed, decimal AfterHoursCharge);
public sealed record CalendarExceptionRequest(DateOnly Date, string Name, bool IsClosed, TimeOnly? OpensAt, TimeOnly? ClosesAt, decimal AfterHoursCharge);
public sealed record CalendarRequest(IReadOnlyList<OperatingPeriodRequest> Periods, IReadOnlyList<CalendarExceptionRequest> Exceptions);
public sealed record BranchServiceRequest(Guid DivisionId, Guid ServiceOfferingId, bool IsActive, bool IsBookable);
public sealed record BranchDivisionConfigurationRequest(bool IsActive, DateOnly? OpenedOn, string? LocalContactEmail,
    string? LocalContactPhone, Guid? DivisionManagerUserId, bool AcceptsBookings, bool HasMaintenanceCapability,
    Guid? DefaultApprovalWorkflowId, string? LocalTermsJson, string? PricingOverridesJson);
