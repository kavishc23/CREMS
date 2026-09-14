using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/business-operations")]
[Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class BusinessOperationsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet("workspace")]
    public async Task<ActionResult> Workspace(CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid();
        var assets = AssetScope(scope);
        var assetIds = assets.Select(x => x.Id);
        var branchId = scope.IsAdministrator ? null : scope.BranchId;
        return Ok(new
        {
            personnel = await db.Personnel.AsNoTracking().Where(x => scope.IsAdministrator || scope.BranchIds.Contains(x.BranchId) && scope.DivisionIds.Contains(x.DivisionId)).OrderBy(x => x.FullName).Select(x => new { x.Id, x.EmployeeNumber, x.FullName, x.Type, x.BranchId, x.DivisionId, x.StandardChargeRate }).ToListAsync(token),
            assignments = await db.BookingPersonnelAssignments.AsNoTracking().Where(x => scope.IsAdministrator || scope.BranchIds.Contains(x.Personnel!.BranchId) && scope.DivisionIds.Contains(x.Personnel.DivisionId)).OrderByDescending(x => x.StartAt).Take(150).Select(x => new { x.Id, personnelName = x.Personnel!.FullName, x.BookingId, x.StartAt, x.EndAt, x.CustomerHourlyRate }).ToListAsync(token),
            suppliers = await db.Suppliers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(token),
            deliveryZones = await db.DeliveryZones.AsNoTracking().Where(x => x.IsActive && (scope.IsAdministrator || (x.BranchId == null || x.BranchId == branchId) && (x.DivisionId == null || x.DivisionId == scope.DivisionId))).OrderBy(x => x.Name).ToListAsync(token),
            alerts = await db.BusinessAlerts.AsNoTracking().Where(x => x.AcknowledgedAt == null && (scope.IsAdministrator || x.BranchId == branchId && (x.DivisionId == null || x.DivisionId == scope.DivisionId))).OrderByDescending(x => x.Priority).ThenBy(x => x.RaisedAt).Take(100).ToListAsync(token),
            alertRules = await db.BusinessAlertRules.AsNoTracking().Where(x => scope.IsAdministrator || x.BranchId == branchId && (x.DivisionId == null || x.DivisionId == scope.DivisionId)).OrderBy(x => x.Category).ToListAsync(token),
            lifecycle = await db.AssetLifecycleEvents.AsNoTracking().Where(x => assetIds.Contains(x.AssetId)).OrderByDescending(x => x.OccurredAt).Take(150).ToListAsync(token),
            meters = await db.AssetMeterReadings.AsNoTracking().Where(x => assetIds.Contains(x.AssetId)).OrderByDescending(x => x.RecordedAt).Take(150).ToListAsync(token),
        });
    }

    [HttpGet("asset-profitability")]
    [Authorize(Policy = SystemPolicies.ViewReports)]
    public async Task<ActionResult<object>> AssetProfitability(CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid();
        var assets = await AssetScope(scope).AsNoTracking().Select(x => new { x.Id, x.AssetNumber, x.Name, x.BranchId, x.DivisionId, x.AcquisitionCost, x.CurrentBookValue, x.CurrentMeterReading, x.MeterUnit }).ToListAsync(token);
        var ids = assets.Select(x => x.Id).ToList();
        var items = await db.BookingItems.AsNoTracking().Where(x => ids.Contains(x.AssetId) && x.Booking != null && (x.Booking.Status == BookingStatus.Completed || x.Booking.Status == BookingStatus.ConvertedToRental)).Select(x => new { x.AssetId, x.BookingId, x.StartAt, x.EndAt, x.DailyRate, x.CreatedAt }).ToListAsync(token);
        var charges = await db.BookingCharges.AsNoTracking().Where(x => x.AssetId.HasValue && ids.Contains(x.AssetId.Value)).ToListAsync(token);
        var maintenance = await db.MaintenanceJobs.AsNoTracking().Where(x => ids.Contains(x.AssetId) && x.Status != MaintenanceStatus.Cancelled).ToListAsync(token);
        var costs = await db.AssetCostEntries.AsNoTracking().Where(x => ids.Contains(x.AssetId)).ToListAsync(token);
        var timesheets = await db.PersonnelTimesheets.AsNoTracking().Include(x => x.Assignment).Where(x => x.Assignment != null && db.BookingItems.Any(i => i.BookingId == x.Assignment.BookingId && ids.Contains(i.AssetId))).ToListAsync(token);
        var rows = assets.Select(asset =>
        {
            var rentals = items.Where(x => x.AssetId == asset.Id).ToList(); var bookingIds = rentals.Select(x => x.BookingId).Distinct().ToHashSet();
            var baseRevenue = rentals.Sum(x => Math.Max(1, (decimal)Math.Ceiling((x.EndAt - x.StartAt).TotalDays)) * x.DailyRate);
            var component = charges.Where(x => x.AssetId == asset.Id).ToList(); var serviceRevenue = component.Sum(x => x.Quantity * x.UnitRate); var componentCost = component.Sum(x => x.Quantity * x.UnitCost);
            var maintenanceCost = maintenance.Where(x => x.AssetId == asset.Id).Sum(x => x.ActualCost ?? x.PartsCost + x.LabourCost + x.TransportCost + x.ExternalServiceCost + x.TaxCost + x.OtherCost);
            var directCost = costs.Where(x => x.AssetId == asset.Id).Sum(x => x.Amount);
            var personnelCost = timesheets.Where(x => bookingIds.Contains(x.Assignment!.BookingId)).Sum(x => x.RegularHours * x.Assignment!.InternalHourlyCost + x.OvertimeHours * x.Assignment.InternalHourlyCost * 1.5m);
            var revenue = baseRevenue + serviceRevenue; var expense = maintenanceCost + directCost + componentCost + personnelCost; var profit = revenue - expense;
            var usage = rentals.Sum(x => Math.Max(0, (decimal)(x.EndAt - x.StartAt).TotalHours));
            return new { asset.Id, asset.AssetNumber, asset.Name, asset.BranchId, asset.DivisionId, revenue, maintenanceCost, directCost, componentCost, personnelCost, totalExpense = expense, grossProfit = profit, profitMargin = revenue == 0 ? 0 : Math.Round(profit * 100 / revenue, 1), asset.AcquisitionCost, asset.CurrentBookValue, costPerOperatingHour = usage == 0 ? 0 : Math.Round(expense / usage, 2), asset.CurrentMeterReading, asset.MeterUnit, lossMaking = profit < 0 };
        }).OrderBy(x => x.grossProfit).ToList();
        var monthStarts = Enumerable.Range(0, 12).Select(offset => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(offset - 11)).ToList();
        var monthlyTrend = monthStarts.Select(month => { var end = month.AddMonths(1); var monthItems = items.Where(x => x.CreatedAt >= month && x.CreatedAt < end).ToList(); var monthBookingIds = monthItems.Select(x => x.BookingId).ToHashSet(); var revenue = monthItems.Sum(x => Math.Max(1, (decimal)Math.Ceiling((x.EndAt - x.StartAt).TotalDays)) * x.DailyRate) + charges.Where(x => x.CreatedAt >= month && x.CreatedAt < end).Sum(x => x.Quantity * x.UnitRate); var expense = charges.Where(x => x.CreatedAt >= month && x.CreatedAt < end).Sum(x => x.Quantity * x.UnitCost) + costs.Where(x => x.OccurredOn >= DateOnly.FromDateTime(month) && x.OccurredOn < DateOnly.FromDateTime(end)).Sum(x => x.Amount) + maintenance.Where(x => x.ReportedAt >= month && x.ReportedAt < end).Sum(x => x.ActualCost ?? 0) + timesheets.Where(x => monthBookingIds.Contains(x.Assignment!.BookingId)).Sum(x => x.RegularHours * x.Assignment!.InternalHourlyCost + x.OvertimeHours * x.Assignment.InternalHourlyCost * 1.5m); return new { month = month.ToString("yyyy-MM"), revenue, expense, profit = revenue - expense }; }).ToList();
        return Ok(new { generatedAt = DateTimeOffset.UtcNow, totals = new { revenue = rows.Sum(x => x.revenue), expense = rows.Sum(x => x.totalExpense), profit = rows.Sum(x => x.grossProfit), lossMakingAssets = rows.Count(x => x.lossMaking) }, monthlyTrend, assets = rows });
    }

    [HttpGet("management-reports")]
    [Authorize(Policy = SystemPolicies.ViewReports)]
    public async Task<ActionResult> ManagementReports(CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid(); var bookings = db.Bookings.AsNoTracking().Where(x => scope.IsAdministrator || x.BranchId == scope.BranchId); var bookingIds = bookings.Select(x => x.Id);
        var customerHistory = await bookings.GroupBy(x => x.CustomerId).Select(x => new { customerId = x.Key, rentals = x.Count(), completed = x.Count(b => b.Status == BookingStatus.Completed), revenue = x.SelectMany(b => b.Items).Sum(i => i.DailyRate * Math.Max(1, EF.Functions.DateDiffDay(i.StartAt, i.EndAt))) + x.SelectMany(b => b.Charges).Sum(c => c.Quantity * c.UnitRate) }).OrderByDescending(x => x.revenue).Take(100).ToListAsync(token);
        var customers = await db.Customers.AsNoTracking().Where(x => customerHistory.Select(h => h.customerId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => new { x.CustomerNumber, x.Name }, token);
        var assignments = await db.BookingPersonnelAssignments.AsNoTracking().Include(x => x.Personnel).Include(x => x.Timesheets).Where(x => bookingIds.Contains(x.BookingId)).ToListAsync(token);
        var quotes = db.SalesQuotes.AsNoTracking().Where(x => scope.IsAdministrator || x.BranchId == scope.BranchId && x.DivisionId == scope.DivisionId); var quoteCount = await quotes.CountAsync(token); var convertedQuotes = await quotes.CountAsync(x => x.Status == QuoteStatus.Converted, token);
        var downtime = await db.MaintenanceJobs.AsNoTracking().Where(x => scope.IsAdministrator || x.BranchId == scope.BranchId && x.Asset!.DivisionId == scope.DivisionId).SumAsync(x => (int?)x.DowntimeHours, token) ?? 0;
        var damage = await db.RentalIncidents.AsNoTracking().Where(x => x.Type == IncidentType.Damage && bookingIds.Contains(x.BookingId)).ToListAsync(token);
        return Ok(new { quoteConversion = new { total = quoteCount, converted = convertedQuotes, rate = quoteCount == 0 ? 0 : Math.Round(convertedQuotes * 100m / quoteCount, 1) }, assetDowntimeHours = downtime, damageRecovery = new { incidents = damage.Count, estimatedCost = damage.Sum(x => x.EstimatedCost), unresolved = damage.Count(x => x.Status != IncidentStatus.Resolved && x.Status != IncidentStatus.Closed) }, customerProfitability = customerHistory.Select(x => new { x.customerId, customers.GetValueOrDefault(x.customerId)?.CustomerNumber, customers.GetValueOrDefault(x.customerId)?.Name, x.rentals, x.completed, x.revenue }), operatorUtilization = assignments.GroupBy(x => new { x.PersonnelId, x.Personnel!.FullName }).Select(x => new { x.Key.PersonnelId, x.Key.FullName, assignments = x.Count(), regularHours = x.SelectMany(a => a.Timesheets).Sum(t => t.RegularHours), overtimeHours = x.SelectMany(a => a.Timesheets).Sum(t => t.OvertimeHours), customerRevenue = x.Sum(a => a.Timesheets.Sum(t => t.RegularHours * a.CustomerHourlyRate + t.OvertimeHours * a.CustomerHourlyRate * 1.5m)), internalCost = x.Sum(a => a.Timesheets.Sum(t => t.RegularHours * a.InternalHourlyCost + t.OvertimeHours * a.InternalHourlyCost * 1.5m)) }).OrderByDescending(x => x.regularHours) });
    }

    [HttpPost("assets/{assetId:guid}/meters")]
    [Authorize(Policy = SystemPolicies.ManageRentals)]
    public async Task<ActionResult> RecordMeter(Guid assetId, MeterRequest request, CancellationToken token)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == assetId, token); if (asset is null) return NotFound(); var scope = await Scope(); if (scope is null || !scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();
        var previous = await db.AssetMeterReadings.Where(x => x.AssetId == assetId && x.Type == request.Type).OrderByDescending(x => x.RecordedAt).Select(x => (decimal?)x.Reading).FirstOrDefaultAsync(token) ?? asset.CurrentMeterReading;
        if (previous.HasValue && request.Reading < previous) return Validation(nameof(request.Reading), "A cumulative meter cannot be lower than its previous reading.");
        var reading = new AssetMeterReading { AssetId = assetId, BookingId = request.BookingId, Type = request.Type, Unit = request.Unit.Trim(), Reading = request.Reading, FuelPercent = request.FuelPercent, Source = request.Source, RecordedAt = request.RecordedAt ?? DateTimeOffset.UtcNow, RecordedByUserId = scope.UserId };
        db.AssetMeterReadings.Add(reading); asset.CurrentMeterReading = request.Reading; asset.MeterUnit = request.Unit.Trim();
        if (request.BookingId.HasValue && request.IncludedUsage.HasValue && request.ExcessRate.HasValue && previous.HasValue)
        { var excess = Math.Max(0, request.Reading - previous.Value - request.IncludedUsage.Value); if (excess > 0) db.BookingCharges.Add(new BookingCharge { BookingId = request.BookingId.Value, AssetId = assetId, Description = $"Excess {request.Unit}", Category = ChargeCategory.Other, Unit = ChargeUnit.Unit, Quantity = excess, UnitRate = request.ExcessRate.Value, UnitCost = 0 }); }
        AuditWriter.Record(db, scope, "Asset meter recorded", nameof(AssetMeterReading), reading.Id, $"{asset.AssetNumber}: {reading.Reading} {reading.Unit}", asset.BranchId); await db.SaveChangesAsync(token); return Ok(reading);
    }

    [HttpPost("assets/{assetId:guid}/lifecycle")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> RecordLifecycle(Guid assetId, LifecycleRequest request, CancellationToken token)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == assetId, token); if (asset is null) return NotFound(); var scope = await Scope(); if (scope is null || !scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();
        if (!AllowedLifecycleTransitions.TryGetValue(asset.Status, out var destinations) || !destinations.Contains(request.ToStatus)) return Validation(nameof(request.ToStatus), $"An asset cannot move directly from {asset.Status} to {request.ToStatus}.");
        if (request.Type is AssetLifecycleEventType.PreHireInspection or AssetLifecycleEventType.PostHireInspection && !request.BookingId.HasValue) return Validation(nameof(request.BookingId), "An inspection transition must be linked to a booking.");
        if (request.MeterReading.HasValue && asset.CurrentMeterReading.HasValue && request.MeterReading < asset.CurrentMeterReading) return Validation(nameof(request.MeterReading), "The meter reading cannot be lower than its previous reading.");
        var item = new AssetLifecycleEvent { AssetId = assetId, Type = request.Type, FromStatus = asset.Status, ToStatus = request.ToStatus, BookingId = request.BookingId, MeterReading = request.MeterReading, Notes = Clean(request.Notes), EvidenceJson = string.IsNullOrWhiteSpace(request.EvidenceJson) ? "[]" : request.EvidenceJson, RecordedByUserId = scope.UserId, RecordedByName = scope.UserName }; asset.Status = request.ToStatus; if (request.MeterReading.HasValue) asset.CurrentMeterReading = request.MeterReading; if (request.Type is AssetLifecycleEventType.Retired or AssetLifecycleEventType.Disposed or AssetLifecycleEventType.Sold) asset.IsActive = false;
        db.AssetLifecycleEvents.Add(item); AuditWriter.Record(db, scope, "Asset lifecycle updated", nameof(Asset), asset.Id, $"{asset.AssetNumber}: {item.Type}, {item.FromStatus} to {item.ToStatus}", asset.BranchId); await db.SaveChangesAsync(token); return Ok(item);
    }

    [HttpPost("personnel")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> SavePersonnel(PersonnelRequest request, CancellationToken token)
    {
        var scope = await Scope(); if (scope is null || !scope.HasAssetAccess(request.BranchId, request.DivisionId)) return Forbid();
        var item = new Personnel { EmployeeNumber = request.EmployeeNumber.Trim().ToUpperInvariant(), FullName = request.FullName.Trim(), BranchId = request.BranchId, DivisionId = request.DivisionId, Type = request.Type, StandardCostRate = request.StandardCostRate, OvertimeCostRate = request.OvertimeCostRate, StandardChargeRate = request.StandardChargeRate, OvertimeChargeRate = request.OvertimeChargeRate };
        db.Personnel.Add(item); await db.SaveChangesAsync(token); return Ok(item);
    }

    [HttpPost("personnel/{personnelId:guid}/qualifications")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> AddQualification(Guid personnelId, QualificationRequest request, CancellationToken token)
    {
        var person = await db.Personnel.FirstOrDefaultAsync(x => x.Id == personnelId, token); if (person is null) return NotFound(); var scope = await Scope(); if (scope is null || !scope.HasAssetAccess(person.BranchId, person.DivisionId)) return Forbid();
        var item = new PersonnelQualification { PersonnelId = personnelId, Name = request.Name.Trim(), CertificateNumber = request.CertificateNumber.Trim(), IssuedOn = request.IssuedOn, ExpiresOn = request.ExpiresOn, SafetyInduction = request.SafetyInduction }; db.PersonnelQualifications.Add(item); await db.SaveChangesAsync(token); return Ok(item);
    }

    [HttpPost("assignments")]
    [Authorize(Policy = SystemPolicies.ManageRentals)]
    public async Task<ActionResult> AssignPersonnel(AssignmentRequest request, CancellationToken token)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(x => x.Id == request.BookingId, token); var person = await db.Personnel.Include(x => x.Qualifications).FirstOrDefaultAsync(x => x.Id == request.PersonnelId, token); if (booking is null || person is null) return NotFound(); var scope = await Scope(); if (scope is null || !scope.HasAssetAccess(booking.BranchId, person.DivisionId)) return Forbid();
        if (request.EndAt <= request.StartAt || await db.BookingPersonnelAssignments.AnyAsync(x => x.PersonnelId == request.PersonnelId && x.Status != AssignmentStatus.Cancelled && x.StartAt < request.EndAt && request.StartAt < x.EndAt, token)) return Validation("schedule", "The employee is already assigned or the period is invalid.");
        if (person.Qualifications.Any(x => x.ExpiresOn < DateOnly.FromDateTime(request.EndAt.Date))) return Validation("qualification", "One or more required qualifications expire before this assignment ends.");
        var item = new BookingPersonnelAssignment { BookingId = request.BookingId, PersonnelId = request.PersonnelId, Role = request.Role.Trim(), StartAt = request.StartAt, EndAt = request.EndAt, CustomerHourlyRate = request.CustomerHourlyRate ?? person.StandardChargeRate, InternalHourlyCost = request.InternalHourlyCost ?? person.StandardCostRate, Status = AssignmentStatus.Confirmed }; db.BookingPersonnelAssignments.Add(item); person.Availability = PersonnelAvailability.Assigned; await db.SaveChangesAsync(token); return Ok(item);
    }

    [HttpPost("assignments/{assignmentId:guid}/timesheets")]
    [Authorize(Policy = SystemPolicies.ManageRentals)]
    public async Task<ActionResult> AddTimesheet(Guid assignmentId, TimesheetRequest request, CancellationToken token)
    {
        var assignment = await db.BookingPersonnelAssignments.Include(x => x.Personnel).FirstOrDefaultAsync(x => x.Id == assignmentId, token); if (assignment is null) return NotFound(); var scope = await Scope(); if (scope is null || !scope.HasAssetAccess(assignment.Personnel!.BranchId, assignment.Personnel.DivisionId)) return Forbid();
        var item = new PersonnelTimesheet { AssignmentId = assignmentId, WorkDate = request.WorkDate, RegularHours = request.RegularHours, OvertimeHours = request.OvertimeHours, Notes = Clean(request.Notes), ApprovedByUserId = scope.UserId, ApprovedAt = DateTimeOffset.UtcNow }; db.PersonnelTimesheets.Add(item);
        var revenue = request.RegularHours * assignment.CustomerHourlyRate + request.OvertimeHours * assignment.CustomerHourlyRate * 1.5m; var cost = request.RegularHours * assignment.InternalHourlyCost + request.OvertimeHours * assignment.InternalHourlyCost * 1.5m;
        db.BookingCharges.Add(new BookingCharge { BookingId = assignment.BookingId, Description = $"{assignment.Role} – {request.WorkDate:yyyy-MM-dd}", Category = assignment.Personnel.Type == PersonnelType.Driver ? ChargeCategory.Driver : ChargeCategory.Operator, Unit = ChargeUnit.Hour, Quantity = request.RegularHours + request.OvertimeHours, UnitRate = revenue / Math.Max(1, request.RegularHours + request.OvertimeHours), UnitCost = cost / Math.Max(1, request.RegularHours + request.OvertimeHours) }); await db.SaveChangesAsync(token); return Ok(item);
    }

    [HttpPost("suppliers")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> AddSupplier(SupplierRequest request, CancellationToken token)
    { var scope = await Scope(); if (scope is null) return Forbid(); var item = new Supplier { SupplierNumber = request.SupplierNumber.Trim().ToUpperInvariant(), Name = request.Name.Trim(), Email = Clean(request.Email), Phone = Clean(request.Phone), Address = Clean(request.Address), TaxNumber = Clean(request.TaxNumber), PaymentTermsDays = request.PaymentTermsDays }; db.Suppliers.Add(item); await db.SaveChangesAsync(token); return Ok(item); }

    [HttpPost("maintenance/{jobId:guid}/parts")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> UsePart(Guid jobId, PartUsageRequest request, CancellationToken token)
    {
        var job = await db.MaintenanceJobs.FirstOrDefaultAsync(x => x.Id == jobId, token); var part = await db.InventoryParts.FirstOrDefaultAsync(x => x.Id == request.InventoryPartId, token); if (job is null || part is null) return NotFound(); var scope = await Scope(); if (scope is null || !scope.HasBranchAccess(job.BranchId)) return Forbid(); if (part.QuantityOnHand - part.QuantityAllocated < request.Quantity) return Validation("quantity", "Insufficient available stock.");
        var item = new MaintenancePartUsage { MaintenanceJobId = jobId, InventoryPartId = part.Id, Quantity = request.Quantity, UnitCost = part.UnitCost }; part.QuantityOnHand -= (int)Math.Ceiling(request.Quantity); job.PartsCost += request.Quantity * part.UnitCost; job.ActualCost = job.PartsCost + job.LabourCost + job.TransportCost + job.ExternalServiceCost + job.TaxCost + job.OtherCost; db.MaintenancePartUsages.Add(item); await db.SaveChangesAsync(token); return Ok(item);
    }

    [HttpPost("delivery-zones")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> AddDeliveryZone(DeliveryZoneRequest request, CancellationToken token)
    { var scope = await Scope(); if (scope is null || (request.BranchId.HasValue && !scope.HasBranchAccess(request.BranchId.Value)) || !scope.HasDivisionAccess(request.DivisionId)) return Forbid(); var item = new DeliveryZone { Name = request.Name.Trim(), BranchId = request.BranchId, DivisionId = request.DivisionId, BaseCharge = request.BaseCharge, CostPerKilometre = request.CostPerKilometre, ChargePerKilometre = request.ChargePerKilometre, FailedDeliveryCharge = request.FailedDeliveryCharge }; db.DeliveryZones.Add(item); await db.SaveChangesAsync(token); return Ok(item); }

    [HttpPost("alert-rules")]
    [Authorize(Policy = SystemPolicies.AdministerSystem)]
    public async Task<ActionResult> AddAlertRule(AlertRuleRequest request, CancellationToken token)
    { var item = new BusinessAlertRule { Name = request.Name.Trim(), Category = request.Category, BranchId = request.BranchId, DivisionId = request.DivisionId, Threshold = request.Threshold, LeadTimeHours = request.LeadTimeHours, Priority = request.Priority, EmailEnabled = request.EmailEnabled }; db.BusinessAlertRules.Add(item); await db.SaveChangesAsync(token); return Ok(item); }

    [HttpPost("alerts/{id:guid}/acknowledge")]
    public async Task<ActionResult> Acknowledge(Guid id, CancellationToken token)
    { var scope = await Scope(); if (scope is null) return Forbid(); var item = await db.BusinessAlerts.FirstOrDefaultAsync(x => x.Id == id, token); if (item is null) return NotFound(); if (!scope.IsAdministrator && item.BranchId != scope.BranchId) return Forbid(); item.AcknowledgedAt = DateTimeOffset.UtcNow; item.AcknowledgedByUserId = scope.UserId; await db.SaveChangesAsync(token); return NoContent(); }

    [HttpPost("alerts/refresh")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> RefreshAlerts(CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid(); var now = DateTimeOffset.UtcNow; var today = DateOnly.FromDateTime(now.DateTime); var created = 0;
        var overdue = await db.Bookings.AsNoTracking().Where(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt < now) && (scope.IsAdministrator || x.BranchId == scope.BranchId)).Select(x => new { x.Id, x.BookingNumber, x.BranchId }).ToListAsync(token);
        foreach (var x in overdue) created += await AddAlert(AlertCategory.OverdueReturn, $"Overdue rental {x.BookingNumber}", "The scheduled return time has passed.", nameof(Booking), x.Id, x.BranchId, null, TaskPriority.High, token);
        var dueMaintenance = await AssetScope(scope).AsNoTracking().Where(x => x.NextServiceDate != null && x.NextServiceDate <= today.AddDays(7)).Select(x => new { x.Id, x.AssetNumber, x.BranchId, x.DivisionId }).ToListAsync(token);
        foreach (var x in dueMaintenance) created += await AddAlert(AlertCategory.MaintenanceDue, $"Maintenance due: {x.AssetNumber}", "Service is due within seven days or is overdue.", nameof(Asset), x.Id, x.BranchId, x.DivisionId, TaskPriority.High, token);
        var expiringInsurance = await AssetScope(scope).AsNoTracking().Where(x => x.InsuranceExpiry != null && x.InsuranceExpiry <= today.AddDays(30)).Select(x => new { x.Id, x.AssetNumber, x.BranchId, x.DivisionId }).ToListAsync(token);
        foreach (var x in expiringInsurance) created += await AddAlert(AlertCategory.ExpiringInsurance, $"Insurance expiry: {x.AssetNumber}", "Insurance expires within 30 days or has expired.", nameof(Asset), x.Id, x.BranchId, x.DivisionId, TaskPriority.Critical, token);
        var qualifications = await db.PersonnelQualifications.AsNoTracking().Where(x => x.ExpiresOn <= today.AddDays(30) && (scope.IsAdministrator || x.Personnel!.BranchId == scope.BranchId && x.Personnel.DivisionId == scope.DivisionId)).Select(x => new { x.Id, x.Name, x.ExpiresOn, x.Personnel!.FullName, x.Personnel.BranchId, x.Personnel.DivisionId }).ToListAsync(token);
        foreach (var x in qualifications) created += await AddAlert(AlertCategory.ExpiringLicence, $"Qualification expiry: {x.FullName}", $"{x.Name} expires on {x.ExpiresOn:yyyy-MM-dd}.", nameof(PersonnelQualification), x.Id, x.BranchId, x.DivisionId, TaskPriority.High, token);
        var invoices = await db.RentalInvoices.AsNoTracking().Where(x => x.BalanceDue > 0 && x.IssuedAt.AddDays(30) < now && (scope.IsAdministrator || x.Booking!.BranchId == scope.BranchId)).Select(x => new { x.Id, x.InvoiceNumber, x.BalanceDue, x.Booking!.BranchId }).ToListAsync(token);
        foreach (var x in invoices) created += await AddAlert(AlertCategory.UnpaidInvoice, $"Unpaid invoice {x.InvoiceNumber}", $"FJD {x.BalanceDue:N2} remains outstanding.", nameof(RentalInvoice), x.Id, x.BranchId, null, TaskPriority.High, token);
        var incidents = await db.RentalIncidents.AsNoTracking().Where(x => x.Type == IncidentType.Damage && x.Status != IncidentStatus.Resolved && x.Status != IncidentStatus.Closed && (scope.IsAdministrator || x.Booking!.BranchId == scope.BranchId)).Select(x => new { x.Id, x.IncidentNumber, x.Booking!.BranchId }).ToListAsync(token);
        foreach (var x in incidents) created += await AddAlert(AlertCategory.UnresolvedDamage, $"Unresolved damage {x.IncidentNumber}", "Damage investigation or recovery is still open.", nameof(RentalIncident), x.Id, x.BranchId, null, TaskPriority.High, token);
        await db.SaveChangesAsync(token); return Ok(new { created });
    }

    [HttpGet("reports/asset-profitability.csv")]
    [Authorize(Policy = SystemPolicies.ViewReports)]
    public async Task<IActionResult> ExportProfitability(CancellationToken token)
    {
        var result = await AssetProfitability(token); if (result.Result is not OkObjectResult ok) return result.Result!; var json = JsonSerializer.Serialize(ok.Value); using var document = JsonDocument.Parse(json); var rows = document.RootElement.GetProperty("assets").EnumerateArray(); var csv = new StringBuilder("Asset number,Asset,Revenue,Expense,Gross profit,Margin %,Loss making\n");
        foreach (var row in rows) csv.AppendLine(string.Join(',', Csv(row.GetProperty("assetNumber").GetString()), Csv(row.GetProperty("name").GetString()), row.GetProperty("revenue").GetDecimal().ToString(CultureInfo.InvariantCulture), row.GetProperty("totalExpense").GetDecimal().ToString(CultureInfo.InvariantCulture), row.GetProperty("grossProfit").GetDecimal().ToString(CultureInfo.InvariantCulture), row.GetProperty("profitMargin").GetDecimal().ToString(CultureInfo.InvariantCulture), row.GetProperty("lossMaking").GetBoolean()));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"asset-profitability-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private async Task<StaffDataScope?> Scope() => await staffScope.GetAsync(User);
    private IQueryable<Asset> AssetScope(StaffDataScope scope) => db.Assets.Where(x => scope.IsAdministrator || scope.BranchIds.Contains(x.BranchId) && x.DivisionId.HasValue && scope.DivisionIds.Contains(x.DivisionId.Value));
    private ActionResult Validation(string key, string message) => BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [key] = [message] }));
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    private static readonly IReadOnlyDictionary<AssetStatus, AssetStatus[]> AllowedLifecycleTransitions = new Dictionary<AssetStatus, AssetStatus[]>
    {
        [AssetStatus.Available] = [AssetStatus.Reserved, AssetStatus.Inspection, AssetStatus.Maintenance, AssetStatus.OutOfService, AssetStatus.Retired],
        [AssetStatus.Reserved] = [AssetStatus.Inspection, AssetStatus.Available],
        [AssetStatus.Inspection] = [AssetStatus.Rented, AssetStatus.Available, AssetStatus.Maintenance, AssetStatus.OutOfService],
        [AssetStatus.Rented] = [AssetStatus.Inspection, AssetStatus.Maintenance, AssetStatus.OutOfService],
        [AssetStatus.Maintenance] = [AssetStatus.Available, AssetStatus.OutOfService, AssetStatus.Retired],
        [AssetStatus.OutOfService] = [AssetStatus.Maintenance, AssetStatus.Available, AssetStatus.Retired],
        [AssetStatus.Retired] = []
    };
    private async Task<int> AddAlert(AlertCategory category, string title, string message, string entityType, Guid entityId, Guid? branchId, Guid? divisionId, TaskPriority priority, CancellationToken token)
    {
        if (await db.BusinessAlerts.AnyAsync(x => x.Category == category && x.EntityType == entityType && x.EntityId == entityId && x.AcknowledgedAt == null, token)) return 0;
        db.BusinessAlerts.Add(new BusinessAlert { Category = category, Title = title, Message = message, EntityType = entityType, EntityId = entityId, BranchId = branchId, DivisionId = divisionId, Priority = priority }); return 1;
    }
}

public sealed record MeterRequest(MeterType Type, [Required] string Unit, [Range(0, 100000000)] decimal Reading, [Range(0, 100)] decimal? FuelPercent, MeterReadingSource Source, Guid? BookingId, decimal? IncludedUsage, decimal? ExcessRate, DateTimeOffset? RecordedAt);
public sealed record LifecycleRequest(AssetLifecycleEventType Type, AssetStatus ToStatus, Guid? BookingId, decimal? MeterReading, string? Notes, string? EvidenceJson);
public sealed record PersonnelRequest([Required] string EmployeeNumber, [Required] string FullName, Guid BranchId, Guid DivisionId, PersonnelType Type, decimal StandardCostRate, decimal OvertimeCostRate, decimal StandardChargeRate, decimal OvertimeChargeRate);
public sealed record QualificationRequest([Required] string Name, [Required] string CertificateNumber, DateOnly IssuedOn, DateOnly ExpiresOn, bool SafetyInduction);
public sealed record AssignmentRequest(Guid BookingId, Guid PersonnelId, [Required] string Role, DateTimeOffset StartAt, DateTimeOffset EndAt, decimal? CustomerHourlyRate, decimal? InternalHourlyCost);
public sealed record TimesheetRequest(DateOnly WorkDate, [Range(0, 24)] decimal RegularHours, [Range(0, 24)] decimal OvertimeHours, string? Notes);
public sealed record SupplierRequest([Required] string SupplierNumber, [Required] string Name, string? Email, string? Phone, string? Address, string? TaxNumber, [Range(0, 365)] int PaymentTermsDays);
public sealed record PartUsageRequest(Guid InventoryPartId, [Range(0.01, 100000)] decimal Quantity);
public sealed record DeliveryZoneRequest([Required] string Name, Guid? BranchId, Guid? DivisionId, decimal BaseCharge, decimal CostPerKilometre, decimal ChargePerKilometre, decimal FailedDeliveryCharge);
public sealed record AlertRuleRequest([Required] string Name, AlertCategory Category, Guid? BranchId, Guid? DivisionId, decimal? Threshold, int LeadTimeHours, TaskPriority Priority, bool EmailEnabled);
