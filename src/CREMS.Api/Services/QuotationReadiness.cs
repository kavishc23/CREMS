using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Rentals;

namespace CREMS.Api.Services;

public static class QuotationReadiness
{
    public static string? Validate(Booking booking, IEnumerable<(string Description, ChargeCategory Category, decimal Rate, decimal Cost)> prices,
        IEnumerable<(string Description, ChargeCategory Category)>? requested = null)
    {
        var lines = prices.ToList();
        if (!lines.Any(line => line.Category == ChargeCategory.BaseHire && line.Rate > 0)) return "Enter a positive hire rate before submitting the quotation.";
        var required = booking.Charges.Where(charge => charge.Category != ChargeCategory.BaseHire).Select(charge => (charge.Description, charge.Category)).ToList();
        required.AddRange(lines.Where(line => line.Category is ChargeCategory.Operator or ChargeCategory.Driver or ChargeCategory.Transport or ChargeCategory.Labour).Select(line => (line.Description, line.Category)));
        if (requested is not null) required.AddRange(requested.Where(line => line.Category != ChargeCategory.BaseHire));
        if (booking.Items.Any(item => item.Asset is not null && AssetCategoryPolicy.RequiresPersonnel(item.Asset)) && !required.Any(charge => charge.Category is ChargeCategory.Operator or ChargeCategory.Driver)) required.Add(("Operator / driver", ChargeCategory.Operator));
        foreach (var charge in required)
        {
            var matching = lines.Where(line => (line.Category == charge.Category || (charge.Category is ChargeCategory.Operator or ChargeCategory.Driver) && (line.Category is ChargeCategory.Operator or ChargeCategory.Driver)) && (charge.Category is ChargeCategory.Operator or ChargeCategory.Driver or ChargeCategory.Transport || line.Description.Equals(charge.Description, StringComparison.OrdinalIgnoreCase))).ToList();
            if (!matching.Any(line => line.Rate > 0)) return $"Enter the selling price for {charge.Description} before submitting the quotation.";
            if (charge.Category is ChargeCategory.Operator or ChargeCategory.Driver or ChargeCategory.Transport or ChargeCategory.Labour && !matching.Any(line => line.Cost > 0)) return $"Enter the internal cost for {charge.Description} before submitting the quotation.";
        }
        return null;
    }
}
