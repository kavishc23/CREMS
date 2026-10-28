using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public class CustomerRentalJourneyTests
{
    [Fact]
    public void Quotation_expiry_does_not_expire_an_accepted_or_confirmed_rental()
    {
        var now = DateTimeOffset.UtcNow;
        var booking = new Booking { BookingNumber = "TEST", Items = [new() { StartAt = now.AddDays(5), EndAt = now.AddDays(6) }] };
        var quote = new SalesQuote { QuoteNumber = "QUO", Status = QuoteStatus.Sent, ValidUntil = now.AddMinutes(-1) };
        Assert.Equal("Expired", CustomerRentalJourney.Stage(booking, quote, now));
        quote.Status = QuoteStatus.Accepted;
        Assert.Equal("AwaitingConfirmation", CustomerRentalJourney.Stage(booking, quote, now));
        booking.Status = BookingStatus.Confirmed;
        Assert.Equal("Confirmed", CustomerRentalJourney.Stage(booking, quote, now));
        booking.Status = BookingStatus.Completed;
        Assert.Equal("Completed", CustomerRentalJourney.Stage(booking, quote, now));
    }

    [Fact]
    public void Staff_decline_and_customer_cancellation_are_distinct()
    {
        var booking = new Booking { BookingNumber = "TEST", Status = BookingStatus.Cancelled };
        Assert.Equal("Declined", CustomerRentalJourney.Stage(booking, null, DateTimeOffset.UtcNow));
        booking.Notes = "Customer cancelled online: No reason supplied";
        Assert.Equal("Cancelled", CustomerRentalJourney.Stage(booking, null, DateTimeOffset.UtcNow));
        booking.Notes = "Cancellation approved by staff.";
        Assert.Equal("Cancelled", CustomerRentalJourney.Stage(booking, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_revised_draft_is_not_a_sent_offer_even_if_an_older_version_was_emailed()
    {
        var quote = new SalesQuote { QuoteNumber = "QUO", Status = QuoteStatus.Draft, LastEmailedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        Assert.False(CustomerRentalJourney.HasOffer(quote));
        quote.Status = QuoteStatus.Negotiating; quote.UpdatedAt = DateTimeOffset.UtcNow;
        Assert.False(CustomerRentalJourney.HasOffer(quote));
        Assert.Equal("AwaitingQuotation", CustomerRentalJourney.Stage(null, quote, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Customer_date_reminders_are_scoped_and_deduplicated_and_completion_says_thanks()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync(token);
        var now = new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero); // Fiji 29 October.
        var branch = new Branch { Code = "TEST", Name = "Test branch" };
        var customer = new Customer { CustomerNumber = "TEST", Name = "Test customer" };
        var asset = new Asset { AssetNumber = "TEST", Name = "Test asset", BranchId = branch.Id, Type = AssetType.LightEquipment };
        var pickup = new Booking { BookingNumber = "PICKUP", BranchId = branch.Id, CustomerId = customer.Id, Status = BookingStatus.Confirmed,
            Items = [new() { AssetId = asset.Id, StartAt = now.AddHours(1), EndAt = now.AddDays(2) }] };
        var returning = new Booking { BookingNumber = "RETURN", BranchId = branch.Id, CustomerId = customer.Id, Status = BookingStatus.ConvertedToRental,
            Items = [new() { AssetId = asset.Id, StartAt = now.AddDays(-3), EndAt = now.AddHours(3) }] };
        db.AddRange(branch, customer, asset, pickup, returning);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
        await CustomerRentalReminders.QueueAsync(db, now, token); await db.SaveChangesAsync(token);
        await CustomerRentalReminders.QueueAsync(db, now, token); await db.SaveChangesAsync(token);
        var notices = await db.InAppNotifications.Where(x => x.Audience == "Customer").ToListAsync(token);
        Assert.Equal(2, notices.Count);
        Assert.All(notices, item => { Assert.Equal(customer.Id, item.CustomerId); Assert.StartsWith("/account?reference=", item.Url); });
        returning.Status = BookingStatus.Completed; await db.SaveChangesAsync(token);
        var completed = await db.InAppNotifications.SingleAsync(x => x.Audience == "Customer" && x.Title == "Rental completed", token);
        Assert.Contains("Thank you", completed.Message);
    }
}
