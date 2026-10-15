using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/reporting")]
[Authorize(Policy = SystemPolicies.ViewReports)]
public sealed class ReportingController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet("workspace")]
    public async Task<ActionResult> Workspace([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? branchId, [FromQuery] Guid? divisionId, [FromQuery] Guid? assetId,
        [FromQuery] BookingStatus? bookingStatus, [FromQuery] InvoiceStatus? invoiceStatus,
        [FromQuery] PaymentMethod? paymentMethod, [FromQuery] MaintenanceStatus? maintenanceStatus,
        CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null) return Forbid();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, FijiTimeZone()).DateTime);
        var startDate = from ?? new DateOnly(today.Year, today.Month, 1);
        var endDate = to ?? today;
        if (startDate > endDate || endDate.DayNumber - startDate.DayNumber > 731)
            return BadRequest(new { message = "Choose an ordered reporting period of no more than two years." });
        if (branchId.HasValue && !scope.HasBranchAccess(branchId.Value)) return Forbid();
        if (divisionId.HasValue && !scope.HasDivisionAccess(divisionId.Value)) return Forbid();

        var (start, end) = ReportingKpis.FijiPeriod(startDate, endDate);
        var scopedAssets = db.Assets.AsNoTracking().Where(a => scope.IsAdministrator ||
            scope.BranchIds.Contains(a.BranchId) && a.DivisionId.HasValue && scope.DivisionIds.Contains(a.DivisionId.Value));
        if (assetId.HasValue && !await scopedAssets.AnyAsync(a => a.Id == assetId.Value, token)) return NotFound();
        var assets = scopedAssets;
        if (branchId.HasValue) assets = assets.Where(a => a.BranchId == branchId.Value);
        if (divisionId.HasValue) assets = assets.Where(a => a.DivisionId == divisionId.Value);
        if (assetId.HasValue) assets = assets.Where(a => a.Id == assetId.Value);
        var assetIds = assets.Select(a => a.Id);
        var assetRows = await assets.OrderBy(a => a.AssetNumber).Select(a => new
        {
            a.Id, a.AssetNumber, a.Name, a.Type, a.Category, a.Status, a.IsActive,
            a.BranchId, branchName = a.Branch!.Name, a.DivisionId, divisionName = a.Division!.Name,
            a.ServiceOfferingId, serviceName = a.ServiceOffering!.Name, a.AssetCategoryId,
            assetCategoryName = a.AssetCategory!.Name, a.DailyRate, a.DefaultBondAmount,
            a.RegistrationNumber, a.SerialNumber, a.VinOrChassisNumber, a.EngineNumber,
            a.Manufacturer, a.Model, a.ModelYear, a.OwnershipType, a.AcquisitionDate,
            a.AcquisitionCost, a.CurrentBookValue, a.CurrentLocation, a.CurrentMeterReading,
            a.MeterUnit, a.NextServiceDate, a.InsurancePolicyNumber, a.InsuranceExpiry,
            a.WarrantyExpiry, a.PersonnelRequirement, a.RequiresDelivery
        }).ToListAsync(token);

        var bookings = db.Bookings.AsNoTracking().Where(b => scope.IsAdministrator ||
            scope.BranchIds.Contains(b.BranchId) && b.Items.Any(i => assetIds.Contains(i.AssetId)));
        if (branchId.HasValue) bookings = bookings.Where(b => b.BranchId == branchId.Value);
        if (divisionId.HasValue || assetId.HasValue)
            bookings = bookings.Where(b => b.Items.Any(i => assetIds.Contains(i.AssetId)));
        if (bookingStatus.HasValue) bookings = bookings.Where(b => b.Status == bookingStatus.Value);
        var bookingRows = await bookings.Where(b => b.Items.Any(i => i.StartAt >= start && i.StartAt < end &&
            (!divisionId.HasValue && !assetId.HasValue || assetIds.Contains(i.AssetId))))
            .OrderByDescending(b => b.CreatedAt).Select(b => new
            {
                b.Id, b.BookingNumber, b.Status, b.CreatedAt, b.ApprovedAt,
                b.DiscountAmount, b.TaxRate, b.DepositRequired, b.BondStatus,
                b.BondAmountHeld, customerName = b.Customer!.Name,
                customerNumber = b.Customer.CustomerNumber,
                branchName = b.Branch!.Name, b.BranchId,
                firstPickup = b.Items.Where(i => i.StartAt >= start && i.StartAt < end &&
                    (!divisionId.HasValue && !assetId.HasValue || assetIds.Contains(i.AssetId)))
                    .Min(i => i.StartAt),
                lastReturn = b.Items.Max(i => i.EndAt),
                assetCount = b.Items.Count(i => !divisionId.HasValue && !assetId.HasValue || assetIds.Contains(i.AssetId))
            }).ToListAsync(token);

        var financialBookings = db.Bookings.AsNoTracking().Where(b => scope.IsAdministrator ||
            scope.BranchIds.Contains(b.BranchId) && b.Items.Any(i => assetIds.Contains(i.AssetId)));
        if (branchId.HasValue) financialBookings = financialBookings.Where(b => b.BranchId == branchId.Value);
        if (divisionId.HasValue || assetId.HasValue)
            financialBookings = financialBookings.Where(b => b.Items.Any(i => assetIds.Contains(i.AssetId)));
        var financialBookingIds = financialBookings.Select(b => b.Id);
        var invoices = db.RentalInvoices.AsNoTracking().Where(i => financialBookingIds.Contains(i.BookingId) && i.IssuedAt >= start && i.IssuedAt < end);
        if (invoiceStatus.HasValue) invoices = invoices.Where(i => i.Status == invoiceStatus.Value);
        var invoiceRows = await invoices.OrderByDescending(i => i.IssuedAt).Select(i => new
        {
            i.Id, i.BookingId, i.InvoiceNumber, i.IssuedAt, i.Status, i.Subtotal,
            i.TaxAmount, i.Total, i.AmountPaid, i.BalanceDue, i.TaxInclusive,
            bookingNumber = i.Booking!.BookingNumber, customerName = i.Booking.Customer!.Name,
            customerNumber = i.Booking.Customer.CustomerNumber, branchName = i.Booking.Branch!.Name
        }).ToListAsync(token);
        var visibleInvoiceIds = invoiceRows.Select(x => x.Id).ToArray();
        var appliedCredits = await db.CreditNotes.AsNoTracking().Where(x => visibleInvoiceIds.Contains(x.InvoiceId) &&
            x.Status == CreditNoteStatus.Applied).Select(x => new { x.InvoiceId, x.Subtotal }).ToListAsync(token);
        var payments = db.RentalPayments.AsNoTracking().Where(p => financialBookingIds.Contains(p.BookingId) && p.CreatedAt >= start && p.CreatedAt < end);
        if (paymentMethod.HasValue) payments = payments.Where(p => p.Method == paymentMethod.Value);
        var paymentRows = await payments.OrderByDescending(p => p.CreatedAt).Select(p => new
        {
            p.Id, p.BookingId, p.ReceiptNumber, p.CreatedAt, p.Type, p.Method, p.Status, p.Amount,
            bookingNumber = p.Booking!.BookingNumber, customerName = p.Booking.Customer!.Name,
            customerNumber = p.Booking.Customer.CustomerNumber,
            branchName = p.Booking.Branch!.Name
        }).ToListAsync(token);
        var maintenance = db.MaintenanceJobs.AsNoTracking().Where(j => assetIds.Contains(j.AssetId) && j.ReportedAt >= start && j.ReportedAt < end);
        if (maintenanceStatus.HasValue) maintenance = maintenance.Where(j => j.Status == maintenanceStatus.Value);
        var maintenanceRows = await maintenance.OrderByDescending(j => j.ReportedAt).Select(j => new
        {
            j.Id, j.JobNumber, j.ReportedAt, j.CompletedAt, j.Status, j.Priority, j.ServiceType,
            j.FaultDescription, j.Supplier, j.IsPreventive, j.WarrantyCovered,
            j.DowntimeHours, j.PartsCost, j.LabourCost, j.TransportCost, j.ExternalServiceCost,
            j.FuelCost, j.OtherCost, j.TaxCost, j.TaxMode, j.ActualCost, j.EstimatedCost,
            j.AssetId, assetNumber = j.Asset!.AssetNumber,
            assetName = j.Asset.Name, branchName = j.Branch!.Name, divisionName = j.Asset.Division!.Name
        }).ToListAsync(token);
        var branchOptions = await scopedAssets.Where(a => a.Branch != null)
            .Select(a => new { id = a.BranchId, name = a.Branch!.Name }).Distinct().OrderBy(x => x.name).ToListAsync(token);
        var divisionOptions = await scopedAssets.Where(a => a.DivisionId != null)
            .Select(a => new { id = a.DivisionId, name = a.Division!.Name }).Distinct().OrderBy(x => x.name).ToListAsync(token);
        var assetOptions = await scopedAssets.OrderBy(a => a.AssetNumber)
            .Select(a => new { id = a.Id, name = a.AssetNumber + " · " + a.Name, a.BranchId, a.DivisionId })
            .ToListAsync(token);
        var periodProfitability = await PeriodProfitabilityReport.Build(db, assets, startDate, endDate, token);
        var historicalUtilization = await HistoricalUtilizationReport.Build(db, assets, startDate, endDate, token);
        var exceptions = await ReportingExceptionsReport.Build(db, assets, startDate, endDate, token);
        var revenue = invoiceRows.Where(x => x.Status != InvoiceStatus.Voided && x.Status != InvoiceStatus.Draft).Sum(x => x.Subtotal) -
            appliedCredits.Where(x => invoiceRows.Any(i => i.Id == x.InvoiceId && i.Status != InvoiceStatus.Voided && i.Status != InvoiceStatus.Draft)).Sum(x => x.Subtotal);

        return Ok(new
        {
            period = new { from = startDate, to = endDate, timeZone = "Pacific/Fiji" },
            filters = new { branchId, divisionId, assetId, bookingStatus, invoiceStatus, paymentMethod, maintenanceStatus },
            options = new { branches = branchOptions, divisions = divisionOptions, assets = assetOptions },
            summary = new
            {
                fleet = assetRows.Count(a => a.IsActive), available = assetRows.Count(a => a.IsActive && a.Status == AssetStatus.Available),
                onHire = assetRows.Count(a => a.IsActive && a.Status == AssetStatus.Rented),
                bookings = bookingRows.Count, completedBookings = bookingRows.Count(b => b.Status == BookingStatus.Completed),
                revenue,
                invoiced = invoiceRows.Where(i => i.Status != InvoiceStatus.Voided && i.Status != InvoiceStatus.Draft).Sum(i => i.Total),
                received = paymentRows.Where(p => p.Status == PaymentStatus.Recorded && p.Type != PaymentType.Refund && p.Type != PaymentType.BondRefund).Sum(p => p.Amount),
                outstanding = invoiceRows.Where(i => i.Status != InvoiceStatus.Voided && i.Status != InvoiceStatus.Draft).Sum(i => Math.Max(0, i.BalanceDue)),
                maintenanceCost = maintenanceRows.Where(j => j.Status != MaintenanceStatus.Cancelled).Sum(j => j.ActualCost ??
                    j.PartsCost + j.LabourCost + j.TransportCost + j.ExternalServiceCost + j.FuelCost + j.OtherCost +
                    (j.TaxMode == MaintenanceTaxMode.Inclusive ? 0 : j.TaxCost)),
                downtimeHours = maintenanceRows.Where(j => j.Status != MaintenanceStatus.Cancelled).Sum(j => j.DowntimeHours)
            },
            definitions = ReportingKpis.Definitions,
            periodProfitability, historicalUtilization, exceptions,
            assets = assetRows, bookings = bookingRows, invoices = invoiceRows, payments = paymentRows, maintenance = maintenanceRows,
            notes = new[]
            {
                "Fleet status is a current snapshot; date range does not change fleet counts.",
                "Bookings are grouped by scheduled pickup date; invoices by issue date; payments by record date; maintenance by report date.",
                "Invoice balances are current balances of invoices issued in the period, not historical period-end receivables.",
                "Invoice and payment ledgers include the full booking when it contains a matching asset; period P&L allocates invoice revenue across its assets."
            }
        });
    }

    private static TimeZoneInfo FijiTimeZone() => TimeZoneInfo.FindSystemTimeZoneById("Pacific/Fiji");
}
