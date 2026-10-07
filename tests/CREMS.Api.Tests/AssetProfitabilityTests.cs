using System.Text;
using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CREMS.Api.Tests;
public class AssetProfitabilityTests
{
    [Theory]
    [InlineData(10.01)]
    [InlineData(-10.01)]
    [InlineData(0.01)]
    public void Shared_amounts_conserve_cents_and_ignore_duplicate_assets(double value)
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001"); var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var split = AssetProfitabilityReport.Allocate((decimal)value, [second,first,first]);
        Assert.Equal(2, split.Count); Assert.Equal((decimal)value, split.Values.Sum());
        Assert.Equal(split, AssetProfitabilityReport.Allocate((decimal)value,[first,second]));
        Assert.All(split.Values, x => Assert.Equal(decimal.Round(x,2),x));
    }

    [Fact]
    public async Task Totals_months_scope_and_export_reconcile_shared_and_unallocated_amounts()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (jobs, first) = await MaintenanceJobsTests.Setup(db);
        var second = new Asset { AssetNumber = "SECOND", Name = "Second", BranchId = Guid.NewGuid(), DivisionId = Guid.NewGuid() };
        var booking = new Booking { BookingNumber = "ACTIVE", BranchId = first.BranchId, Status = BookingStatus.Completed };
        var empty = new Booking { BookingNumber = "NO-ASSET", BranchId = first.BranchId, Status = BookingStatus.ConvertedToRental };
        var start = DateTimeOffset.UtcNow;
        db.AddRange(second, booking, empty);
        foreach (var asset in new[] { first, first, second }) db.Add(new BookingItem { BookingId = booking.Id, AssetId = asset.Id, StartAt = start, EndAt = start.AddDays(1), DailyRate = 100 });
        db.Add(new BookingCharge { BookingId = booking.Id, Description = "Shared", Quantity = 1, UnitRate = 10.01m, UnitCost = 4.01m });
        db.Add(new BookingCharge { BookingId = empty.Id, Description = "Unallocated", Quantity = 1, UnitRate = 20, UnitCost = 5 });
        foreach (var status in new[] { BookingStatus.Cancelled, BookingStatus.Draft, BookingStatus.Confirmed, BookingStatus.Expired }) {
            var excluded = new Booking { BookingNumber = status.ToString(), BranchId = first.BranchId, Status = status }; db.Add(excluded);
            db.Add(new BookingCharge { BookingId = excluded.Id, AssetId = first.Id, Description = "Excluded", Quantity = 1, UnitRate = 999, UnitCost = 999 });
        }
        foreach (var pair in new[] { (booking, 10.01m), (empty, 3m) }) {
            var assignment = new BookingPersonnelAssignment { BookingId = pair.Item1.Id, Role = "Operator", InternalHourlyCost = pair.Item2 }; db.Add(assignment);
            db.Add(new PersonnelTimesheet { AssignmentId = assignment.Id, WorkDate = pair.Item1 == booking ? MaintenanceRules.LocalDate(start).AddMonths(-1) : MaintenanceRules.LocalDate(start), RegularHours = 1 });
        }
        db.Add(new MaintenanceJob { JobNumber = "LEGACY", AssetId = first.Id, BranchId = first.BranchId, ServiceType = "Repair", Status = MaintenanceStatus.Cancelled, PartsCost = 110, TaxMode = MaintenanceTaxMode.Inclusive, TaxCost = 10, ActualCost = null });
        db.Add(new MaintenanceJob { JobNumber = "ESTIMATE", AssetId = first.Id, BranchId = first.BranchId, ServiceType = "Repair", EstimatedCost = 999 });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
        JsonElement Json(object report) => JsonSerializer.SerializeToElement(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var report = Json(await AssetProfitabilityReport.Build(db,db.Assets,true,token));
        Assert.Equal(330.01m, report.GetProperty("totals").GetProperty("revenue").GetDecimal());
        Assert.Equal(132.02m, report.GetProperty("totals").GetProperty("expense").GetDecimal());
        Assert.Equal(197.99m, report.GetProperty("totals").GetProperty("profit").GetDecimal());
        Assert.Equal(20m,report.GetProperty("unallocated").GetProperty("revenue").GetDecimal());
        Assert.Equal(8m,report.GetProperty("unallocated").GetProperty("expense").GetDecimal());
        Assert.Equal(132.02m,report.GetProperty("monthlyTrend").EnumerateArray().Sum(x=>x.GetProperty("expense").GetDecimal()));
        Assert.Equal(330.01m,report.GetProperty("monthlyTrend").EnumerateArray().Sum(x=>x.GetProperty("revenue").GetDecimal()));
        var previousMonth = MaintenanceRules.LocalDate(start).AddMonths(-1).ToString("yyyy-MM");
        Assert.Equal(10.01m,report.GetProperty("monthlyTrend").EnumerateArray().Single(x=>x.GetProperty("month").GetString()==previousMonth).GetProperty("expense").GetDecimal());
        var fullRow = report.GetProperty("assets").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==first.Id);
        var restricted = Json(await AssetProfitabilityReport.Build(db, db.Assets.Where(x=>x.Id==first.Id),false,token));
        var row = Assert.Single(restricted.GetProperty("assets").EnumerateArray());
        Assert.Equal(fullRow.GetProperty("totalExpense").GetDecimal(), row.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(fullRow.GetProperty("revenue").GetDecimal(), row.GetProperty("revenue").GetDecimal());
        Assert.Equal(0,restricted.GetProperty("unallocated").GetProperty("expense").GetDecimal());
        var controller = new BusinessOperationsController(db,new CurrentStaffScope(db)) {ControllerContext = jobs.ControllerContext};
        var csv = Encoding.UTF8.GetString(Assert.IsType<FileContentResult>(await controller.ExportProfitability(token)).FileContents);
        Assert.Contains("TEST-1",csv);
        var unallocatedLine = csv.Split('\n').Single(x=>x.StartsWith("Unallocated," )).Split(',');
        Assert.Equal(20m,decimal.Parse(unallocatedLine[2],System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(8m,decimal.Parse(unallocatedLine[3],System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(12m,decimal.Parse(unallocatedLine[4],System.Globalization.CultureInfo.InvariantCulture));
    }
}
