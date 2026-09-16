using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Assets;

public sealed class Asset : Entity
{
    public required string AssetNumber { get; set; }
    public required string Name { get; set; }
    public AssetType Type { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Available;
    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }
    public Guid? ServiceOfferingId { get; set; }
    public ServiceOffering? ServiceOffering { get; set; }
    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public int? ModelYear { get; set; }
    public string? VinOrChassisNumber { get; set; }
    public string? EngineNumber { get; set; }
    public string? MeterUnit { get; set; }
    public decimal? CurrentMeterReading { get; set; }
    public DateOnly? AcquisitionDate { get; set; }
    public decimal AcquisitionCost { get; set; }
    public decimal? CurrentBookValue { get; set; }
    public string? OwnershipType { get; set; }
    public string? InsurancePolicyNumber { get; set; }
    public DateOnly? InsuranceExpiry { get; set; }
    public DateOnly? WarrantyExpiry { get; set; }
    public string? CurrentLocation { get; set; }
    public string PhotoUrlsJson { get; set; } = "[]";
    public string? Category { get; set; }
    public Guid? AssetCategoryId { get; set; }
    public AssetCategory? AssetCategory { get; set; }
    public string? SpecificationsJson { get; set; }
    public ICollection<AssetAttributeValue> AttributeValues { get; set; } = [];
    public PersonnelRequirement PersonnelRequirement { get; set; }
    public bool RequiresDelivery { get; set; }
    public decimal DailyRate { get; set; }
    public decimal DefaultBondAmount { get; set; }
    public bool InheritBond { get; set; }
    public PersonnelRequirement? PersonnelOverride { get; set; }
    public DateOnly? NextServiceDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AssetAttributeValue : Entity
{
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public Guid AttributeDefinitionId { get; set; }
    public AssetAttributeDefinition? AttributeDefinition { get; set; }
    public string? Value { get; set; }
}

public sealed class AssetCostEntry : Entity
{
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public Guid BranchId { get; set; }
    public Guid? BookingId { get; set; }
    public AssetCostCategory Category { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly OccurredOn { get; set; }
    public string? Supplier { get; set; }
    public string? ReferenceNumber { get; set; }
    public Guid RecordedByUserId { get; set; }
    public required string RecordedByName { get; set; }
}

public enum AssetCostCategory { Transport, Labour, Operator, Fuel, Cleaning, Insurance, Registration, Storage, Damage, Other }

public sealed class InspectionTemplate : Entity
{
    public Guid AssetCategoryId { get; set; }
    public AssetCategory? AssetCategory { get; set; }
    public required string Name { get; set; }
    public InspectionStage Stage { get; set; }
    public string ChecklistJson { get; set; } = "[]";
    public bool RequiresCustomerSignature { get; set; }
    public bool RequiresStaffSignature { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public sealed class AssetInspection : Entity
{
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? TemplateId { get; set; }
    public InspectionTemplate? Template { get; set; }
    public InspectionStage Stage { get; set; }
    public InspectionOutcome Outcome { get; set; }
    public string ResponsesJson { get; set; } = "[]";
    public string EvidenceJson { get; set; } = "[]";
    public string? DamageMapJson { get; set; }
    public decimal? MeterReading { get; set; }
    public decimal? FuelPercent { get; set; }
    public string? CustomerSignatureName { get; set; }
    public string? CustomerSignatureDataUrl { get; set; }
    public string? StaffSignatureName { get; set; }
    public string? StaffSignatureDataUrl { get; set; }
    public string? Notes { get; set; }
    public Guid CompletedByUserId { get; set; }
    public required string CompletedByName { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum InspectionStage { Commissioning, PreHire, PostHire, TransferOut, TransferIn, Maintenance, Disposal }
public enum InspectionOutcome { Passed, PassedWithNotes, Failed, DamageDetected }

public enum AssetType
{
    Vehicle = 0,             // Legacy data compatibility
    Equipment = 1,           // Legacy data compatibility
    PassengerVehicle = 2,
    CommercialVehicle = 3,
    HeavyEquipment = 4,
    MaterialHandlingEquipment = 5,
    PowerEquipment = 6,
    LightEquipment = 7,
    Scaffolding = 8,
    PortableSanitation = 9,
    WasteContainer = 10,
}

public enum AssetStatus
{
    Available,
    Reserved,
    Rented,
    Inspection,
    Maintenance,
    OutOfService,
    Retired
}
