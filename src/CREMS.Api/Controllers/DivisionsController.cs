using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/divisions")]
[Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class DivisionsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DivisionResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User); if (scope is null) return Unauthorized();
        var query = Query(); if (!scope.IsAdministrator) query = query.Where(x => scope.DivisionIds.Contains(x.Id));
        return Ok(await query.ToListAsync(cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = SystemPolicies.ManageDivisions)]
    public async Task<ActionResult<DivisionResponse>> Create(SaveDivisionRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Divisions.AnyAsync(x => x.Code == code, cancellationToken))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [nameof(request.Code)] = ["This division code is already in use."] }));

        var division = new Division { Code = code, Name = request.Name.Trim(), Description = Clean(request.Description),
            ContactEmail = Clean(request.ContactEmail)?.ToLowerInvariant(), ContactPhone = Clean(request.ContactPhone),
            Capabilities = request.Capabilities, IsPublic = request.IsPublic, IsActive = true, BrandingJson = request.BrandingJson ?? "{}", DefaultCurrency = request.DefaultCurrency.Trim().ToUpperInvariant(), DefaultTaxRate = request.DefaultTaxRate, DefaultBondAmount = request.DefaultBondAmount, DefaultRentalTerms = Clean(request.DefaultRentalTerms), DefaultApprovalWorkflowId = request.DefaultApprovalWorkflowId, CustomerBookingConfigurationJson = request.CustomerBookingConfigurationJson ?? "{}" };
        db.Divisions.Add(division);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetAll), ToResponse(division));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = SystemPolicies.ManageDivisions)]
    public async Task<ActionResult<DivisionResponse>> Update(Guid id, SaveDivisionRequest request, CancellationToken cancellationToken)
    {
        var division = await db.Divisions.FindAsync([id], cancellationToken);
        if (division is null) return NotFound();
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Divisions.AnyAsync(x => x.Id != id && x.Code == code, cancellationToken))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [nameof(request.Code)] = ["This division code is already in use."] }));
        division.Code = code; division.Name = request.Name.Trim(); division.Description = Clean(request.Description);
        division.ContactEmail = Clean(request.ContactEmail)?.ToLowerInvariant(); division.ContactPhone = Clean(request.ContactPhone);
        if (!request.IsActive && (await db.Bookings.AnyAsync(x => x.Items.Any(i => i.Asset!.DivisionId == id) && x.Status != Domain.Rentals.BookingStatus.Completed && x.Status != Domain.Rentals.BookingStatus.Cancelled && x.Status != Domain.Rentals.BookingStatus.Expired, cancellationToken) || await db.Assets.AnyAsync(x => x.DivisionId == id && x.IsActive, cancellationToken))) return Conflict(new { message = "Transfer or retire active assets and close active rentals before deactivating this division." });
        division.Capabilities = request.Capabilities; division.IsPublic = request.IsPublic; division.IsActive = request.IsActive; division.BrandingJson = request.BrandingJson ?? "{}"; division.DefaultCurrency = request.DefaultCurrency.Trim().ToUpperInvariant(); division.DefaultTaxRate = request.DefaultTaxRate; division.DefaultBondAmount = request.DefaultBondAmount; division.DefaultRentalTerms = Clean(request.DefaultRentalTerms); division.DefaultApprovalWorkflowId = request.DefaultApprovalWorkflowId; division.CustomerBookingConfigurationJson = request.CustomerBookingConfigurationJson ?? "{}";
        division.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(division));
    }

    [HttpPost("{divisionId:guid}/services")]
    [Authorize(Policy = SystemPolicies.ManageDivisions)]
    public async Task<ActionResult> AddService(Guid divisionId, SaveServiceRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Divisions.AnyAsync(x => x.Id == divisionId, cancellationToken)) return NotFound();
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.ServiceOfferings.AnyAsync(x => x.DivisionId == divisionId && x.Code == code, cancellationToken))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [nameof(request.Code)] = ["This service code is already in use for the division."] }));
        db.ServiceOfferings.Add(new ServiceOffering { DivisionId = divisionId, Code = code, Name = request.Name.Trim(),
            Description = Clean(request.Description), Type = request.Type, PersonnelRequirement = request.PersonnelRequirement,
            IsBookableOnline = request.IsBookableOnline, RequiresQuote = request.RequiresQuote, IsActive = true, DefaultHireUnit = request.DefaultHireUnit, RequiresDelivery = request.RequiresDelivery, RequiredDocumentsJson = request.RequiredDocumentsJson ?? "[]", DefaultDepositAmount = request.DefaultDepositAmount, InheritBond = request.InheritBond, InspectionRequirementsJson = request.InspectionRequirementsJson ?? "{}", MeterType = Clean(request.MeterType), MaintenanceRulesJson = request.MaintenanceRulesJson ?? "{}" });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("{divisionId:guid}/services/{serviceId:guid}")]
    [Authorize(Policy = SystemPolicies.ManageDivisions)]
    public async Task<ActionResult> UpdateService(Guid divisionId, Guid serviceId, SaveServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await db.ServiceOfferings.FirstOrDefaultAsync(x => x.Id == serviceId && x.DivisionId == divisionId, cancellationToken);
        if (service is null) return NotFound();
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.ServiceOfferings.AnyAsync(x => x.DivisionId == divisionId && x.Id != serviceId && x.Code == code, cancellationToken))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [nameof(request.Code)] = ["This service code is already in use for the division."] }));
        service.Code = code; service.Name = request.Name.Trim(); service.Description = Clean(request.Description);
        service.Type = request.Type; service.PersonnelRequirement = request.PersonnelRequirement;
        if (!request.IsActive && await db.BookingItems.AnyAsync(x => x.Asset!.ServiceOfferingId == serviceId && x.EndAt > DateTimeOffset.UtcNow && x.Booking!.Status != Domain.Rentals.BookingStatus.Cancelled && x.Booking.Status != Domain.Rentals.BookingStatus.Expired, cancellationToken)) return Conflict(new { message = "This service has future bookings and cannot be disabled." });
        service.IsBookableOnline = request.IsBookableOnline; service.RequiresQuote = request.RequiresQuote; service.DefaultHireUnit = request.DefaultHireUnit; service.RequiresDelivery = request.RequiresDelivery; service.RequiredDocumentsJson = request.RequiredDocumentsJson ?? "[]"; service.DefaultDepositAmount = request.DefaultDepositAmount; service.InheritBond = request.InheritBond; service.InspectionRequirementsJson = request.InspectionRequirementsJson ?? "{}"; service.MeterType = Clean(request.MeterType); service.MaintenanceRulesJson = request.MaintenanceRulesJson ?? "{}";
        service.IsActive = request.IsActive; service.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private IQueryable<DivisionResponse> Query() => db.Divisions.AsNoTracking()
        .OrderBy(x => x.Name).Select(x =>
        new DivisionResponse(x.Id, x.Code, x.Name, x.Description, x.ContactEmail, x.ContactPhone, x.Capabilities,
            x.IsPublic, x.IsActive, x.ServiceOfferings
                .OrderBy(s => s.Name).Select(s => new ServiceResponse(s.Id, s.Code,
                s.Name, s.Description, s.Type, s.PersonnelRequirement, s.IsBookableOnline, s.RequiresQuote, s.IsActive, s.DefaultHireUnit, s.RequiresDelivery, s.RequiredDocumentsJson, s.DefaultDepositAmount, s.InspectionRequirementsJson, s.MeterType, s.MaintenanceRulesJson, s.InheritBond)).ToList(), x.BrandingJson, x.DefaultCurrency, x.DefaultTaxRate, x.DefaultRentalTerms, x.DefaultApprovalWorkflowId, x.CustomerBookingConfigurationJson, x.DefaultBondAmount));
    private static DivisionResponse ToResponse(Division x) => new(x.Id, x.Code, x.Name, x.Description, x.ContactEmail,
        x.ContactPhone, x.Capabilities, x.IsPublic, x.IsActive, [], x.BrandingJson, x.DefaultCurrency, x.DefaultTaxRate, x.DefaultRentalTerms, x.DefaultApprovalWorkflowId, x.CustomerBookingConfigurationJson, x.DefaultBondAmount);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SaveDivisionRequest([Required, MaxLength(30)] string Code, [Required, MaxLength(150)] string Name,
    [MaxLength(1000)] string? Description, [EmailAddress, MaxLength(254)] string? ContactEmail,
    [MaxLength(50)] string? ContactPhone, DivisionCapabilities Capabilities, bool IsPublic, bool IsActive = true, string? BrandingJson = "{}", string DefaultCurrency = "FJD", [Range(0,100)] decimal DefaultTaxRate = FijiRentalDefaults.VatRate, string? DefaultRentalTerms = null, Guid? DefaultApprovalWorkflowId = null, string? CustomerBookingConfigurationJson = "{}", [Range(0,1_000_000)] decimal DefaultBondAmount = 0);
