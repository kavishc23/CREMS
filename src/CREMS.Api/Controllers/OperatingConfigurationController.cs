using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController, Route("api/organization/operating-context"), Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class OperatingConfigurationController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        var query = db.BranchDivisions.AsNoTracking().Where(x => x.IsActive && x.Branch!.IsActive && x.Division!.IsActive);
        if (!scope.IsAdministrator) query = query.Where(x => scope.BranchIds.Contains(x.BranchId) && scope.DivisionIds.Contains(x.DivisionId));
        var links = await query.OrderBy(x => x.Division!.Name).ThenBy(x => x.Branch!.Name).Select(x => new {
            x.BranchId, x.DivisionId, branchName = x.Branch!.Name, divisionName = x.Division!.Name, x.AcceptsBookings,
            x.Branch.PickupInstructions, x.Branch.ReturnInstructions, flowJson = x.Division.CustomerBookingConfigurationJson,
            periods = db.BranchOperatingPeriods.Where(p => p.BranchId == x.BranchId).OrderBy(p => p.DayOfWeek).Select(p => new { p.DayOfWeek, p.OpensAt, p.ClosesAt, p.IsClosed }).ToList(),
            exceptions = db.BranchCalendarExceptions.Where(p => p.BranchId == x.BranchId && p.Date >= DateOnly.FromDateTime(DateTime.UtcNow)).OrderBy(p => p.Date).Select(p => new { p.Date, p.Name, p.IsClosed }).Take(20).ToList(),
            services = db.BranchDivisionServices.Where(b => b.BranchId == x.BranchId && b.DivisionId == x.DivisionId && b.IsActive)
                .Join(db.ServiceOfferings.Where(s => s.IsActive), b => b.ServiceOfferingId, s => s.Id, (b, s) => new {
                    s.Id, s.Code, s.Name, s.Type, s.DefaultHireUnit, s.PersonnelRequirement, b.IsBookable, s.RequiresDelivery, s.MeterType, s.RequiredDocumentsJson, s.MaintenanceRulesJson,
                    categories = db.AssetCategories.Where(c => c.DivisionId == x.DivisionId && (c.ServiceOfferingId == s.Id || c.ServiceOfferingId == null) && c.IsActive).OrderBy(c => c.Name).Select(c => new {
                        c.Id, c.Name, c.Code, c.DefaultAssetType, c.DefaultMeterType, fields = c.AttributeDefinitions.OrderBy(a => a.DisplayOrder).Select(a => new { a.Name, a.IsRequired }).ToList(),
                        templates = db.InspectionTemplates.Where(t => t.AssetCategoryId == c.Id && t.IsActive && (t.Stage == Domain.Assets.InspectionStage.PreHire || t.Stage == Domain.Assets.InspectionStage.PostHire)).OrderBy(t => t.Name).Select(t => new { t.Name, t.Stage, t.ChecklistJson }).ToList() }).ToList() }).ToList()
        }).ToListAsync(token);
        return Ok(links.Select(x => new { x.BranchId, x.DivisionId, x.branchName, x.divisionName, x.AcceptsBookings, x.PickupInstructions, x.ReturnInstructions, x.periods, x.exceptions,
            flow = DivisionBookingFlow.Parse(x.flowJson), x.services }));
    }
}
