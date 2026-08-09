using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Rentals;

public sealed class AuthorizedDriver : Entity
{
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public required string FullName { get; set; }
    public required string LicenceNumber { get; set; }
    public string? LicenceClass { get; set; }
    public DateOnly LicenceExpiry { get; set; }
    public bool IsPrimary { get; set; }
    public bool Verified { get; set; }
}

public sealed class RentalPayment : Entity
{
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public PaymentType Type { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public required string ReceiptNumber { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Recorded;
    public string? Note { get; set; }
    public Guid RecordedByUserId { get; set; }
    public required string RecordedByName { get; set; }
}

public sealed class RentalNotification : Entity
{
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public NotificationChannel Channel { get; set; }
    public required string Recipient { get; set; }
    public required string Subject { get; set; }
    public required string Message { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Queued;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public string? FailureReason { get; set; }
}

public sealed class RentalIncident : Entity
{
    public required string IncidentNumber { get; set; }
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public IncidentType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string Description { get; set; }
    public string? Location { get; set; }
    public string? PoliceReference { get; set; }
    public string? InsuranceClaimNumber { get; set; }
    public decimal EstimatedCost { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;
    public string? EvidenceJson { get; set; }
}

public sealed class RentalInvoice : Entity
{
    public required string InvoiceNumber { get; set; }
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public required string LineItemsJson { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal BalanceDue { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RentalAgreementAddendum : Entity
{
    public Guid RentalAgreementId { get; set; }
    public RentalAgreement? RentalAgreement { get; set; }
    public required string AddendumNumber { get; set; }
    public required string Reason { get; set; }
    public required string ChangesJson { get; set; }
    public required string CustomerSignatureName { get; set; }
    public DateTimeOffset CustomerSignedAt { get; set; }
    public Guid ApprovedByUserId { get; set; }
    public required string ApprovedByName { get; set; }
}

public enum PaymentType { RentalCharge, Deposit, Refund, AdditionalCharge }
public enum PaymentMethod { Cash, Card, BankTransfer, PurchaseOrder, MobileMoney }
public enum PaymentStatus { Recorded, Refunded, Voided }
public enum NotificationChannel { Email, Sms }
public enum NotificationStatus { Queued, Sent, Failed }
public enum IncidentType { Accident, Damage, Breakdown, Theft, TrafficOffence, Other }
public enum IncidentStatus { Open, Investigating, AwaitingInsurance, Resolved, Closed }
public enum InvoiceStatus { Draft, Issued, PartiallyPaid, Paid, Voided }
