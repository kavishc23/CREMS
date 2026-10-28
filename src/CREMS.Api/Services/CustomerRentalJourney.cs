using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Rentals;

namespace CREMS.Api.Services;

/// <summary>Customer stages combine the quotation and its linked booking without changing operational statuses.</summary>
public static class CustomerRentalJourney
{
    public static bool CustomerCancelled(Booking booking) =>
        booking.Notes?.Contains("Customer cancelled online", StringComparison.OrdinalIgnoreCase) == true ||
        booking.Notes?.Contains("Cancellation approved by staff", StringComparison.OrdinalIgnoreCase) == true;

    public static bool HasOffer(SalesQuote quote) => quote.Status != QuoteStatus.Draft &&
        (quote.Status != QuoteStatus.Negotiating || quote.LastEmailedAt >= quote.UpdatedAt) && (quote.LastEmailedAt.HasValue ||
        quote.Status is QuoteStatus.Sent or QuoteStatus.Accepted or QuoteStatus.Converted);

    public static string Stage(Booking? booking, SalesQuote? quote, DateTimeOffset now, bool staffDeclined = false)
    {
        if (booking?.Status == BookingStatus.Completed) return "Completed";
        if (booking?.Status == BookingStatus.ConvertedToRental) return "OnHire";
        if (booking?.Status == BookingStatus.Confirmed) return "Confirmed";
        if (booking?.Status == BookingStatus.Cancelled)
            return CustomerCancelled(booking) ? "Cancelled" : "Declined";
        if (quote?.LostReason?.StartsWith("Customer cancelled", StringComparison.OrdinalIgnoreCase) == true) return "Cancelled";
        if (staffDeclined || quote?.Status == QuoteStatus.Rejected) return "Declined";
        if (booking?.Status == BookingStatus.Expired || quote?.Status == QuoteStatus.Expired) return "Expired";
        if (quote is not null && HasOffer(quote) && quote.Status is QuoteStatus.Sent or QuoteStatus.Negotiating && quote.ValidUntil <= now) return "Expired";
        var pickup = booking?.Items.OrderBy(x => x.StartAt).FirstOrDefault()?.StartAt;
        if (pickup.HasValue && pickup.Value.ToOffset(TimeSpan.FromHours(12)).Date < now.ToOffset(TimeSpan.FromHours(12)).Date)
            return "Expired";
        if (quote is null || quote.Status is QuoteStatus.Accepted or QuoteStatus.Converted) return "AwaitingConfirmation";
        return HasOffer(quote) && quote.Status is QuoteStatus.Sent or QuoteStatus.Negotiating ? "ReviewQuotation" : "AwaitingQuotation";
    }
}
