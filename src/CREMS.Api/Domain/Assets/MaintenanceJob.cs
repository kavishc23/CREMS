using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Assets;

public sealed class MaintenanceJob : Entity
{
    public required string JobNumber { get; set; }
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }
    public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Open;
    public required string ServiceType { get; set; }
    public string FaultDescription { get; set; } = string.Empty;
    public string? Description { get; set; }
    public MaintenancePriority Priority { get; set; } = MaintenancePriority.Normal;
    public string? AssignedTo { get; set; }
    public string? Supplier { get; set; }
    public Guid? SupplierId { get; set; }
    public bool IsPreventive { get; set; }
    public decimal? NextServiceMeter { get; set; }
    public string? WarrantyClaimNumber { get; set; }
    public bool WarrantyCovered { get; set; }
    public Guid? ParentFailureJobId { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }
    public decimal PartsCost { get; set; }
    public decimal LabourCost { get; set; }
    public decimal TransportCost { get; set; }
    public decimal ExternalServiceCost { get; set; }
    public decimal TaxCost { get; set; }
    public decimal OtherCost { get; set; }
    public string? PartsUsed { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal? MeterReading { get; set; }
    public int DowntimeHours { get; set; }
    public DateTimeOffset ReportedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateOnly? NextServiceDate { get; set; }
}

public enum MaintenanceStatus { Open, InProgress, WaitingForParts, Completed, Cancelled }
public enum MaintenancePriority { Low, Normal, High, Critical }
