using CREMS.Api.Domain.Rentals;

namespace CREMS.Api.Services;

// One rounded, tax-aware pricing schedule for screens, agreements and exports.
public static class RentalFinancials
{
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static decimal Services(Booking booking) => booking.Charges.Sum(x => Money(x.Quantity * x.UnitRate));
    public static decimal Unitemised(Booking booking) => Math.Max(0, Money(booking.AdditionalCharges - Services(booking)));
    public static decimal Net(Booking booking) => Math.Max(0, Money(booking.Items.Sum(HirePricing.Amount) - booking.DiscountAmount + Services(booking) + Unitemised(booking)));
    public static decimal Total(Booking booking) => Net(booking) + HirePricing.Tax(booking);
    public static decimal Paid(Booking booking) => booking.Payments.Where(x => x.Status == PaymentStatus.Recorded && (x.Type == PaymentType.RentalCharge || x.Type == PaymentType.AdditionalCharge)).Sum(x => x.Amount) + (booking.BondSettledAt.HasValue ? booking.BondDeductionAmount : 0);

    public static IReadOnlyList<FinancialLine> Lines(Booking booking)
    {
        var lines = booking.Items.Select(x => new FinancialLine($"{x.Asset?.Name ?? "Asset"} hire", HirePricing.Quantity(x), x.DailyRate, x.HireUnit.ToString(), x.HireIsTaxable)).ToList();
        lines.AddRange(booking.Charges.Select(x => new FinancialLine(x.Description, x.Quantity, x.UnitRate, x.Unit.ToString(), x.IsTaxable)));
        if (Unitemised(booking) > 0) lines.Add(new(booking.AdditionalChargesDescription ?? "Other agreed charges", 1, Unitemised(booking), "Fixed", true));
        if (booking.DiscountAmount > 0) lines.Add(new("Discount", 1, -booking.DiscountAmount, "Fixed", true));
        return lines;
    }
}

public sealed record FinancialLine(string Description, decimal Quantity, decimal Rate, string Unit, bool IsTaxable)
{
    public decimal Amount => RentalFinancials.Money(Quantity * Rate);
}
