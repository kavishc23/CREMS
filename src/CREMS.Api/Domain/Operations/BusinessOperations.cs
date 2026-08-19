using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Rentals;

namespace CREMS.Api.Domain.Operations;

public sealed class AssetLifecycleEvent : Entity
{
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public AssetLifecycleEventType Type { get; set; }
    public AssetStatus? FromStatus { get; set; }
    public AssetStatus ToStatus { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? BookingId { get; set; }
    public decimal? MeterReading { get; set; }
    public string? Notes { get; set; }
    public string EvidenceJson { get; set; } = "[]";
    public Guid RecordedByUserId { get; set; }
    public required string RecordedByName { get; set; }
}

public sealed class AssetMeterReading : Entity
{
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public Guid? BookingId { get; set; }
    public MeterType Type { get; set; }
    public required string Unit { get; set; }
    public decimal Reading { get; set; }
    public decimal? FuelPercent { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public MeterReadingSource Source { get; set; }
    public Guid RecordedByUserId { get; set; }
}

public sealed class Supplier : Entity
{
    public required string SupplierNumber { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public int PaymentTermsDays { get; set; } = 30;
    public bool IsActive { get; set; } = true;
}

public sealed class MaintenancePartUsage : Entity
{
    public Guid MaintenanceJobId { get; set; }
    public MaintenanceJob? MaintenanceJob { get; set; }
    public Guid InventoryPartId { get; set; }
    public InventoryPart? InventoryPart { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public sealed class Personnel : Entity
{
    public required string EmployeeNumber { get; set; }
    public required string FullName { get; set; }
    public Guid BranchId { get; set; }
    public Guid DivisionId { get; set; }
    public PersonnelType Type { get; set; }
    public PersonnelAvailability Availability { get; set; } = PersonnelAvailability.Available;
    public decimal StandardCostRate { get; set; }
    public decimal OvertimeCostRate { get; set; }
    public decimal StandardChargeRate { get; set; }
    public decimal OvertimeChargeRate { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<PersonnelQualification> Qualifications { get; set; } = [];
}

public sealed class PersonnelQualification : Entity
{
    public Guid PersonnelId { get; set; }
    public Personnel? Personnel { get; set; }
    public required string Name { get; set; }
    public required string CertificateNumber { get; set; }
    public DateOnly IssuedOn { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public bool SafetyInduction { get; set; }
}

public sealed class BookingPersonnelAssignment : Entity
{
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public Guid PersonnelId { get; set; }
    public Personnel? Personnel { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public decimal CustomerHourlyRate { get; set; }
    public decimal InternalHourlyCost { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Scheduled;
    public ICollection<PersonnelTimesheet> Timesheets { get; set; } = [];
}

public sealed class PersonnelTimesheet : Entity
{
    public Guid AssignmentId { get; set; }
    public BookingPersonnelAssignment? Assignment { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal RegularHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public string? Notes { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}

public sealed class DeliveryZone : Entity
{
    public required string Name { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? DivisionId { get; set; }
    public decimal BaseCharge { get; set; }
    public decimal CostPerKilometre { get; set; }
    public decimal ChargePerKilometre { get; set; }
    public decimal FailedDeliveryCharge { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class QuoteRevision : Entity
{
    public Guid SalesQuoteId { get; set; }
    public SalesQuote? SalesQuote { get; set; }
    public int Version { get; set; }
    public required string SnapshotJson { get; set; }
    public required string ChangeReason { get; set; }
    public Guid ChangedByUserId { get; set; }
}

public sealed class ApprovalDelegation : Entity
{
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public Guid? DivisionId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class BusinessAlertRule : Entity
{
    public required string Name { get; set; }
    public AlertCategory Category { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? DivisionId { get; set; }
    public decimal? Threshold { get; set; }
    public int LeadTimeHours { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public bool EmailEnabled { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class BusinessAlert : Entity
{
    public Guid? RuleId { get; set; }
    public BusinessAlertRule? Rule { get; set; }
    public AlertCategory Category { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? DivisionId { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public TaskPriority Priority { get; set; }
    public DateTimeOffset RaisedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
}

public enum AssetLifecycleEventType { Purchased, Commissioned, Available, Allocated, PreHireInspection, CheckedOut, OnHire, PostHireInspection, DamageReported, MaintenanceStarted, ReturnedToService, Transferred, Retired, Disposed, Sold }
public enum MeterType { Odometer, EngineHours, OperatingHours, RentalDays, Units, FuelLevel }
public enum MeterReadingSource { Manual, PreHireInspection, PostHireInspection, Telematics, Maintenance, Transfer }
public enum PersonnelType { Operator, Driver, Technician, Labourer, Supervisor }
public enum PersonnelAvailability { Available, Assigned, Leave, Training, Unavailable }
public enum AssignmentStatus { Scheduled, Confirmed, InProgress, Completed, Cancelled }
public enum AlertCategory { OverdueReturn, UpcomingPickup, DelayedApproval, MaintenanceDue, ExpiringLicence, ExpiringInsurance, LossMakingAsset, LowAvailability, UnpaidInvoice, ExcessUsage, UnresolvedDamage }
