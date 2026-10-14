namespace CREMS.Api.Domain.Rentals;

public static class ReturnChargePolicy
{
    // Prorate the agreed daily rate by elapsed overdue time, including partial hours.
    public static decimal LateFee(IEnumerable<BookingItem> items, DateTimeOffset returnedAt, decimal multiplier = 1m) =>
        decimal.Round(multiplier * items.Sum(item => item.DailyRate * Math.Max(0m,
            (decimal)(returnedAt - item.EndAt).Ticks / TimeSpan.TicksPerDay)), 2, MidpointRounding.AwayFromZero);
}
