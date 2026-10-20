using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Rentals;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class DivisionBookingFlowTests
{
    [Fact]
    public void Division_policy_round_trips_and_asset_overrides_win()
    {
        var configured = new DivisionBookingFlow(PersonnelRequirement.Required, "DirectWhenPriced", QuoteValidityDays: 21);
        var division = new Division { Code = "MOT", Name = "Motors", DefaultBondAmount = 500,
            CustomerBookingConfigurationJson = configured.ToJson() };
        var asset = new Asset { AssetNumber = "CAR-1", Name = "Test car", Division = division, InheritBond = true };

        Assert.Equal(PersonnelRequirement.Required, AssetCategoryPolicy.Personnel(asset));
        Assert.Equal(500, AssetCategoryPolicy.Bond(asset));
        Assert.Equal(21, DivisionBookingFlow.For(division).QuoteValidityDays);

        asset.PersonnelOverride = PersonnelRequirement.None;
        asset.InheritBond = false;
        asset.DefaultBondAmount = 750;
        Assert.Equal(PersonnelRequirement.None, AssetCategoryPolicy.Personnel(asset));
        Assert.Equal(750, AssetCategoryPolicy.Bond(asset));
    }

    [Theory]
    [InlineData("Always", false, false, true)]
    [InlineData("DirectWhenPriced", true, false, false)]
    [InlineData("DirectWhenPriced", false, true, true)]
    [InlineData("Automatic", true, false, false)]
    [InlineData("Automatic", false, true, true)]
    public void Personnel_pricing_follows_quote_mode(string mode, bool priced, bool business, bool expected)
    {
        var flow = new DivisionBookingFlow(QuoteMode: mode);
        Assert.Equal(expected, flow.RequiresQuote(false, business, PersonnelRequirement.Optional,
            personnelRequested: true, pricedPersonnelAvailable: priced,
            deliveryRequested: false, pricedTransportAvailable: false));
    }

    [Fact]
    public void Required_operator_and_unpriced_delivery_trigger_automatic_quote()
    {
        var flow = new DivisionBookingFlow();
        Assert.True(flow.RequiresQuote(false, false, PersonnelRequirement.Required, true, true, false, false));
        Assert.True(flow.RequiresQuote(false, false, PersonnelRequirement.None, false, false, true, false));
        Assert.False(flow.RequiresQuote(false, false, PersonnelRequirement.None, false, false, true, true));
    }

    [Fact]
    public void Invalid_admin_limits_are_rejected()
    {
        Assert.False(new DivisionBookingFlow(MaximumHireDays: 0).IsValid);
        Assert.False(new DivisionBookingFlow(MinimumHireDays: 20, MaximumHireDays: 10).IsValid);
        Assert.False(new DivisionBookingFlow(QuoteMode: "SomethingElse").IsValid);
        Assert.True(new DivisionBookingFlow().IsValid);
    }
}
