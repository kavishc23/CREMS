using CREMS.Api.Services;
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

[ApiController, Route("api/assets/{assetId:guid}"), Authorize(Policy=SystemPermissions.AssetsView)]
public sealed class AssetProfilesController(ApplicationDbContext db, CurrentStaffScope staffScope):ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult> Profile(Guid assetId,CancellationToken token)
    {
        var asset=await db.Assets.AsNoTracking().Include(x=>x.Branch).Include(x=>x.Division).Include(x=>x.ServiceOffering).Include(x=>x.AssetCategory).ThenInclude(x=>x!.AttributeDefinitions).Include(x=>x.AttributeValues).ThenInclude(x=>x.AttributeDefinition).FirstOrDefaultAsync(x=>x.Id==assetId,token);
        if(asset is null)return NotFound();var scope=await staffScope.GetAsync(User);if(scope is null||!scope.HasAssetAccess(asset.BranchId,asset.DivisionId))return Forbid();
        var financialRequirement = new PermissionRequirement(SystemPermissions.AssetsViewFinancials);
        var financialContext = new AuthorizationHandlerContext([financialRequirement], User, null);
        await new PermissionAuthorizationHandler(db).HandleAsync(financialContext);
        var canFinancial = financialContext.HasSucceeded;
        const int historyLimit = 50;
        var bookingItems=await db.BookingItems.AsNoTracking().Where(x=>x.AssetId==assetId).Include(x=>x.Booking).OrderByDescending(x=>x.StartAt).Take(historyLimit).ToListAsync(token);
        var maintenance=await db.MaintenanceJobs.AsNoTracking().Where(x=>x.AssetId==assetId).OrderByDescending(x=>x.ReportedAt).Take(historyLimit).ToListAsync(token);
        var inspections=await db.AssetInspections.AsNoTracking().Where(x=>x.AssetId==assetId).OrderByDescending(x=>x.CompletedAt).ThenByDescending(x=>x.Id).ToListAsync(token);
        var rentalQuery = db.RentalInspections.AsNoTracking()
            .Where(x => x.Booking!.Items.Any(item => item.AssetId == assetId));
        if (!scope.IsAdministrator)
            rentalQuery = rentalQuery.Where(x => scope.BranchIds.Contains(x.Booking!.BranchId));
        var rentalInspections = await rentalQuery.OrderByDescending(x => x.CompletedAt).ThenByDescending(x => x.Id)
            .Select(x => new { x.Id, x.BookingId, x.Booking!.BookingNumber, x.Type, x.CompletedAt,
                x.CompletedByName, x.ConditionNotes, x.DamageNotes, x.MeterReading, x.FuelLevelPercent,
                x.EvidenceJson, x.SignatureName, x.SignatureDataUrl }).ToListAsync(token);
        var lifecycle=await db.AssetLifecycleEvents.AsNoTracking().Where(x=>x.AssetId==assetId).OrderByDescending(x=>x.OccurredAt).Take(historyLimit).ToListAsync(token);
        var meters=await db.AssetMeterReadings.AsNoTracking().Where(x=>x.AssetId==assetId).OrderByDescending(x=>x.RecordedAt).Take(historyLimit).ToListAsync(token);
        var transfers=await db.AssetTransfers.AsNoTracking().Where(x=>x.AssetId==assetId).OrderByDescending(x=>x.CreatedAt).Take(50).ToListAsync(token);
        var documents=await db.DocumentRecords.AsNoTracking().Where(x=>x.EntityType==nameof(Asset)&&x.EntityId==assetId).OrderByDescending(x=>x.CreatedAt).Take(historyLimit).ToListAsync(token);
        var audits=await db.AuditEvents.AsNoTracking().Where(x=>x.EntityType==nameof(Asset)&&x.EntityId==assetId).OrderByDescending(x=>x.OccurredAt).Take(historyLimit).ToListAsync(token);
        var now=DateTimeOffset.UtcNow;var future=bookingItems.Where(x=>x.EndAt>now&&x.Booking!.Status is not BookingStatus.Cancelled and not BookingStatus.Expired).OrderBy(x=>x.StartAt).ToList();
        var unavailableReason=asset.Status switch{AssetStatus.Maintenance=>"Under maintenance",AssetStatus.Rented=>"Currently on hire",AssetStatus.Reserved=>"Reserved for a customer",AssetStatus.Inspection=>"Awaiting inspection",AssetStatus.OutOfService=>"Out of service",AssetStatus.Retired=>"Retired",_=>future.FirstOrDefault() is { } b?$"Reserved from {b.StartAt:dd MMM yyyy}":null};
        var alternativeQuery=db.Assets.AsNoTracking().Where(x=>x.Id!=assetId&&x.IsActive&&x.Status==AssetStatus.Available&&x.BranchId==asset.BranchId&&(asset.AssetCategoryId==null?x.Type==asset.Type:x.AssetCategoryId==asset.AssetCategoryId));
        if(future.Count>0){var rangeStart=future.Min(x=>x.StartAt);var rangeEnd=future.Max(x=>x.EndAt);alternativeQuery=alternativeQuery.Where(x=>!db.BookingItems.Any(i=>i.AssetId==x.Id&&i.StartAt<rangeEnd&&rangeStart<i.EndAt&&i.Booking!.Status!=BookingStatus.Cancelled&&i.Booking.Status!=BookingStatus.Expired));}
        var alternatives=await alternativeQuery.Select(x=>new{x.Id,x.AssetNumber,x.Name,x.Status}).Take(8).ToListAsync(token);
        var assetProfile=new
        {
            asset.Id,asset.AssetNumber,asset.Name,asset.Type,asset.Status,asset.DivisionId,asset.ServiceOfferingId,
            asset.BranchId,asset.RegistrationNumber,asset.SerialNumber,asset.Manufacturer,asset.Model,asset.ModelYear,
            asset.VinOrChassisNumber,asset.EngineNumber,asset.MeterUnit,asset.CurrentMeterReading,asset.AcquisitionDate,
            asset.AcquisitionCost,asset.CurrentBookValue,asset.OwnershipType,asset.InsurancePolicyNumber,
            asset.InsuranceExpiry,asset.WarrantyExpiry,asset.CurrentLocation,asset.PhotoUrlsJson,asset.Category,
            asset.AssetCategoryId,asset.SpecificationsJson,PersonnelRequirement = AssetCategoryPolicy.Personnel(asset),asset.DailyRate,asset.NextServiceDate,
            asset.DefaultBondAmount, asset.InheritBond, BondAmount = AssetCategoryPolicy.Bond(asset), asset.PersonnelOverride,
            asset.IsActive,
            branch=asset.Branch is null?null:new{asset.Branch.Id,asset.Branch.Code,asset.Branch.Name},
            division=asset.Division is null?null:new{asset.Division.Id,asset.Division.Code,asset.Division.Name},
            serviceOffering=asset.ServiceOffering is null?null:new{asset.ServiceOffering.Id,asset.ServiceOffering.Code,asset.ServiceOffering.Name},
            assetCategory=asset.AssetCategory is null?null:new{asset.AssetCategory.Id,asset.AssetCategory.Code,asset.AssetCategory.Name,asset.AssetCategory.DefaultMeterType},
            attributeValues=asset.AttributeValues.Select(value=>new
            {
                value.Id,value.AttributeDefinitionId,value.Value,
                attributeDefinition=value.AttributeDefinition is null?null:new
                {
                    value.AttributeDefinition.Id,value.AttributeDefinition.Code,value.AttributeDefinition.Name,
                    value.AttributeDefinition.DataType,value.AttributeDefinition.Unit,value.AttributeDefinition.IsRequired,
                    value.AttributeDefinition.IsCustomerVisible,value.AttributeDefinition.DisplayOrder
                }
            })
        };
        return Ok(new{asset=assetProfile,availability=new{isAvailable=asset.Status==AssetStatus.Available,unavailableReason,expectedAvailableAt=future.Select(x=>(DateTimeOffset?)x.EndAt).FirstOrDefault(),bookings=bookingItems.Select(x=>new{x.StartAt,x.EndAt,status=x.Booking!.Status,reference=x.Booking.BookingNumber}),maintenance=maintenance.Select(x=>new{x.ReportedAt,endAt=x.CompletedAt,status=x.Status,reference=x.JobNumber}),transfers},alternatives,inspections,rentalInspections,meters,maintenance = maintenance.Select(x => MaintenanceWorkspace.Record(x, canFinancial)),documents,lifecycle,audits});
    }

    [HttpPut("attributes"),Authorize(Policy=SystemPermissions.AssetsEdit)]
    public async Task<ActionResult> SaveAttributes(Guid assetId,IReadOnlyList<AssetAttributeInput> values,CancellationToken token)
    {var asset=await AssetForScope(assetId,token);if(asset.Result is not null)return asset.Result;var item=asset.Value!;if(!item.AssetCategoryId.HasValue)return BadRequest(new{message="Assign an asset category first."});var definitions=await db.AssetAttributeDefinitions.Where(x=>x.AssetCategoryId==item.AssetCategoryId).ToListAsync(token);foreach(var required in definitions.Where(x=>x.IsRequired))if(!values.Any(x=>x.AttributeDefinitionId==required.Id&&!string.IsNullOrWhiteSpace(x.Value)))return BadRequest(new{message=$"{required.Name} is required."});var current=await db.AssetAttributeValues.Where(x=>x.AssetId==assetId).ToListAsync(token);db.AssetAttributeValues.RemoveRange(current);foreach(var value in values.Where(x=>!string.IsNullOrWhiteSpace(x.Value)&&definitions.Any(d=>d.Id==x.AttributeDefinitionId)))db.AssetAttributeValues.Add(new AssetAttributeValue{AssetId=assetId,AttributeDefinitionId=value.AttributeDefinitionId,Value=value.Value.Trim()});await db.SaveChangesAsync(token);return NoContent();}

    [HttpPost("meters"),Authorize(Policy=SystemPermissions.AssetsRecordMeter)]
    public async Task<ActionResult> Meter(Guid assetId,AssetMeterInput request,CancellationToken token)
    {await using var transaction = await MaintenanceRules.BeginAsync(db, token);var result=await AssetForScope(assetId,token);if(result.Result is not null)return result.Result;var asset=result.Value!;if(asset.CurrentMeterReading.HasValue&&request.Reading<asset.CurrentMeterReading)return BadRequest(new{message="A cumulative meter reading cannot decrease."});var scope=(await staffScope.GetAsync(User))!;var reading=new AssetMeterReading{AssetId=assetId,BookingId=request.BookingId,Type=request.Type,Unit=request.Unit.Trim(),Reading=request.Reading,FuelPercent=request.FuelPercent,Source=request.Source,RecordedByUserId=scope.UserId};asset.CurrentMeterReading=request.Reading;asset.MeterUnit=request.Unit.Trim();db.AssetMeterReadings.Add(reading);await MaintenanceRules.GenerateMeterJobAsync(db,asset,token);AuditWriter.Record(db,scope,"Asset meter recorded",nameof(Asset),asset.Id,$"{request.Reading} {request.Unit}",asset.BranchId);await db.SaveChangesAsync(token);if(transaction is not null)await transaction.CommitAsync(token);return Ok(reading);}

    [HttpPost("inspections"),Authorize(Policy=SystemPermissions.AssetsInspect)]
    public async Task<ActionResult> Inspect(Guid assetId,AssetInspectionInput request,CancellationToken token)
    {await using var transaction = await MaintenanceRules.BeginAsync(db, token);var result=await AssetForScope(assetId,token);if(result.Result is not null)return result.Result;var asset=result.Value!;var scope=(await staffScope.GetAsync(User))!;if(request.MeterReading.HasValue&&asset.CurrentMeterReading.HasValue&&request.MeterReading<asset.CurrentMeterReading)return BadRequest(new{message="The inspection meter reading cannot decrease."});if(request.Stage is InspectionStage.PreHire or InspectionStage.PostHire)return BadRequest(new{message="Complete pre-hire and post-hire inspections from Hire operations for the related booking."});if(request.Outcome is InspectionOutcome.Failed or InspectionOutcome.DamageDetected && asset.Status is AssetStatus.Rented or AssetStatus.Reserved or AssetStatus.Retired)return BadRequest(new{message="Resolve the hire or reservation through Hire operations before taking this asset into maintenance."});var previousStatus=asset.Status;var inspection=new AssetInspection{AssetId=assetId,BookingId=request.BookingId,TemplateId=request.TemplateId,Stage=request.Stage,Outcome=request.Outcome,ResponsesJson=request.ResponsesJson??"[]",EvidenceJson=request.EvidenceJson??"[]",DamageMapJson=request.DamageMapJson,MeterReading=request.MeterReading,FuelPercent=request.FuelPercent,CustomerSignatureName=request.CustomerSignatureName,CustomerSignatureDataUrl=request.CustomerSignatureDataUrl,StaffSignatureName=request.StaffSignatureName,StaffSignatureDataUrl=request.StaffSignatureDataUrl,Notes=request.Notes,CompletedByUserId=scope.UserId,CompletedByName=scope.UserName};db.AssetInspections.Add(inspection);if(request.MeterReading.HasValue){asset.CurrentMeterReading=request.MeterReading;db.AssetMeterReadings.Add(new AssetMeterReading{AssetId=assetId,BookingId=request.BookingId,Type=asset.MeterUnit?.Contains("hour",StringComparison.OrdinalIgnoreCase)==true?MeterType.EngineHours:MeterType.Odometer,Unit=asset.MeterUnit??"unit",Reading=request.MeterReading.Value,FuelPercent=request.FuelPercent,Source=request.Stage==InspectionStage.PreHire?MeterReadingSource.PreHireInspection:MeterReadingSource.PostHireInspection,RecordedByUserId=scope.UserId});}if(request.Outcome is InspectionOutcome.Failed or InspectionOutcome.DamageDetected){asset.Status=AssetStatus.Maintenance;db.MaintenanceJobs.Add(new MaintenanceJob{JobNumber=$"MNT-{Guid.NewGuid():N}"[..29],AssetId=assetId,BranchId=asset.BranchId,ServiceType=request.Outcome==InspectionOutcome.DamageDetected?"Damage assessment":"Inspection failure",FaultDescription=string.IsNullOrWhiteSpace(request.Notes)?"Created automatically from failed inspection":request.Notes.Trim(),SourceType=request.Outcome==InspectionOutcome.DamageDetected?"Damage":"Inspection",SourceInspectionId=inspection.Id,SourceReference=$"{request.Stage} inspection",ReportedByName=scope.UserName,Priority=MaintenancePriority.High,Status=MaintenanceStatus.Open});}db.AssetLifecycleEvents.Add(new AssetLifecycleEvent{AssetId=assetId,BookingId=request.BookingId,Type=request.Stage==InspectionStage.PreHire?AssetLifecycleEventType.PreHireInspection:request.Outcome==InspectionOutcome.DamageDetected?AssetLifecycleEventType.DamageReported:AssetLifecycleEventType.PostHireInspection,FromStatus=previousStatus,ToStatus=asset.Status,MeterReading=request.MeterReading,Notes=request.Notes,EvidenceJson=request.EvidenceJson??"[]",RecordedByUserId=scope.UserId,RecordedByName=scope.UserName});await db.SaveChangesAsync(token);if(transaction is not null)await transaction.CommitAsync(token);return Ok(inspection);}

    [HttpGet("inspection-templates")]
    public async Task<ActionResult> Templates(Guid assetId,CancellationToken token){var result=await AssetForScope(assetId,token);if(result.Result is not null)return result.Result;var category=result.Value!.AssetCategoryId;if(!category.HasValue)return Ok(Array.Empty<object>());return Ok(await db.InspectionTemplates.AsNoTracking().Where(x=>x.AssetCategoryId==category&&x.IsActive).OrderBy(x=>x.Stage).ThenBy(x=>x.Name).ToListAsync(token));}

    [HttpPost("inspection-templates"),Authorize(Policy=SystemPermissions.AssetCategoriesConfigure)]
    public async Task<ActionResult> CreateTemplate(Guid assetId,InspectionTemplateInput request,CancellationToken token){var result=await AssetForScope(assetId,token);if(result.Result is not null)return result.Result;var asset=result.Value!;if(!asset.AssetCategoryId.HasValue)return BadRequest(new{message="Assign a category first."});var template=new InspectionTemplate{AssetCategoryId=asset.AssetCategoryId.Value,Name=request.Name.Trim(),Stage=request.Stage,ChecklistJson=request.ChecklistJson??"[]",RequiresCustomerSignature=request.RequiresCustomerSignature,RequiresStaffSignature=request.RequiresStaffSignature};db.InspectionTemplates.Add(template);await db.SaveChangesAsync(token);return Ok(template);}

    private async Task<ActionResult<Asset>> AssetForScope(Guid id,CancellationToken token){var item=await db.Assets.FirstOrDefaultAsync(x=>x.Id==id,token);if(item is null)return NotFound();var scope=await staffScope.GetAsync(User);if(scope is null||!scope.HasAssetAccess(item.BranchId,item.DivisionId))return Forbid();return item;}

}
public sealed record AssetAttributeInput(Guid AttributeDefinitionId,string Value);
public sealed record AssetMeterInput(MeterType Type,string Unit,decimal Reading,decimal? FuelPercent,MeterReadingSource Source,Guid? BookingId);
public sealed record AssetInspectionInput(Guid? BookingId,Guid? TemplateId,InspectionStage Stage,InspectionOutcome Outcome,string? ResponsesJson,string? EvidenceJson,string? DamageMapJson,decimal? MeterReading,decimal? FuelPercent,string? CustomerSignatureName,string? CustomerSignatureDataUrl,string? StaffSignatureName,string? StaffSignatureDataUrl,string? Notes);
public sealed record InspectionTemplateInput(string Name,InspectionStage Stage,string? ChecklistJson,bool RequiresCustomerSignature,bool RequiresStaffSignature);
