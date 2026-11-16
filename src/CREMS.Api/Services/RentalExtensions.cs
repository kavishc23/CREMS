using CREMS.Api.Domain.Rentals;

namespace CREMS.Api.Services;

public static class RentalExtensions
{
    // Keep the agreed rate and any agreed quantity adjustment. Charge only newly
    // started billing units; fixed and measured hires do not gain units with time.
    public static IReadOnlyList<FinancialLine> Apply(Booking booking, DateTimeOffset requestedEnd)
    {
        var lines = new List<FinancialLine>();
        foreach (var item in booking.Items)
        {
            var quantity = HirePricing.Quantity(item);
            var added = HirePricing.IsMeasured(item.HireUnit) ? 0 : Math.Max(0,
                HirePricing.Quantity(item.HireUnit, item.StartAt, requestedEnd.ToOffset(item.StartAt.Offset)) -
                HirePricing.Quantity(item.HireUnit, item.StartAt, item.EndAt.ToOffset(item.StartAt.Offset)));
            item.HireQuantity = quantity + added;
            if (added > 0) lines.Add(new FinancialLine($"Hire extension — {item.Asset?.Name ?? "Asset"} ({item.EndAt:u} to {requestedEnd:u})",
                added, item.DailyRate, item.HireUnit.ToString(), item.HireIsTaxable));
            item.EndAt = requestedEnd;
        }
        return lines;
    }
}
