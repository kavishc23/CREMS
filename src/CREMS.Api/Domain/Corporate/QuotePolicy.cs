namespace CREMS.Api.Domain.Corporate;

public static class QuotePolicy
{
    public static bool IsValidTransition(QuoteStatus current, QuoteStatus next) => current == next || (current, next) switch
    {
        (QuoteStatus.Draft, QuoteStatus.Sent) => true,
        (QuoteStatus.Sent, QuoteStatus.Negotiating) => true,
        (QuoteStatus.Sent, QuoteStatus.Accepted) => true,
        (QuoteStatus.Sent, QuoteStatus.Rejected) => true,
        (QuoteStatus.Sent, QuoteStatus.Expired) => true,
        (QuoteStatus.Negotiating, QuoteStatus.Sent) => true,
        (QuoteStatus.Negotiating, QuoteStatus.Accepted) => true,
        (QuoteStatus.Negotiating, QuoteStatus.Rejected) => true,
        _ => false,
    };

    public static (decimal Subtotal, decimal Tax, decimal Total) Calculate(
        IEnumerable<(decimal Quantity, decimal Rate)> lines, decimal discount, decimal taxRate)
    {
        var subtotal = lines.Sum(x => x.Quantity * x.Rate);
        var taxable = Math.Max(0, subtotal - discount);
        var tax = decimal.Round(taxable * taxRate / 100m, 2, MidpointRounding.AwayFromZero);
        return (subtotal, tax, taxable + tax);
    }
}
