using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;

namespace CREMS.Api.Domain.Rentals;

public sealed class Booking : Entity
{
    public required string BookingNumber { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Draft;
    public string? Notes { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; } = 15m;
    public decimal DepositRequired { get; set; }
    public decimal AdditionalCharges { get; set; }
    public string? AdditionalChargesDescription { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public ICollection<BookingItem> Items { get; set; } = [];
    public ICollection<RentalInspection> Inspections { get; set; } = [];
    public RentalAgreement? RentalAgreement { get; set; }
    public ICollection<AuthorizedDriver> AuthorizedDrivers { get; set; } = [];
    public ICollection<RentalPayment> Payments { get; set; } = [];
    public ICollection<RentalNotification> Notifications { get; set; } = [];
    public ICollection<RentalIncident> Incidents { get; set; } = [];
    public RentalInvoice? Invoice { get; set; }
}

public sealed class RentalInspection : Entity
{
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public InspectionType Type { get; set; }
    public bool IdentificationVerified { get; set; }
    public bool DriverLicenceVerified { get; set; }
    public decimal? MeterReading { get; set; }
    public int? FuelLevelPercent { get; set; }
    public string? ConditionNotes { get; set; }
    public string? DamageNotes { get; set; }
    public string? SignatureName { get; set; }
    public string? SignatureDataUrl { get; set; }
    public bool PaymentVerified { get; set; }
    public string? EvidenceJson { get; set; }
    public Guid CompletedByUserId { get; set; }
    public string? CompletedByName { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum InspectionType { Handover, Return }

public sealed class BookingItem : Entity
{
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public decimal DailyRate { get; set; }
}

public enum BookingStatus
{
    Draft,
    Confirmed,
    Cancelled,
    ConvertedToRental,
    Completed,
    Expired
}
