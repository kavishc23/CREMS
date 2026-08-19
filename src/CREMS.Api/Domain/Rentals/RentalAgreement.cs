using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Rentals;

public sealed class RentalAgreement : Entity
{
    public required string AgreementNumber { get; set; }
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }
    public required string TermsVersion { get; set; }
    public required string TermsJson { get; set; }
    public required string CustomerSnapshotJson { get; set; }
    public required string AssetSnapshotJson { get; set; }
    public required string PricingSnapshotJson { get; set; }
    public required string CustomerSignatureName { get; set; }
    public string? CustomerSignatureDataUrl { get; set; }
    public DateTimeOffset CustomerSignedAt { get; set; }
    public Guid ApprovedByUserId { get; set; }
    public required string ApprovedByName { get; set; }
    public DateTimeOffset ApprovedAt { get; set; }
    public AgreementStatus Status { get; set; } = AgreementStatus.Signed;
    public string? LastEmailedTo { get; set; }
    public DateTimeOffset? LastEmailedAt { get; set; }
    public Guid? LastEmailId { get; set; }
    public ICollection<RentalAgreementAddendum> Addendums { get; set; } = [];
}

public enum AgreementStatus { Draft, ReadyForPickup, Signed, Active, Completed, Superseded }
