namespace CREMS.Api.Domain.Common;

public sealed class Division : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public bool IsPublic { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DivisionCapabilities Capabilities { get; set; }
    public string BrandingJson { get; set; } = "{}";
    public string DefaultCurrency { get; set; } = "FJD";
    public decimal DefaultTaxRate { get; set; } = 15m;
    public decimal DefaultBondAmount { get; set; }
    public string? DefaultRentalTerms { get; set; }
    public Guid? DefaultApprovalWorkflowId { get; set; }
    public string CustomerBookingConfigurationJson { get; set; } = "{}";
    public ICollection<ServiceOffering> ServiceOfferings { get; set; } = [];
}

[Flags]
public enum DivisionCapabilities
{
    None = 0,
    Rental = 1,
    Maintenance = 2,
    PropertyLeasing = 4,
    Logistics = 8,
    Retail = 16,
    PersonnelSupportedHire = 32,
}

public sealed class ServiceOffering : Entity
{
    public Guid DivisionId { get; set; }
    public Division? Division { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ServiceOfferingType Type { get; set; }
    public PersonnelRequirement PersonnelRequirement { get; set; }
    public bool IsBookableOnline { get; set; }
    public bool RequiresQuote { get; set; }
    public bool IsActive { get; set; } = true;
    public ChargeUnit DefaultHireUnit { get; set; } = ChargeUnit.Day;
    public bool RequiresDelivery { get; set; }
    public string RequiredDocumentsJson { get; set; } = "[]";
    public decimal DefaultDepositAmount { get; set; }
    public bool InheritBond { get; set; }
    public string InspectionRequirementsJson { get; set; } = "{}";
    public string? MeterType { get; set; }
    public string MaintenanceRulesJson { get; set; } = "{}";
    public ICollection<ChargeDefinition> ChargeDefinitions { get; set; } = [];
}

public sealed class ChargeDefinition : Entity
{
    public Guid DivisionId { get; set; }
    public Division? Division { get; set; }
    public Guid? ServiceOfferingId { get; set; }
    public ServiceOffering? ServiceOffering { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public ChargeCategory Category { get; set; }
    public ChargeUnit Unit { get; set; }
    public decimal DefaultSellingRate { get; set; }
    public decimal DefaultCostRate { get; set; }
    public bool IsTaxable { get; set; } = true;
    public bool IsRequired { get; set; }
    public bool IsCustomerVisible { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public enum ChargeUnit { Hour, Day, Week, Month, Unit, Trip, Kilometre, Tonne, SquareMetre, Fixed }
public enum ChargeCategory { BaseHire, Operator, Driver, Transport, Labour, Maintenance, Fuel, Cleaning, Setup, Insurance, Other }

public enum ServiceOfferingType
{
    VehicleRental,
    EquipmentHire,
    PropertyLease,
    LogisticsService,
    MaintenanceService,
    Other,
}

public enum PersonnelRequirement
{
    None,
    Optional,
    Required,
}

public sealed class AssetCategory : Entity
{
    public Guid DivisionId { get; set; }
    public Division? Division { get; set; }
    public Guid? ServiceOfferingId { get; set; }
    public ServiceOffering? ServiceOffering { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? DefaultMeterType { get; set; }
    public PersonnelRequirement PersonnelRequirement { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<AssetAttributeDefinition> AttributeDefinitions { get; set; } = [];
}

public sealed class AssetAttributeDefinition : Entity
{
    public Guid AssetCategoryId { get; set; }
    public AssetCategory? AssetCategory { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public AttributeDataType DataType { get; set; }
    public string? Unit { get; set; }
    public bool IsRequired { get; set; }
    public bool IsSearchable { get; set; }
    public bool IsCustomerVisible { get; set; }
    public bool IsReportable { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string OptionsJson { get; set; } = "[]";
}
public enum AttributeDataType { Text, Number, Integer, Boolean, Date, Choice }
