using CREMS.Api.Data;
using CREMS.Api.Services;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CREMS.Api.Tests;
public class InspectionTrackingTests
{
    [Fact]
    public void New_inspection_on_loaded_booking_must_be_inserted()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(local);Database=TrackingOnly;Integrated Security=true;TrustServerCertificate=true").Options);
        var booking = new Booking { BookingNumber="TEST" };
        db.Attach(booking);
        var inspection = RentalInspectionPersistence.GetOrCreatePreparation(db, booking, "Test");
        db.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Added, db.Entry(inspection).State);
        Assert.Same(inspection, RentalInspectionPersistence.GetOrCreatePreparation(db, booking, "Test"));
    }
}
