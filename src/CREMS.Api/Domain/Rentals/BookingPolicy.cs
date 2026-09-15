using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Rentals;

public static class BookingPolicy
{
    public static bool RequiresPublicQuotation(bool explicitlyRequested, bool isMotors,
        bool businessCustomer, bool serviceRequiresQuote, PersonnelRequirement personnelPolicy,
        bool personnelRequested, bool pricedPersonnelAvailable, bool deliveryRequested,
        bool pricedTransportAvailable)
    {
        if (explicitlyRequested) return true;
        if (!isMotors)
            return businessCustomer || serviceRequiresQuote ||
                personnelPolicy == PersonnelRequirement.Required || personnelRequested;

        return businessCustomer || serviceRequiresQuote ||
            personnelPolicy == PersonnelRequirement.Required ||
            personnelRequested && !pricedPersonnelAvailable ||
            deliveryRequested && !pricedTransportAvailable;
    }

    public static bool PeriodsOverlap(DateTimeOffset firstStart, DateTimeOffset firstEnd,
        DateTimeOffset secondStart, DateTimeOffset secondEnd) => firstStart < secondEnd && firstEnd > secondStart;

    public static bool IsOperational(Asset asset) => asset.IsActive &&
        asset.Status is not (AssetStatus.Maintenance or AssetStatus.OutOfService or AssetStatus.Retired);

    public static bool BlocksAvailability(BookingStatus status, DateTimeOffset createdAt, DateTimeOffset now) =>
        status is BookingStatus.Confirmed or BookingStatus.ConvertedToRental ||
        status == BookingStatus.Draft && createdAt > now.AddMinutes(-30);

    public static bool IsValidTransition(BookingStatus current, BookingStatus next) =>
        current == next || (current, next) switch
        {
            (BookingStatus.Draft, BookingStatus.Confirmed) => true,
            (BookingStatus.Draft, BookingStatus.Cancelled) => true,
            (BookingStatus.Confirmed, BookingStatus.Cancelled) => true,
            (BookingStatus.Confirmed, BookingStatus.ConvertedToRental) => true,
            (BookingStatus.ConvertedToRental, BookingStatus.Completed) => true,
            _ => false,
        };
}
