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
    public required string FaultDescription { get; set; }
    public string? AssignedTo { get; set; }
    public string? Supplier { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }
    public string? PartsUsed { get; set; }
    public DateTimeOffset ReportedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateOnly? NextServiceDate { get; set; }
}

public enum MaintenanceStatus { Open, InProgress, WaitingForParts, Completed, Cancelled }
