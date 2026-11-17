namespace CREMS.Api.Domain.Rentals;

public sealed class IncidentSettlement
{
    public Guid BookingId { get; set; }
    public required string DetailsJson { get; set; }
    public DateOnly? PaymentDueDate { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
