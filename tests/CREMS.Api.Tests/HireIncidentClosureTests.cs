using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class HireIncidentClosureTests
{
    [Theory]
    [InlineData(AssetStatus.OutOfService, false)]
    [InlineData(AssetStatus.Maintenance, true)]
    public async Task Serious_incident_ends_hire_and_blocks_asset_without_recording_a_return(AssetStatus disposition, bool recordIncident)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        asset.Status = AssetStatus.Rented;
        var customer = new Customer { CustomerNumber = "INC-CUSTOMER", Name = "Incident test customer" };
        db.Customers.Add(customer);
        var start = DateTimeOffset.UtcNow.AddDays(-2);
        var booking = new Booking
        {
            BookingNumber = "INC-HIRE-1", CustomerId = customer.Id, BranchId = asset.BranchId, Status = BookingStatus.ConvertedToRental,
            DepositRequired = 100, BondAmountHeld = 100, BondStatus = BondStatus.Held,
            Items = [new BookingItem { AssetId = asset.Id, StartAt = start, EndAt = start.AddDays(1), DailyRate = 50 }]
        };
        db.Bookings.Add(booking);
        RentalIncident? reported = null;
        if (!recordIncident)
        {
            reported = new RentalIncident
            {
                BookingId = booking.Id, IncidentNumber = "CUSTOMER-INC", Type = IncidentType.Accident,
                Description = "Existing customer report.", OccurredAt = start.AddHours(2), ImmediateReturnRequested = true
            };
            db.RentalIncidents.Add(reported);
        }
        using (db.SuppressNotifications()) await db.SaveChangesAsync(token);

        var lifecycle = new RentalLifecycleController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        var request = new EndHireAfterIncidentRequest(disposition, "Serious accident prevents normal return.",
            "With recovery provider", IncidentType.Accident, start.AddHours(2));

        Assert.IsType<BadRequestObjectResult>(await lifecycle.EndHireAfterIncident(booking.Id, reported?.Id,
            request with { AssetStatus = AssetStatus.Available }, token));
        Assert.IsType<NotFoundResult>(await lifecycle.EndHireAfterIncident(booking.Id, Guid.NewGuid(), request, token));
        Assert.Equal(BookingStatus.ConvertedToRental, booking.Status);
        Assert.IsType<OkObjectResult>(await lifecycle.EndHireAfterIncident(booking.Id, reported?.Id, request, token));
        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.NotNull(booking.IncidentClosedAt);
        Assert.Equal(disposition, asset.Status);
        Assert.Equal(start.AddDays(1), booking.Items.Single().EndAt);
        Assert.Equal(100m, booking.BondAmountHeld);
        Assert.Null(booking.BondSettledAt);
        Assert.Empty(await db.RentalPayments.ToListAsync(token));
        Assert.Empty(await db.RentalInspections.ToListAsync(token));
        Assert.Equal(recordIncident ? 1 : 0, await db.MaintenanceJobs.CountAsync(token));
        var incident = Assert.Single(await db.RentalIncidents.ToListAsync(token));
        Assert.Equal(IncidentStatus.Investigating, incident.Status);
        Assert.False(incident.ImmediateReturnRequested);

        var rentals = new RentalOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        Assert.IsType<BadRequestObjectResult>(await rentals.ReturnCharges(booking.Id, token));
        Assert.IsType<ConflictObjectResult>(await lifecycle.EndHireAfterIncident(booking.Id, incident.Id, request, token));
        Assert.Single(await db.ManagementTasks.Where(x => x.SourceEntityId == booking.Id && !x.IsCompleted).ToListAsync(token));
        Assert.Contains(await db.InAppNotifications.ToListAsync(token), x => x.Audience == "Customer" && x.Title == "Hire ended");
        Assert.Single(await db.AuditEvents.Where(x => x.Action == "Hire ended after incident").ToListAsync(token));
    }
}
