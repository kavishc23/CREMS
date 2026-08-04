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
    public ICollection<BookingItem> Items { get; set; } = [];
}

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
