using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Rentals;

namespace CREMS.Api.Services;

/// <summary>Authoritative arithmetic and period boundaries for management reporting.</summary>
public static class ReportingKpis
{
    public const string TimeZone = "Pacific/Fiji";
    public const decimal UnusualDiscountPercent = 15m;

    public static (DateTimeOffset Start, DateTimeOffset End) FijiPeriod(DateOnly from, DateOnly to)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        return (new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), zone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), zone)));
    }

    public static decimal Revenue(IEnumerable<RentalInvoice> invoices, IEnumerable<CreditNote> credits)
    {
        var issued = invoices.Where(x => x.Status != InvoiceStatus.Voided && x.Status != InvoiceStatus.Draft).ToArray();
        var ids = issued.Select(x => x.Id).ToHashSet();
        return issued.Sum(x => x.Subtotal) - credits.Where(x => ids.Contains(x.InvoiceId) && x.Status == CreditNoteStatus.Applied).Sum(x => x.Subtotal);
    }
    public static decimal GrossInvoiced(IEnumerable<RentalInvoice> invoices) => invoices
        .Where(x => x.Status != InvoiceStatus.Voided && x.Status != InvoiceStatus.Draft).Sum(x => x.Total);
    public static decimal Outstanding(IEnumerable<RentalInvoice> invoices) => invoices
        .Where(x => x.Status != InvoiceStatus.Voided && x.Status != InvoiceStatus.Draft).Sum(x => Math.Max(0, x.BalanceDue));
    public static decimal MaintenanceExpense(IEnumerable<MaintenanceJob> jobs) => jobs
        .Where(x => x.Status != MaintenanceStatus.Cancelled).Sum(x => x.ActualCost ?? MaintenanceCosts.Total(x));
    public static decimal Profit(decimal revenue, decimal maintenance, decimal direct, decimal bookingCosts, decimal personnel)
        => revenue - maintenance - direct - bookingCosts - personnel;
    public static decimal Margin(decimal revenue, decimal profit)
        => revenue == 0 ? 0 : Math.Round(profit * 100 / revenue, 1);
    public static decimal Utilization(decimal hiredDays, decimal capacityDays)
        => capacityDays == 0 ? 0 : Math.Round(hiredDays * 100 / capacityDays, 1);

    public static IReadOnlyDictionary<string, string> Definitions { get; } = new Dictionary<string, string>
    {
        ["revenue"] = "Pre-tax subtotal on issued, non-voided invoices in the selected Fiji period, less applied credit-note subtotal on those invoices. Drafts are excluded. Not cash received or scheduled booking value.",
        ["grossInvoiced"] = "Sum of tax-inclusive total on non-voided invoices issued in the selected Fiji period.",
        ["outstanding"] = "Current positive balance on non-voided invoices issued in the selected period; not a historical period-end receivables balance.",
        ["profit"] = "Invoiced pre-tax revenue allocated to booked assets, less recorded maintenance, direct asset costs, booking charge costs and personnel costs for the period. Excludes company overhead and is an operational contribution estimate.",
        ["utilization"] = "Union of on-hire booking intervals overlapping the selected period, divided by available asset-days since acquisition; overlapping bookings are counted once.",
        ["downtime"] = "Union of recorded maintenance intervals overlapping the selected period. Open jobs are measured through the earlier of now or period end.",
        ["maintenanceCost"] = "Non-cancelled maintenance jobs reported in the period: actual cost if recorded, otherwise the sum of recorded cost components. Estimates alone are excluded."
    };
}
