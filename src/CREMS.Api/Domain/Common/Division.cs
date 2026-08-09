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
}

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
