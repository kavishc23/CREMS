using CREMS.Api.Domain.Common;
namespace CREMS.Api.Domain.Rentals;

public static class HirePricing
{
    public static bool IsMeasured(ChargeUnit unit) => unit is ChargeUnit.Unit or ChargeUnit.Trip or ChargeUnit.Kilometre or ChargeUnit.Tonne or ChargeUnit.SquareMetre;
    // A month is a calendar month, with a started partial month charged as a full month.
    public static decimal Quantity(ChargeUnit unit, DateTimeOffset start, DateTimeOffset end, decimal? measured = null)
    {
        if (end <= start) throw new ArgumentException("The rental end must be after its start.");
        if (IsMeasured(unit)) return measured is > 0 and <= 1000000 ? measured.Value : throw new ArgumentException($"Enter a positive quantity in {unit}.");
        if (unit == ChargeUnit.Fixed) return 1;
        if (unit == ChargeUnit.Month)
        {
            var months = (end.Year-start.Year)*12+end.Month-start.Month;
            if(start.AddMonths(months)<end) months++;
            return Math.Max(1,months);
        }
        return unit switch {
            ChargeUnit.Hour => Math.Max(1,(decimal)Math.Ceiling((end-start).TotalHours)),
            ChargeUnit.Day => Math.Max(1,(decimal)Math.Ceiling((end-start).TotalDays)),
            ChargeUnit.Week => Math.Max(1,(decimal)Math.Ceiling((end-start).TotalDays/7)),
            _ => throw new ArgumentException("Select a supported billing unit.")
        };
    }
    public static decimal Quantity(BookingItem item) => item.HireQuantity ?? (item.HireUnit == ChargeUnit.Day ? Math.Max(1, (decimal)Math.Ceiling((item.EndAt-item.StartAt).TotalDays)) : Quantity(item.HireUnit,item.StartAt,item.EndAt));
    public static decimal Amount(BookingItem item) => decimal.Round(item.DailyRate*Quantity(item),2,MidpointRounding.AwayFromZero);
    public static decimal Tax(Booking booking) => decimal.Round(Math.Max(0,
        booking.Items.Where(x=>x.HireIsTaxable).Sum(Amount) - booking.DiscountAmount +
        booking.Charges.Where(x=>x.IsTaxable).Sum(x=>x.Quantity*x.UnitRate) +
        Math.Max(0, booking.AdditionalCharges-booking.Charges.Sum(x=>x.Quantity*x.UnitRate))) * booking.TaxRate/100m,2);
    public static decimal DailyEquivalent(BookingItem item) => item.HireUnit == ChargeUnit.Day && item.HireQuantity is null ? item.DailyRate : Amount(item)/Math.Max(1,(decimal)Math.Ceiling((item.EndAt-item.StartAt).TotalDays));
}