public sealed record SaveServiceRequest([Required, MaxLength(40)] string Code, [Required, MaxLength(150)] string Name,
    [MaxLength(1000)] string? Description, ServiceOfferingType Type, PersonnelRequirement PersonnelRequirement,
    bool IsBookableOnline, bool RequiresQuote, bool IsActive = true, ChargeUnit DefaultHireUnit = ChargeUnit.Day, bool RequiresDelivery = false, string? RequiredDocumentsJson = "[]", [Range(0,1_000_000)] decimal DefaultDepositAmount = 0, string? InspectionRequirementsJson = "{}", string? MeterType = null, string? MaintenanceRulesJson = "{}", bool InheritBond = false);
public sealed record DivisionResponse(Guid Id, string Code, string Name, string? Description, string? ContactEmail,
    string? ContactPhone, DivisionCapabilities Capabilities, bool IsPublic, bool IsActive, IReadOnlyList<ServiceResponse> Services, string BrandingJson, string DefaultCurrency, decimal DefaultTaxRate, string? DefaultRentalTerms, Guid? DefaultApprovalWorkflowId, string CustomerBookingConfigurationJson, decimal DefaultBondAmount);
public sealed record ServiceResponse(Guid Id, string Code, string Name, string? Description, ServiceOfferingType Type,
    PersonnelRequirement PersonnelRequirement, bool IsBookableOnline, bool RequiresQuote, bool IsActive, ChargeUnit DefaultHireUnit, bool RequiresDelivery, string RequiredDocumentsJson, decimal DefaultDepositAmount, string InspectionRequirementsJson, string? MeterType, string MaintenanceRulesJson, bool InheritBond);
