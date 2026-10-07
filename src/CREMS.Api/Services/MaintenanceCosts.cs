using CREMS.Api.Domain.Assets;

namespace CREMS.Api.Services;

public static class MaintenanceCosts
{
    public static decimal Subtotal(MaintenanceJob job) => job.PartsCost + job.LabourCost + job.TransportCost
        + job.ExternalServiceCost + job.OtherCost + job.FuelCost;

    public static decimal TaxableSubtotal(MaintenanceJob job, decimal issuedStockCost)
    {
        var flags = job.TaxableCosts;
        return (flags.HasFlag(MaintenanceTaxableCosts.Parts) ? Math.Max(0, job.PartsCost - issuedStockCost) : 0)
            + (flags.HasFlag(MaintenanceTaxableCosts.Labour) ? job.LabourCost : 0)
            + (flags.HasFlag(MaintenanceTaxableCosts.Transport) ? job.TransportCost : 0)
            + (flags.HasFlag(MaintenanceTaxableCosts.ExternalService) ? job.ExternalServiceCost : 0)
            + (flags.HasFlag(MaintenanceTaxableCosts.Other) ? job.OtherCost : 0)
            + (flags.HasFlag(MaintenanceTaxableCosts.Fuel) ? job.FuelCost : 0);
    }

    public static decimal Total(MaintenanceJob job) => Subtotal(job)
        + (job.TaxMode == MaintenanceTaxMode.Inclusive ? 0 : job.TaxCost);

    public static void Calculate(MaintenanceJob job, decimal issuedStockCost)
    {
        var taxable = TaxableSubtotal(job, issuedStockCost);
        job.TaxCost = job.TaxMode switch
        {
            MaintenanceTaxMode.None => 0,
            MaintenanceTaxMode.Exclusive => decimal.Round(taxable * job.TaxRate / 100, 2, MidpointRounding.AwayFromZero),
            MaintenanceTaxMode.Inclusive => decimal.Round(taxable * job.TaxRate / (100 + job.TaxRate), 2, MidpointRounding.AwayFromZero),
            _ => job.TaxCost
        };
        job.ActualCost = Total(job);
    }
}
