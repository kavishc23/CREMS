using CREMS.Api.Domain.Corporate;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class QuotePolicyTests
{
    [Fact]
    public void Calculates_discount_tax_and_total()
    {
        var result = QuotePolicy.Calculate([(2m, 500m), (1m, 200m)], 100m, 15m);
        Assert.Equal(1200m, result.Subtotal); Assert.Equal(165m, result.Tax); Assert.Equal(1265m, result.Total);
    }

    [Theory]
    [InlineData(QuoteStatus.Draft, QuoteStatus.Sent, true)]
    [InlineData(QuoteStatus.Draft, QuoteStatus.Accepted, false)]
    [InlineData(QuoteStatus.Sent, QuoteStatus.Accepted, true)]
    [InlineData(QuoteStatus.Accepted, QuoteStatus.Converted, false)]
    [InlineData(QuoteStatus.Rejected, QuoteStatus.Sent, false)]
    public void Enforces_quote_lifecycle(QuoteStatus current, QuoteStatus next, bool expected) =>
        Assert.Equal(expected, QuotePolicy.IsValidTransition(current, next));
}
