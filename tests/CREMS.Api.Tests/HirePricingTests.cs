using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Services;
using Microsoft.EntityFrameworkCore;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Rentals;
using Xunit;

namespace CREMS.Api.Tests;
public sealed class HirePricingTests
{
    [Theory]
    [InlineData("2028-01-31", "2028-02-29", 1)]
    [InlineData("2028-01-31", "2028-03-01", 2)]
    [InlineData("2026-12-15", "2027-02-15", 2)]
    public void Months_follow_calendar_anniversaries(string start, string end, decimal expected)
        => Assert.Equal(expected, HirePricing.Quantity(ChargeUnit.Month, DateTimeOffset.Parse(start), DateTimeOffset.Parse(end)));
    [Fact]
    public void Measured_hire_requires_quantity()
    {
        var start=DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(()=>HirePricing.Quantity(ChargeUnit.SquareMetre,start,start.AddDays(2)));
        Assert.Equal(12.5m,HirePricing.Quantity(ChargeUnit.SquareMetre,start,start.AddDays(2),12.5m));
    }
    [Fact]
    public void Saved_monthly_quantity_controls_total_and_late_fee_daily_equivalent()
    {
        var start=new DateTimeOffset(2026,4,1,0,0,0,TimeSpan.Zero);
        var item=new BookingItem {StartAt=start,EndAt=start.AddMonths(1),DailyRate=900,HireUnit=ChargeUnit.Month,HireQuantity=1};
        Assert.Equal(900,HirePricing.Amount(item));
        Assert.Equal(30,HirePricing.DailyEquivalent(item));
    }
    [Fact]
    public void Legacy_daily_bookings_keep_daily_rate()
    {
        var start=DateTimeOffset.UtcNow;
        var item=new BookingItem {StartAt=start,EndAt=start.AddHours(25),DailyRate=100};
        Assert.Equal(200,HirePricing.Amount(item));
        Assert.Equal(100,HirePricing.DailyEquivalent(item));
    }
    [Fact]
    public async Task Service_rate_overrides_division_and_ambiguity_is_rejected()
    {
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var division=Guid.NewGuid();var service=Guid.NewGuid();
        var asset=new Asset {AssetNumber="FLAT1",Name="Flat",DivisionId=division,ServiceOfferingId=service,UseServiceHireRate=true,Type=AssetType.PropertyUnit};
        db.ChargeDefinitions.AddRange(new ChargeDefinition {Code="GENERAL",Name="General",DivisionId=division,Category=ChargeCategory.BaseHire,DefaultSellingRate=100,Unit=ChargeUnit.Day},new ChargeDefinition {Code="FLAT",Name="Flat monthly",DivisionId=division,ServiceOfferingId=service,Category=ChargeCategory.BaseHire,DefaultSellingRate=900,Unit=ChargeUnit.Month,IsTaxable=false});
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var rate=await ConfiguredHirePricing.Resolve(db,asset,TestContext.Current.CancellationToken);
        Assert.Equal(900,rate.Rate);Assert.Equal(ChargeUnit.Month,rate.Unit);Assert.False(rate.IsTaxable);
        db.ChargeDefinitions.Add(new ChargeDefinition {Code="DUPLICATE",Name="Duplicate",DivisionId=division,ServiceOfferingId=service,Category=ChargeCategory.BaseHire});
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>ConfiguredHirePricing.Resolve(db,asset,TestContext.Current.CancellationToken));
        asset.UseServiceHireRate=false;asset.DailyRate=850;asset.HireUnit=ChargeUnit.Month;
        Assert.Equal(850,(await ConfiguredHirePricing.Resolve(db,asset,TestContext.Current.CancellationToken)).Rate);
    }
    [Fact]
    public void Property_does_not_require_vehicle_or_equipment_checks()
    {
        var asset=new Asset {AssetNumber="FLAT1",Name="Flat",Type=AssetType.PropertyUnit,PersonnelRequirement=PersonnelRequirement.Required};
        Assert.False(AssetCategoryPolicy.TracksFuel(asset));Assert.False(AssetCategoryPolicy.TracksMeter(asset));
        Assert.False(AssetCategoryPolicy.RequiresDrivingLicence(asset,false));
        Assert.Equal(PersonnelRequirement.None,AssetCategoryPolicy.Personnel(asset));
    }
    [Fact]
    public void Tax_uses_saved_taxable_flags()
    {
        var booking=new Booking {BookingNumber="TEST",TaxRate=10,AdditionalCharges=100,Items=[new BookingItem {DailyRate=900,HireUnit=ChargeUnit.Month,HireQuantity=1,HireIsTaxable=false}],Charges=[new BookingCharge {Description="Cleaning",Quantity=1,UnitRate=100,IsTaxable=true}]};
        Assert.Equal(10,HirePricing.Tax(booking));
    }
}
