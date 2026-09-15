using CREMS.Api.Data;
using CREMS.Api.Domain.Rentals;
namespace CREMS.Api.Services;

public static class RentalInspectionPersistence
{
    public static RentalInspection GetOrCreatePreparation(ApplicationDbContext db, Booking booking, string staffName)
    {
        var existing = booking.Inspections.Where(x => x.Type == InspectionType.Handover)
            .OrderByDescending(x => x.CompletedAt).FirstOrDefault();
        if (existing is not null) return existing;
        var inspection = new RentalInspection { BookingId=booking.Id, Type=InspectionType.Handover, CompletedByName=staffName };
        // Entity creates a GUID in its constructor. Explicit Add is required so EF
        // inserts this new row instead of treating its populated key as existing.
        db.RentalInspections.Add(inspection);
        return inspection;
    }
}
