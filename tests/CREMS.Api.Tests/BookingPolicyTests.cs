using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Rentals;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class BookingPolicyTests
{
    [Fact]
    public void Adjacent_periods_do_not_overlap()
    {
        var start = DateTimeOffset.Parse("2026-08-10T09:00:00+12:00");
        Assert.False(BookingPolicy.PeriodsOverlap(start, start.AddDays(2), start.AddDays(2), start.AddDays(4)));
    }

    [Fact]
    public void Intersecting_periods_overlap()
    {
        var start = DateTimeOffset.Parse("2026-08-10T09:00:00+12:00");
        Assert.True(BookingPolicy.PeriodsOverlap(start, start.AddDays(3), start.AddDays(2), start.AddDays(4)));
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.ConvertedToRental, true)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Completed, false)]
    public void Final_statuses_have_expected_availability_effect(BookingStatus status, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, BookingPolicy.BlocksAvailability(status, now, now));
    }

    [Fact]
    public void Draft_hold_expires_after_thirty_minutes()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(BookingPolicy.BlocksAvailability(BookingStatus.Draft, now.AddMinutes(-10), now));
        Assert.False(BookingPolicy.BlocksAvailability(BookingStatus.Draft, now.AddMinutes(-31), now));
    }

    [Theory]
    [InlineData(AssetStatus.Available, true)]
    [InlineData(AssetStatus.Maintenance, false)]
    [InlineData(AssetStatus.OutOfService, false)]
    [InlineData(AssetStatus.Retired, false)]
    public void Asset_operational_state_is_enforced(AssetStatus status, bool expected)
    {
        var asset = new Asset { AssetNumber = "TEST", Name = "Test asset", IsActive = true, Status = status };
        Assert.Equal(expected, BookingPolicy.IsOperational(asset));
    }

    [Theory]
    [InlineData(BookingStatus.Draft, BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Draft, BookingStatus.Completed, false)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.ConvertedToRental, true)]
    [InlineData(BookingStatus.ConvertedToRental, BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.ConvertedToRental, BookingStatus.Completed, true)]
    public void Lifecycle_allows_only_supported_transitions(BookingStatus current, BookingStatus next, bool expected) =>
        Assert.Equal(expected, BookingPolicy.IsValidTransition(current, next));

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, false, true)]
    public void Motors_optional_driver_only_quotes_when_requested_or_unpriced(
        bool explicitlyRequested, bool pricedDriver, bool expectedBooking, bool expectedQuotation)
    {
        var result = BookingPolicy.RequiresPublicQuotation(explicitlyRequested, true, false, false,
            CREMS.Api.Domain.Common.PersonnelRequirement.None, true, pricedDriver, false, false);
        Assert.Equal(expectedQuotation, result);
        Assert.Equal(expectedBooking, !result);
    }

    [Fact]
    public void Existing_non_motors_personnel_rule_remains_quote_only() =>
        Assert.True(BookingPolicy.RequiresPublicQuotation(false, false, false, false,
            CREMS.Api.Domain.Common.PersonnelRequirement.None, true, true, false, false));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Motors_delivery_only_quotes_without_a_configured_rate(bool pricedTransport, bool expected) =>
        Assert.Equal(expected, BookingPolicy.RequiresPublicQuotation(false, true, false, false,
            CREMS.Api.Domain.Common.PersonnelRequirement.None, false, false, true, pricedTransport));

    [Fact]
    public void Asset_configuration_controls_operator_requirement()
    {
        var asset = new Asset
        {
            AssetNumber = "EQP-TEST", Name = "Configured equipment",
            PersonnelRequirement = CREMS.Api.Domain.Common.PersonnelRequirement.Optional,
            PersonnelOverride = CREMS.Api.Domain.Common.PersonnelRequirement.Optional,
            AssetCategory = new CREMS.Api.Domain.Common.AssetCategory
            {
                Code = "CONFIGURED", Name = "Configured category",
                PersonnelRequirement = CREMS.Api.Domain.Common.PersonnelRequirement.Required,
            },
        };

        Assert.Equal(CREMS.Api.Domain.Common.PersonnelRequirement.Optional, AssetCategoryPolicy.Personnel(asset));
    }
}
