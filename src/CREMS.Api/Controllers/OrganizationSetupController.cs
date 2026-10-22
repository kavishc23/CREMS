using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController, Route("api/organization/setup"), Authorize(Policy = SystemPolicies.ManageDivisions)]
public sealed class OrganizationSetupController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult> Get(Guid id, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasDivisionAccess(id)) return Forbid();
        var document = await Document(id, token);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpPost]
    public async Task<ActionResult> Save(DivisionSetupDocument request, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || request.Id.HasValue && !scope.HasDivisionAccess(request.Id)) return Forbid();
        var error = Validate(request);
        if (error is not null) return Invalid(error);
        // Range locks plus the configuration fingerprint prevent lost updates across every editor.
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
        var existing = request.Id.HasValue ? await Document(request.Id.Value, token) : null;
        if (request.Id.HasValue && existing is null) return NotFound();
        if (existing is not null && existing.Revision != request.Revision) return Conflict(new { message = "These settings changed since setup was opened. Reopen the wizard to load the current configuration." });
        var code = request.Division.Code.Trim().ToUpperInvariant();
        if (await db.Divisions.AnyAsync(x => x.Id != request.Id && x.Code == code, token)) return Conflict(new { message = "This division code is already in use." });
        var division = request.Id.HasValue ? await db.Divisions.SingleAsync(x => x.Id == request.Id, token) : new Division { Code = code, Name = request.Division.Name.Trim() };
        if (!request.Id.HasValue) db.Divisions.Add(division);
        if (!request.Division.IsActive && await DivisionInUse(division.Id, token)) return Conflict(new { message = "Close active hires and transfer or retire active assets before disabling the division." });
        ApplyDivision(division, request);
        foreach (var input in request.Services)
        {
            var old = existing?.Services.SingleOrDefault(x => x.Id == input.Id);
            if (input.Id.HasValue && old is null) return Invalid("A service does not belong to this division.");
            var service = input.Id.HasValue ? await db.ServiceOfferings.SingleAsync(x => x.Id == input.Id, token) : new ServiceOffering { DivisionId = division.Id, Code = input.Service.Code.Trim().ToUpperInvariant(), Name = input.Service.Name.Trim() };
            if (await db.ServiceOfferings.AnyAsync(x => x.DivisionId == division.Id && x.Id != input.Id && x.Code == input.Service.Code.Trim().ToUpper(), token)) return Conflict(new { message = "A service code is already used in this division." });
            if (input.Id.HasValue && !input.Service.IsActive && await db.BookingItems.AnyAsync(x => x.Asset!.ServiceOfferingId == service.Id && x.Booking!.Status != BookingStatus.Cancelled && x.Booking.Status != BookingStatus.Expired && x.Booking.Status != BookingStatus.Completed && x.EndAt > DateTimeOffset.UtcNow, token)) return Conflict(new { message = "A service with future bookings cannot be disabled." });
            if (!input.Id.HasValue) db.ServiceOfferings.Add(service);
            ApplyService(service, input.Service);
            if (input.BranchIds is not null)
            {
                var selected = input.BranchIds.Distinct().ToArray();
                var oldBranches = old?.BranchIds ?? [];
                var allowed = await db.Branches.Where(x => selected.Contains(x.Id) && (x.IsActive || oldBranches.Contains(x.Id))).Select(x => x.Id).ToListAsync(token);
                if (selected.Except(allowed).Any()) return Invalid("Select active operating branches.");
                var links = await db.BranchDivisions.Where(x => x.DivisionId == division.Id).ToListAsync(token);
                foreach (var branchId in selected)
                {
                    var link = links.FirstOrDefault(x => x.BranchId == branchId) ?? db.BranchDivisions.Local.FirstOrDefault(x => x.BranchId == branchId && x.DivisionId == division.Id);
                    if (link is null) db.BranchDivisions.Add(new BranchDivision { BranchId = branchId, DivisionId = division.Id });
                    else if (!link.IsActive) return Invalid("Activate the division at this branch before enabling its services.");
                }
                var rows = await db.BranchDivisionServices.Where(x => x.ServiceOfferingId == service.Id).ToListAsync(token);
                foreach (var row in rows) { row.IsActive = selected.Contains(row.BranchId); row.IsBookable = row.IsActive && (service.IsBookableOnline || service.RequiresQuote); row.UpdatedAt = DateTimeOffset.UtcNow; }
                foreach (var branchId in selected.Where(id => rows.All(x => x.BranchId != id))) db.BranchDivisionServices.Add(new BranchDivisionService { BranchId = branchId, DivisionId = division.Id, ServiceOfferingId = service.Id, IsBookable = service.IsBookableOnline || service.RequiresQuote });
            }
            foreach (var categoryInput in input.Categories)
            {
                var oldCategory = old?.Categories.SingleOrDefault(x => x.Id == categoryInput.Id);
                if (categoryInput.Id.HasValue && oldCategory is null) return Invalid("An asset category does not belong to this service.");
                var categoryCode = categoryInput.Category.Code.Trim().ToUpperInvariant();
                if (await db.AssetCategories.AnyAsync(x => x.DivisionId == division.Id && x.Id != categoryInput.Id && x.Code == categoryCode, token) || db.AssetCategories.Local.Any(x => x.DivisionId == division.Id && x.Id != categoryInput.Id && x.Code == categoryCode)) return Invalid("Category codes must be unique in a division.");
                var category = categoryInput.Id.HasValue ? await db.AssetCategories.Include(x => x.AttributeDefinitions).SingleAsync(x => x.Id == categoryInput.Id, token) : new AssetCategory { DivisionId = division.Id, ServiceOfferingId = service.Id, Code = categoryCode, Name = categoryInput.Category.Name.Trim() };
                if (!categoryInput.Id.HasValue) db.AssetCategories.Add(category);
                category.Code = categoryCode; category.Name = categoryInput.Category.Name.Trim(); category.Description = categoryInput.Category.Description;
                category.DefaultMeterType = categoryInput.Category.DefaultMeterType; category.PersonnelRequirement = categoryInput.Category.PersonnelRequirement; category.IsActive = categoryInput.Category.IsActive; category.UpdatedAt = DateTimeOffset.UtcNow;
                category.DefaultAssetType = categoryInput.Category.DefaultAssetType;
                var removed = category.AttributeDefinitions.Where(x => !categoryInput.Attributes.Any(a => a.Code.Trim().Equals(x.Code, StringComparison.OrdinalIgnoreCase))).ToArray();
                var removedIds = removed.Select(x => x.Id).ToArray();
                if (await db.AssetAttributeValues.AnyAsync(x => removedIds.Contains(x.AttributeDefinitionId), token)) return Conflict(new { message = "A field contains asset data. Keep that field to preserve existing records." });
                db.AssetAttributeDefinitions.RemoveRange(removed);
                foreach (var a in categoryInput.Attributes)
                {
                    var attrCode = a.Code.Trim().ToUpperInvariant();
                    var attribute = category.AttributeDefinitions.FirstOrDefault(x => x.Code.Equals(attrCode, StringComparison.OrdinalIgnoreCase));
                    if (attribute is not null && (attribute.DataType != a.DataType || attribute.OptionsJson != a.OptionsJson) && await db.AssetAttributeValues.AnyAsync(x => x.AttributeDefinitionId == attribute.Id, token)) return Conflict(new { message = $"{attribute.Name} contains asset data. Keep its type and choices or create a new field." });
                    attribute ??= new AssetAttributeDefinition { AssetCategoryId = category.Id, Code = attrCode, Name = a.Name.Trim() };
                    if (!category.AttributeDefinitions.Contains(attribute)) db.AssetAttributeDefinitions.Add(attribute);
                    attribute.Name = a.Name.Trim(); attribute.DataType = a.DataType; attribute.Unit = a.Unit; attribute.IsRequired = a.IsRequired; attribute.IsSearchable = a.IsSearchable;
                    attribute.IsCustomerVisible = a.IsCustomerVisible; attribute.IsReportable = a.IsReportable; attribute.DisplayOrder = a.DisplayOrder; attribute.OptionsJson = a.OptionsJson ?? "[]";
                }
                foreach (var t in categoryInput.Templates)
                {
                    var template = t.Id.HasValue ? await db.InspectionTemplates.SingleOrDefaultAsync(x => x.Id == t.Id && x.AssetCategoryId == category.Id, token) : null;
                    if (t.Id.HasValue && template is null) return Invalid("An inspection template does not belong to this category.");
                    template ??= new InspectionTemplate { AssetCategoryId = category.Id, Name = t.Name.Trim(), Stage = t.Stage };
                    if (!t.Id.HasValue) db.InspectionTemplates.Add(template);
                    template.Name = t.Name.Trim(); template.Stage = t.Stage; template.ChecklistJson = t.ChecklistJson; template.IsActive = t.IsActive;
                    template.RequiresCustomerSignature = t.RequiresCustomerSignature; template.RequiresStaffSignature = t.RequiresStaffSignature; template.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
        }
        AuditWriter.Record(db, scope, request.Id.HasValue ? "Division setup updated" : "Division setup created", nameof(Division), division.Id, $"{division.Name}: guided setup saved with services, branch assignments, customer requirements and asset templates.", null);
        await db.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return Ok(new { division.Id, message = "Division configuration saved." });
    }

    private async Task<DivisionSetupDocument?> Document(Guid id, CancellationToken token)
    {
        var d = await db.Divisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (d is null) return null;
        var services = await db.ServiceOfferings.AsNoTracking().Where(x => x.DivisionId == id).OrderBy(x => x.Id).ToListAsync(token);
        var categories = await db.AssetCategories.AsNoTracking().Include(x => x.AttributeDefinitions).Where(x => x.DivisionId == id).OrderBy(x => x.Id).ToListAsync(token);
        var categoryIds = categories.Select(x => x.Id).ToArray();
        var templates = await db.InspectionTemplates.AsNoTracking().Where(x => categoryIds.Contains(x.AssetCategoryId) && (x.Stage == InspectionStage.PreHire || x.Stage == InspectionStage.PostHire)).OrderBy(x => x.Id).ToListAsync(token);
        var branches = await db.BranchDivisionServices.AsNoTracking().Where(x => x.DivisionId == id && x.IsActive).OrderBy(x => x.BranchId).ToListAsync(token);
        var document = new DivisionSetupDocument(id, null, new SaveDivisionRequest(d.Code, d.Name, d.Description, d.ContactEmail, d.ContactPhone, d.Capabilities, d.IsPublic, d.IsActive, d.BrandingJson, d.DefaultCurrency, d.DefaultTaxRate, d.DefaultRentalTerms, d.DefaultApprovalWorkflowId, d.CustomerBookingConfigurationJson, d.DefaultBondAmount), DivisionBookingFlow.For(d),
            services.Select(s => new SetupService(s.Id, new SaveServiceRequest(s.Code, s.Name, s.Description, s.Type, s.PersonnelRequirement, s.IsBookableOnline, s.RequiresQuote, s.IsActive, s.DefaultHireUnit, s.RequiresDelivery, s.RequiredDocumentsJson, s.DefaultDepositAmount, s.InspectionRequirementsJson, s.MeterType, s.MaintenanceRulesJson, s.InheritBond),
                branches.Where(b => b.ServiceOfferingId == s.Id).Select(b => b.BranchId).ToArray(),
                categories.Where(c => c.ServiceOfferingId == s.Id).Select(c => new SetupCategory(c.Id, new CategoryRequest(c.DivisionId, c.ServiceOfferingId, c.Code, c.Name, c.Description, c.DefaultMeterType, c.PersonnelRequirement, c.IsActive, c.DefaultAssetType),
                    c.AttributeDefinitions.OrderBy(a => a.DisplayOrder).ThenBy(a => a.Code).Select(a => new AttributeRequest(a.Code, a.Name, a.DataType, a.Unit, a.IsRequired, a.IsSearchable, a.IsCustomerVisible, a.IsReportable, a.DisplayOrder, a.OptionsJson)).ToArray(),
                    templates.Where(t => t.AssetCategoryId == c.Id).Select(t => new SetupTemplate(t.Id, t.Name, t.Stage, t.ChecklistJson, t.RequiresCustomerSignature, t.RequiresStaffSignature, t.IsActive)).ToArray())).ToArray())).ToArray());
        return document with { Revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { document,
            branchStates = branches.Select(x => new { x.BranchId, x.ServiceOfferingId, x.IsBookable, x.UpdatedAt }) }, OrganizationConfiguration.Json))) };
    }

    private static string? Validate(DivisionSetupDocument request)
    {
        if (request.Flow is null || !request.Flow.IsValid) return OrganizationConfiguration.ValidateQuestions(request.Flow?.CustomerFields) ?? "Check the division booking policy and hire limits.";
        if (!string.Equals(request.Division.DefaultCurrency, "FJD", StringComparison.OrdinalIgnoreCase)) return "CREMS supports FJD transactions only.";
        if (!DivisionsController.ValidObject(request.Division.BrandingJson)) return "Branding must be a JSON object.";
        if (request.Services.Count is < 1 or > 50 || request.Services.GroupBy(x => x.Service.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1) || request.Services.Where(x => x.Id.HasValue).GroupBy(x => x.Id).Any(x => x.Count() > 1)) return "Add 1–50 services with unique codes.";
        var categoryCodes = request.Services.SelectMany(x => x.Categories).Select(x => x.Category.Code.Trim()).ToArray();
        if (categoryCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != categoryCodes.Length) return "Category codes must be unique in the division.";
        if (request.Services.SelectMany(s => s.Categories).Where(c => c.Id.HasValue).GroupBy(c => c.Id).Any(g => g.Count() > 1)) return "An asset category may only appear once in setup.";
        foreach (var input in request.Services)
        {
            var s = input.Service;
            if (!Enum.IsDefined(s.Type) || !Enum.IsDefined(s.DefaultHireUnit) || !Enum.IsDefined(s.PersonnelRequirement) || !DivisionsController.ValidDocuments(s.RequiredDocumentsJson) || !DivisionsController.ValidObject(s.InspectionRequirementsJson) || !DivisionsController.ValidMaintenance(s.MaintenanceRulesJson)) return "Check the service documents, maintenance and inspection settings.";
            foreach (var c in input.Categories)
            {
                if (c.Category.DefaultAssetType.HasValue && !Enum.IsDefined(c.Category.DefaultAssetType.Value)) return "Choose a supported default asset type.";
                if (c.Attributes.Count > 80 || c.Attributes.Any(a => string.IsNullOrWhiteSpace(a.Code) || a.Code.Length > 40 || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 150 || !Enum.IsDefined(a.DataType)) || c.Attributes.GroupBy(a => a.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) return "Asset fields need unique codes, names and valid data types.";
                foreach (var a in c.Attributes.Where(a => a.DataType == AttributeDataType.Choice))
                { try { var choices = JsonSerializer.Deserialize<string[]>(a.OptionsJson ?? "[]"); if (choices is not { Length: > 0 and <= 100 } || choices.Any(string.IsNullOrWhiteSpace) || choices.Distinct().Count() != choices.Length) return $"Provide unique choices for {a.Name}."; } catch (JsonException) { return $"Check the choices for {a.Name}."; } }
                if (c.Templates.Where(t => t.IsActive).GroupBy(t => t.Stage).Any(x => x.Count() > 1)) return "Enable only one handover and one return template per category.";
                foreach (var t in c.Templates)
                {
                    if (t.Stage is not (InspectionStage.PreHire or InspectionStage.PostHire) || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 150) return "Check the inspection template name and stage.";
                    var error = OrganizationConfiguration.ValidateChecklist(t.ChecklistJson); if (error is not null) return error;
                }
            }
        }
        foreach (var q in request.Flow.CustomerFields ?? [])
        {
            if (!string.IsNullOrWhiteSpace(q.ServiceCode) && !request.Services.Any(s => s.Service.Code.Equals(q.ServiceCode, StringComparison.OrdinalIgnoreCase))) return "A customer question refers to an unknown service.";
            if (!string.IsNullOrWhiteSpace(q.CategoryCode) && !request.Services.SelectMany(s => s.Categories).Any(c => c.Category.Code.Equals(q.CategoryCode, StringComparison.OrdinalIgnoreCase))) return "A customer question refers to an unknown asset category.";
            if (!string.IsNullOrWhiteSpace(q.ServiceCode) && !string.IsNullOrWhiteSpace(q.CategoryCode) && !request.Services.Where(s => s.Service.Code.Equals(q.ServiceCode, StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.Categories).Any(c => c.Category.Code.Equals(q.CategoryCode, StringComparison.OrdinalIgnoreCase))) return "A customer question's category must belong to its selected service.";
        }
        return null;
    }
    private async Task<bool> DivisionInUse(Guid id, CancellationToken token) => await db.Assets.AnyAsync(x => x.DivisionId == id && x.IsActive, token) ||
        await db.Bookings.AnyAsync(x => x.Items.Any(i => i.Asset!.DivisionId == id) && x.Status != BookingStatus.Completed && x.Status != BookingStatus.Cancelled && x.Status != BookingStatus.Expired, token);
    private static void ApplyDivision(Division d, DivisionSetupDocument request)
    {
        var v = request.Division;
        d.Code = v.Code.Trim().ToUpperInvariant(); d.Name = v.Name.Trim(); d.Description = v.Description?.Trim(); d.ContactEmail = v.ContactEmail?.Trim(); d.ContactPhone = v.ContactPhone?.Trim();
        d.Capabilities = v.Capabilities; d.IsPublic = v.IsPublic; d.IsActive = v.IsActive; d.BrandingJson = v.BrandingJson ?? "{}";
        d.DefaultCurrency = "FJD"; d.DefaultTaxRate = v.DefaultTaxRate; d.DefaultBondAmount = v.DefaultBondAmount; d.DefaultRentalTerms = v.DefaultRentalTerms; d.DefaultApprovalWorkflowId = v.DefaultApprovalWorkflowId;
        d.CustomerBookingConfigurationJson = OrganizationConfiguration.Merge(d.CustomerBookingConfigurationJson, request.Flow); d.UpdatedAt = DateTimeOffset.UtcNow;
    }
    private static void ApplyService(ServiceOffering s, SaveServiceRequest v)
    {
        s.Code = v.Code.Trim().ToUpperInvariant(); s.Name = v.Name.Trim(); s.Description = v.Description; s.Type = v.Type; s.PersonnelRequirement = v.PersonnelRequirement;
        s.IsActive = v.IsActive; s.IsBookableOnline = v.IsBookableOnline; s.RequiresQuote = v.RequiresQuote; s.DefaultHireUnit = v.DefaultHireUnit; s.RequiresDelivery = v.RequiresDelivery;
        s.RequiredDocumentsJson = v.RequiredDocumentsJson ?? "[]"; s.DefaultDepositAmount = v.DefaultDepositAmount; s.InheritBond = v.InheritBond; s.MeterType = v.MeterType;
        s.InspectionRequirementsJson = v.InspectionRequirementsJson ?? "{}"; s.MaintenanceRulesJson = v.MaintenanceRulesJson ?? "{}"; s.UpdatedAt = DateTimeOffset.UtcNow;
    }
    private BadRequestObjectResult Invalid(string message) => BadRequest(new { message });
}

public sealed record DivisionSetupDocument(Guid? Id, string? Revision, [Required] SaveDivisionRequest Division, [Required] DivisionBookingFlow Flow, [Required] IReadOnlyList<SetupService> Services);
public sealed record SetupService(Guid? Id, [Required] SaveServiceRequest Service, IReadOnlyList<Guid>? BranchIds, [Required] IReadOnlyList<SetupCategory> Categories);
public sealed record SetupCategory(Guid? Id, [Required] CategoryRequest Category, [Required] IReadOnlyList<AttributeRequest> Attributes, [Required] IReadOnlyList<SetupTemplate> Templates);
public sealed record SetupTemplate(Guid? Id, string Name, InspectionStage Stage, string ChecklistJson, bool RequiresCustomerSignature = true, bool RequiresStaffSignature = true, bool IsActive = true);
