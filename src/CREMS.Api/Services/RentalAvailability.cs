using CREMS.Api.Domain.Assets;

namespace CREMS.Api.Services;

public static class RentalAvailability
{
    public static DateTimeOffset FijiTime(DateOnly date, TimeOnly time) =>
        new(date.ToDateTime(time), TimeSpan.FromHours(12));

    public static int Buffer(Asset asset) => Math.Clamp(asset.TurnaroundMinutes, 0, 1440);

    public static bool Conflicts(DateTimeOffset requestedStart, DateTimeOffset requestedEnd,
        DateTimeOffset bookedStart, DateTimeOffset bookedEnd, int turnaroundMinutes) =>
        bookedStart < requestedEnd.AddMinutes(turnaroundMinutes) && bookedEnd.AddMinutes(turnaroundMinutes) > requestedStart;

    public static DateTimeOffset EarliestStart(DateTimeOffset requestedStart, TimeSpan duration,
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> reservations, int turnaroundMinutes)
    {
        var next = requestedStart;
        foreach (var reservation in reservations.OrderBy(x => x.Start))
            if (Conflicts(next, next.Add(duration), reservation.Start, reservation.End, turnaroundMinutes))
                next = reservation.End.AddMinutes(turnaroundMinutes);
        return next;
    }
}
