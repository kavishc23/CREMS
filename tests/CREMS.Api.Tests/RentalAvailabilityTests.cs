using CREMS.Api.Services;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class RentalAvailabilityTests
{
    [Fact]
    public void One_hour_turnaround_blocks_early_pickup_and_allows_ten_am()
    {
        var returnAt = RentalAvailability.FijiTime(new DateOnly(2026, 11, 5), new TimeOnly(9, 0));
        var eight = RentalAvailability.FijiTime(new DateOnly(2026, 11, 5), new TimeOnly(8, 0));
        var ten = RentalAvailability.FijiTime(new DateOnly(2026, 11, 5), new TimeOnly(10, 0));
        var previousStart = returnAt.AddDays(-2);

        Assert.True(RentalAvailability.Conflicts(eight, eight.AddDays(1), previousStart, returnAt, 60));
        Assert.False(RentalAvailability.Conflicts(ten, ten.AddDays(1), previousStart, returnAt, 60));
        Assert.Equal(ten, RentalAvailability.EarliestStart(eight, TimeSpan.FromDays(1), [(previousStart, returnAt)], 60));
    }

    [Fact]
    public void Next_slot_moves_past_multiple_reservations()
    {
        var first = RentalAvailability.FijiTime(new DateOnly(2026, 11, 5), new TimeOnly(9, 0));
        var reservations = new[] { (first.AddHours(-2), first), (first.AddHours(1), first.AddHours(5)) };
        Assert.Equal(first.AddHours(6), RentalAvailability.EarliestStart(first.AddHours(-1), TimeSpan.FromHours(2), reservations, 60));
    }

    [Fact]
    public void Turnaround_also_blocks_a_return_immediately_before_the_next_pickup()
    {
        var nextPickup = RentalAvailability.FijiTime(new DateOnly(2026, 11, 5), new TimeOnly(10, 0));
        Assert.True(RentalAvailability.Conflicts(nextPickup.AddHours(-3), nextPickup.AddMinutes(-30), nextPickup, nextPickup.AddDays(1), 60));
        Assert.False(RentalAvailability.Conflicts(nextPickup.AddHours(-3), nextPickup.AddHours(-1), nextPickup, nextPickup.AddDays(1), 60));
    }
}
