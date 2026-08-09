using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Corporate;

public sealed class SalesQuote : Entity
{
    public required string QuoteNumber { get; set; }
    public Guid CustomerId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public DateTimeOffset ValidUntil { get; set; }
    public string? JobSite { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public int Version { get; set; } = 1;
    public string? LineItemsJson { get; set; }
    public string? LostReason { get; set; }
    public Guid? ConvertedBookingId { get; set; }
}

public sealed class CorporateAccount : Entity
{
    public Guid CustomerId { get; set; }
    public required string LegalName { get; set; }
    public string? TaxIdentificationNumber { get; set; }
    public string? BillingContactJson { get; set; }
    public string? AuthorizedContactsJson { get; set; }
    public string? JobSitesJson { get; set; }
    public decimal CreditLimit { get; set; }
    public int PaymentTermsDays { get; set; } = 30;
    public bool PurchaseOrderRequired { get; set; }
    public bool CreditHold { get; set; }
    public string? ContractPricingJson { get; set; }
}

public sealed class DispatchJob : Entity
{
    public required string DispatchNumber { get; set; }
    public Guid BookingId { get; set; }
    public Guid BranchId { get; set; }
    public DispatchType Type { get; set; }
    public DispatchStatus Status { get; set; } = DispatchStatus.Scheduled;
    public DateTimeOffset ScheduledAt { get; set; }
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? AssignedDriver { get; set; }
    public string? TransportVehicle { get; set; }
    public decimal DeliveryCharge { get; set; }
    public string? ProofJson { get; set; }
}

public sealed class AssetTransfer : Entity
{
    public required string TransferNumber { get; set; }
    public Guid AssetId { get; set; }
    public Guid FromBranchId { get; set; }
    public Guid ToBranchId { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Requested;
    public required string Reason { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? DepartedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public decimal? DepartureMeter { get; set; }
    public decimal? ArrivalMeter { get; set; }
    public string? InspectionJson { get; set; }
    public decimal TransferCost { get; set; }
}

public sealed class PricingRule : Entity
{
    public required string Name { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? AssetType { get; set; }
    public RatePeriod Period { get; set; }
    public decimal Rate { get; set; }
    public decimal IncludedUsage { get; set; }
    public decimal ExcessUsageRate { get; set; }
    public int MinimumDuration { get; set; } = 1;
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ApprovalRequest : Entity
{
    public required string RequestNumber { get; set; }
    public Guid BranchId { get; set; }
    public ApprovalType Type { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

public sealed class InventoryPart : Entity
{
    public required string PartNumber { get; set; }
    public required string Name { get; set; }
    public Guid BranchId { get; set; }
    public string? Supplier { get; set; }
    public decimal UnitCost { get; set; }
    public int QuantityOnHand { get; set; }
    public int QuantityAllocated { get; set; }
    public int QuantityOnOrder { get; set; }
    public int ReorderLevel { get; set; }
}

public sealed class PurchaseOrder : Entity
{
    public required string PurchaseOrderNumber { get; set; }
    public Guid BranchId { get; set; }
    public required string Supplier { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public decimal Total { get; set; }
    public string? LinesJson { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
}

public sealed class TelematicsSnapshot : Entity
{
    public Guid AssetId { get; set; }
    public required string Provider { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? Odometer { get; set; }
    public decimal? EngineHours { get; set; }
    public decimal? FuelPercent { get; set; }
    public string? FaultCodesJson { get; set; }
    public bool UnauthorizedMovement { get; set; }
}

public sealed class CustomerCase : Entity
{
    public required string CaseNumber { get; set; }
    public Guid CustomerId { get; set; }
    public Guid BranchId { get; set; }
    public CaseType Type { get; set; }
    public CaseStatus Status { get; set; } = CaseStatus.Open;
    public CasePriority Priority { get; set; } = CasePriority.Normal;
    public required string Subject { get; set; }
    public required string Description { get; set; }
    public Guid? AssignedUserId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public string? Resolution { get; set; }
}

public sealed class DocumentRecord : Entity
{
    public required string DocumentNumber { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public Guid? BranchId { get; set; }
    public required string Type { get; set; }
    public required string FileName { get; set; }
    public required string StoragePath { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? ContentHash { get; set; }
}

public sealed class ManagementTask : Entity
{
    public Guid? BranchId { get; set; }
    public TaskCategory Category { get; set; }
    public TaskPriority Priority { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public Guid? AssignedUserId { get; set; }
    public bool IsCompleted { get; set; }
    public string? SourceEntityType { get; set; }
    public Guid? SourceEntityId { get; set; }
}

public enum QuoteStatus { Draft, Sent, Negotiating, Accepted, Rejected, Expired, Converted }
public enum DispatchType { CustomerPickup, Delivery, CustomerReturn, Collection }
public enum DispatchStatus { Scheduled, Assigned, EnRoute, Arrived, Completed, Failed, Cancelled }
public enum TransferStatus { Requested, Approved, InTransit, Received, Inspected, Rejected, Cancelled }
public enum RatePeriod { Hourly, Daily, Weekly, Monthly }
public enum ApprovalType { Discount, DepositWaiver, Refund, DamageWaiver, CreditLimit, AssetTransfer, PurchaseOrder, MajorRepair, WriteOff, AssetDisposal }
public enum ApprovalStatus { Pending, Approved, Rejected, Cancelled }
public enum PurchaseOrderStatus { Draft, PendingApproval, Approved, Ordered, PartiallyReceived, Received, Cancelled }
public enum CaseType { Enquiry, Complaint, Dispute, RefundRequest, Breakdown, General }
public enum CaseStatus { Open, InProgress, WaitingForCustomer, Resolved, Closed }
public enum CasePriority { Low, Normal, High, Critical }
public enum TaskCategory { OverdueRental, UnpaidInvoice, ExpiringDocument, MaintenanceDue, PendingApproval, LowStock, FailedNotification, QuoteFollowUp, CustomerCase }
public enum TaskPriority { Low, Normal, High, Critical }
